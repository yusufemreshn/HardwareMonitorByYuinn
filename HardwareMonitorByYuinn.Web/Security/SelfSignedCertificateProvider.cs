using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HardwareMonitorByYuinn.Web.Security;

/// <summary>
/// Ayarlar sayfasındaki "HTTPS kullan" anahtarı açıldığında Kestrel'in LAN dinleyicisine (5251)
/// takılacak kendinden imzalı sertifikayı üretir/saklar. Gerçek bir sertifika otoritesi (Let's
/// Encrypt vb.) özel IP adresleri (192.168.x.x gibi) için sertifika vermediğinden, bu proje için
/// tek pratik yol budur; bağlanan her cihazda tarayıcı ilk seferde "bağlantı güvenli değil"
/// uyarısı gösterir — bu, Ayarlar sayfasında kullanıcıya açıkça belirtilir.
///
/// Sertifika yeniden başlatmalar arasında (disk üzerinde, remote-access-cert.pfx) kalıcı olmalı;
/// aksi hâlde her açılışta yeni bir sertifika üretilir ve daha önce cihazında "her zaman güven"
/// demiş bir kullanıcıda uyarı her seferinde geri gelirdi.
/// </summary>
internal static class SelfSignedCertificateProvider
{
    private static readonly string CertPath = Path.Combine(AppContext.BaseDirectory, "remote-access-cert.pfx");

    // Bu parola dosyayı gerçek bir sırdan korumuyor (.pfx dosyasına erişebilen zaten özel anahtara
    // da erişmiş olur) — yalnızca X509Certificate2'nin şart koştuğu API'yi karşılıyor.
    private const string CertPassword = "hwmon-local";

    public static X509Certificate2 GetOrCreate()
    {
        return TryLoadValid() ?? CreateAndSave();
    }

    /// <summary>
    /// Ayarlar sayfasındaki "Sertifikayı Yeniden Oluştur" butonu tarafından çağrılır — ör. ev/ofis
    /// ağı değişip LAN IP'si değiştiğinde, eski sertifikanın SAN listesindeki IP'ler artık geçersiz
    /// kaldığında (tarayıcı bu durumda basit bir "güvenmiyorum" uyarısından daha sert, atlanması
    /// zor bir "adres uyuşmuyor" hatası gösterebilir). Silme işleminden sonra bir sonraki açılışta
    /// GetOrCreate() güncel IP'lerle yeni bir sertifika üretir.
    /// </summary>
    public static void DeleteExisting()
    {
        try { File.Delete(CertPath); } catch { /* zaten yoksa yapacak bir şey yok */ }
    }

    private static X509Certificate2? TryLoadValid()
    {
        if (!File.Exists(CertPath))
        {
            return null;
        }

        try
        {
            X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(CertPath, CertPassword, X509KeyStorageFlags.Exportable);
            return cert.NotAfter > DateTime.Now.AddDays(7) ? cert : null;
        }
        catch
        {
            // Bozuk/okunamayan bir dosya kalmışsa (ör. yarım yazma) yeniden üretilir.
            return null;
        }
    }

    private static X509Certificate2 CreateAndSave()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=HardwareMonitorByYuinn", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], critical: false)); // sunucu kimlik doğrulaması

        // Modern tarayıcılar CN'i değil SAN'ı esas alır; bağlanılabilecek her adres (loopback +
        // şu an algılanan tüm LAN IP'leri) burada olmalı, aksi hâlde "adres uyuşmuyor" hatası alınır.
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        foreach (string ip in LanAddressHelper.GetIPv4Addresses())
        {
            sanBuilder.AddIpAddress(IPAddress.Parse(ip));
        }
        request.CertificateExtensions.Add(sanBuilder.Build());

        using X509Certificate2 cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        byte[] pfxBytes = cert.Export(X509ContentType.Pfx, CertPassword);
        File.WriteAllBytes(CertPath, pfxBytes);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, CertPassword, X509KeyStorageFlags.Exportable);
    }
}
