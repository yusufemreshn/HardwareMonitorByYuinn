using HardwareMonitorByYuinn.DataAccess.History;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.Web.Services;

/// <summary>
/// "Geçmiş" sayfasındaki 4 özet sorgusunu (bkz. SqliteHistoryStore'daki StatusCache notu) sayfa hiç
/// açılmadan, uygulama başlar başlamaz arka planda ısıtır. BroadcastBackgroundService gibi bu da bir
/// IHostedService olduğundan, masaüstü penceresi hiç gösterilmese bile (Windows açılışında otomatik
/// başlatmanın kullandığı "--minimized"/sessiz tepsi başlangıcı dahil, bkz. Program.cs) aynı şekilde
/// çalışır — ısıtma, WebView2 penceresine değil ASP.NET Core host'un kendisine bağlıdır. Kullanıcı
/// saatlerce tepsiden hiç bakmadan çalıştırıp sonra Geçmiş'e tıklasa bile COUNT(*) taramasını O AN
/// beklemez; en son ısıtmadan kalan önbellekten anında yanıt alır.
///
/// BackgroundService.StartAsync, ExecuteAsync'in bitmesini BEKLEMEDEN döner (host'un standart
/// davranışı); bu yüzden buradaki ilk (potansiyel olarak saniyeler süren) ısıtma turu ne Kestrel'in
/// başlamasını, ne splash ekranının kapanmasını geciktirir.
/// </summary>
public sealed class HistoryStatusWarmupService(IHistoryStore historyStore, ILogger<HistoryStatusWarmupService> logger) : BackgroundService
{
    // SqliteHistoryStore.StatusCacheDuration ile bilerek aynı: ısıtıcı önbelleği bu aralıkla
    // tazeler, önbellek süresi de en kötü ihtimalde (ısıtıcı gecikirse/hata alırsa) aynı süre
    // sonunda kendi kendine bir sonraki gerçek istekte yeniden hesaplar.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            try
            {
                await Task.WhenAll(
                    historyStore.GetStatusAsync(stoppingToken),
                    historyStore.GetLoginAttemptsSummaryAsync(stoppingToken),
                    historyStore.GetGameSessionsSummaryAsync(stoppingToken),
                    historyStore.GetProcessSamplesSummaryAsync(stoppingToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isıtma başarısız olsa bile uygulamayı etkilememeli; bir sonraki gerçek Geçmiş
                // sayfası isteği zaten kendi içinde (StatusCache boşsa) yeniden hesaplar.
                logger.LogWarning(ex, "Geçmiş özet önbelleği ısıtılamadı");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
