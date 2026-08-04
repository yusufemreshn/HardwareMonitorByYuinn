namespace HardwareMonitorByYuinn.Web.Security;

/// <summary>
/// "Yerel ağa açma" ayarı. Kestrel'in hangi adrese bağlanacağına (127.0.0.1 ya da 0.0.0.0) karar
/// vermek için <c>Program.cs</c>'de <c>builder.Build()</c>'dan ÖNCE okunur; bu yüzden appsettings.json
/// yerine kendi küçük dosyasında (<c>remote-access.json</c>) tutulur ve düz bir dosya okumasıyla
/// yüklenir (DI konteynerine ihtiyaç duymadan).
/// </summary>
public sealed class RemoteAccessOptions
{
    public bool Enabled { get; set; }

    /// <summary>PIN'in kendisi hiçbir zaman diskte düz metin olarak tutulmaz, yalnızca PBKDF2 özeti.</summary>
    public string? PinHash { get; set; }

    /// <summary>PinHash'i üretmek için kullanılan, PIN başına rastgele üretilen tuz (hex).</summary>
    public string? PinSalt { get; set; }

    /// <summary>
    /// Açıksa LAN erişimi düz HTTP (5250) yerine kendinden imzalı bir sertifikayla HTTPS (5251)
    /// üzerinden sunulur (bkz. SelfSignedCertificateProvider). Sahibinin kendi bilgisayarından
    /// (loopback, 127.0.0.1:5250) erişimini etkilemez.
    /// </summary>
    public bool HttpsEnabled { get; set; }
}

/// <summary>
/// Uygulama başlarken (Program.cs, builder.Build()'dan önce) okunan ayarın anlık görüntüsü.
/// Ayarlar sayfasında "şu an etkin mi" ile "kaydedilen ama henüz yeniden başlatılmamış" ayrımını
/// göstermek için kullanılır.
/// </summary>
public sealed record RemoteAccessStartupSnapshot(bool Enabled, bool PinRequired, bool HttpsEnabled);
