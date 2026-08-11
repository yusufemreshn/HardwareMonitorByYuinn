using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// Uygulamanın masaüstü penceresi: içinde WebView2 ile aynı süreçteki Kestrel'in
/// sunduğu <c>http://127.0.0.1:5250/</c>'yi gösterir. Yerel ağa açma özelliği hâlâ
/// düz HTTP üzerinden çalışmaya devam eder (bkz. Program.cs) — bu pencere yalnızca
/// sahibinin kendi bilgisayarındaki birincil arayüzü değiştirir.
/// </summary>
internal sealed class ShellForm : Form
{
    private static readonly Color InitialBackground = ColorTranslator.FromHtml("#0b0f14");

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly NotifyIcon _trayIcon;

    /// <summary>true olduğunda kapatma her zaman gerçek çıkıştır — tepsi ayarı ne olursa olsun.</summary>
    private bool _allowExit;

    public ShellForm(string startUrl, bool startMinimized = false, SplashForm? splash = null)
    {
        Text = "HardwareMonitorByYuinn";

        // Sabit varsayılan (kullanıcı isteğiyle 1280x1280). Ekran boyutuna göre küçültme burada
        // DEĞİL, aşağıdaki Load olayında yapılır — Screen.PrimaryScreen, elevated (UAC) bir
        // süreçte pencere daha oluşmadan (constructor'da) bazen geçici/yanlış (ör. birkaç
        // piksellik) bir WorkingArea döndürebiliyor; bu, canlı testte pencerenin 160x28 gibi
        // anlamsız bir boyutta açılmasına yol açtı. Handle oluştuktan sonra Screen.FromControl
        // çok daha güvenilir.
        Width = 1280;
        Height = 1280;
        StartPosition = FormStartPosition.CenterScreen;

        // Sayfa henüz yüklenmeden/yeniden boyutlandırmada görünebilecek pencere zemini — JS tarafı
        // gerçek temayı bildirene kadar (bkz. OnWebMessageReceived) varsayılan koyu temayla eşleşir
        // (bkz. _Layout.cshtml'deki "hwmon-theme" varsayılanı), böylece ilk açılışta beyaz bir
        // pencere zemini/kenarlığı çakmaz.
        BackColor = InitialBackground;
        _webView.DefaultBackgroundColor = InitialBackground;

        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            // app.ico ayrı bir dosya olarak kopyalanmıyor (yalnızca exe kaynaklarına gömülü, bkz.
            // csproj → ApplicationIcon); bu yüzden ikon doğrudan çalışan exe'den çıkarılır.
            using Icon? extracted = Icon.ExtractAssociatedIcon(exePath);
            if (extracted is not null)
            {
                Icon = (Icon)extracted.Clone();
            }
        }

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Göster", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Çıkış", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });

        _trayIcon = new NotifyIcon
        {
            Icon = Icon,
            Text = "HardwareMonitorByYuinn",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        Controls.Add(_webView);
        Load += (_, _) => ShrinkToFitScreenIfNeeded();
        Load += async (_, _) =>
        {
            splash?.SetProgress(30);
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            splash?.SetProgress(65);

            if (splash is not null)
            {
                // Sayfa (ve içindeki SignalR/JS başlatma) tamamen yüklenene kadar bekleyip splash'i
                // ancak o zaman kapatıyoruz — aksi hâlde splash kapanır kapanmaz boş/yarım bir pencere
                // görünürdü, bu da splash'in amacını (bekleme hissini gizlemek) boşa çıkarırdı.
                void OnNavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                    splash.CompleteAndClose(() =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke(() =>
                            {
                                Show();
                                Activate();
                            });
                        }
                    });
                }
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            }

            _webView.CoreWebView2.Navigate(startUrl);
        };

        if (startMinimized || splash is not null)
        {
            // Hide(), Application.Run'ın pencereyi görünür kılma sürecinin BİR PARÇASI olarak tetiklenen
            // Load olayı sırasında çağrılıyor — pencere henüz ekrana çizilmeden gizlendiğinden gözle
            // görülür bir açılıp-kapanma (flaş) yaşanmaz. Tepsi simgesi zaten yukarıda Visible=true.
            // startMinimized ise burada sonsuza dek gizli kalır; splash varsa yukarıdaki
            // NavigationCompleted tamamlanınca tekrar gösterilir.
            Load += (_, _) => Hide();
        }
    }

    // Yalnızca gerçekten gerekliyse (küçük ekranlı bir dizüstü vb.) küçültür — 900x700 altına hiç
    // inmez, bu yüzden Screen API'sinin döndürdüğü değer yanlışlıkla anlamsız küçük çıksa bile
    // (bkz. yukarıdaki not) pencere kullanılamaz hâle gelmez.
    private void ShrinkToFitScreenIfNeeded()
    {
        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        int maxWidth = Math.Max(900, workArea.Width - 40);
        int maxHeight = Math.Max(700, workArea.Height - 40);

        if (Width > maxWidth) Width = maxWidth;
        if (Height > maxHeight) Height = maxHeight;

        if (Width != 1280 || Height != 1280)
        {
            CenterToScreen();
        }
    }

    /// <summary>
    /// Host tarafı bir kapanma (ör. RemoteAccessController.Restart'ın çağırdığı StopApplication())
    /// pencereyi HER ZAMAN gerçekten kapatır — "sistem tepsisine küçült" ayarı burada uygulanmaz,
    /// aksi hâlde süreç hiç sonlanmaz ve Restart'ın başlattığı yeni süreç mutex'i hâlâ tutulduğu
    /// için sessizce çıkardı.
    /// </summary>
    internal void ForceClose()
    {
        _allowExit = true;
        if (IsHandleCreated)
        {
            BeginInvoke(Close);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && DesktopShellSettings.GetCloseBehavior() == DesktopShellSettings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DwmHelper.SetTitleBarColor(Handle, InitialBackground);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// site.js → window.chrome.webview.postMessage({type:"hwmon-bg", bg}) ile gönderilen, o an
    /// sayfada uygulanan GERÇEK arka plan rengini dinler; pencere zemini, WebView2'nin kendi zemini
    /// ve başlık çubuğu/kenarlık rengi (Windows 11'de birebir, bkz. DwmHelper) buna göre güncellenir.
    /// Tema Ayarlar sayfasından değiştirildiğinde de aynı olay tetiklenir, pencere anında uyum sağlar.
    /// </summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = doc.RootElement;
            if (root.GetProperty("type").GetString() != "hwmon-bg")
            {
                return;
            }

            Color bg = ColorTranslator.FromHtml(root.GetProperty("bg").GetString() ?? "#0b0f14");

            if (!IsHandleCreated)
            {
                return;
            }

            BeginInvoke(() =>
            {
                BackColor = bg;
                _webView.DefaultBackgroundColor = bg;
                DwmHelper.SetTitleBarColor(Handle, bg);
            });
        }
        catch
        {
            // Beklenmeyen/bozuk mesaj arayüzü etkilememeli, sessizce yok say.
        }
    }
}
