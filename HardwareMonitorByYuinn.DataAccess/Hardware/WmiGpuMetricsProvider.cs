using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>Tek bir ekran kartının Windows performans sayaçlarından okunan değerleri.</summary>
internal sealed record WmiGpuReading(string Luid, double? LoadPercent, double? MemoryUsedMb);

/// <summary>
/// Ekran kartı kullanımını ve bellek tüketimini Windows'un kendi GPU performans sayaçlarından okur.
/// Görev Yöneticisi de aynı sayaçları kullanır; satıcıdan bağımsızdır ve sürücü gerektirmez.
/// </summary>
internal sealed partial class WmiGpuMetricsProvider(ILogger logger)
{
    private readonly ILogger _logger = logger;

    // Bu sağlayıcı saniyede bir çağrılır; başarısızlık nedeni kalıcı olduğu için yığın izlemesini
    // yalnızca ilk seferde yazarız, yoksa log dosyası kısa sürede şişer.
    private bool _loadFailureLogged;
    private bool _memoryFailureLogged;

    // Örnek adları: "pid_1234_luid_0x00000000_0x0000C4E7_phys_0_eng_1_engtype_3D"
    [GeneratedRegex(@"luid_0x([0-9A-Fa-f]{8})_0x([0-9A-Fa-f]{8})", RegexOptions.Compiled)]
    private static partial Regex LuidRegex();

    [GeneratedRegex(@"_eng_(\d+)_engtype_(\w+)", RegexOptions.Compiled)]
    private static partial Regex EngineRegex();

    [GeneratedRegex(@"^pid_(\d+)_", RegexOptions.Compiled)]
    private static partial Regex ProcessIdRegex();

    public (IReadOnlyDictionary<string, WmiGpuReading> PerAdapter, IReadOnlyDictionary<int, double> PerProcessLoadPercent) Read()
    {
        (Dictionary<string, double> load, Dictionary<int, double> perProcess) = ReadEngineUtilization();
        Dictionary<string, double> memory = ReadDedicatedMemoryMb();

        var result = new Dictionary<string, WmiGpuReading>(StringComparer.OrdinalIgnoreCase);
        foreach (string luid in load.Keys.Union(memory.Keys))
        {
            result[luid] = new WmiGpuReading(
                luid,
                load.TryGetValue(luid, out double l) ? l : null,
                memory.TryGetValue(luid, out double m) ? m : null);
        }

        return (result, perProcess);
    }

    /// <summary>
    /// Kart başına ve süreç başına GPU kullanım yüzdesi — ikisi de aynı sayaç satırlarından (bir
    /// satır hem LUID hem çoğunlukla PID taşır) TEK bir WMI taramasında çıkarılır; ayrı ayrı
    /// sorgulamak saniyede bir gereksiz ikinci bir tarama anlamına gelirdi. Her motor (3D, Copy,
    /// VideoDecode...) ayrı sayılır; bir kartın/sürecin farklı motorlardaki payları toplanmaz, en
    /// yükseği alınır — Görev Yöneticisi'nin "GPU %" değeri de bu şekilde hesaplanır (motorlar
    /// paralel çalıştığı için toplamak yanıltıcı olurdu).
    /// </summary>
    private (Dictionary<string, double> PerAdapter, Dictionary<int, double> PerProcess) ReadEngineUtilization()
    {
        var perAdapterEngine = new Dictionary<(string Luid, string Engine), double>();
        var perProcessEngine = new Dictionary<(int Pid, string Engine), double>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item["Name"] is not string name || item["UtilizationPercentage"] is not { } raw)
                    continue;

                double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                Match engine = EngineRegex().Match(name);
                string engineKey = engine.Success ? $"{engine.Groups[1].Value}:{engine.Groups[2].Value}" : "unknown";

                if (ExtractLuid(name) is { } luid)
                {
                    var adapterKey = (luid, engineKey);
                    perAdapterEngine[adapterKey] = perAdapterEngine.GetValueOrDefault(adapterKey) + value;
                }

                Match pidMatch = ProcessIdRegex().Match(name);
                if (pidMatch.Success)
                {
                    int pid = int.Parse(pidMatch.Groups[1].Value);
                    var processKey = (pid, engineKey);
                    perProcessEngine[processKey] = perProcessEngine.GetValueOrDefault(processKey) + value;
                }
            }
        }
        catch (Exception ex)
        {
            if (!_loadFailureLogged)
            {
                _loadFailureLogged = true;
                _logger.LogWarning(ex, "Ekran kartı kullanımı WMI performans sayaçlarından okunamadı");
            }

            return ([], []);
        }

        var perAdapter = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (((string luid, _), double value) in perAdapterEngine)
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (clamped > perAdapter.GetValueOrDefault(luid))
                perAdapter[luid] = clamped;
        }

        var perProcess = new Dictionary<int, double>();
        foreach (((int pid, _), double value) in perProcessEngine)
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (clamped > perProcess.GetValueOrDefault(pid))
                perProcess[pid] = clamped;
        }

        return (perAdapter, perProcess);
    }

    private Dictionary<string, double> ReadDedicatedMemoryMb()
    {
        var perAdapter = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item["Name"] is not string name)
                    continue;

                string? luid = ExtractLuid(name);
                if (luid is null)
                    continue;

                // Ayrık kartlarda kullanılan bellek ayrılmış VRAM'dir; tümleşik kartlarda ayrılmış bellek
                // olmadığı için sistem belleğinden paylaşılan miktar anlamlı olan değerdir.
                double dedicated = ToDouble(item["DedicatedUsage"]);
                double shared = ToDouble(item["SharedUsage"]);
                double bytes = dedicated > 0 ? dedicated : shared;
                if (bytes <= 0)
                    continue;

                perAdapter[luid] = perAdapter.GetValueOrDefault(luid) + bytes / 1024d / 1024d;
            }
        }
        catch (Exception ex)
        {
            if (!_memoryFailureLogged)
            {
                _memoryFailureLogged = true;
                _logger.LogWarning(ex, "Ekran kartı bellek kullanımı WMI performans sayaçlarından okunamadı");
            }
        }

        return perAdapter;
    }

    private static double ToDouble(object? value) =>
        value is null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    /// <summary>Sayaç örnek adındaki LUID'i D3DKMT tarafıyla aynı biçime getirir, böylece iki kaynak eşleştirilebilir.</summary>
    private static string? ExtractLuid(string instanceName)
    {
        Match match = LuidRegex().Match(instanceName);
        if (!match.Success)
            return null;

        return $"0x{match.Groups[1].Value.ToLowerInvariant()}_0x{match.Groups[2].Value.ToLowerInvariant()}";
    }
}
