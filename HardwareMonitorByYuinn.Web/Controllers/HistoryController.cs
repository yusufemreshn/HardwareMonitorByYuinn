using System.Globalization;
using System.Text;
using System.Text.Json;
using HardwareMonitorByYuinn.DataAccess.History;
using HardwareMonitorByYuinn.Entity.History;
using HardwareMonitorByYuinn.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace HardwareMonitorByYuinn.Web.Controllers;

public sealed class HistoryController(IHistoryStore historyStore, SystemEventReader systemEventReader) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Türkiye'de Windows/Excel'in varsayılan ayıracı noktalı virgüldür (virgül, ondalık ayıracı
    // olarak kullanıldığı için). Bu yüzden CSV'yi bu kültürün sayı biçimiyle ve ";" ile üretiriz;
    // aksi hâlde dosya çift tıklanıp açıldığında tüm satır tek bir sütuna sıkışır.
    private static readonly CultureInfo CsvCulture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Çok geniş bir aralık sayfada binlerce satır oluşturmaya çalışmasın diye bir üst sınır.</summary>
    private const int MaxDisplayRows = 1500;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        DateTime nowLocal = DateTime.Now;
        HistoryStoreStatus status = await historyStore.GetStatusAsync(cancellationToken);
        HistoryStoreStatus loginAttemptsStatus = await historyStore.GetLoginAttemptsSummaryAsync(cancellationToken);
        HistoryStoreStatus gameSessionsStatus = await historyStore.GetGameSessionsSummaryAsync(cancellationToken);
        HistoryStoreStatus processSamplesStatus = await historyStore.GetProcessSamplesSummaryAsync(cancellationToken);

        // "Kayıtları Görüntüle" tarih kutuları en son kaydedilen dakikadan geriye 15 dakikalık bir
        // aralıkla dolu gelir; sayfa açılır açılmaz bir şey görünsün diye. Henüz hiç kayıt yoksa
        // "şimdi"den geriye 15 dakika kullanılır (sonuç: boş tablo, bu da doğrudur). Dışa Aktar
        // butonu da aynı formdaki (kullanıcı tarafından değiştirilebilen) aralığı kullanır.
        DateTime defaultToLocal = status.NewestSampleUtc?.ToLocalTime() ?? nowLocal;
        var model = new HistoryViewModel
        {
            Status = status,
            LoginAttemptsStatus = loginAttemptsStatus,
            GameSessionsStatus = gameSessionsStatus,
            ProcessSamplesStatus = processSamplesStatus,
            DefaultFromLocal = defaultToLocal.AddMinutes(-15),
            DefaultToLocal = defaultToLocal,
            MaxDisplayRows = MaxDisplayRows
        };

        return View(model);
    }

    /// <summary>
    /// Yazdırılabilir/PDF'e çevrilebilir (tarayıcının "Yazdır → PDF olarak kaydet"i ile) HTML rapor
    /// sayfası — harici bir PDF kütüphanesi eklemek yerine (bkz. bağımlılık minimizasyonu kararı).
    /// Sayfanın kendisi boş döner; veriler client-side (report.js) zaten var olan
    /// RangeAverage/HealthTrend/SystemEvents endpoint'lerinden çekilir.
    /// </summary>
    [HttpGet]
    public IActionResult Report(int days = 7)
    {
        if (days <= 0 || days > 3650) days = 7;
        return View(new ReportViewModel { Days = days, GeneratedAtLocal = DateTime.Now });
    }

    /// <summary>
    /// "from"/"to", tarayıcının &lt;input type="datetime-local"&gt; alanından geldiği için saat dilimsiz
    /// (yerel) gelir; bu makine yalnızca 127.0.0.1'e kapalı çalıştığından tarayıcı ile sunucu her zaman
    /// aynı yerel saat dilimindedir, bu yüzden doğrudan Local kabul edip UTC'ye çeviririz.
    /// </summary>
    private static (DateTime FromUtc, DateTime ToUtc) ParseRangeToUtc(DateTime from, DateTime to)
    {
        DateTime fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime();
        DateTime toUtc = DateTime.SpecifyKind(to, DateTimeKind.Local).ToUniversalTime();
        return toUtc < fromUtc ? (toUtc, fromUtc) : (fromUtc, toUtc);
    }

    [HttpGet]
    public async Task<IActionResult> Export(DateTime from, DateTime to, string format, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return BadRequest(new { error = "Bitiş tarihi başlangıçtan önce olamaz." });
        }

        (DateTime fromUtc, DateTime toUtc) = ParseRangeToUtc(from, to);
        IReadOnlyList<HistorySample> samples = await historyStore.QueryAsync(fromUtc, toUtc, cancellationToken);
        string fileNameStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            // Depoda UTC olarak tutulur (yaz saati / saat dilimi belirsizliğinden kaçınmak için), ama
            // kullanıcıya sunulan dosyada yerel saate çevrilir; aksi hâlde "18:24" gibi bir değer,
            // makinenin +03:00 saat diliminde 3 saat "eski" görünüyormuş gibi yanlış anlaşılabilir.
            var exportRows = samples.Select(s => new
            {
                Zaman = s.TimestampUtc.ToLocalTime(),
                s.CpuUsagePercent,
                s.CpuClockMhz,
                s.CpuPowerWatts,
                s.CpuTemperatureC,
                s.GpuUsagePercent,
                s.GpuCoreClockMhz,
                s.GpuCoreTemperatureC,
                s.GpuPowerWatts,
                s.RamUsagePercent,
                s.FpsValue,
                s.NetworkDownloadBytesPerSec,
                s.NetworkUploadBytesPerSec
            });
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(exportRows, JsonOptions);
            return File(json, "application/json", $"hardwaremonitor-gecmis-{fileNameStamp}.json");
        }

        byte[] csv = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(BuildCsv(samples));
        return File(csv, "text/csv", $"hardwaremonitor-gecmis-{fileNameStamp}.csv");
    }

    /// <summary>Ayarlar sayfasındaki filtreye göre, seçilen aralığın satırlarını (dakika dakika) sayfada göstermek için.</summary>
    [HttpGet]
    public async Task<IActionResult> Rows(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return BadRequest(new { error = "Bitiş tarihi başlangıçtan önce olamaz." });
        }

        (DateTime fromUtc, DateTime toUtc) = ParseRangeToUtc(from, to);
        IReadOnlyList<HistorySample> samples = await historyStore.QueryAsync(fromUtc, toUtc, cancellationToken);

        var rows = samples.Take(MaxDisplayRows).Select(s => new
        {
            zaman = s.TimestampUtc.ToLocalTime(),
            s.CpuUsagePercent,
            s.CpuClockMhz,
            s.CpuPowerWatts,
            s.CpuTemperatureC,
            s.GpuUsagePercent,
            s.GpuCoreClockMhz,
            s.GpuCoreTemperatureC,
            s.GpuPowerWatts,
            s.RamUsagePercent,
            s.FpsValue,
            s.NetworkDownloadBytesPerSec,
            s.NetworkUploadBytesPerSec
        });

        return Json(new { totalCount = samples.Count, truncated = samples.Count > MaxDisplayRows, rows });
    }

    /// <summary>
    /// Karşılaştırma sayfasındaki "İki Zaman Aralığını Karşılaştır" için: seçilen aralıktaki TÜM
    /// örneklerin (1500 satır sınırı olan `Rows`'un aksine) ortalamasını sunucuda hesaplayıp döner
    /// — geniş bir aralık (ör. "sürücü güncellemesi öncesi" 2 hafta) binlerce satır olarak tarayıcıya
    /// taşınmaz, yalnızca tek bir ortalama satırı döner.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> RangeAverage(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return BadRequest(new { error = "Bitiş tarihi başlangıçtan önce olamaz." });
        }

        (DateTime fromUtc, DateTime toUtc) = ParseRangeToUtc(from, to);
        IReadOnlyList<HistorySample> samples = await historyStore.QueryAsync(fromUtc, toUtc, cancellationToken);

        return Json(new
        {
            sampleCount = samples.Count,
            avgCpuUsage = AverageOrNull(samples.Select(s => s.CpuUsagePercent)),
            avgCpuTemp = AverageOrNull(samples.Select(s => s.CpuTemperatureC)),
            avgCpuPower = AverageOrNull(samples.Select(s => s.CpuPowerWatts)),
            avgGpuUsage = AverageOrNull(samples.Select(s => s.GpuUsagePercent)),
            avgGpuTemp = AverageOrNull(samples.Select(s => s.GpuCoreTemperatureC)),
            avgGpuPower = AverageOrNull(samples.Select(s => s.GpuPowerWatts)),
            avgRamUsage = AverageOrNull(samples.Select(s => s.RamUsagePercent)),
            avgFps = AverageOrNull(samples.Select(s => s.FpsValue))
        });
    }

    /// <summary>
    /// Kalıcı geçmişteki sıcaklık verisini yerel saat/gün bazında kümeleyip ısı haritası için döndürür.
    /// Kümeleme burada (SQL'de değil) yapılır çünkü satırlar UTC saklanır; yerel saate göre gruplamak
    /// C# tarafında <c>ToLocalTime()</c> ile daha güvenilirdir (SQLite'ın kendi tarih fonksiyonları
    /// keyfi saat dilimi ofsetlerini kolayca desteklemez).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Heatmap(int days, CancellationToken cancellationToken)
    {
        if (days <= 0 || days > 3650) days = 14;
        DateTime toUtc = DateTime.UtcNow;
        DateTime fromUtc = toUtc.AddDays(-days);

        IReadOnlyList<HistorySample> samples = await historyStore.QueryAsync(fromUtc, toUtc, cancellationToken);

        var buckets = samples
            .Select(s => new { Local = s.TimestampUtc.ToLocalTime(), s.CpuTemperatureC, s.GpuCoreTemperatureC })
            .GroupBy(s => new { Date = s.Local.Date, s.Local.Hour })
            .Select(g => new
            {
                date = g.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                hour = g.Key.Hour,
                avgCpuTemp = AverageOrNull(g.Select(x => x.CpuTemperatureC)),
                avgGpuTemp = AverageOrNull(g.Select(x => x.GpuCoreTemperatureC))
            })
            .OrderBy(b => b.date).ThenBy(b => b.hour)
            .ToList();

        return Json(buckets);
    }

    /// <summary>
    /// Sistem Sağlığı Trendi (Geçmiş sayfası) için günlük ortalamalar. Puanın kendisi burada
    /// HESAPLANMAZ — eşikler (Ayarlar'da) yalnızca tarayıcıda (localStorage) tutulduğu için puan
    /// istemci tarafında (history.js) bu ortalamalardan hesaplanır; burada yalnızca binlerce ham
    /// dakikalık satırı (Isı Haritası'ndaki gibi) günlere indirgeyip aktarım boyutunu küçültürüz.
    /// Kalıcı geçmiş disk sıcaklığı tutmadığından (yalnızca CPU/GPU/RAM), puan da bunlarla sınırlıdır.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> HealthTrend(int days, CancellationToken cancellationToken)
    {
        if (days <= 0 || days > 3650) days = 14;
        DateTime toUtc = DateTime.UtcNow;
        DateTime fromUtc = toUtc.AddDays(-days);

        IReadOnlyList<HistorySample> samples = await historyStore.QueryAsync(fromUtc, toUtc, cancellationToken);

        var buckets = samples
            .Select(s => new { Local = s.TimestampUtc.ToLocalTime(), s.CpuUsagePercent, s.CpuTemperatureC, s.GpuUsagePercent, s.GpuCoreTemperatureC, s.RamUsagePercent })
            .GroupBy(s => s.Local.Date)
            .Select(g => new
            {
                date = g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                sampleCount = g.Count(),
                avgCpuUsage = AverageOrNull(g.Select(x => x.CpuUsagePercent)),
                avgCpuTemp = AverageOrNull(g.Select(x => x.CpuTemperatureC)),
                avgGpuUsage = AverageOrNull(g.Select(x => x.GpuUsagePercent)),
                avgGpuTemp = AverageOrNull(g.Select(x => x.GpuCoreTemperatureC)),
                avgRamUsage = AverageOrNull(g.Select(x => x.RamUsagePercent))
            })
            .OrderBy(b => b.date)
            .ToList();

        return Json(buckets);
    }

    /// <summary>
    /// Sistem Olayı Zaman Çizelgesi — Windows Olay Günlüğü'nden (System log) uyku/uyanma/başlatma/
    /// kapatma/güncelleme olaylarını okur. SQLite'a kalıcı olarak kaydedilmez, her istekte canlı
    /// okunur (Windows zaten kendi günlüğünde tutuyor).
    /// </summary>
    [HttpGet]
    public IActionResult SystemEvents(int days)
    {
        if (days <= 0 || days > 3650) days = 7;
        DateTime toUtc = DateTime.UtcNow;
        DateTime fromUtc = toUtc.AddDays(-days);

        IReadOnlyList<SystemEventEntry> events = systemEventReader.Read(fromUtc, toUtc);
        var result = events.Select(e => new { timestamp = e.Timestamp, type = e.Type, description = e.Description });
        return Json(result);
    }

    /// <summary>Kalıcı geçmişte en az bir dakika "üst processler" listesinde görünmüş process adları.</summary>
    [HttpGet]
    public async Task<IActionResult> ProcessNames(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names = await historyStore.GetKnownProcessNamesAsync(cancellationToken);
        return Json(names);
    }

    /// <summary>Bir process'in son N gündeki dakikalık CPU/RAM geçmişi (Geçmiş sayfası "Process Geçmişi").</summary>
    [HttpGet]
    public async Task<IActionResult> ProcessHistory(string processName, int days, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return Json(Array.Empty<object>());
        if (days <= 0 || days > 3650) days = 7;

        DateTime toUtc = DateTime.UtcNow;
        DateTime fromUtc = toUtc.AddDays(-days);
        IReadOnlyList<ProcessHistorySample> samples = await historyStore.GetProcessHistoryAsync(processName, fromUtc, toUtc, cancellationToken);

        // Aralık MaxDisplayRows'u aşarsa en YENİ kayıtları tut (liste zaten eskiden yeniye sıralı
        // geliyor, en baştakileri atlamak en eskiyi düşürür) — "Rows" uç noktasıyla aynı desen.
        bool truncated = samples.Count > MaxDisplayRows;
        IEnumerable<ProcessHistorySample> visible = truncated
            ? samples.Skip(samples.Count - MaxDisplayRows)
            : samples;

        var rows = visible.Select(s => new
        {
            timeLocal = s.TimestampUtc.ToLocalTime(),
            avgCpuPercent = s.AverageCpuPercent,
            avgRamMb = s.AverageRamMb
        });

        return Json(new { totalCount = samples.Count, truncated, rows });
    }

    /// <summary>
    /// Yerel ağa açma PIN kapısına yapılan son N giriş denemesi — Ayarlar → Ağ ve Erişim'de
    /// tanımlanan özellik, ama görüntülemesi diğer kalıcı geçmiş verileriyle aynı yerde (Geçmiş
    /// sayfası) daha tutarlı olduğu için buraya eklendi.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LoginAttempts(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0 || limit > 500) limit = 100;

        IReadOnlyList<LoginAttemptEntry> attempts = await historyStore.GetRecentLoginAttemptsAsync(limit, cancellationToken);
        var result = attempts.Select(a => new
        {
            timeLocal = a.TimestampUtc.ToLocalTime(),
            ipAddress = a.IpAddress,
            success = a.Success,
            causedLockout = a.CausedLockout
        });

        return Json(result);
    }

    /// <summary>Kullanıcının "Kayıt Bilgileri" panellerinden seçebileceği sabit gün seçenekleri —
    /// dropdown'a değil sunucuya da güvenilir (elle kurgulanmış bir POST isteği bu kümenin dışına
    /// çıkamaz).</summary>
    private static readonly int[] AllowedCleanupDays = [1, 2, 5, 10, 15, 30];

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOldestSamples(int days, CancellationToken cancellationToken)
    {
        if (!AllowedCleanupDays.Contains(days))
            return BadRequest(new { error = "Geçersiz gün sayısı." });

        int deleted = await historyStore.DeleteOldestSamplesAsync(days, cancellationToken);
        return Json(new { deletedCount = deleted });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOldestGameSessions(int days, CancellationToken cancellationToken)
    {
        if (!AllowedCleanupDays.Contains(days))
            return BadRequest(new { error = "Geçersiz gün sayısı." });

        int deleted = await historyStore.DeleteOldestGameSessionsAsync(days, cancellationToken);
        return Json(new { deletedCount = deleted });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOldestProcessSamples(int days, CancellationToken cancellationToken)
    {
        if (!AllowedCleanupDays.Contains(days))
            return BadRequest(new { error = "Geçersiz gün sayısı." });

        int deleted = await historyStore.DeleteOldestProcessSamplesAsync(days, cancellationToken);
        return Json(new { deletedCount = deleted });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOldestLoginAttempts(int days, CancellationToken cancellationToken)
    {
        if (!AllowedCleanupDays.Contains(days))
            return BadRequest(new { error = "Geçersiz gün sayısı." });

        int deleted = await historyStore.DeleteOldestLoginAttemptsAsync(days, cancellationToken);
        return Json(new { deletedCount = deleted });
    }

    private static double? AverageOrNull(IEnumerable<double?> values)
    {
        List<double> present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count > 0 ? present.Average() : null;
    }

    /// <summary>Kalıcı geçmişte en az bir oturumu kaydedilmiş oyunların/uygulamaların listesi.</summary>
    [HttpGet]
    public async Task<IActionResult> GameList(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names = await historyStore.GetKnownGameProcessNamesAsync(cancellationToken);
        return Json(names);
    }

    /// <summary>Bir oyunun son N oturumu; Geçmiş sayfasındaki "Oyun Geçmişi" trend listesi için.</summary>
    [HttpGet]
    public async Task<IActionResult> GameSessions(string processName, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return Json(Array.Empty<object>());
        if (limit <= 0 || limit > 500) limit = 10;

        IReadOnlyList<GameSessionSummary> sessions = await historyStore.GetRecentSessionsAsync(processName, limit, cancellationToken);
        var result = sessions.Select(s => new
        {
            startLocal = s.StartUtc.ToLocalTime(),
            endLocal = s.EndUtc.ToLocalTime(),
            durationMinutes = (s.EndUtc - s.StartUtc).TotalMinutes,
            averageFps = s.AverageFps,
            averageCpuTemperatureC = s.AverageCpuTemperatureC,
            averageGpuTemperatureC = s.AverageGpuTemperatureC
        });

        return Json(result);
    }

    // Başlıklarda Türkçe özel karakter (ş, ğ, ı, ç, ° vb.) KULLANILMAZ: bu karakterler UTF-8'de birden
    // fazla bayttan oluşur, açan programın kod sayfasını yanlış tahmin etmesi hâlinde hem bozuk görünür
    // hem de (daha kötüsü) bu baytlar sahte bir ayıraç gibi okunup satırın geri kalan sütunlarını
    // kaydırabilir. ASCII kullanmak bunu çözer ama tek başına yetmedi: bazı programların CSV içe
    // aktarma diyaloğunda ";" yanında BOŞLUK da ayıraç olarak işaretli geliyor — başlıklardaki
    // "Islemci Kullanim (%)" gibi kelime aralarındaki boşluklar da sütun böldüğü için başlıklar
    // veri hücrelerinden (boşluksuz, tek parça) daha fazla sütuna yayılıp kayıyordu. Bu yüzden
    // başlıklarda alt çizgi kullanılır, hiç boşluk bırakılmaz.
    private static string BuildCsv(IReadOnlyList<HistorySample> samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';',
            "Tarih", "Saat", "CPU_Kullanim_%", "CPU_Frekans_MHz", "CPU_Guc_W", "CPU_Sicaklik_C",
            "GPU_Kullanim_%", "GPU_Frekans_MHz", "GPU_Sicaklik_C", "GPU_Guc_W",
            "RAM_Kullanim_%", "Kare_Hizi_FPS", "Indirme_MBs", "Yukleme_MBs"));

        foreach (HistorySample s in samples)
        {
            // Depoda UTC olarak tutulur; burada yalnızca gösterim/dışa aktarım anında yerel saate çevrilir.
            DateTime local = s.TimestampUtc.ToLocalTime();
            sb.AppendLine(string.Join(';',
                local.ToString("dd.MM.yyyy", CsvCulture),
                local.ToString("HH:mm", CsvCulture),
                Csv(s.CpuUsagePercent, 1),
                Csv(s.CpuClockMhz, 0),
                Csv(s.CpuPowerWatts, 1),
                Csv(s.CpuTemperatureC, 1),
                Csv(s.GpuUsagePercent, 1),
                Csv(s.GpuCoreClockMhz, 0),
                Csv(s.GpuCoreTemperatureC, 1),
                Csv(s.GpuPowerWatts, 1),
                Csv(s.RamUsagePercent, 1),
                Csv(s.FpsValue, 1),
                Csv(ToMbPerSec(s.NetworkDownloadBytesPerSec), 2),
                Csv(ToMbPerSec(s.NetworkUploadBytesPerSec), 2)));
        }

        return sb.ToString();
    }

    private static double? ToMbPerSec(double? bytesPerSec) =>
        bytesPerSec.HasValue ? bytesPerSec.Value / 1024d / 1024d : null;

    private static string Csv(double? value, int digits) =>
        value.HasValue ? value.Value.ToString("F" + digits, CsvCulture) : string.Empty;
}
