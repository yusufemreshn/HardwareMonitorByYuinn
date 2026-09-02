using System.Windows.Forms;
using HardwareMonitorByYuinn.DataAccess.History;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// WebView2'nin gerektirdiği STA (single-threaded apartment) mesaj döngüsünü ayrı bir iş parçacığında
/// başlatıp masaüstü penceresi kapanana kadar bloklar. Program.cs top-level statements kullandığından
/// giriş noktasının apartment durumuna güvenmek yerine bu iş parçacığı açıkça STA olarak işaretlenir.
///
/// İki giriş noktası var: <see cref="Run"/> (basit, sistem tepsisinde sessiz başlarken kullanılır,
/// splash göstermeye gerek yok) ve <see cref="BeginEarly"/>+<see cref="Handle.Continue"/> (normal
/// açılışta splash'i ASP.NET Core'un builder/DI kurulumu daha BAŞLAMADAN göstermek için Program.cs'in
/// çok erken bir noktasından çağrılır; Kestrel/host hazır olunca <c>Continue</c> ile devam sinyali
/// verilir).
/// </summary>
internal static class DesktopShellRunner
{
    /// <summary>
    /// Pencere kapanana (kullanıcı elle kapatır YA DA <paramref name="hostStopping"/> tetiklenir,
    /// ör. Ayarlar → Yerel Ağa Açma → "Yeniden Başlat") ya da WebView2 Runtime hiç kurulamayana
    /// kadar bloklar. Splash göstermez; yalnızca sistem tepsisinde sessiz başlarken kullanılır,
    /// o durumda zaten hiçbir pencere görünmeyecek.
    /// </summary>
    internal static void Run(string startUrl, CancellationToken hostStopping, bool startMinimized = false)
    {
        var uiThread = new Thread(() =>
        {
            InitializeWinForms();
            // Sessiz (tepsi) başlangıçta hiçbir pencere gösterilmediği ilkeye sadık kalınıp onarım da
            // sessiz yapılır; sorun log dosyasına yazılır, kullanıcıya ekstra bir pencere çıkmaz.
            RunRepairIfNeeded(showUi: false);
            CreateAndRunShellForm(startUrl, hostStopping, startMinimized, splash: null);
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
    }

    /// <summary>
    /// Splash'ı HEMEN (çağrıldığı an) ayrı bir STA iş parçacığında gösterip döner; Program.cs bunu
    /// <c>WebApplication.CreateBuilder</c>'dan hemen sonra, asıl builder/DI/Kestrel kurulumu
    /// başlamadan ÖNCE çağırır. Kestrel/host hazır olduğunda <see cref="Handle.Continue"/> çağrılana
    /// kadar bu iş parçacığı yalnızca mesaj pompalayıp bekler (splash animasyonunun donmaması için).
    /// </summary>
    internal static Handle BeginEarly()
    {
        var handle = new Handle();
        var uiThread = new Thread(() => RunEarlyMessageLoop(handle));
        uiThread.SetApartmentState(ApartmentState.STA);
        handle.UiThread = uiThread;
        uiThread.Start();
        return handle;
    }

    private static void RunEarlyMessageLoop(Handle handle)
    {
        InitializeWinForms();

        // Ana iş parçacığındaki builder/DI kurulumu daha başlamadan, ASP.NET Core hiç bu veritabanı
        // dosyalarına dokunmadan ÖNCE çalışır; böylece bir sorun varsa Host.StartAsync (dolayısıyla
        // tüm süreç) hiç çökmeden düzeltilmiş olur (bkz. HistoryDatabaseRepair'in doc yorumu:
        // 2026-08-27 gecesi yaşanan gerçek olay).
        RunRepairIfNeeded(showUi: true);

        var splash = new SplashForm();
        splash.Show();
        splash.SetProgress(10);

        // ASP.NET Core'un builder/DI kurulumu ve Kestrel başlangıcı ana iş parçacığında SÜRERKEN
        // burada mesaj pompalanmaya devam ediliyor, aksi hâlde splash'in Timer'ı (dolma animasyonu)
        // ve yeniden çizimi donardı. Application.DoEvents() burada güvenli: splash hiçbir kullanıcı
        // girdisi almıyor (tıklanabilir/odaklanabilir bir kontrolü yok), reentrancy riski yok.
        while (!handle.IsReady)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }

        splash.SetProgress(20);
        CreateAndRunShellForm(handle.StartUrl!, handle.HostStopping, startMinimized: false, splash);
    }

    private static void InitializeWinForms()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    /// <summary>
    /// Dosyalar sağlamken (olağan durum) HistoryDatabaseRepair hiçbir şey yapmadığından bu, RepairForm'u
    /// hiç oluşturmadan birkaç milisaniyede döner. Yalnızca gerçekten bir dosya onarıldığında (ör.
    /// beklenmedik bir kapanmadan sonra) <paramref name="showUi"/> true ise RepairForm gösterilip
    /// onarım tamamlanana kadar (Application.DoEvents ile mesaj pompalanarak) beklenir.
    /// </summary>
    private static void RunRepairIfNeeded(bool showUi)
    {
        string historyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HardwareMonitorByYuinn", "history");

        RepairForm? form = null;
        void Report(string message, int percent)
        {
            if (showUi)
            {
                form ??= CreateAndShowRepairForm();
                form.SetStatus(message, percent);
                Application.DoEvents();
            }
        }

        HistoryDatabaseRepair.RepairIfNeeded(
            historyDirectory,
            onProgress: Report,
            repairingMessageTemplate: Loc.T("{0} bozuk görünüyor, onarılıyor…"),
            completedMessage: Loc.T("Tamamlandı"));

        if (form is not null)
        {
            // "Tamamlandı" mesajı göz açıp kapayana kadar geçmesin, kullanıcı okuyabilsin.
            Thread.Sleep(500);
            form.Close();
        }
    }

    private static RepairForm CreateAndShowRepairForm()
    {
        var form = new RepairForm();
        form.Show();
        Application.DoEvents();
        return form;
    }

    private static void CreateAndRunShellForm(string startUrl, CancellationToken hostStopping, bool startMinimized, SplashForm? splash)
    {
        if (!WebView2RuntimeInstaller.IsInstalled() && !WebView2RuntimeInstaller.TryInstallSilently())
        {
            splash?.Close();
            MessageBox.Show(
                Loc.T("Bu uygulamanın arayüzü için gereken Microsoft Edge WebView2 bileşeni kurulamadı (muhtemelen internet bağlantısı yok). Lütfen internete bağlanıp uygulamayı tekrar başlatın, ya da https://developer.microsoft.com/microsoft-edge/webview2/consumer/ adresinden elle kurun."),
                "HardwareMonitorByYuinn",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var form = new ShellForm(startUrl, startMinimized, splash);

        // Host taraflı bir durdurma (ör. RemoteAccessController.Restart'ın çağırdığı
        // StopApplication()) pencereyi otomatik kapatmaz; bu callback olmadan Kestrel arka planda
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
        // sonlandırır; bu iş parçacığının (Run'daki uiThread.Join() ya da Handle.Continue'daki
        // UiThread.Join()) dönüşü bunu yeterli sinyal olarak kullanır.
        Application.Run(form);
    }

    /// <summary>
    /// <see cref="BeginEarly"/> tarafından döndürülen, ana iş parçacığının Kestrel/host hazır
    /// olduğunda splash'i bekleten STA iş parçacığına "devam et" sinyali vermesini sağlayan tutamaç.
    /// </summary>
    internal sealed class Handle
    {
        internal Thread? UiThread;
        internal volatile bool IsReady;
        internal string? StartUrl;
        internal CancellationToken HostStopping;

        /// <summary>
        /// Kestrel/host hazır olduğunda çağrılır; bekleyen STA iş parçacığına devam sinyali verir ve
        /// pencere kapanana kadar (eskiden <c>uiThread.Join()</c> ile yapılan bloklamayla aynı
        /// şekilde) bloklar.
        /// </summary>
        internal void Continue(string startUrl, CancellationToken hostStopping)
        {
            StartUrl = startUrl;
            HostStopping = hostStopping;
            IsReady = true;
            UiThread!.Join();
        }
    }
}
