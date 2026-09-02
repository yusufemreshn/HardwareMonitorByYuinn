using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.History;

/// <summary>
/// Uygulama beklenmedik şekilde (ör. bir çökme, elektrik kesintisi, "görevi sonlandır") öldüğünde WAL
/// dosyaları yarım kalabilir; bu genelde SQLite'ın kendi WAL kurtarma mekanizmasıyla sorunsuz açılır,
/// ama nadiren (2026-08-27 gecesi canlı olayda gözlemlendiği gibi) eşlik eden "-shm" dosyası WAL ile
/// tutarsız kalıp her açılışta "disk I/O error" fırlatır; bu, <see cref="SqliteHistoryStore"/>'un
/// kurucusunu (dolayısıyla Host.StartAsync'i, dolayısıyla TÜM uygulamayı) çökertir; kullanıcı açılış
/// ekranının belirip kaybolmasından başka bir şey görmez ve elle müdahale (dosyaları silme) gerekir.
///
/// Bu sınıf, ASP.NET Core host'u hiç kurulmadan ÖNCE (bkz. Desktop/DesktopShellRunner.cs) her .db
/// dosyasını hızlıca sağlık kontrolünden geçirir; sorun varsa o gece elle uygulanan adımların aynısını
/// (önce yalnızca "-shm" sil, olmazsa "-wal"ı da sil, o da olmazsa dosyayı kenara ayırıp sıfırdan
/// başlat) otomatik dener, böylece uygulama kendi kendine ayağa kalkar, tekrar elle müdahaleye gerek
/// kalmaz.
/// </summary>
public static class HistoryDatabaseRepair
{
    public readonly record struct StepResult(string FileName, string Status);

    /// <summary>
    /// Klasördeki her ".db" dosyasını kontrol eder. Hepsi sağlamsa (olağan durum) <paramref
    /// name="onProgress"/> HİÇ çağrılmaz; çağıran taraf (bkz. DesktopShellRunner) bunu, bir onarım
    /// arayüzünü yalnızca gerçekten gerektiğinde göstermek için kullanır. Onarım denenen dosyalar için
    /// dönen listede yer alır; sonuç listesi boşsa hiçbir dosya sorunlu değildi demektir.
    /// </summary>
    public static IReadOnlyList<StepResult> RepairIfNeeded(
        string historyDirectory,
        ILogger? logger = null,
        Action<string, int>? onProgress = null,
        string repairingMessageTemplate = "{0} bozuk görünüyor, onarılıyor…",
        string completedMessage = "Tamamlandı")
    {
        var results = new List<StepResult>();
        if (!Directory.Exists(historyDirectory))
            return results;

        string[] dbFiles = Directory.GetFiles(historyDirectory, "*.db");
        for (int i = 0; i < dbFiles.Length; i++)
        {
            string dbPath = dbFiles[i];
            string name = Path.GetFileName(dbPath);
            int basePercent = dbFiles.Length == 0 ? 0 : (i * 100) / dbFiles.Length;

            if (IsHealthy(dbPath))
                continue;

            logger?.LogWarning("{Db} sağlık kontrolünden geçemedi, otomatik onarım deneniyor", name);
            onProgress?.Invoke(string.Format(repairingMessageTemplate, name), basePercent);

            string status = TryRepair(dbPath, logger);
            results.Add(new StepResult(name, status));
        }

        if (results.Count > 0)
            onProgress?.Invoke(completedMessage, 100);

        return results;
    }

    private static bool IsHealthy(string dbPath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() && reader.GetString(0) == "ok";
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static string TryRepair(string dbPath, ILogger? logger)
    {
        // Microsoft.Data.Sqlite bağlantıları varsayılan olarak havuzlanır; IsHealthy() başarısız
        // olduktan sonra bile alttaki yerel dosya tutamacı havuzda açık kalabilir ve aşağıdaki
        // silme/taşıma denemelerini "dosya kullanımda" hatasıyla sessizce başarısız kılabilir.
        SqliteConnection.ClearAllPools();

        // Canlı olayda tek başına yeterli olan adım: eski "-shm" (paylaşılan bellek index'i) WAL ile
        // tutarsız kalmış olabilir; onu silip yeniden açmak SQLite'ın WAL'ı normal şekilde (veri kaybı
        // olmadan) yeniden oynatmasına izin veriyor.
        string shmPath = dbPath + "-shm";
        if (File.Exists(shmPath))
        {
            TryDelete(shmPath, logger);
            if (IsHealthy(dbPath))
            {
                logger?.LogInformation("{Db} onarıldı (eski -shm dosyası silindi, WAL yeniden oynatıldı)", Path.GetFileName(dbPath));
                return "Onarıldı";
            }
        }

        // Yetmediyse WAL'ın kendisini de atıp son checkpoint'teki hâline dön; bir miktar (WAL'daki
        // en son yazılmamış dakikalar) veri kaybı olur ama uygulama en azından açılabilir.
        string walPath = dbPath + "-wal";
        if (File.Exists(walPath))
        {
            TryDelete(walPath, logger);
            if (IsHealthy(dbPath))
            {
                logger?.LogWarning("{Db} onarıldı (WAL atıldı, son checkpoint'e dönüldü; yakın zamandaki bazı kayıtlar kaybolmuş olabilir)", Path.GetFileName(dbPath));
                return "Onarıldı (yakın kayıtlar kaybolmuş olabilir)";
            }
        }

        // Dosyanın kendisi de bozuksa kurtarma yolu yok; kenara ayırıp uygulamanın sıfırdan boş bir
        // dosya oluşturmasına izin ver, en azından uygulama bir daha hiç açılamaz duruma düşmesin.
        SqliteConnection.ClearAllPools();
        string backupPath = $"{dbPath}.corrupted-{DateTime.Now:yyyyMMddHHmmss}";
        TryMove(dbPath, backupPath, logger);
        logger?.LogError("{Db} kurtarılamadı, kenara ayrıldı ({Backup}) ve sıfırdan oluşturulacak", Path.GetFileName(dbPath), backupPath);
        return "Kurtarılamadı, sıfırlandı";
    }

    private static void TryDelete(string path, ILogger? logger)
    {
        try { File.Delete(path); }
        catch (Exception ex) { logger?.LogWarning(ex, "{Path} silinemedi", path); }
    }

    private static void TryMove(string source, string destination, ILogger? logger)
    {
        try { File.Move(source, destination, overwrite: true); }
        catch (Exception ex) { logger?.LogWarning(ex, "{Source} kenara ayrılamadı", source); }
    }
}
