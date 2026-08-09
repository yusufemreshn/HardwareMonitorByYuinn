using System.Windows.Forms;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// Kestrel zaten arka planda (<c>app.RunAsync()</c>) çalışırken, WebView2'nin gerektirdiği
/// STA (single-threaded apartment) mesaj döngüsünü ayrı bir iş parçacığında başlatıp
/// masaüstü penceresi kapanana kadar bloklar. Program.cs top-level statements kullandığından
/// giriş noktasının apartment durumuna güvenmek yerine bu iş parçacığı açıkça STA olarak
/// işaretlenir.
/// </summary>
internal static class DesktopShellRunner
{
    /// <summary>
    /// Pencere kapanana (kullanıcı elle kapatır YA DA <paramref name="hostStopping"/> tetiklenir
    /// — ör. Ayarlar → Yerel Ağa Açma → "Yeniden Başlat") ya da WebView2 Runtime hiç kurulamayana
    /// kadar bloklar.
    /// </summary>
    internal static void Run(string startUrl, CancellationToken hostStopping)
    {
        var uiThread = new Thread(() => RunMessageLoop(startUrl, hostStopping));
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
    }

    private static void RunMessageLoop(string startUrl, CancellationToken hostStopping)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (!WebView2RuntimeInstaller.IsInstalled() && !WebView2RuntimeInstaller.TryInstallSilently())
        {
            MessageBox.Show(
                "Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı " +
                "(muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar " +
                "başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ " +
                "adresinden elle kurun.",
                "HardwareMonitorByYuinn",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var form = new ShellForm(startUrl);

        // Host taraflı bir durdurma (ör. RemoteAccessController.Restart'ın çağırdığı
        // StopApplication()) pencereyi otomatik kapatmaz — bu callback olmadan Kestrel arka planda
        // durur ama pencere açık/bağlantısız kalır, süreç hiç sonlanmaz ve Restart'ın başlattığı
        // yeni süreç, eskisi mutex'i hâlâ tuttuğu için sessizce çıkar. ForceClose(), "sistem
        // tepsisine küçült" ayarını devre dışı bırakıp Application.Run'ın aşağıda dönmesini
        // tetikler, bu da bu iş parçacığının (ve dolayısıyla tüm sürecin) düzgünce kapanmasını sağlar.
        using CancellationTokenRegistration registration = hostStopping.Register(() =>
        {
            try
            {
                if (!form.IsDisposed)
                {
                    form.ForceClose();
                }
            }
            catch
            {
                // Pencere zaten kapanmış/kapanıyor olabilir (ör. kullanıcı aynı anda elle kapattı).
            }
        });

        // Application.Run(Form), belirtilen form kapandığında mesaj döngüsünü kendiliğinden
        // sonlandırır — Program.cs'deki uiThread.Join() dönüşü bunu yeterli sinyal olarak kullanır.
        Application.Run(form);
    }
}
