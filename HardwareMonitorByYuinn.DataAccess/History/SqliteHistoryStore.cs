using System.Collections.Concurrent;
using System.Globalization;
using HardwareMonitorByYuinn.Entity.Hardware;
using HardwareMonitorByYuinn.Entity.History;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.DataAccess.History;

/// <summary>
/// Ortalamayı biriktirip dakikada bir tek satır yazan SQLite tabanlı kalıcı geçmiş. Yazma işi,
/// FileLogWriter ile aynı sebepten (disk G/Ç'nin saniyelik anket döngüsünü asla bloklamaması için)
/// arka planda tek bir iş parçacığından yapılır; <see cref="Record"/> yalnızca bellekte biriktirir.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore, IDisposable
{
    /// <summary>Bu süreden eski satırlar otomatik silinir; veritabanının sınırsız büyümesini önler.</summary>
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    /// <summary>Budama her yazımda değil, en fazla bu sıklıkta çalışır; gereksiz taramayı önler.</summary>
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);

    /// <summary>Bundan daha kısa süren "oturumlar" (ör. yanlışlıkla alt-tab) gürültü sayılıp kaydedilmez.</summary>
    private static readonly TimeSpan MinSessionDuration = TimeSpan.FromSeconds(30);

    /// <summary>Kesintisiz bir oturum bu süreyi geçince kaydedilip aynı process için hemen yeni bir
    /// oturum başlatılır — saatlerce süren tek bir dev satır yerine, "Oyun Geçmişi" tablosunda daha
    /// okunabilir, sabit uzunlukta parçalar (en fazla 15 dk) birikir.</summary>
    private static readonly TimeSpan MaxSessionDuration = TimeSpan.FromMinutes(15);

    private readonly ILogger<SqliteHistoryStore> _logger;
    private readonly string _connectionString;

    // login_attempts, "Giriş Denemesi Özeti" panelindeki "Veritabanı boyutu" değerinin ana geçmişten
    // bağımsız/dürüst olabilmesi için kardeş bir dosyaya (login-attempts.db) taşındı — artık tüm
    // login_attempts okuma/yazma/budama işlemleri bu bağlantı string'ini kullanır.
    private readonly string _loginAttemptsConnectionString;

    // Aynı gerekçeyle (bağımsız/dürüst "veritabanı boyutu") game_sessions ve process_samples de
    // kendi dosyalarına ayrıldı. Bu ikisi, login_attempts'in aksine GERÇEK/büyük kullanıcı verisi
    // içerdiğinden (oyun geçmişi, process geçmişi) eski history.db'den terk edilmez, ilk açılışta
    // gerçekten taşınır (bkz. MigrateLegacyTableIfPresent).
    private readonly string _gameSessionsConnectionString;
    private readonly string _processSamplesConnectionString;

    private readonly BlockingCollection<(DateTime MinuteUtc, HistorySample Sample)> _queue = new();
    private readonly Thread _writer;

    // Process bazlı kaynak kullanım geçmişi (C20) — ayrı bir kuyruk/iş parçacığı kullanır çünkü
    // bir dakikada birden fazla satır (her üst process için biri) yazılması gerekir; ana `_queue`
    // tek bir HistorySample'a göre tasarlanmıştı, karıştırmamak için ayrı tutuldu.
    private readonly BlockingCollection<(DateTime MinuteUtc, Dictionary<string, ProcessMinuteAccumulator> Data)> _processQueue = new();
    private readonly Thread _processWriter;

    private readonly object _accumulatorLock = new();
    private MinuteAccumulator _current = new();
    private Dictionary<string, ProcessMinuteAccumulator> _currentProcesses = new();
    private DateTime? _currentMinuteUtc;

    private readonly object _sessionLock = new();
    private string? _currentSessionProcessName;
    private DateTime _currentSessionStartUtc;
    private DateTime _currentSessionLastUtc;
    private RunningAverage _sessionFps;
    private RunningAverage _sessionCpuTemp;
    private RunningAverage _sessionGpuTemp;

    private DateTime _lastPruneUtc = DateTime.MinValue;
    private bool _disposed;

    public SqliteHistoryStore(ILogger<SqliteHistoryStore> logger) : this(logger, DefaultDatabasePath())
    {
    }

    /// <summary>Testlerin geçici bir dosya yolu vermesine izin veren iç kurucu.</summary>
    internal SqliteHistoryStore(ILogger<SqliteHistoryStore> logger, string databasePath)
    {
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = $"Data Source={databasePath}";

        // Ana yoldan deterministik türetilir (ör. test kurucusundaki geçici GUID'li yol) ki testler
        // var olan geçici dosya izolasyonunu bu ikinci dosya için de otomatik miras alsın.
        string directory = Path.GetDirectoryName(databasePath)!;
        string loginAttemptsPath = Path.Combine(directory, "login-attempts.db");
        _loginAttemptsConnectionString = $"Data Source={loginAttemptsPath}";
        string gameSessionsPath = Path.Combine(directory, "game-sessions.db");
        _gameSessionsConnectionString = $"Data Source={gameSessionsPath}";
        string processSamplesPath = Path.Combine(directory, "process-samples.db");
        _processSamplesConnectionString = $"Data Source={processSamplesPath}";

        InitializeSchema();
        InitializeLoginAttemptsSchema();
        InitializeGameSessionsSchema();
        InitializeProcessSamplesSchema();

        // login_attempts'in aksine bunlar GERÇEK kullanıcı verisi taşıyabilir (eski history.db'de
        // hâlâ samples ile aynı dosyada oluşturulmuş olabilir) — terk edilmez, gerçekten taşınır.
        bool migratedGameSessions = MigrateLegacyTableIfPresent(databasePath, _gameSessionsConnectionString, "game_sessions");
        bool migratedProcessSamples = MigrateLegacyTableIfPresent(databasePath, _processSamplesConnectionString, "process_samples");
        if (migratedGameSessions || migratedProcessSamples)
        {
            // DELETE/DROP tek başına dosya boyutunu küçültmez; taşınan onbinlerce satırın boşalttığı
            // yeri gerçekten geri kazanmak için bir kereliğine VACUUM edilir. Migration olmayan
            // (ikinci ve sonraki) çalıştırmalarda bu blok hiç çalışmaz, gereksiz maliyet oluşmaz.
            using SqliteConnection legacyVacuumConnection = OpenConnection();
            using (SqliteCommand vacuum = legacyVacuumConnection.CreateCommand())
            {
                vacuum.CommandText = "VACUUM;";
                vacuum.ExecuteNonQuery();
            }

            // WAL modunda VACUUM'un küçülttüğü içerik önce "-wal" dosyasına yazılır, ana .db dosyası
            // otomatik checkpoint'e kadar ESKİ (büyük) boyutunda kalır — bu da "Veritabanı boyutu"nun
            // (ana dosya + wal toplamı) migration hemen sonrasında olduğundan daha BÜYÜK görünmesine
            // yol açardı. TRUNCATE checkpoint, WAL içeriğini hemen ana dosyaya yazıp WAL'ı sıfırlar.
            using SqliteCommand checkpoint = legacyVacuumConnection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }

        _writer = new Thread(ProcessQueue) { IsBackground = true, Name = "HardwareMonitorByYuinn geçmiş yazıcı" };
        _writer.Start();

        _processWriter = new Thread(ProcessProcessQueue) { IsBackground = true, Name = "HardwareMonitorByYuinn process geçmiş yazıcı" };
        _processWriter.Start();
    }

    // AppContext.BaseDirectory (bin çıktısı) KULLANILMAZ: bir bin/obj temizliğinde (ör. kilitli DLL
    // hatasını gidermek için) bu klasör tümüyle silinip yeniden oluşturuluyor, dolayısıyla veritabanı
    // da sıfırlanıp tüm oyun geçmişi kaybolurdu (2026-07-29'da tam olarak bu yüzden Overwatch geçmişi
    // gitti). LocalApplicationData, build çıktısından bağımsız, kalıcı bir kullanıcı verisi konumudur.
    private static string DefaultDatabasePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HardwareMonitorByYuinn", "history", "history.db");

    private void InitializeSchema()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand pragma = connection.CreateCommand();
        // WAL modu, yazıcı iş parçacığı satır eklerken Dışa Aktarma sayfasının aynı anda okuma
        // yapabilmesini sağlar; aksi hâlde SQLite'ın varsayılan kilitlemesi okuyucuyu bekletirdi.
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();

        using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS samples (
                timestamp_utc TEXT PRIMARY KEY,
                cpu_usage REAL, cpu_clock REAL, cpu_power REAL, cpu_temp REAL,
                gpu_usage REAL, gpu_clock REAL, gpu_temp REAL, gpu_power REAL,
                ram_usage REAL, fps REAL, net_down REAL, net_up REAL
            );
            """;
        create.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>login-attempts.db dosyasını oluşturur, WAL modunu açar ve login_attempts tablosunu
    /// kurar. Eski/paylaşılan history.db'deki aynı isimli tabloya kasıtlı olarak dokunulmaz/migrate
    /// edilmez (bkz. bekleyen işler notu, adım 5) — orada kalan az sayıdaki test verisi kullanılmaz
    /// hâlde bırakılır, ileriye dönük tüm okuma/yazma bu yeni dosyaya gider.</summary>
    private void InitializeLoginAttemptsSchema()
    {
        using SqliteConnection connection = OpenLoginAttemptsConnection();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();

        using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS login_attempts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc TEXT NOT NULL,
                ip_address TEXT NOT NULL,
                success INTEGER NOT NULL,
                caused_lockout INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_login_attempts_ts ON login_attempts(timestamp_utc);
            """;
        create.ExecuteNonQuery();
    }

    private SqliteConnection OpenLoginAttemptsConnection()
    {
        var connection = new SqliteConnection(_loginAttemptsConnectionString);
        connection.Open();
        return connection;
    }

    private void InitializeGameSessionsSchema()
    {
        using SqliteConnection connection = OpenGameSessionsConnection();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();

        using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS game_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                process_name TEXT NOT NULL,
                start_utc TEXT NOT NULL,
                end_utc TEXT NOT NULL,
                avg_fps REAL NOT NULL,
                avg_cpu_temp REAL,
                avg_gpu_temp REAL
            );
            """;
        create.ExecuteNonQuery();
    }

    private SqliteConnection OpenGameSessionsConnection()
    {
        var connection = new SqliteConnection(_gameSessionsConnectionString);
        connection.Open();
        return connection;
    }

    private void InitializeProcessSamplesSchema()
    {
        using SqliteConnection connection = OpenProcessSamplesConnection();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();

        using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS process_samples (
                timestamp_utc TEXT NOT NULL,
                process_name TEXT NOT NULL,
                avg_cpu_percent REAL,
                avg_ram_mb REAL,
                PRIMARY KEY (timestamp_utc, process_name)
            );
            CREATE INDEX IF NOT EXISTS idx_process_samples_name_ts ON process_samples(process_name, timestamp_utc);
            """;
        create.ExecuteNonQuery();
    }

    private SqliteConnection OpenProcessSamplesConnection()
    {
        var connection = new SqliteConnection(_processSamplesConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Eski (taşımadan önceki) history.db'de hâlâ <paramref name="tableName"/> tablosu varsa, onu
    /// yeni bağımsız dosyaya gerçekten taşır (login_attempts'ten farklı olarak burada veri terk
    /// edilmez — game_sessions/process_samples gerçek/büyük kullanıcı verisi olabilir). Kopyalama
    /// ve eski tabloyu silme TEK bir transaction içinde yapılır: taskkill gibi bir kesinti yarıda
    /// kalırsa geri alınır, ne veri kaybı ne çoğalma olur — bir sonraki açılışta baştan denenir.
    /// Tablo eski dosyada zaten yoksa (önceki bir çalıştırmada taşınmışsa) hiçbir şey yapmaz; bu,
    /// aynı zamanda idempotency garantisidir. <paramref name="tableName"/> her zaman bu sınıfın
    /// kendi sabit literalleriyle çağrılır, asla dışarıdan/kullanıcıdan gelmez.
    /// </summary>
    private static bool MigrateLegacyTableIfPresent(string legacyDbPath, string newConnectionString, string tableName)
    {
        if (!File.Exists(legacyDbPath))
            return false;

        using (var legacyCheck = new SqliteConnection($"Data Source={legacyDbPath};Mode=ReadOnly"))
        {
            legacyCheck.Open();
            using SqliteCommand checkCmd = legacyCheck.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
            checkCmd.Parameters.AddWithValue("@name", tableName);
            long exists = (long)checkCmd.ExecuteScalar()!;
            if (exists == 0)
                return false; // Daha önce taşınmış (ya da hiç oluşmamış).
        }

        using var newConnection = new SqliteConnection(newConnectionString);
        newConnection.Open();

        using (SqliteCommand attach = newConnection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE @path AS legacy;";
            attach.Parameters.AddWithValue("@path", legacyDbPath);
            attach.ExecuteNonQuery();
        }

        using (SqliteTransaction tx = newConnection.BeginTransaction())
        {
            using (SqliteCommand copy = newConnection.CreateCommand())
            {
                copy.Transaction = tx;
                copy.CommandText = $"INSERT INTO {tableName} SELECT * FROM legacy.{tableName};";
                copy.ExecuteNonQuery();
            }
            using (SqliteCommand drop = newConnection.CreateCommand())
            {
                drop.Transaction = tx;
                drop.CommandText = $"DROP TABLE legacy.{tableName};";
                drop.ExecuteNonQuery();
            }
            tx.Commit();
        }

        using (SqliteCommand detach = newConnection.CreateCommand())
        {
            detach.CommandText = "DETACH DATABASE legacy;";
            detach.ExecuteNonQuery();
        }

        return true;
    }

    public void Record(HardwareSnapshot snapshot, DateTime nowUtc)
    {
        DateTime minuteUtc = new(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, nowUtc.Minute, 0, DateTimeKind.Utc);

        lock (_accumulatorLock)
        {
            if (_currentMinuteUtc is { } previousMinute && previousMinute != minuteUtc)
            {
                EnqueueFlush(previousMinute, _current);
                if (_currentProcesses.Count > 0) _processQueue.Add((previousMinute, _currentProcesses));
                _current = new MinuteAccumulator();
                _currentProcesses = new Dictionary<string, ProcessMinuteAccumulator>();
            }

            _currentMinuteUtc = minuteUtc;

            _current.CpuUsage.Add(snapshot.Cpu?.TotalLoadPercent);
            _current.CpuClock.Add(snapshot.Cpu?.PackageClockMhz);
            _current.CpuPower.Add(snapshot.Cpu?.PackagePowerWatts);
            _current.CpuTemp.Add(snapshot.Cpu?.PackageTemperatureC);
            _current.GpuUsage.Add(snapshot.Gpu?.LoadPercent);
            _current.GpuClock.Add(snapshot.Gpu?.CoreClockMhz);
            _current.GpuTemp.Add(snapshot.Gpu?.CoreTemperatureC);
            _current.GpuPower.Add(snapshot.Gpu?.PowerWatts);
            _current.RamUsage.Add(snapshot.Ram?.UsedPercent);
            _current.Fps.Add(snapshot.Fps?.FramesPerSecond);
            _current.NetDown.Add(snapshot.Network?.DownloadBytesPerSec);
            _current.NetUp.Add(snapshot.Network?.UploadBytesPerSec);

            foreach (Entity.Hardware.ProcessMetric p in snapshot.TopProcesses)
            {
                if (!_currentProcesses.TryGetValue(p.Name, out ProcessMinuteAccumulator acc))
                    acc = new ProcessMinuteAccumulator();
                acc.Cpu.Add(p.CpuPercent);
                acc.Ram.Add(p.RamMb);
                _currentProcesses[p.Name] = acc;
            }
        }
    }

    private void EnqueueFlush(DateTime minuteUtc, MinuteAccumulator accumulator)
    {
        var sample = new HistorySample
        {
            TimestampUtc = minuteUtc,
            CpuUsagePercent = accumulator.CpuUsage.Average(),
            CpuClockMhz = accumulator.CpuClock.Average(),
            CpuPowerWatts = accumulator.CpuPower.Average(),
            CpuTemperatureC = accumulator.CpuTemp.Average(),
            GpuUsagePercent = accumulator.GpuUsage.Average(),
            GpuCoreClockMhz = accumulator.GpuClock.Average(),
            GpuCoreTemperatureC = accumulator.GpuTemp.Average(),
            GpuPowerWatts = accumulator.GpuPower.Average(),
            RamUsagePercent = accumulator.RamUsage.Average(),
            FpsValue = accumulator.Fps.Average(),
            NetworkDownloadBytesPerSec = accumulator.NetDown.Average(),
            NetworkUploadBytesPerSec = accumulator.NetUp.Average()
        };

        try
        {
            _queue.Add((minuteUtc, sample));
        }
        catch (InvalidOperationException)
        {
            // Kuyruk kapatılmış; uygulama sonlanıyor demektir.
        }
    }

    private void ProcessProcessQueue()
    {
        // Yazıcı thread'i ömrü boyunca tek bir bağlantı açık tutulur — eskiden her dakika (kuyruk
        // öğesi başına) yeni bir SqliteConnection açılıp kapatılıyordu; WAL modu (InitializeSchema)
        // zaten aktif olduğundan tek bağlantıyı yeniden kullanmak güvenlidir.
        using SqliteConnection connection = OpenProcessSamplesConnection();
        foreach ((DateTime minuteUtc, Dictionary<string, ProcessMinuteAccumulator> data) in _processQueue.GetConsumingEnumerable())
        {
            try
            {
                foreach (KeyValuePair<string, ProcessMinuteAccumulator> kvp in data)
                    WriteProcessSample(connection, minuteUtc, kvp.Key, kvp.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Process geçmiş satırı yazılamadı ({MinuteUtc})", minuteUtc);
            }
        }
    }

    private static void WriteProcessSample(SqliteConnection connection, DateTime minuteUtc, string processName, ProcessMinuteAccumulator accumulator)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO process_samples (timestamp_utc, process_name, avg_cpu_percent, avg_ram_mb)
            VALUES (@ts, @name, @cpu, @ram);
            """;
        command.Parameters.AddWithValue("@ts", minuteUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@name", processName);
        AddNullable(command, "@cpu", accumulator.Cpu.Average());
        AddNullable(command, "@ram", accumulator.Ram.Average());
        command.ExecuteNonQuery();
    }

    private void ProcessQueue()
    {
        // Yazıcı thread'i ömrü boyunca tek bir bağlantı açık tutulur (bkz. ProcessProcessQueue'daki
        // aynı not) — her dakika yeniden aç/kapa yerine WAL modunda güvenle yeniden kullanılır.
        using SqliteConnection connection = OpenConnection();
        foreach ((DateTime minuteUtc, HistorySample sample) in _queue.GetConsumingEnumerable())
        {
            try
            {
                WriteSample(connection, sample);

                if (DateTime.UtcNow - _lastPruneUtc > PruneInterval)
                {
                    _lastPruneUtc = DateTime.UtcNow;
                    Prune(connection);
                    PruneProcessSamples();
                    PruneGameSessions();
                    PruneLoginAttempts();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kalıcı geçmiş satırı yazılamadı ({MinuteUtc})", minuteUtc);
            }
        }
    }

    private static void WriteSample(SqliteConnection connection, HistorySample sample)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO samples
                (timestamp_utc, cpu_usage, cpu_clock, cpu_power, cpu_temp,
                 gpu_usage, gpu_clock, gpu_temp, gpu_power, ram_usage, fps, net_down, net_up)
            VALUES
                (@ts, @cpuUsage, @cpuClock, @cpuPower, @cpuTemp,
                 @gpuUsage, @gpuClock, @gpuTemp, @gpuPower, @ramUsage, @fps, @netDown, @netUp);
            """;

        command.Parameters.AddWithValue("@ts", sample.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
        AddNullable(command, "@cpuUsage", sample.CpuUsagePercent);
        AddNullable(command, "@cpuClock", sample.CpuClockMhz);
        AddNullable(command, "@cpuPower", sample.CpuPowerWatts);
        AddNullable(command, "@cpuTemp", sample.CpuTemperatureC);
        AddNullable(command, "@gpuUsage", sample.GpuUsagePercent);
        AddNullable(command, "@gpuClock", sample.GpuCoreClockMhz);
        AddNullable(command, "@gpuTemp", sample.GpuCoreTemperatureC);
        AddNullable(command, "@gpuPower", sample.GpuPowerWatts);
        AddNullable(command, "@ramUsage", sample.RamUsagePercent);
        AddNullable(command, "@fps", sample.FpsValue);
        AddNullable(command, "@netDown", sample.NetworkDownloadBytesPerSec);
        AddNullable(command, "@netUp", sample.NetworkUploadBytesPerSec);

        command.ExecuteNonQuery();
    }

    private static void AddNullable(SqliteCommand command, string name, double? value) =>
        command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);

    private void Prune(SqliteConnection connection)
    {
        DateTime cutoff = DateTime.UtcNow - RetentionPeriod;
        string cutoffText = cutoff.ToString("O", CultureInfo.InvariantCulture);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM samples WHERE timestamp_utc < @cutoff;";
        command.Parameters.AddWithValue("@cutoff", cutoffText);
        int deleted = command.ExecuteNonQuery();
        if (deleted > 0)
            _logger.LogInformation("Kalıcı geçmişten {Count} eski satır silindi ({RetentionDays} günden eski)", deleted, RetentionPeriod.TotalDays);
    }

    /// <summary>process_samples artık ayrı bir dosyada (process-samples.db) olduğu için budaması da
    /// ana `Prune`'dan ayrı, kendi bağlantısını açan bir metotla yapılır (bkz. PruneLoginAttempts
    /// ile aynı gerekçe).</summary>
    private void PruneProcessSamples()
    {
        DateTime cutoff = DateTime.UtcNow - RetentionPeriod;
        string cutoffText = cutoff.ToString("O", CultureInfo.InvariantCulture);

        try
        {
            using SqliteConnection connection = OpenProcessSamplesConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM process_samples WHERE timestamp_utc < @cutoff;";
            command.Parameters.AddWithValue("@cutoff", cutoffText);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Process geçmişi budanamadı");
        }
    }

    /// <summary>game_sessions için de diğer tablolarla tutarlı 30 günlük otomatik saklama —
    /// eskiden bu tabloya hiç budama uygulanmıyordu (oyun oturumları sınırsız birikiyordu), kendi
    /// dosyasına ayrılırken bu tutarsızlık da giderildi.</summary>
    private void PruneGameSessions()
    {
        DateTime cutoff = DateTime.UtcNow - RetentionPeriod;
        string cutoffText = cutoff.ToString("O", CultureInfo.InvariantCulture);

        try
        {
            using SqliteConnection connection = OpenGameSessionsConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM game_sessions WHERE start_utc < @cutoff;";
            command.Parameters.AddWithValue("@cutoff", cutoffText);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Oyun geçmişi budanamadı");
        }
    }

    /// <summary>login_attempts artık ayrı bir dosyada (login-attempts.db) olduğu için budaması da
    /// ana `Prune`'dan ayrı, kendi bağlantısını açan bir metotla yapılır — giriş denemesi budaması
    /// nadir olduğundan (6 saatte bir) kalıcı bir bağlantı tutmaya gerek yoktur.</summary>
    private void PruneLoginAttempts()
    {
        DateTime cutoff = DateTime.UtcNow - RetentionPeriod;
        string cutoffText = cutoff.ToString("O", CultureInfo.InvariantCulture);

        try
        {
            using SqliteConnection connection = OpenLoginAttemptsConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM login_attempts WHERE timestamp_utc < @cutoff;";
            command.Parameters.AddWithValue("@cutoff", cutoffText);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Giriş denemesi geçmişi budanamadı");
        }
    }

    public async Task<IReadOnlyList<HistorySample>> QueryAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT timestamp_utc, cpu_usage, cpu_clock, cpu_power, cpu_temp,
                   gpu_usage, gpu_clock, gpu_temp, gpu_power, ram_usage, fps, net_down, net_up
            FROM samples
            WHERE timestamp_utc >= @from AND timestamp_utc <= @to
            ORDER BY timestamp_utc ASC;
            """;
        command.Parameters.AddWithValue("@from", fromUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@to", toUtc.ToString("O", CultureInfo.InvariantCulture));

        var results = new List<HistorySample>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new HistorySample
            {
                TimestampUtc = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CpuUsagePercent = ReadNullable(reader, 1),
                CpuClockMhz = ReadNullable(reader, 2),
                CpuPowerWatts = ReadNullable(reader, 3),
                CpuTemperatureC = ReadNullable(reader, 4),
                GpuUsagePercent = ReadNullable(reader, 5),
                GpuCoreClockMhz = ReadNullable(reader, 6),
                GpuCoreTemperatureC = ReadNullable(reader, 7),
                GpuPowerWatts = ReadNullable(reader, 8),
                RamUsagePercent = ReadNullable(reader, 9),
                FpsValue = ReadNullable(reader, 10),
                NetworkDownloadBytesPerSec = ReadNullable(reader, 11),
                NetworkUploadBytesPerSec = ReadNullable(reader, 12)
            });
        }

        return results;
    }

    private static long FileSizeOrZero(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static double? ReadNullable(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetDouble(index);

    /// <summary>
    /// Ön plandaki işlem adı değişince (oyun kapanınca/başka bir uygulama ön plana gelince) önceki
    /// oturumu, yeterince uzun sürdüyse (<see cref="MinSessionDuration"/>) kalıcı olarak kaydeder.
    /// Oturum sonu nadir olduğu için (saniyede bir değil, yalnızca değişimde) yazma işlemi burada
    /// senkron yapılır; dakikalık örnekler gibi ayrı bir kuyruğa gerek yoktur. Aynı process kesintisiz
    /// <see cref="MaxSessionDuration"/>'ı geçerse, process değişmemiş olsa bile oturum burada bölünür.
    /// </summary>
    public void RecordFpsSample(string? processName, DateTime nowUtc, double? fps, double? cpuTemperatureC, double? gpuTemperatureC)
    {
        lock (_sessionLock)
        {
            if (processName != _currentSessionProcessName)
            {
                FlushCurrentSession();
                StartNewSession(processName, nowUtc);
            }

            if (_currentSessionProcessName is null)
                return; // "oyun yok" durumu için oturum tutulmaz

            _currentSessionLastUtc = nowUtc;
            _sessionFps.Add(fps);
            _sessionCpuTemp.Add(cpuTemperatureC);
            _sessionGpuTemp.Add(gpuTemperatureC);

            if (nowUtc - _currentSessionStartUtc >= MaxSessionDuration)
            {
                string continuingProcessName = _currentSessionProcessName;
                FlushCurrentSession();
                StartNewSession(continuingProcessName, nowUtc);
            }
        }
    }

    /// <summary>Çağıran taraf zaten <see cref="_sessionLock"/> tutuyor olmalıdır.</summary>
    private void StartNewSession(string? processName, DateTime nowUtc)
    {
        _currentSessionProcessName = processName;
        _currentSessionStartUtc = nowUtc;
        _sessionFps = new RunningAverage();
        _sessionCpuTemp = new RunningAverage();
        _sessionGpuTemp = new RunningAverage();
    }

    /// <summary>Çağıran taraf zaten <see cref="_sessionLock"/> tutuyor olmalıdır.</summary>
    private void FlushCurrentSession()
    {
        if (_currentSessionProcessName is null)
            return;

        TimeSpan duration = _currentSessionLastUtc - _currentSessionStartUtc;
        double? averageFps = _sessionFps.Average();
        if (duration < MinSessionDuration || averageFps is null)
            return;

        try
        {
            using SqliteConnection connection = OpenGameSessionsConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO game_sessions (process_name, start_utc, end_utc, avg_fps, avg_cpu_temp, avg_gpu_temp)
                VALUES (@name, @start, @end, @fps, @cpuTemp, @gpuTemp);
                """;
            command.Parameters.AddWithValue("@name", _currentSessionProcessName);
            command.Parameters.AddWithValue("@start", _currentSessionStartUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@end", _currentSessionLastUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@fps", averageFps.Value);
            AddNullable(command, "@cpuTemp", _sessionCpuTemp.Average());
            AddNullable(command, "@gpuTemp", _sessionGpuTemp.Average());
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Oyun oturumu kaydedilemedi ({ProcessName})", _currentSessionProcessName);
        }
    }

    public async Task<IReadOnlyList<string>> GetKnownGameProcessNamesAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_gameSessionsConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT process_name FROM game_sessions ORDER BY process_name COLLATE NOCASE;";

        var results = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(reader.GetString(0));

        return results;
    }

    public async Task<IReadOnlyList<GameSessionSummary>> GetRecentSessionsAsync(string processName, int limit, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_gameSessionsConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT process_name, start_utc, end_utc, avg_fps, avg_cpu_temp, avg_gpu_temp
            FROM game_sessions
            WHERE process_name = @name
            ORDER BY start_utc DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@name", processName);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<GameSessionSummary>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new GameSessionSummary
            {
                ProcessName = reader.GetString(0),
                StartUtc = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                EndUtc = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                AverageFps = reader.GetDouble(3),
                AverageCpuTemperatureC = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                AverageGpuTemperatureC = reader.IsDBNull(5) ? null : reader.GetDouble(5)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetKnownProcessNamesAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_processSamplesConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT process_name FROM process_samples ORDER BY process_name COLLATE NOCASE;";

        var results = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(reader.GetString(0));

        return results;
    }

    /// <summary>En fazla 2000 satır döner — bir process aralık boyunca sürekli üst sırada kalırsa
    /// bile sayfaya/aktarıma binlerce satır yüklenmesin diye (Kayıtları Görüntüle'deki 1500 satır
    /// sınırıyla aynı gerekçe).</summary>
    public async Task<IReadOnlyList<ProcessHistorySample>> GetProcessHistoryAsync(string processName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_processSamplesConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        // Not: burada bilerek bir LIMIT yok — "Rows" uç noktasıyla (HistoryController.Rows) aynı
        // desen izleniyor: tüm aralık belleğe okunur, kırpma (en YENİ N kayıt tutulacak şekilde)
        // controller katmanında yapılır. Eskiden burada "ORDER BY ASC LIMIT 2000" vardı — bu, uzun
        // bir process için (ör. 30 gün) aralığın en ESKİ 2000 kaydını döndürüp en YENİ veriyi sessizce
        // düşürüyordu; kullanıcı "son 30 gün" seçip aslında en eski ~1,4 günü görüyordu, hiçbir uyarı
        // da yoktu (kullanıcı tarafından bulunan, canlı doğrulanmış hata).
        command.CommandText = """
            SELECT timestamp_utc, avg_cpu_percent, avg_ram_mb
            FROM process_samples
            WHERE process_name = @name AND timestamp_utc >= @from AND timestamp_utc <= @to
            ORDER BY timestamp_utc ASC;
            """;
        command.Parameters.AddWithValue("@name", processName);
        command.Parameters.AddWithValue("@from", fromUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@to", toUtc.ToString("O", CultureInfo.InvariantCulture));

        var results = new List<ProcessHistorySample>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ProcessHistorySample
            {
                TimestampUtc = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                AverageCpuPercent = reader.IsDBNull(1) ? null : reader.GetDouble(1),
                AverageRamMb = reader.IsDBNull(2) ? null : reader.GetDouble(2)
            });
        }

        return results;
    }

    /// <summary>
    /// Yerel ağa açma girişleri nadir olduğu için (dakikalık örnekler gibi bir kuyruğa gerek yok)
    /// yazma burada, oyun oturumu bitişiyle aynı desende (bkz. <see cref="FlushCurrentSession"/>)
    /// senkron yapılır: kendi bağlantısını açar, hata olursa yalnızca loglar (isteği asla düşürmez).
    /// </summary>
    public void RecordLoginAttempt(string ipAddress, bool success, bool causedLockout, DateTime nowUtc)
    {
        try
        {
            using SqliteConnection connection = OpenLoginAttemptsConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO login_attempts (timestamp_utc, ip_address, success, caused_lockout)
                VALUES (@ts, @ip, @success, @lockout);
                """;
            command.Parameters.AddWithValue("@ts", nowUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@ip", ipAddress);
            command.Parameters.AddWithValue("@success", success ? 1 : 0);
            command.Parameters.AddWithValue("@lockout", causedLockout ? 1 : 0);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Giriş denemesi kaydedilemedi ({IpAddress})", ipAddress);
        }
    }

    public async Task<IReadOnlyList<LoginAttemptEntry>> GetRecentLoginAttemptsAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_loginAttemptsConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT timestamp_utc, ip_address, success, caused_lockout
            FROM login_attempts
            ORDER BY timestamp_utc DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<LoginAttemptEntry>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LoginAttemptEntry
            {
                TimestampUtc = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                IpAddress = reader.GetString(1),
                Success = reader.GetInt64(2) != 0,
                CausedLockout = reader.GetInt64(3) != 0
            });
        }

        return results;
    }

    public async Task<HistoryStoreStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(timestamp_utc), MAX(timestamp_utc) FROM samples;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        int count = 0;
        DateTime? oldest = null;
        DateTime? newest = null;
        if (await reader.ReadAsync(cancellationToken))
        {
            count = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) oldest = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (!reader.IsDBNull(2)) newest = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return new HistoryStoreStatus
        {
            SampleCount = count,
            OldestSampleUtc = oldest,
            NewestSampleUtc = newest,
            DatabaseSizeBytes = DatabaseSizeOnDisk(connection)
        };
    }

    public async Task<HistoryStoreStatus> GetLoginAttemptsSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_loginAttemptsConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(timestamp_utc), MAX(timestamp_utc) FROM login_attempts;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        int count = 0;
        DateTime? oldest = null;
        DateTime? newest = null;
        if (await reader.ReadAsync(cancellationToken))
        {
            count = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) oldest = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (!reader.IsDBNull(2)) newest = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return new HistoryStoreStatus
        {
            SampleCount = count,
            OldestSampleUtc = oldest,
            NewestSampleUtc = newest,
            DatabaseSizeBytes = DatabaseSizeOnDisk(connection)
        };
    }

    public async Task<HistoryStoreStatus> GetGameSessionsSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_gameSessionsConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(start_utc), MAX(start_utc) FROM game_sessions;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        int count = 0;
        DateTime? oldest = null;
        DateTime? newest = null;
        if (await reader.ReadAsync(cancellationToken))
        {
            count = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) oldest = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (!reader.IsDBNull(2)) newest = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return new HistoryStoreStatus
        {
            SampleCount = count,
            OldestSampleUtc = oldest,
            NewestSampleUtc = newest,
            DatabaseSizeBytes = DatabaseSizeOnDisk(connection)
        };
    }

    public async Task<HistoryStoreStatus> GetProcessSamplesSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = new(_processSamplesConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(timestamp_utc), MAX(timestamp_utc) FROM process_samples;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        int count = 0;
        DateTime? oldest = null;
        DateTime? newest = null;
        if (await reader.ReadAsync(cancellationToken))
        {
            count = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) oldest = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (!reader.IsDBNull(2)) newest = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return new HistoryStoreStatus
        {
            SampleCount = count,
            OldestSampleUtc = oldest,
            NewestSampleUtc = newest,
            DatabaseSizeBytes = DatabaseSizeOnDisk(connection)
        };
    }

    /// <summary>
    /// Kullanıcının Geçmiş sayfasındaki "Kayıt Bilgileri" panellerinden elle tetiklediği temizlik:
    /// "en eski N gün" sabit seçeneklerinden biri seçilir (bkz. HistoryController) ve tablodaki EN
    /// ESKİ kayıttan itibaren o kadar günlük dilim silinir — otomatik <see cref="Prune"/> ile aynı
    /// "eşikten eskisini sil" mantığı, tek farkı eşiğin "şimdi − 30 gün" yerine "en eski kayıt + N
    /// gün" olması. Silme sonrası dosya boyutunun kullanıcıya GERÇEKTEN küçülmüş görünmesi için
    /// (DELETE tek başına sayfaları boşaltır ama dosyayı küçültmez) VACUUM ile geri kazanılır; bu
    /// nadir/manuel bir işlem olduğundan maliyeti kabul edilebilir.
    /// </summary>
    private static async Task<int> DeleteOldestAsync(string connectionString, string tableName, string timestampColumn, int days, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand minCommand = connection.CreateCommand();
        minCommand.CommandText = $"SELECT MIN({timestampColumn}) FROM {tableName};";
        object? minResult = await minCommand.ExecuteScalarAsync(cancellationToken);
        if (minResult is null || minResult is DBNull)
            return 0;

        DateTime oldest = DateTime.Parse((string)minResult, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        DateTime cutoff = oldest.AddDays(days);

        await using SqliteCommand deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = $"DELETE FROM {tableName} WHERE {timestampColumn} < @cutoff;";
        deleteCommand.Parameters.AddWithValue("@cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
        int deleted = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        if (deleted > 0)
        {
            await using (SqliteCommand vacuum = connection.CreateCommand())
            {
                vacuum.CommandText = "VACUUM;";
                await vacuum.ExecuteNonQueryAsync(cancellationToken);
            }

            // WAL modunda VACUUM'un küçülttüğü içerik önce "-wal" dosyasına yazılır; TRUNCATE
            // checkpoint olmadan kullanıcı "Veritabanı boyutu"nun silme sonrası GERÇEKTEN küçüldüğünü
            // hemen göremezdi (bkz. migration sonrası aynı gerekçe, kurucudaki VACUUM bloğu).
            await using SqliteCommand checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteNonQueryAsync(cancellationToken);
        }

        return deleted;
    }

    public Task<int> DeleteOldestSamplesAsync(int days, CancellationToken cancellationToken = default) =>
        DeleteOldestAsync(_connectionString, "samples", "timestamp_utc", days, cancellationToken);

    public Task<int> DeleteOldestGameSessionsAsync(int days, CancellationToken cancellationToken = default) =>
        DeleteOldestAsync(_gameSessionsConnectionString, "game_sessions", "start_utc", days, cancellationToken);

    public Task<int> DeleteOldestProcessSamplesAsync(int days, CancellationToken cancellationToken = default) =>
        DeleteOldestAsync(_processSamplesConnectionString, "process_samples", "timestamp_utc", days, cancellationToken);

    public Task<int> DeleteOldestLoginAttemptsAsync(int days, CancellationToken cancellationToken = default) =>
        DeleteOldestAsync(_loginAttemptsConnectionString, "login_attempts", "timestamp_utc", days, cancellationToken);

    // WAL modunda asıl veri, otomatik checkpoint tetiklenene kadar ana .db dosyasına değil "-wal"
    // dosyasına yazılır (ana dosya yalnızca birkaç KB'lık şema sayfası olarak kalabilir). Yalnızca
    // ana dosyayı ölçmek gerçek boyutu neredeyse her zaman "0 MB" gibi gösterirdi; gerçek disk
    // kullanımı için "-wal"/"-shm" dosyaları da toplama dahil edilir. `samples`/`game_sessions`/
    // `process_samples` (history.db) ve `login_attempts` (login-attempts.db) artık AYRI dosyalarda
    // olduğundan, bu metot hangi bağlantı verilirse yalnızca O dosyanın gerçek/bağımsız boyutunu döner.
    private static long DatabaseSizeOnDisk(SqliteConnection connection)
    {
        string dbPath = connection.DataSource;
        return FileSizeOrZero(dbPath) + FileSizeOrZero(dbPath + "-wal") + FileSizeOrZero(dbPath + "-shm");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Kapanış anındaki yarım dakikanın verisi de kaybolmasın diye kuyruğa son bir kez eklenir.
        lock (_accumulatorLock)
        {
            if (_currentMinuteUtc is { } minute)
            {
                EnqueueFlush(minute, _current);
                if (_currentProcesses.Count > 0) _processQueue.Add((minute, _currentProcesses));
            }
        }

        // Kapanış anında yarım kalan oyun oturumu da (yeterince uzun sürdüyse) kaydedilir.
        lock (_sessionLock)
        {
            FlushCurrentSession();
        }

        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(3));
        _queue.Dispose();

        _processQueue.CompleteAdding();
        _processWriter.Join(TimeSpan.FromSeconds(3));
        _processQueue.Dispose();
    }

    private struct RunningAverage
    {
        private double _sum;
        private int _count;

        public void Add(double? value)
        {
            if (!value.HasValue)
                return;

            _sum += value.Value;
            _count++;
        }

        public readonly double? Average() => _count > 0 ? _sum / _count : null;
    }

    private struct MinuteAccumulator
    {
        public RunningAverage CpuUsage;
        public RunningAverage CpuClock;
        public RunningAverage CpuPower;
        public RunningAverage CpuTemp;
        public RunningAverage GpuUsage;
        public RunningAverage GpuClock;
        public RunningAverage GpuTemp;
        public RunningAverage GpuPower;
        public RunningAverage RamUsage;
        public RunningAverage Fps;
        public RunningAverage NetDown;
        public RunningAverage NetUp;
    }

    private struct ProcessMinuteAccumulator
    {
        public RunningAverage Cpu;
        public RunningAverage Ram;
    }
}
