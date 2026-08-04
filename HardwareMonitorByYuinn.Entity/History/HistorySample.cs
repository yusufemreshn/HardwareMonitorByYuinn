namespace HardwareMonitorByYuinn.Entity.History;

/// <summary>
/// Kalıcı geçmişte saklanan tek bir dakikalık örnek. Ham (saniyelik) veri değil, o dakika boyunca
/// gelen tüm ölçümlerin ortalamasıdır; disk kullanımını makul tutmak için per-çekirdek ve GPU sıcak
/// nokta gibi ayrıntılar burada tutulmaz (bunlar hâlâ canlı 15 dakikalık TimeSeriesStore'da mevcuttur).
/// </summary>
public sealed class HistorySample
{
    public required DateTime TimestampUtc { get; init; }
    public double? CpuUsagePercent { get; init; }
    public double? CpuClockMhz { get; init; }
    public double? CpuPowerWatts { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? GpuUsagePercent { get; init; }
    public double? GpuCoreClockMhz { get; init; }
    public double? GpuCoreTemperatureC { get; init; }
    public double? GpuPowerWatts { get; init; }
    public double? RamUsagePercent { get; init; }
    public double? FpsValue { get; init; }
    public double? NetworkDownloadBytesPerSec { get; init; }
    public double? NetworkUploadBytesPerSec { get; init; }
}
