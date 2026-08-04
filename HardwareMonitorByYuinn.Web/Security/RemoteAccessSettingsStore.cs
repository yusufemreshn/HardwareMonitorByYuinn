using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HardwareMonitorByYuinn.Web.Security;

/// <summary>
/// <c>remote-access.json</c> dosyasını okur/yazar. Program.cs, DI konteyneri kurulmadan ÖNCE
/// (Kestrel bağlanma adresine karar vermek için) bunu çağırdığından, bu sınıf kasıtlı olarak
/// statik ve bağımlılıksızdır (herhangi bir servis enjekte edilmez, düz dosya G/Ç yapılır).
/// </summary>
public static class RemoteAccessSettingsStore
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "remote-access.json");
    private static readonly object SyncRoot = new();

    public static RemoteAccessOptions Load()
    {
        lock (SyncRoot)
        {
            if (!File.Exists(FilePath))
            {
                return new RemoteAccessOptions();
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<RemoteAccessOptions>(json) ?? new RemoteAccessOptions();
            }
            catch
            {
                return new RemoteAccessOptions();
            }
        }
    }

    public static void Save(RemoteAccessOptions options)
    {
        lock (SyncRoot)
        {
            string json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });

            // Doğrudan FilePath'e yazmak yerine önce yanına geçici bir dosyaya yazılıp sonra
            // yerine taşınır. Ani kapanma (elektrik kesintisi, taskkill) tam File.WriteAllText
            // sırasında olursa yarım/bozuk bir JSON kalabilir; bir sonraki Load() bunu parse
            // edemeyip sessizce varsayılana (Enabled=false, PIN yok) düşer — kullanıcı PIN'ini
            // fark etmeden kaybeder. Geçici dosya + Move/Replace, NTFS'te atomik bir yeniden
            // adlandırma olduğundan yarım yazma ihtimalini ortadan kaldırır: kesinti olursa ya
            // eski (tam) dosya ya da hiç değişmemiş hâliyle kalır, asla yarım bir dosya olmaz.
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(FilePath))
            {
                File.Replace(tempPath, FilePath, null);
            }
            else
            {
                File.Move(tempPath, FilePath);
            }
        }
    }

    private const int Pbkdf2Iterations = 210_000;
    private const int Pbkdf2OutputBytes = 32;

    /// <summary>Yeni bir PIN kaydedilirken çağrılır — her PIN için rastgele, tek kullanımlık bir tuz üretir.</summary>
    public static string GenerateSalt()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>
    /// PIN'i verilen tuzla PBKDF2-SHA256 (210.000 tur) ile özetler. Tek turlu, tuzsuz SHA-256'nın
    /// aksine kaba kuvvet/gökkuşağı tablosu saldırılarını hesaplama maliyetiyle yavaşlatır.
    /// </summary>
    public static string HashPin(string pin, string saltHex)
    {
        byte[] salt = Convert.FromHexString(saltHex);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, Pbkdf2OutputBytes);
        return Convert.ToHexString(hash);
    }
}
