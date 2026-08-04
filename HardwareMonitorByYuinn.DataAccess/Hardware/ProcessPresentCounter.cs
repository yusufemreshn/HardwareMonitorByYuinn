namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>Bir sayaçtan okunan anlık FPS ve düşük yüzdelik (1% / 0.1% low) istatistikleri.</summary>
internal readonly record struct FpsStats(double FramesPerSecond, double FrameTimeMs, double? Low1PercentFps, double? LowPoint1PercentFps);

/// <summary>
/// Tek bir sürecin "present" (ekrana kare sunma) zaman damgalarını tutar ve bunlardan anlık kare
/// hızını hesaplar. Sabit periyotlu bir sayaç yerine kayan bir pencere kullanır, böylece değer her
/// an "son 1 saniyede gerçekten kaç kare sunuldu" sorusuna cevap verir; sabit dilimli bir sayaç
/// (ör. "her tam saniyede sıfırla") bir önceki dilimin sonucunu göstermeye devam ederdi.
/// </summary>
internal sealed class ProcessPresentCounter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 1% / 0.1% low hesaplamak için daha uzun bir pencere; 1 saniyelik pencerede yüzdelik anlamlı
    /// olacak kadar örnek birikmez (1000 kare düşünüldüğünde en kötü %0.1'lik dilim yalnızca 1 kare eder).
    /// </summary>
    private static readonly TimeSpan LowFpsWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Bu süre kadar hiç present gelmezse ölçüm "veri yok" sayılır (uygulama minimize/kapandı).
    /// ETW olaylarının dahili tamponlama nedeniyle gerçek zamana göre 1-2 saniye gecikmeli teslim
    /// edilebildiği ölçülmüştür; bu payı normal gecikmeyle karıştırmamak için biraz geniş tutulur.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);

    /// <summary>Yüzdelik hesaplaması için gereken en az kare sayısı; azında tek bir kare "%1"i domine eder.</summary>
    private const int MinFrameTimesForLowStats = 30;

    private readonly object _gate = new();
    private readonly Queue<DateTime> _recentPresents = new();
    private readonly Queue<(DateTime TimestampUtc, double FrameTimeMs)> _recentFrameTimes = new();
    private DateTime _lastPresentUtc = DateTime.MinValue;

    public void RecordPresent(DateTime timestampUtc)
    {
        lock (_gate)
        {
            if (_lastPresentUtc != DateTime.MinValue)
            {
                double frameTimeMs = (timestampUtc - _lastPresentUtc).TotalMilliseconds;

                // Süreç uzun süre arka planda kalıp sonra tekrar sunmaya başladığında aradaki dev
                // boşluk gerçek bir "yavaş kare" değildir; yüzdelik hesabını anlamsızca bozar.
                if (frameTimeMs > 0 && frameTimeMs < LowFpsWindow.TotalMilliseconds)
                    _recentFrameTimes.Enqueue((timestampUtc, frameTimeMs));
            }

            _recentPresents.Enqueue(timestampUtc);
            _lastPresentUtc = timestampUtc;
            TrimOlderThan(timestampUtc - Window);
            TrimFrameTimesOlderThan(timestampUtc - LowFpsWindow);
        }
    }

    public double? GetFps(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_recentPresents.Count == 0)
                return null;

            // "Durdu mu" sorusu gerçek zamana göre sorulur — ETW'nin normal teslim gecikmesi
            // (ölçülen: ~1-2 saniye) bu eşiğin altında kalır, gerçekten kesilen bir akıştan ayrılır.
            if (nowUtc - _lastPresentUtc > StaleAfter)
                return null;

            // Pencere "şu an"a göre değil, kuyruktaki en son present'e göre hesaplanır. ETW olayları
            // gerçek zamana göre gecikmeli teslim edilir; pencereyi gerçek "şimdi"ye göre almak, bu
            // gecikme kadar veriyi her seferinde "eski" sayıp siler ve kare hızını olduğundan çok
            // düşük (hatta sürekli sıfır) gösterirdi — tam da bu hata canlı testte yakalanmıştır.
            TrimOlderThan(_lastPresentUtc - Window);
            return _recentPresents.Count / Window.TotalSeconds;
        }
    }

    /// <summary>Anlık FPS'e ek olarak son kare süresi ile %1/%0.1 low değerlerini döndürür; hiçbiri hesaplanamıyorsa null.</summary>
    public FpsStats? GetStats(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_recentPresents.Count == 0 || nowUtc - _lastPresentUtc > StaleAfter)
                return null;

            TrimOlderThan(_lastPresentUtc - Window);
            TrimFrameTimesOlderThan(_lastPresentUtc - LowFpsWindow);

            double fps = _recentPresents.Count / Window.TotalSeconds;
            double lastFrameTimeMs = _recentFrameTimes.Count > 0 ? _recentFrameTimes.Last().FrameTimeMs : 1000.0 / Math.Max(fps, 0.001);

            double? low1 = null;
            double? low01 = null;
            if (_recentFrameTimes.Count >= MinFrameTimesForLowStats)
            {
                // En uzun (en yavaş) kare süreleri en başa gelecek şekilde sıralanır; bu dilimin
                // ortalaması, "en kötü anların" ortalama FPS'ine (1000 / ortalama ms) çevrilir.
                List<double> sortedDescending = [.. _recentFrameTimes.Select(f => f.FrameTimeMs).OrderDescending()];
                low1 = 1000.0 / AverageOfWorstFraction(sortedDescending, 0.01);
                low01 = 1000.0 / AverageOfWorstFraction(sortedDescending, 0.001);
            }

            return new FpsStats(fps, lastFrameTimeMs, low1, low01);
        }
    }

    private static double AverageOfWorstFraction(List<double> sortedDescending, double fraction)
    {
        int count = Math.Max(1, (int)Math.Ceiling(sortedDescending.Count * fraction));
        double sum = 0;
        for (int i = 0; i < count; i++)
            sum += sortedDescending[i];
        return sum / count;
    }

    private void TrimOlderThan(DateTime cutoffUtc)
    {
        while (_recentPresents.Count > 0 && _recentPresents.Peek() < cutoffUtc)
            _recentPresents.Dequeue();
    }

    private void TrimFrameTimesOlderThan(DateTime cutoffUtc)
    {
        while (_recentFrameTimes.Count > 0 && _recentFrameTimes.Peek().TimestampUtc < cutoffUtc)
            _recentFrameTimes.Dequeue();
    }
}
