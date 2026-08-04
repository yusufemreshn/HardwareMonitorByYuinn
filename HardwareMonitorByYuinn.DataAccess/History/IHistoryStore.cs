using HardwareMonitorByYuinn.Entity.Hardware;
using HardwareMonitorByYuinn.Entity.History;

namespace HardwareMonitorByYuinn.DataAccess.History;

/// <summary>
/// Donanım anlık görüntülerini dakikalık ortalamalar hâlinde diske kalıcı olarak yazar; uygulama
/// yeniden başlasa (hatta gün/hafta sonra) bile geçmiş veri kaybolmaz. Canlı 15 dakikalık
/// <c>TimeSeriesStore</c>'un yerini almaz, onu tamamlar.
/// </summary>
public interface IHistoryStore
{
    /// <summary>Her anlık görüntüyü (saniyede bir) biriktirir; gerçek disk yazımı yalnızca dakika sınırı geçtiğinde olur.</summary>
    void Record(HardwareSnapshot snapshot, DateTime nowUtc);

    Task<IReadOnlyList<HistorySample>> QueryAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    Task<HistoryStoreStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Her anket turunda (saniyede bir) çağrılır; ön plandaki işlem adı değiştiğinde önceki
    /// oturumu (yeterince uzun sürdüyse) kalıcı olarak kaydeder.
    /// </summary>
    void RecordFpsSample(string? processName, DateTime nowUtc, double? fps, double? cpuTemperatureC, double? gpuTemperatureC);

    Task<IReadOnlyList<string>> GetKnownGameProcessNamesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameSessionSummary>> GetRecentSessionsAsync(string processName, int limit, CancellationToken cancellationToken = default);

    /// <summary>Kalıcı geçmişte en az bir dakika "üst processler" listesinde görünmüş process adları.</summary>
    Task<IReadOnlyList<string>> GetKnownProcessNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Bir process'in belirli bir aralıktaki dakikalık CPU/RAM ortalamaları.</summary>
    Task<IReadOnlyList<ProcessHistorySample>> GetProcessHistoryAsync(string processName, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yerel ağa açma PIN kapısına yapılan bir giriş denemesini kalıcı olarak kaydeder. PIN'in
    /// kendisi asla parametre olarak alınmaz/saklanmaz.
    /// </summary>
    void RecordLoginAttempt(string ipAddress, bool success, bool causedLockout, DateTime nowUtc);

    /// <summary>En son N giriş denemesi, en yeniden eskiye.</summary>
    Task<IReadOnlyList<LoginAttemptEntry>> GetRecentLoginAttemptsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Giriş denemesi tablosunun özeti (kayıt sayısı, aralık, veritabanı boyutu) —
    /// "Kayıt Bilgileri"ndeki genel durumla aynı şekil, Geçmiş → Güvenlik'te gösterilir. Kendi ayrı
    /// dosyasında (login-attempts.db) tutulduğundan boyutu diğer tablolardan bağımsızdır.</summary>
    Task<HistoryStoreStatus> GetLoginAttemptsSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Oyun oturumları tablosunun özeti (oturum sayısı, aralık, veritabanı boyutu) —
    /// Geçmiş → Oyun Geçmişi → Kayıt Bilgileri'nde gösterilir. Kendi ayrı dosyasında
    /// (game-sessions.db) tutulur.</summary>
    Task<HistoryStoreStatus> GetGameSessionsSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Process bazlı geçmiş tablosunun özeti (kayıt sayısı, aralık, veritabanı boyutu) —
    /// Geçmiş → Süreç ve Sistem Olayları → Kayıt Bilgileri'nde gösterilir. Kendi ayrı dosyasında
    /// (process-samples.db) tutulur.</summary>
    Task<HistoryStoreStatus> GetProcessSamplesSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kullanıcının elle tetiklediği temizlik: tablodaki EN ESKİ kayıttan itibaren <paramref
    /// name="days"/> günlük dilimi siler (otomatik 30 günlük budamayla aynı mantık, eşik kullanıcı
    /// tarafından seçilir). Silinen satır sayısını döner.
    /// </summary>
    Task<int> DeleteOldestSamplesAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>Bkz. <see cref="DeleteOldestSamplesAsync"/> — aynı mantık, game_sessions için.</summary>
    Task<int> DeleteOldestGameSessionsAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>Bkz. <see cref="DeleteOldestSamplesAsync"/> — aynı mantık, process_samples için.</summary>
    Task<int> DeleteOldestProcessSamplesAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>Bkz. <see cref="DeleteOldestSamplesAsync"/> — aynı mantık, login_attempts için.</summary>
    Task<int> DeleteOldestLoginAttemptsAsync(int days, CancellationToken cancellationToken = default);
}
