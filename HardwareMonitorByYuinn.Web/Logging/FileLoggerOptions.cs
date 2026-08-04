namespace HardwareMonitorByYuinn.Web.Logging;

/// <summary>Dosya loglayıcısının davranışını belirleyen ayarlar. <c>appsettings.json</c> içindeki "Logging:File" bölümünden okunur.</summary>
public sealed class FileLoggerOptions
{
    /// <summary>Log dosyalarının yazılacağı klasör. Göreli verilirse uygulamanın çalıştığı klasöre göre çözümlenir.</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>Dosya adının tarih ekinden önceki sabit kısmı.</summary>
    public string FileNamePrefix { get; set; } = "hardwaremonitor";

    /// <summary>Kaç günlük log saklanacağı. Daha eski dosyalar otomatik silinir.</summary>
    public int RetainedFileCountLimit { get; set; } = 14;

    /// <summary>Tek bir dosyanın büyüyebileceği en fazla boyut. Aşılırsa aynı gün için yeni bir parça açılır.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Kuyrukta bekleyebilecek en fazla kayıt. Dolduğunda yeni kayıtlar atılır; loglama yüzünden
    /// uygulamanın yavaşlaması ya da belleğin şişmesi, birkaç satır log kaybetmekten daha kötüdür.
    /// </summary>
    public int MaxQueuedMessages { get; set; } = 4096;
}
