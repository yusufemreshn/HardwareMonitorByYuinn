using System.Management;
using System.Runtime.InteropServices;
using HardwareMonitorByYuinn.Entity.Hardware;
using Microsoft.Win32;
using HardwareMonitorByYuinn.Entity.System;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

public sealed class LibreHardwareReader : IHardwareReader
{
    private readonly Computer _computer;
    private readonly ILogger<LibreHardwareReader> _logger;
    private readonly object _pollLock = new();

    // GetSystemInfo yalnızca HTTP istek thread'lerinden (Dashboard/Details sayfa yüklemesi) çağrılır
    // ve bu iki değer bir boot oturumu boyunca pratikte hiç değişmez (RAM miktarı asla, GPU sürücü
    // sürümü de kullanıcı sürücüyü güncelleyip makineyi yeniden başlatmadan değişmez); önbelleğe
    // alınmadan önce her sayfa yüklemesinde gereksiz bir WMI sorgusu tekrarlanıyordu.
    private readonly object _systemInfoCacheLock = new();
    private double? _cachedTotalRamGb;
    private Dictionary<string, string>? _cachedGpuDriverVersions;
    private readonly double? _ramClockMhz;
    private readonly Dictionary<string, double> _lastKnownGpuMemoryMb = [];
    private readonly HashSet<string> _loggedHardwareFailures = [];
    private readonly WmiCpuMetricsProvider _wmiCpuMetrics;
    private readonly GpuMetricsProvider _gpuMetrics;
    private readonly EtwFpsProvider _etwFps;
    private readonly NetworkMetricsProvider _networkMetrics = new();
    private readonly ProcessMetricsProvider _processMetrics = new();
    private readonly PhysicalDiskInfoProvider _physicalDiskInfo;
    private readonly DiskSmartInfoProvider _diskSmartInfo;
    private volatile bool _lowLevelSensorsAvailable;
    private bool _lowLevelDiagnosticsLogged;
    private readonly HashSet<string> _loggedGpuSensorDumps = [];
    private readonly HashSet<string> _loggedCpuSensorDumps = [];
    private bool _disposed;

    public LibreHardwareReader(ILogger<LibreHardwareReader> logger)
    {
        _logger = logger;
        _wmiCpuMetrics = new WmiCpuMetricsProvider(logger);
        _gpuMetrics = new GpuMetricsProvider(logger);
        _etwFps = new EtwFpsProvider(logger);
        _physicalDiskInfo = new PhysicalDiskInfoProvider(logger);
        _diskSmartInfo = new DiskSmartInfoProvider(logger);
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true
        };
        _computer.Open();
        _ramClockMhz = TryReadRamClockMhzFromWmi();

        // Algılanan donanım listesi başlangıçta bir kez yazılır. Uygulama başka makinelerde de
        // çalışacağı için, bir değer eksik göründüğünde ilk bakılacak yer burasıdır.
        _logger.LogInformation(
            "Algılanan donanım: {Hardware}",
            string.Join(" | ", _computer.Hardware.Select(h => $"{h.HardwareType}={h.Name}")));

