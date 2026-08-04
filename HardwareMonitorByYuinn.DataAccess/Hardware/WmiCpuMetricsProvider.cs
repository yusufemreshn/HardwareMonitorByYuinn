using System.Management;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>
/// LibreHardwareMonitor'un işlemci frekans/güç/sıcaklık sensörleri MSR ve SMU okumalarına dayanır;
/// bunlar da yalnızca PawnIO çekirdek sürücüsü kuruluysa çalışır. Sürücü yoksa kütüphane bu
/// sensörler için <c>null</c> değil <c>0</c> döndürür, yani veri yokluğu "sıfır değer" gibi görünür.
/// Bu sağlayıcı, hiçbir sürücü gerektirmeyen WMI performans sayaçları ve ACPI termal bölgeleri
/// üzerinden en azından frekans ve sıcaklık için gerçek değerler üretir.
/// </summary>
internal sealed class WmiCpuMetricsProvider(ILogger logger)
{
    private static readonly TimeSpan TemperatureCacheDuration = TimeSpan.FromSeconds(2);

    /// <summary>
    /// ACPI sorgusu başarısızsa (çoğunlukla yönetici yetkisi olmadığı için) neden kalıcıdır.
    /// Saniyede bir yeniden denemek hem boşuna iştir hem de log dosyasını şişirir.
    /// </summary>
    private static readonly TimeSpan TemperatureRetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger = logger;
    private readonly object _gate = new();
    private double? _baseClockMhz;
    private bool _baseClockResolved;
    private double? _cachedTemperatureC;
    private DateTime _temperatureReadAtUtc = DateTime.MinValue;
    private int _physicalCoreCount;
    private bool _physicalCoreCountResolved;
    private bool _lastTemperatureReadFailed;
    private bool _temperatureFailureLogged;
    private bool _clockFailureLogged;

    /// <summary>Anlık işlemci frekansı: taban frekans × "% Processor Performance". Task Manager de aynı formülü kullanır.</summary>
    public CpuClockReading ReadClocks()
    {
        double? baseClock = GetBaseClockMhz();
        if (baseClock is not > 0)
            return CpuClockReading.Empty;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation");

            double? totalMhz = null;
            var perInstance = new List<(int Group, int Cpu, double Mhz)>();

            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item["Name"] is not string name || item["PercentProcessorPerformance"] is not { } rawPercent)
                    continue;

                double mhz = baseClock.Value * Convert.ToDouble(rawPercent) / 100d;

                // Örnek adları: "_Total" (tüm sistem), "0,_Total" (grup toplamı), "0,5" (grup 0 / mantıksal çekirdek 5).
                if (name.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                {
                    totalMhz = mhz;
                    continue;
                }

                string[] parts = name.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int group) || !int.TryParse(parts[1], out int cpu))
                    continue;

                perInstance.Add((group, cpu, mhz));
            }

            // Çok gruplu (64+ mantıksal çekirdekli) sistemlerde sayaç adları gruba göre sıfırdan başlar;
            // sıralayıp yeniden numaralandırarak LibreHardwareMonitor'un "CPU Core #N" indeksleriyle hizalarız.
            var perCore = new Dictionary<int, double>();
            int index = 1;
            foreach ((_, _, double mhz) in perInstance.OrderBy(x => x.Group).ThenBy(x => x.Cpu))
                perCore[index++] = mhz;

            return new CpuClockReading(totalMhz, perCore);
        }
        catch (Exception ex)
        {
            // Saniyede bir çağrıldığı için burada da yalnızca ilk hata yazılır.
            if (!_clockFailureLogged)
            {
                _clockFailureLogged = true;
                _logger.LogWarning(ex, "İşlemci frekansı WMI performans sayaçlarından okunamadı");
            }

            return CpuClockReading.Empty;
        }
    }

    /// <summary>
    /// ACPI termal bölgelerinden en sıcak makul değeri döndürür. Bu, işlemci lehimindeki Tctl/Tdie
    /// sensörü değildir; kart üzerindeki termal bölgedir, dolayısıyla yaklaşık bir değerdir.
    /// </summary>
    public double? ReadAcpiTemperatureC()
    {
        lock (_gate)
        {
            TimeSpan cacheDuration = _lastTemperatureReadFailed ? TemperatureRetryAfterFailure : TemperatureCacheDuration;
            if (DateTime.UtcNow - _temperatureReadAtUtc < cacheDuration)
                return _cachedTemperatureC;

            _temperatureReadAtUtc = DateTime.UtcNow;
            _cachedTemperatureC = QueryAcpiTemperatureC();
            return _cachedTemperatureC;
        }
    }

    private double? QueryAcpiTemperatureC()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"));

            double? hottest = null;
            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item["CurrentTemperature"] is not { } raw)
                    continue;

                // Değer 1/10 Kelvin cinsindendir.
                double celsius = Convert.ToDouble(raw) / 10d - 273.15;

                // Bazı bölgeler 0 (tanımsız) ya da ortam sıcaklığı bildirir; sadece işlemci için makul aralığı alırız.
                if (celsius is < 25 or > 110)
                    continue;

                if (hottest is null || celsius > hottest)
                    hottest = celsius;
            }

            _lastTemperatureReadFailed = false;
            return hottest;
        }
        catch (Exception ex)
        {
            _lastTemperatureReadFailed = true;

            // Aynı hata saniyede bir tekrarlanıyor; yığın izlemesini yalnızca ilk seferde yazarız,
            // aksi hâlde log dosyası dakikalar içinde megabaytlara ulaşır.
            if (!_temperatureFailureLogged)
            {
                _temperatureFailureLogged = true;
                _logger.LogWarning(ex, "ACPI termal bölge sıcaklığı okunamadı, bu kaynak seyrek aralıklarla yeniden denenecek");
            }

            return null;
        }
    }

    /// <summary>
    /// Fiziksel çekirdek sayısı. Yük sayaçları mantıksal iş parçacığı başına gelirken frekans ve güç
    /// fiziksel çekirdek başına ölçülür; iki ölçeği hizalamak için bu sayıya ihtiyaç duyulur.
    /// </summary>
    public int GetPhysicalCoreCount()
    {
        lock (_gate)
        {
            if (_physicalCoreCountResolved)
                return _physicalCoreCount;

            _physicalCoreCountResolved = true;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    if (item["NumberOfCores"] is { } raw)
                        _physicalCoreCount += Convert.ToInt32(raw); // Birden fazla yuva varsa toplanır
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fiziksel çekirdek sayısı WMI üzerinden okunamadı");
            }

            return _physicalCoreCount;
        }
    }

    private double? GetBaseClockMhz()
    {
        lock (_gate)
        {
            if (_baseClockResolved)
                return _baseClockMhz;

            _baseClockResolved = true;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    if (item["MaxClockSpeed"] is { } raw)
                    {
                        _baseClockMhz = Convert.ToDouble(raw);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "İşlemci taban frekansı WMI üzerinden okunamadı");
            }

            return _baseClockMhz;
        }
    }
}

internal readonly record struct CpuClockReading(double? TotalMhz, IReadOnlyDictionary<int, double> PerCoreMhz)
{
    public static CpuClockReading Empty { get; } = new(null, new Dictionary<int, double>());
}
