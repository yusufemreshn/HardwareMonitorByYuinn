using DiskInfoToolkit;
using DiskInfoToolkit.Enums.Interop;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

internal sealed record DiskSmartInfo(
    string Name,
    string? Status,
    int? LifePercent,
    ulong? PowerOnHours,
    double? HostReadsGb,
    double? HostWritesGb);

/// <summary>
/// SMART sağlık verisini (genel durum, kalan ömür, güç açılma saati, toplam okuma/yazma) LibreHardwareMonitor
/// değil, DiskInfoToolkit üzerinden okur — LibreHardwareMonitor bu verileri sensör olarak sunmuyor.
/// Ham SMART sorgusu diğer sensörlere göre daha pahalı olduğundan <see cref="CacheDuration"/> boyunca
/// önbelleğe alınır. Sonuç, adına göre bir donanım girdisiyle eşleştirilir (bkz. <see cref="Match"/>),
/// <see cref="PhysicalDiskInfoProvider"/> ile aynı normalize-ve-kapsama mantığıyla.
/// </summary>
internal sealed class DiskSmartInfoProvider(ILogger logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = logger;
    private readonly object _gate = new();
    private IReadOnlyList<DiskSmartInfo>? _cached;
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private bool _reloaded;

    public IReadOnlyList<DiskSmartInfo> Read()
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
                return _cached;

            var results = new List<DiskSmartInfo>();
            try
            {
                // İlk çağrıda disk listesi taranır; sonraki çağrılarda yalnızca mevcut disklerin
                // (Storage.Update ile) SMART verisi tazelenir — donanım değişikliği (disk takma/çıkarma)
                // beklenmediğinden her turda yeniden taramaya gerek yok.
                if (!_reloaded)
                {
                    StorageManager.ReloadStorages();
                    _reloaded = true;
                }

                foreach (Storage storage in StorageManager.Storages)
                {
                    try
                    {
                        storage.Update();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Disk} için SMART verisi tazelenemedi", storage.Model);
                        continue;
                    }

                    SmartInfo smart = storage.Smart;
                    string? status = smart.DiskStatus switch
                    {
                        DiskStatus.Good => "İyi",
                        DiskStatus.Caution => "Dikkat",
                        DiskStatus.Bad => "Kötü",
                        _ => null
                    };

                    ulong? powerOnHours = smart.DetectedPowerOnHours > 0
                        ? smart.DetectedPowerOnHours
                        : smart.MeasuredPowerOnHours > 0 ? smart.MeasuredPowerOnHours : null;

                    results.Add(new DiskSmartInfo(
                        storage.Model ?? storage.DeviceID ?? "Disk",
                        status,
                        smart.Life,
                        powerOnHours,
                        smart.HostReads.HasValue ? (double)smart.HostReads.Value : null,
                        smart.HostWrites.HasValue ? (double)smart.HostWrites.Value : null));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SMART disk bilgisi okunamadı");
            }

            _cached = results;
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static string NormalizeName(string name) =>
        new string([.. name.Where(char.IsLetterOrDigit)]).ToLowerInvariant();

    /// <summary>Adlardan biri diğerini kapsıyorsa eşleşme kabul edilir (LibreHardwareMonitor ve DiskInfoToolkit aynı diski farklı ayrıntıda adlandırabilir).</summary>
    public static DiskSmartInfo? Match(IReadOnlyList<DiskSmartInfo> disks, string hardwareName)
    {
        string key = NormalizeName(hardwareName);
        if (key.Length == 0)
            return null;

        DiskSmartInfo? best = null;
        int bestLength = 0;
        foreach (DiskSmartInfo disk in disks)
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
