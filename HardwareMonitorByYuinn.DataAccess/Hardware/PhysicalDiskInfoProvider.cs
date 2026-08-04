using System.Management;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

internal sealed record PhysicalDiskInfo(string Name, double TotalGb, string MediaType);

/// <summary>
/// Fiziksel disklerin toplam kapasitesini ve türünü (SSD/HDD) LibreHardwareMonitor değil, Windows
/// Depolama Yönetimi WMI sınıfından (<c>MSFT_PhysicalDisk</c>) okur — bu bilgi sensör olarak mevcut
/// değildir. Sonuç, adına göre bir donanım girdisiyle eşleştirilir (bkz. <see cref="Match"/>).
/// Kapasite ve tür neredeyse hiç değişmediği için bir süre önbelleğe alınır; ama kalıcı değil —
/// uygulama açıkken bir harici disk takılıp çıkarılırsa (bu durumda SystemInfo.Drives her istekte
/// taze okunduğundan iki kaynak arasında tutarsızlık oluşmasın diye) en geç <see cref="CacheDuration"/>
/// içinde yeniden sorgulanır.
/// </summary>
internal sealed class PhysicalDiskInfoProvider(ILogger logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = logger;
    private readonly object _gate = new();
    private IReadOnlyList<PhysicalDiskInfo>? _cached;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public IReadOnlyList<PhysicalDiskInfo> Read()
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
                return _cached;

            var results = new List<PhysicalDiskInfo>();
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery("SELECT FriendlyName, Size, MediaType FROM MSFT_PhysicalDisk"));

                foreach (ManagementBaseObject item in searcher.Get())
                {
                    if (item["FriendlyName"] is not string name || string.IsNullOrWhiteSpace(name) || item["Size"] is not { } sizeRaw)
                        continue;

                    ulong bytes = Convert.ToUInt64(sizeRaw);
                    if (bytes == 0)
                        continue;

                    // MediaType: 0=Belirsiz, 3=HDD, 4=SSD, 5=SCM (bkz. MSFT_PhysicalDisk belgeleri).
                    ushort mediaTypeRaw = item["MediaType"] is { } m ? Convert.ToUInt16(m) : (ushort)0;
                    string mediaType = mediaTypeRaw switch
                    {
                        4 => "SSD",
                        3 => "HDD",
                        _ => "Depolama"
                    };

                    results.Add(new PhysicalDiskInfo(name.Trim(), bytes / 1024d / 1024d / 1024d, mediaType));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fiziksel disk kapasitesi/türü WMI üzerinden okunamadı");
            }

            _cached = results;
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static string NormalizeName(string name) =>
        new string([.. name.Where(char.IsLetterOrDigit)]).ToLowerInvariant();

    /// <summary>Adlardan biri diğerini kapsıyorsa eşleşme kabul edilir (LibreHardwareMonitor ve WMI aynı diski farklı ayrıntıda adlandırabilir).</summary>
    public static PhysicalDiskInfo? Match(IReadOnlyList<PhysicalDiskInfo> disks, string hardwareName)
    {
        string key = NormalizeName(hardwareName);
        if (key.Length == 0)
            return null;

        PhysicalDiskInfo? best = null;
        int bestLength = 0;
        foreach (PhysicalDiskInfo disk in disks)
        {
            string diskKey = NormalizeName(disk.Name);
            if (diskKey.Length == 0)
                continue;

            bool related = diskKey.Contains(key, StringComparison.Ordinal) || key.Contains(diskKey, StringComparison.Ordinal);
            if (related && diskKey.Length > bestLength)
            {
                best = disk;
                bestLength = diskKey.Length;
            }
        }

        return best;
    }
}
