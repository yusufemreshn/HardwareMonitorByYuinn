namespace HardwareMonitorByYuinn.Entity.Hardware;

/// <summary>
/// Tüm ağ adaptörlerinin (Wi-Fi, Ethernet vb.) toplam indirme/yükleme hızı. Adaptör bazında değil,
/// tek bir toplam olarak tutulur; tipik kullanım "internet hızım ne kadar" sorusuna cevap vermektir.
/// </summary>
public sealed class NetworkSnapshot
{
    public double? DownloadBytesPerSec { get; init; }
    public double? UploadBytesPerSec { get; init; }
    public string? AdapterName { get; init; }
    public double? LinkSpeedMbps { get; init; }
    public double TotalDownloadedGb { get; init; }
    public double TotalUploadedGb { get; init; }
}