        // İlk okuma, sensörleri ısıtır ve düşük seviyeli sürücünün var olup olmadığını belirler; böylece
        // ilk sayfa isteği arka plan servisinin ilk turundan önce gelse bile sistem bilgisi doğru olur.
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Başlangıç sensör okuması başarısız oldu");
        }
    }

    /// <summary>
    /// İşlemcinin MSR/SMU kayıtlarına erişebilen çekirdek sürücüsünün (PawnIO) kullanılabilir olup olmadığı.
    /// Kullanılamıyorsa güç tüketimi hiç okunamaz; frekans ve sıcaklık yaklaşık kaynaklardan gelir.
    /// </summary>
    public bool LowLevelSensorsAvailable => _lowLevelSensorsAvailable;

    public HardwareSnapshot Poll()
    {
        lock (_pollLock)
        {
            CpuSnapshot? cpu = null;
            var gpus = new List<GpuSnapshot>();
            var gpuIdentifiers = new List<string>();
            RamSnapshot? ram = null;
            var storages = new List<StorageSnapshot>();

            // Kendi sürücüsüz okumalarımız tüm kartlar için tek seferde alınır; satıcı kütüphanesinin
            // döndürdüğü değerlerden önce gelirler. Adaptör ve süreç bazlı GPU kullanımı tek bir WMI
            // taramasından geldiği için burada bir kerede alınır (bkz. GpuMetricsProvider.Read).
            (IReadOnlyDictionary<string, GpuReading> ownGpuReadings, IReadOnlyDictionary<int, double> gpuPercentByPid) = _gpuMetrics.Read();
            IReadOnlyList<PhysicalDiskInfo> physicalDisks = _physicalDiskInfo.Read();
            IReadOnlyList<DiskSmartInfo> diskSmartInfo = _diskSmartInfo.Read();

            foreach (IHardware hw in _computer.Hardware)
            {
                try
                {
                    UpdateRecursive(hw);
                    switch (hw.HardwareType)
                    {
                        case HardwareType.Cpu:
                            // Çok yuvalı sistemlerde işlemci başına bir donanım listelenir; veri taşıyanı koruruz.
                            cpu = PreferReadingWithData(cpu, ReadCpu(hw), c => c.TotalLoadPercent);

                            // P-core/E-core hibrit mimaride (ör. Intel 12. nesil) çekirdek tablosunda
                            // bazı satırların frekansı boş kalabiliyor; bunu tahmin etmek yerine, ham
                            // "Core #N" sensör isimlerini bir kez loglayıp LibreHardwareMonitor'ün bu
                            // CPU için gerçekte hangi indeksleri yayınladığına bakılabilir (GPU ham
                            // sensör dökümüyle aynı desen).
                            string cpuId = hw.Identifier.ToString() ?? hw.Name;
                            if (_loggedCpuSensorDumps.Add(cpuId))
                            {
                                _logger.LogInformation(
                                    "{Cpu} ham çekirdek sensörleri: {Sensors}",
                                    hw.Name,
                                    string.Join(" | ", hw.Sensors
                                        .Where(s => SensorLookup.CoreIndexRegex().IsMatch(s.Name))
                                        .Select(s => $"{s.SensorType}:{s.Name}={s.Value}")));
                            }
                            break;
                        case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel:
                            GpuSnapshot gpuSnapshot = ReadGpu(hw, GpuMetricsProvider.Match(ownGpuReadings, hw.Name));
                            gpus.Add(gpuSnapshot);
                            string gpuId = hw.Identifier.ToString() ?? hw.Name;
                            gpuIdentifiers.Add(gpuId);

                            // Bazı APU/iGPU'larda güç sensörü "GPU Package"/"GPU Power" adlarıyla
                            // eşleşmiyor (ör. entegre grafiklerde ayrı bir güç kaydı olmayabilir ya da
                            // farklı isimlendirilir). Bunu tahmin etmek yerine, o donanımın TÜM ham
                            // sensörlerini bir kez loglayıp gerçek isimlere bakıyoruz.
                            if (_loggedGpuSensorDumps.Add(gpuId))
                            {
                                _logger.LogInformation(
                                    "{Gpu} ham sensörleri: {Sensors}",
                                    hw.Name,
                                    string.Join(" | ", hw.Sensors.Select(s => $"{s.SensorType}:{s.Name}={s.Value}")));
                            }
                            if (gpuSnapshot.MemoryTotalMb is { } memoryMb)
                                _lastKnownGpuMemoryMb[gpuId] = memoryMb;
                            break;
                        case HardwareType.Memory:
                            // Düşük seviyeli sürücü kuruluyken kütüphane "Memory" türünde birden fazla
                            // donanım listeler: "Virtual Memory" (RAM + sayfa dosyası, istemediğimiz),
                            // "Total Memory" (istediğimiz fiziksel RAM) ve takılı her bellek modülü
                            // (kullanım sensörü yok). Sanal belleği baştan eleriz; kalanlar arasında da
                            // veri taşıyanı koruruz, çünkü modüllerin sıralaması sistemden sisteme değişebilir.
                            if (!hw.Name.Equals("Virtual Memory", StringComparison.OrdinalIgnoreCase))
                                ram = PreferReadingWithData(ram, ReadRam(hw), r => r.UsedPercent);
                            break;
                        case HardwareType.Storage:
                            storages.Add(ReadStorage(hw, physicalDisks, diskSmartInfo));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Poll saniyede bir çalışır ve arızalı bir sensör her turda aynı hatayı verir;
                    // bileşen başına yalnızca ilk hatayı yazarak log dosyasının şişmesini önleriz.
                    if (_loggedHardwareFailures.Add(hw.Identifier.ToString() ?? hw.Name))
                        _logger.LogWarning(ex, "{Hardware} sensörleri okunurken hata oluştu, bu bileşen atlandı", hw.Name);
                }
            }

            GpuSnapshot? primaryGpu = SelectPrimaryGpu(gpus, gpuIdentifiers, _lastKnownGpuMemoryMb);

            FpsReading? fpsReading = _etwFps.GetForegroundFps();
            FpsSnapshot? fps = fpsReading is null
                ? null
                : new FpsSnapshot
                {
                    FramesPerSecond = fpsReading.FramesPerSecond,
                    SourceProcessName = fpsReading.ProcessName,
                    FrameTimeMs = fpsReading.FrameTimeMs,
                    Low1PercentFps = fpsReading.Low1PercentFps,
                    LowPoint1PercentFps = fpsReading.LowPoint1PercentFps
                };

            NetworkSnapshot? network = _networkMetrics.Read();
            IReadOnlyList<ProcessMetric> topProcesses = _processMetrics.Read(8, gpuPercentByPid);

            return new HardwareSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                Cpu = cpu,
                Gpu = primaryGpu,
                AllGpus = gpus,
                Ram = ram,
                Fps = fps,
                Storages = storages,
                Network = network,
                TopProcesses = topProcesses
            };
        }
    }

    public SystemInfo GetSystemInfo(DateTime appStartedAtUtc)
    {
        // `_computer.Hardware` arka planda saniyede bir `Poll()` (bkz. `_pollLock`) tarafından
        // `UpdateRecursive` ile güncelleniyor. Bu metod HTTP istek thread'lerinden çağrıldığı için
        // (Dashboard/Details sayfa yüklemesi), kilitsiz bir enumerate ile Poll() aynı anda çalışırsa
        // LibreHardwareMonitor'un thread-safe olduğu belgelenmemiş koleksiyonunda "collection was
        // modified" istisnası ya da tutarsız/yarım okunmuş veri riski oluşuyordu. Aynı kilit burada
        // da alınarak yarış durumu ortadan kaldırılıyor.
        string cpuName;
        List<string> gpuNames;
        lock (_pollLock)
        {
            cpuName = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu)?.Name
                ?? "Bilinmeyen İşlemci";
            gpuNames = _computer.Hardware
                .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                .Select(h => h.Name)
                .ToList();
        }
        Dictionary<string, string> gpuDriverVersions = TryReadGpuDriverVersionsFromWmi(gpuNames);

        List<StorageDriveInfo> drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new StorageDriveInfo
            {
                Name = d.Name,
                VolumeLabel = SafeRead(() => d.VolumeLabel),
                DriveFormat = SafeRead(() => d.DriveFormat),
                TotalBytes = d.TotalSize,
                FreeBytes = d.AvailableFreeSpace
            })
            .ToList();

        List<PhysicalDiskSummary> physicalDisks = _physicalDiskInfo.Read()
            .Select(d => new PhysicalDiskSummary { Name = d.Name, TotalGb = d.TotalGb, MediaType = d.MediaType })
            .ToList();

        return new SystemInfo
        {
            MachineName = Environment.MachineName,
            OsDescription = GetFriendlyOsName(),
            CpuName = cpuName,
            GpuNames = gpuNames,
            GpuDriverVersions = gpuDriverVersions,
            TotalRamGb = TryReadTotalRamGbFromWmi(),
            AppStartedAtUtc = appStartedAtUtc,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            Drives = drives,
            PhysicalDisks = physicalDisks,
            LowLevelSensorsAvailable = _lowLevelSensorsAvailable
        };
    }

    /// <summary>
    /// Aynı türden birden fazla donanım listelendiğinde, aranan ölçümü gerçekten taşıyan okumayı seçer.
    /// Elde veri varsa ve yeni okuma boşsa mevcut okuma korunur; böylece sıralamadan bağımsız çalışır.
    /// </summary>
    internal static T PreferReadingWithData<T>(T? existing, T candidate, Func<T, double?> keyMetric)
        where T : class
    {
        if (existing is null)
            return candidate;

        return keyMetric(existing) is null && keyMetric(candidate) is not null ? candidate : existing;
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    private const string ClockSourceHardware = "Donanım sensörü";
    private const string ClockSourcePerfCounter = "Windows performans sayacı";
    private const string TemperatureSourceDie = "İşlemci çip sensörü (Tctl/Tdie)";
    private const string TemperatureSourceAcpi = "ACPI termal bölgesi (yaklaşık)";

    private CpuSnapshot ReadCpu(IHardware hw)
    {
        double? total = hw.FindValue(SensorType.Load, n => n.Equals("CPU Total", StringComparison.OrdinalIgnoreCase));

        // Bu üç değer MSR/SMU okumasına dayanır. PawnIO sürücüsü yoksa kütüphane null yerine 0 döndürdüğü
        // için NullIfZero ile eleriz; aksi halde arayüzde "0 W / 0 °C" gerçek bir ölçümmüş gibi görünür.
        double? packagePower = (hw.FindValue(SensorType.Power, n => n.Contains("Package", StringComparison.OrdinalIgnoreCase))
            ?? hw.FindValue(SensorType.Power, n => n.Contains("Cores", StringComparison.OrdinalIgnoreCase))).NullIfZero();
        double? packageTemp = (hw.FindValue(SensorType.Temperature, n => n.Contains("Package", StringComparison.OrdinalIgnoreCase))
            ?? hw.FindValue(SensorType.Temperature, n => n.Contains("Tctl", StringComparison.OrdinalIgnoreCase))).NullIfZero();
        double? packageClockSensor = hw.FindValue(SensorType.Clock, n => n.Equals("Cores (Average)", StringComparison.OrdinalIgnoreCase)).NullIfZero();

        // Güç ya da çip sıcaklığı okunabiliyorsa düşük seviyeli sürücü çalışıyor demektir.
        _lowLevelSensorsAvailable = packagePower.HasValue || packageTemp.HasValue;

        // PawnIO uyarısının doğru koşulda çıkıp çıkmadığını (kurulu değilken yanlışlıkla
        // bastırılıp bastırılmadığını) günlükten teyit edebilmek için, bu değer ilk hesaplandığında
        // ham sensör değerleri bir kez loglanır — sorun tekrar bildirilirse log dosyasına bakmak
        // "hangi sensör/isim eşleşti" sorusunu tahmin etmeden kesin olarak yanıtlar.
        if (!_lowLevelDiagnosticsLogged)
        {
            _lowLevelDiagnosticsLogged = true;
            _logger.LogInformation(
                "Düşük seviyeli sensör kontrolü: packagePower={PackagePower}, packageTemp={PackageTemp}, sonuç={Available}",
                packagePower, packageTemp, _lowLevelSensorsAvailable);
        }

        // Yük sensörleri (Load) tüm fiziksel çekirdekleri birleşik sırayla numaralandırır, ama
        // Frekans/Voltaj/Sıcaklık sensörleri P-core/E-core hibrit CPU'larda tür başına ayrı
        // numaralandırma kullanır ("P-Core #1-4", "E-Core #1-8") — bkz. SensorLookup.ResolveCoreIndex.
        // E-core'ların kaçıncı fiziksel çekirdekten başladığını bilmek için önce P-core sayısı bulunur.
        int pCoreCount = hw.Sensors
            .Select(s => SensorLookup.PCoreIndexRegex().Match(s.Name))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        var coreClocks = new Dictionary<int, double>();
        var coreEffectiveClocks = new Dictionary<int, double>();
        var coreLoadSums = new Dictionary<int, double>();
        var coreLoadCounts = new Dictionary<int, int>();
        var corePower = new Dictionary<int, double>();
        foreach (ISensor sensor in hw.Sensors)
        {
            if (sensor.Value is not { } value)
                continue;

            if (SensorLookup.ResolveCoreIndex(sensor.Name, pCoreCount) is not { } index)
                continue;

            bool isEffective = sensor.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase);
            switch (sensor.SensorType)
            {
                case SensorType.Clock when value > 0 && isEffective:
                    coreEffectiveClocks[index] = value;
                    break;
                case SensorType.Clock when value > 0:
                    coreClocks[index] = value;
                    break;
                case SensorType.Load:
                    // P-core'larda iş parçacığı başına ayrı bir Yük sensörü olduğundan ("Core #N
                    // Thread #1"/"#2", ikisi de aynı N fiziksel çekirdek indeksine çözülür) burada
                    // birden fazla okuma birikebilir; fiziksel çekirdeğin gerçek kullanımını yansıtsın
                    // diye ortalanır (aksi hâlde sonuncusu öncekinin üzerine yazıp bir iş parçacığının
                    // verisi sessizce kaybolurdu — %0 yük de geçerli bir ölçüm olduğundan elenmez).
                    coreLoadSums[index] = coreLoadSums.TryGetValue(index, out double sum) ? sum + value : value;
                    coreLoadCounts[index] = coreLoadCounts.TryGetValue(index, out int count) ? count + 1 : 1;
                    break;
                case SensorType.Power when value > 0:
                    corePower[index] = value;
                    break;
            }
        }

        var coreLoads = coreLoadSums.ToDictionary(kv => kv.Key, kv => kv.Value / coreLoadCounts[kv.Key]);

        // Frekans ve güç fiziksel çekirdek başına, yük ise mantıksal iş parçacığı başına ölçülür.
        // Aynı tabloda gösterebilmek için yükü çekirdeğine katlarız; aksi hâlde SMT'li işlemcilerde
        // çekirdek sayısının iki katı satır oluşur ve fazlalıkların frekansı boş görünür.
        int physicalCoreCount = _wmiCpuMetrics.GetPhysicalCoreCount();

        // Ham "Core #N", P-state kaydından (CurCpuFid/CurCpuDfsId) anlık okunur ve LibreHardwareMonitor
        // gerekirse APERF/MPERF oranıyla tıkanma (throttle) düzeltmesi de uygular; CPU-Z/HWiNFO'nun anlık
        // gösterdiği değere en yakın olan budur. "Core #N (Effective)" ise anlık bir ölçüm değil, tüm
        // örnekleme aralığı (yaklaşık 1 sn) boyunca APERF sayacının süreye bölünmesiyle elde edilen bir
        // ORTALAMADIR; çekirdek o saniyenin çoğunda boşta kalıp kısa patlamalarla yükseliyorsa bu ortalama
        // her zaman düşük ve "takılı" görünür — CPU-Z'nin anlık/dalgalı gösterimiyle asla örtüşmez. Bu
        // yüzden ham veri varsa o tercih edilir; yalnızca hiç ham çekirdek sensörü yoksa etkin olana düşülür.
        IReadOnlyDictionary<int, double> resolvedCoreClocks = coreClocks.Count > 0 ? coreClocks : coreEffectiveClocks;
        string? clockSource = resolvedCoreClocks.Count > 0 || packageClockSensor.HasValue ? ClockSourceHardware : null;

        if (resolvedCoreClocks.Count == 0 || packageClockSensor is null)
        {
            CpuClockReading fallback = _wmiCpuMetrics.ReadClocks();
            if (resolvedCoreClocks.Count == 0 && fallback.PerCoreMhz.Count > 0)
            {
                // Performans sayaçları da iş parçacığı başınadır; aynı ölçeğe indirilir.
                resolvedCoreClocks = SensorLookup.FoldThreadsIntoCores(fallback.PerCoreMhz, physicalCoreCount);
                clockSource = ClockSourcePerfCounter;
            }

            if (packageClockSensor is null && fallback.TotalMhz.HasValue)
            {
                packageClockSensor = fallback.TotalMhz;
                clockSource ??= ClockSourcePerfCounter;
            }
        }

        // Yük %100 değilken çekirdeklerin bir kısmı boşta/düşük P-state'te kalırken bir kısmı yükselir
        // (ör. %19 yükte çekirdekler 613-2119 MHz arasında ölçülmüştü); bu değerlerin ORTALAMASINI almak
        // boştaki çekirdekleri de katarak gerçekte çalışan çekirdeğin hızını sulandırır. CPU-Z/HWiNFO'nun
        // gösterdiği "işlemci hızı" o an fiilen çalışan (en yüksek) çekirdeği yansıtır; yalnızca %100 yükte
        // tüm çekirdekler zaten tepede olduğu için ortalama da o zaman doğru görünüyordu. Bu yüzden paket
        // frekansı en yüksek çekirdek değerinden hesaplanır. Çekirdek başı hiç veri yoksa ham paket
        // sensörüne/WMI sonucuna düşülür.
        double? packageClock = resolvedCoreClocks.Count > 0 ? resolvedCoreClocks.Values.Max() : packageClockSensor;

        string? temperatureSource = packageTemp.HasValue ? TemperatureSourceDie : null;
        if (packageTemp is null)
        {
            packageTemp = _wmiCpuMetrics.ReadAcpiTemperatureC();
            if (packageTemp.HasValue)
                temperatureSource = TemperatureSourceAcpi;
        }

        IReadOnlyDictionary<int, double> resolvedCoreLoads =
            SensorLookup.FoldThreadsIntoCores(coreLoads, physicalCoreCount);

        List<CoreSensor> cores = resolvedCoreClocks.Keys
            .Union(resolvedCoreLoads.Keys)
            .Union(corePower.Keys)
            .OrderBy(i => i)
            .Select(i => new CoreSensor
            {
                CoreIndex = i,
                ClockMhz = resolvedCoreClocks.TryGetValue(i, out double c) ? c : null,
                LoadPercent = resolvedCoreLoads.TryGetValue(i, out double l) ? l : null,
                PowerWatts = corePower.TryGetValue(i, out double p) ? p : null
            })
            .ToList();

        return new CpuSnapshot
        {
            Name = hw.Name,
            TotalLoadPercent = total,
            PackageClockMhz = packageClock,
            PackagePowerWatts = packagePower,
            PackageTemperatureC = packageTemp,
            TemperatureSource = temperatureSource,
            ClockSource = clockSource,
            Cores = cores
        };
    }

    // Hibrit grafikli (iGPU+dGPU) sistemlerde ayrık kart neredeyse her zaman çok daha fazla VRAM'e sahiptir; birincil olarak onu seçeriz.
    // Guç tasarrufu modunda dGPU'nun VRAM sensoru gecici olarak null donebilir; bu durumda son bilinen degeri kullanarak
    // secimin her poll'da farkli karta atlamasini (flicker) onleriz.
    internal static GpuSnapshot? SelectPrimaryGpu(
        IReadOnlyList<GpuSnapshot> gpus,
        IReadOnlyList<string> identifiers,
        IReadOnlyDictionary<string, double> lastKnownMemoryMb)
    {
        if (gpus.Count == 0)
            return null;

        double RankValue(int index) =>
            gpus[index].MemoryTotalMb ?? lastKnownMemoryMb.GetValueOrDefault(identifiers[index], 0);

        int bestIndex = 0;
        double bestValue = RankValue(0);
        for (int i = 1; i < gpus.Count; i++)
        {
            double value = RankValue(i);
            if (value > bestValue)
            {
                bestValue = value;
                bestIndex = i;
            }
        }

        return gpus[bestIndex];
    }

    /// <summary>
    /// Ekran kartı değerlerini üretir. Kullanım, bellek, sıcaklık ve bellek frekansı öncelikle kendi
    /// sürücüsüz okuyucumuzdan (<paramref name="own"/>) alınır; satıcı kütüphanesi yalnızca oradan
    /// gelmeyen değerler ve yalnızca NVAPI/ADL ile okunabilen çekirdek frekansı, güç ve hotspot için kullanılır.
    /// </summary>
    private static GpuSnapshot ReadGpu(IHardware hw, GpuReading? own)
    {
        double? load = hw.FindValue(SensorType.Load, n => n.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
            ?? hw.FindValue(SensorType.Load, n => n.Contains("D3D", StringComparison.OrdinalIgnoreCase));
        double? coreClock = hw.FindValue(SensorType.Clock, n => n.Contains("GPU Core", StringComparison.OrdinalIgnoreCase)).NullIfZero();
        double? memClock = hw.FindValue(SensorType.Clock, n => n.Contains("GPU Memory", StringComparison.OrdinalIgnoreCase)).NullIfZero();
        double? hotSpot = hw.FindValue(SensorType.Temperature, n => n.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)).NullIfZero();
        double? coreTemp = hw.FindValue(SensorType.Temperature, n => n.Contains("GPU Core", StringComparison.OrdinalIgnoreCase)).NullIfZero();
        double? power = (hw.FindValue(SensorType.Power, n => n.Contains("GPU Package", StringComparison.OrdinalIgnoreCase))
            ?? hw.FindValue(SensorType.Power, n => n.Contains("GPU Power", StringComparison.OrdinalIgnoreCase))
            ?? ReadAmdApuGpuPowerFallback(hw)).NullIfZero();
        double? memUsed = hw.FindValue(SensorType.SmallData, n => n.Contains("GPU Memory Used", StringComparison.OrdinalIgnoreCase)).NullIfZero();
        double? memTotal = hw.FindValue(SensorType.SmallData, n => n.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase)).NullIfZero();

        return new GpuSnapshot
        {
            Name = hw.Name,
            LoadPercent = own?.LoadPercent ?? load,
            CoreClockMhz = coreClock,
            MemoryClockMhz = own?.MemoryClockMhz ?? memClock,
            CoreTemperatureC = own?.TemperatureC ?? coreTemp,
            HotSpotTemperatureC = hotSpot,
            PowerWatts = power,
            MemoryUsedMb = own?.MemoryUsedMb ?? memUsed,
            MemoryTotalMb = own?.MemoryTotalMb ?? memTotal
        };
    }

    /// <summary>
    /// AMD entegre grafiklerde (APU) tek bir "GPU Package"/"GPU Power" sensörü yayınlanmaz; bunun
    /// yerine ayrı "GPU Core" ve "GPU SoC" güç kayıtları vardır (bir Ryzen 7840HS/Radeon 780M
    /// sisteminde ham sensör dökümüyle doğrulandı — ör. Core=7W, SoC=1W). Toplam GPU güç tüketimine
    /// en yakın değeri vermek için ikisi bulunabildiği kadarıyla toplanır; hiçbiri yoksa null döner.
    /// </summary>
    private static double? ReadAmdApuGpuPowerFallback(IHardware hw)
    {
        double? corePower = hw.FindValue(SensorType.Power, n => n.Equals("GPU Core", StringComparison.OrdinalIgnoreCase));
        double? socPower = hw.FindValue(SensorType.Power, n => n.Equals("GPU SoC", StringComparison.OrdinalIgnoreCase));
        if (corePower is null && socPower is null)
            return null;

        return (corePower ?? 0) + (socPower ?? 0);
    }

    private static StorageSnapshot ReadStorage(
        IHardware hw,
        IReadOnlyList<PhysicalDiskInfo> physicalDisks,
        IReadOnlyList<DiskSmartInfo> diskSmartInfo)
    {
        double? usedPercent = hw.FindValue(SensorType.Load, n => n.Contains("Used Space", StringComparison.OrdinalIgnoreCase));
        double? temperature = hw.FindValue(SensorType.Temperature, n => n.Contains("Temperature", StringComparison.OrdinalIgnoreCase)).NullIfZero();
        double? readRate = hw.FindValue(SensorType.Throughput, n => n.Contains("Read Rate", StringComparison.OrdinalIgnoreCase));
        double? writeRate = hw.FindValue(SensorType.Throughput, n => n.Contains("Write Rate", StringComparison.OrdinalIgnoreCase));

        // Bazı OEM NVMe sürücülerinde LibreHardwareMonitor'ün okuduğu isim bozuk gelir (yalnızca
        // görünmez kontrol karakterleri/boşluk) — tarayıcıda "kare" (tofu) glyph olarak render edilir
        // ve isme dayalı SMART/kapasite eşleştirmesi hiç tutmaz (log kanıtı: bkz. CHANGELOG 2026-08-03,
        // "Algılanan donanım" satırında "Storage=" tamamen boş gelmişti). Görünmez karakterler ayıklanır;
        // sonuç hâlâ boşsa WMI'dan (PhysicalDiskInfoProvider, LibreHardwareMonitor'dan bağımsız bir
        // kaynak) gelen isim -- yalnızca tek fiziksel disk varsa, aksi hâlde hangisi olduğu belirsiz
        // kalır -- yerine kullanılır.
        string effectiveName = new string(hw.Name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (effectiveName.Length == 0)
            effectiveName = physicalDisks.Count == 1 ? physicalDisks[0].Name : "Depolama Aygıtı";

        // İsme dayalı eşleşme başarısız olursa ve tam olarak tek aday varsa, o adayın doğru disk
        // olduğu zaten kesindir (başka olasılık yok) — isim ne kadar bozuk olursa olsun buna düşülür.
        PhysicalDiskInfo? physicalDisk = PhysicalDiskInfoProvider.Match(physicalDisks, effectiveName)
            ?? (physicalDisks.Count == 1 ? physicalDisks[0] : null);
        DiskSmartInfo? smart = DiskSmartInfoProvider.Match(diskSmartInfo, effectiveName)
            ?? (diskSmartInfo.Count == 1 ? diskSmartInfo[0] : null);

        return new StorageSnapshot
        {
            Name = effectiveName,
            UsedPercent = usedPercent,
            TemperatureC = temperature,
            ReadRateBytesPerSec = readRate,
            WriteRateBytesPerSec = writeRate,
            TotalGb = physicalDisk?.TotalGb,
            SmartStatus = smart?.Status,
            SmartLifePercent = smart?.LifePercent,
            PowerOnHours = smart?.PowerOnHours,
            HostReadsGb = smart?.HostReadsGb,
            HostWritesGb = smart?.HostWritesGb
        };
    }

    private RamSnapshot ReadRam(IHardware hw)
    {
        double? usedPercent = hw.FindValue(SensorType.Load, n => n.Contains("Memory", StringComparison.OrdinalIgnoreCase));
        double? usedGb = hw.FindValue(SensorType.Data, n => n.Contains("Memory Used", StringComparison.OrdinalIgnoreCase));
        double? availableGb = hw.FindValue(SensorType.Data, n => n.Contains("Memory Available", StringComparison.OrdinalIgnoreCase));

        return new RamSnapshot
        {
            UsedPercent = usedPercent,
            UsedGb = usedGb,
            TotalGb = usedGb.HasValue && availableGb.HasValue ? usedGb + availableGb : null,
            ClockMhz = _ramClockMhz
        };
    }

    private double? TryReadRamClockMhzFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            var speeds = new List<double>();
            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item["ConfiguredClockSpeed"] is uint cfg && cfg > 0)
                    speeds.Add(cfg);
                else if (item["Speed"] is uint sp)
                    speeds.Add(sp);
            }

            return speeds.Count > 0 ? speeds.Average() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAM frekansı WMI üzerinden okunamadı");
            return null;
        }
    }

    private double TryReadTotalRamGbFromWmi()
    {
        lock (_systemInfoCacheLock)
        {
            if (_cachedTotalRamGb is { } cached) return cached;
        }

        double value = TryReadTotalRamGbFromWmiCore();
        // Yalnızca gerçekten anlamlı bir değer önbelleğe alınır; geçici bir WMI hatası (0 dönen)
        // sonsuza dek "0 GB" göstermeye devam etmesin, bir sonraki sayfa yüklemesinde tekrar denensin.
        if (value > 0)
        {
            lock (_systemInfoCacheLock) { _cachedTotalRamGb = value; }
        }

        return value;
    }

    private double TryReadTotalRamGbFromWmiCore()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item["TotalPhysicalMemory"] is ulong bytes)
                    return Math.Round(bytes / 1024d / 1024d / 1024d, 1);
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Toplam RAM miktarı WMI üzerinden okunamadı");
            return 0;
        }
    }

    /// <summary>
    /// LibreHardwareMonitor sürücü sürümü sağlamadığı için Win32_VideoController üzerinden okunur.
    /// WMI'daki kart adı LibreHardwareMonitor'ünkiyle birebir aynı olmayabilir (ör. üretici öneki farkı),
    /// bu yüzden önce tam eşleşme, olmazsa karşılıklı alt dize içerme denenir.
    /// </summary>
    private Dictionary<string, string> TryReadGpuDriverVersionsFromWmi(List<string> gpuNames)
    {
        if (gpuNames.Count == 0)
            return [];

        lock (_systemInfoCacheLock)
        {
            if (_cachedGpuDriverVersions is not null) return _cachedGpuDriverVersions;
        }

        Dictionary<string, string> value = TryReadGpuDriverVersionsFromWmiCore(gpuNames);
        // Yalnızca en az bir sürücü sürümü bulunabildiyse önbelleğe alınır; boş dönen bir geçici
        // WMI hatası sonsuza dek "sürücü bilgisi yok" göstermeye devam etmesin.
        if (value.Count > 0)
        {
            lock (_systemInfoCacheLock) { _cachedGpuDriverVersions = value; }
        }

        return value;
    }

    private Dictionary<string, string> TryReadGpuDriverVersionsFromWmiCore(List<string> gpuNames)
    {
        var result = new Dictionary<string, string>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            var wmiEntries = new List<(string Name, string DriverVersion)>();
            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item["Name"] is string name && item["DriverVersion"] is string driverVersion && driverVersion.Length > 0)
                    wmiEntries.Add((name, driverVersion));
            }

            foreach (string gpuName in gpuNames)
            {
                int exactIndex = wmiEntries.FindIndex(e => string.Equals(e.Name, gpuName, StringComparison.OrdinalIgnoreCase));
                int index = exactIndex >= 0
                    ? exactIndex
                    : wmiEntries.FindIndex(e =>
                        gpuName.Contains(e.Name, StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Contains(gpuName, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                    result[gpuName] = wmiEntries[index].DriverVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ekran kartı sürücü sürümü WMI üzerinden okunamadı");
        }

        return result;
    }

    /// <summary>
    /// <see cref="RuntimeInformation.OSDescription"/> Windows 11'de bile "Microsoft Windows 10.0.26200"
    /// gibi çekirdek sürüm dizesini döndürür; kayıt defterindeki ürün adı ve sürüm etiketi ("23H2")
    /// kullanıcıya çok daha anlamlı gelir. Ancak Microsoft, eski uygulamalarla uyumluluk için Windows
    /// 11'de bile <c>ProductName</c> değerini bilerek "Windows 10 ..." olarak bırakıyor; gerçek sürüm
    /// yalnızca build numarasından ayırt edilebilir (<c>CurrentBuildNumber</c> &gt;= 22000 → Windows 11).
    /// Okunamazsa ham dizeye döner.
    /// </summary>
    private static string GetFriendlyOsName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string? productName = key?.GetValue("ProductName") as string;
            if (string.IsNullOrWhiteSpace(productName))
                return RuntimeInformation.OSDescription;

            if (productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out int buildNumber)
                && buildNumber >= 22000)
            {
                productName = "Windows 11" + productName["Windows 10".Length..];
            }

            string? displayVersion = key?.GetValue("DisplayVersion") as string;
            return string.IsNullOrWhiteSpace(displayVersion) ? productName : $"{productName} ({displayVersion})";
        }
        catch
        {
            return RuntimeInformation.OSDescription;
        }
    }

    private static string? SafeRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _computer.Close();
        _etwFps.Dispose();
        _disposed = true;
    }
}
