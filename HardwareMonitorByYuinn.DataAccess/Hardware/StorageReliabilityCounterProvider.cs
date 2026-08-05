using System.Management;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

internal sealed record StorageReliabilityInfo(string Name, int? LifePercent, ulong? PowerOnHours);

/// <summary>
/// Ömür yüzdesi ve güç açılma süresi için Windows Depolama Yönetimi WMI sınıfına
/// (<c>MSFT_PhysicalDisk.GetStorageReliabilityCounter</c> → <c>MSFT_StorageReliabilityCounter</c>)
/// bağımsız bir ikinci kaynak. <see cref="DiskSmartInfoProvider"/> (DiskInfoToolkit ile ham SMART/NVMe
/// log sayfasını doğrudan okur) bazı denetleyicilerde bu değerleri okuyamıyor veya geçersiz üretiyor
/// (bkz. DiskSmartInfoProvider'daki 0-100 aralık doğrulaması); burası ise aynı bilgiyi Windows'un
/// storage sürücü çatısının zaten normalize ettiği farklı bir yoldan sağlıyor — ikisi aynı anda aynı
/// denetleyicide başarısız olma ihtimali tek bir kaynaktan daha düşük. LibreHardwareReader bu sonucu
/// yalnızca DiskSmartInfoProvider'ın değeri yoksa/geçersizse yedek olarak kullanır. Bu WMI sınıfı da
/// (Wear alanı) her sürücü/sürücü yazılımı tarafından doldurulmuyor; o durumda burası da null döner.
/// Sonuç, adına göre bir donanım girdisiyle eşleştirilir (bkz. <see cref="Match"/>),
/// <see cref="PhysicalDiskInfoProvider"/> ile aynı normalize-ve-kapsama mantığıyla.
/// </summary>
internal sealed class StorageReliabilityCounterProvider(ILogger logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = logger;
    private readonly object _gate = new();
    private IReadOnlyList<StorageReliabilityInfo>? _cached;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public IReadOnlyList<StorageReliabilityInfo> Read()
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
                return _cached;

            var results = new List<StorageReliabilityInfo>();
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery("SELECT FriendlyName FROM MSFT_PhysicalDisk"));

                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using var disk = (ManagementObject)item;
                    if (disk["FriendlyName"] is not string name || string.IsNullOrWhiteSpace(name))
                        continue;

                    try
                    {
                        using ManagementBaseObject output = disk.InvokeMethod("GetStorageReliabilityCounter", null, null);
                        if (output?["Counter"] is not ManagementBaseObject counter)
                            continue;

                        int? life = counter["Wear"] is { } wearRaw ? 100 - Convert.ToInt32(wearRaw) : null;
                        if (life is < 0 or > 100)
                            life = null;

                        ulong? powerOnHours = counter["PowerOnHours"] is { } hoursRaw && Convert.ToUInt64(hoursRaw) > 0
                            ? Convert.ToUInt64(hoursRaw)
                            : null;

                        if (life is null && powerOnHours is null)
                            continue;

                        results.Add(new StorageReliabilityInfo(name.Trim(), life, powerOnHours));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Disk} için Windows Depolama Güvenilirlik Sayacı okunamadı", name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Windows Depolama Güvenilirlik Sayacı (MSFT_StorageReliabilityCounter) okunamadı");
            }

            _cached = results;
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private static string NormalizeName(string name) =>
        new string([.. name.Where(char.IsLetterOrDigit)]).ToLowerInvariant();

    /// <summary>Adlardan biri diğerini kapsıyorsa eşleşme kabul edilir (LibreHardwareMonitor ve WMI aynı diski farklı ayrıntıda adlandırabilir).</summary>
    public static StorageReliabilityInfo? Match(IReadOnlyList<StorageReliabilityInfo> disks, string hardwareName)
    {
        string key = NormalizeName(hardwareName);
        if (key.Length == 0)
            return null;

        StorageReliabilityInfo? best = null;
        int bestLength = 0;
        foreach (StorageReliabilityInfo disk in disks)
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
