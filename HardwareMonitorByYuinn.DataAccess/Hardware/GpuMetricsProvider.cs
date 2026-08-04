using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>Kendi kodumuzla, satıcı kütüphanesi olmadan okunan ekran kartı değerleri.</summary>
internal sealed class GpuReading
{
    public required string Name { get; init; }
    public double? LoadPercent { get; init; }
    public double? MemoryUsedMb { get; init; }
    public double? MemoryTotalMb { get; init; }
    public double? MemoryClockMhz { get; init; }
    public double? TemperatureC { get; init; }
}

/// <summary>
/// Ekran kartı ölçümlerini iki sürücüsüz kaynaktan birleştirir: kullanım ve bellek tüketimi Windows GPU
/// performans sayaçlarından, sıcaklık ve bellek frekansı D3DKMT'den gelir. İkisi de kartın LUID'i ile
/// eşleştirilir. Bu sayede NVIDIA/AMD/Intel ayrımı olmadan tüm kartlarda aynı kod çalışır ve satıcı
/// kütüphanesine yalnızca çekirdek frekansı, güç ve hotspot sıcaklığı için ihtiyaç kalır.
/// </summary>
internal sealed class GpuMetricsProvider(ILogger logger)
{
    private readonly D3DKmtGpuProvider _d3dKmt = new(logger);
    private readonly WmiGpuMetricsProvider _wmi = new(logger);

    /// <summary>
    /// Kart adının normalleştirilmiş hâline göre okumalar, ve ayrıca süreç kimliğine göre GPU kullanım
    /// yüzdesi ("Kaynak Kullanımı" kartının GPU sıralaması için). İkisi de tek bir WMI taramasından
    /// (<see cref="WmiGpuMetricsProvider.Read"/>) geldiği için burada ayrı ayrı sorgulanmaz; D3DKMT
    /// hiç adaptör bulamasa bile süreç bazlı GPU verisi bağımsız olarak döner.
    /// </summary>
    public (IReadOnlyDictionary<string, GpuReading> PerAdapter, IReadOnlyDictionary<int, double> PerProcessLoadPercent) Read()
    {
        IReadOnlyList<D3DKmtGpuReading> adapters = _d3dKmt.Read();
        (IReadOnlyDictionary<string, WmiGpuReading> counters, IReadOnlyDictionary<int, double> perProcess) = _wmi.Read();

        if (adapters.Count == 0)
            return (new Dictionary<string, GpuReading>(), perProcess);

        var result = new Dictionary<string, GpuReading>(StringComparer.Ordinal);
        foreach (D3DKmtGpuReading adapter in adapters)
        {
            counters.TryGetValue(adapter.Luid, out WmiGpuReading? counter);

            var reading = new GpuReading
            {
                Name = adapter.Name,
                LoadPercent = counter?.LoadPercent,
                MemoryUsedMb = counter?.MemoryUsedMb,
                MemoryTotalMb = adapter.MemoryTotalMb,
                MemoryClockMhz = adapter.MemoryClockMhz,
                TemperatureC = adapter.TemperatureC
            };

            // Aynı model iki kez takılıysa adlar çakışır; ilk kartı korumak, her poll'da farklı karta
            // atlamaktan iyidir. Bu durumda ikinci kartın değerleri satıcı kütüphanesinden gelir.
            result.TryAdd(NormalizeName(adapter.Name), reading);
        }

        return (result, perProcess);
    }

    /// <summary>
    /// Kart adlarını eşleştirmek için sadeleştirir. LibreHardwareMonitor ile sürücü kayıt defteri aynı
    /// kartı farklı yazabiliyor ("AMD Radeon(TM) 780M Graphics" / "AMD Radeon 780M"), bu yüzden marka
    /// işaretlerini, noktalama ve boşlukları atıp yalnızca harf ve rakamları karşılaştırırız.
    /// </summary>
    public static string NormalizeName(string name)
    {
        string lowered = name.ToLowerInvariant();

        // Marka işaretleri harflerden oluştuğu için noktalama temizliğinden sağ çıkar ("(TM)" → "tm");
        // eşleşmeyi bozmamaları için önce kendileri atılmalı.
        foreach (string mark in TrademarkMarks)
            lowered = lowered.Replace(mark, string.Empty, StringComparison.Ordinal);

        Span<char> buffer = stackalloc char[lowered.Length];
        int length = 0;
        foreach (char c in lowered)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = c;
        }

        string normalized = new(buffer[..length]);

        // Sondaki genel sözcükler ("... Laptop GPU", "... Graphics") aynı kartın farklı yazımlarında
        // olabilir de olmayabilir de; hepsi düşene kadar tekrar tekrar ayıklarız.
        bool trimmed = true;
        while (trimmed)
        {
            trimmed = false;
            foreach (string noise in NameNoise)
            {
                if (normalized.Length > noise.Length && normalized.EndsWith(noise, StringComparison.Ordinal))
                {
                    normalized = normalized[..^noise.Length];
                    trimmed = true;
                }
            }
        }

        return normalized;
    }

    private static readonly string[] TrademarkMarks = ["(tm)", "(r)", "(c)", "™", "®", "©"];
    private static readonly string[] NameNoise = ["graphics", "gpu", "laptop"];

    /// <summary>
    /// Bir kart adı için okumayı bulur. Tam eşleşme yoksa, adlardan biri diğerini kapsıyorsa onu kabul eder;
    /// böylece "rtx4060" ile "rtx4060laptopgpu" gibi farklı ayrıntı seviyeleri de eşleşir.
    /// </summary>
    public static GpuReading? Match(IReadOnlyDictionary<string, GpuReading> readings, string hardwareName)
    {
        if (readings.Count == 0)
            return null;

        string key = NormalizeName(hardwareName);
        if (readings.TryGetValue(key, out GpuReading? exact))
            return exact;

        GpuReading? best = null;
        int bestLength = 0;
        foreach ((string candidate, GpuReading reading) in readings)
        {
            if (candidate.Length == 0 || key.Length == 0)
                continue;

            bool related = candidate.Contains(key, StringComparison.Ordinal)
                || key.Contains(candidate, StringComparison.Ordinal);

            if (related && candidate.Length > bestLength)
            {
                best = reading;
                bestLength = candidate.Length;
            }
        }

        return best;
    }
}
