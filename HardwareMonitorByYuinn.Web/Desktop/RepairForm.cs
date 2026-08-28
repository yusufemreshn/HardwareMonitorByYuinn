using System.Drawing;
using System.Windows.Forms;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// Yalnızca <see cref="HardwareMonitorByYuinn.DataAccess.History.HistoryDatabaseRepair"/> gerçekten bir
/// sorun bulup onardığı zaman gösterilen ayrı bir ekran (bkz. DesktopShellRunner) — normal açılışta
/// (dosyalar sağlamken) bu form hiç oluşturulmaz. SplashForm ile aynı görsel dile sahip, ama kullanıcı
/// "arka planda ne olduğunu anlamadan bekleme" hissine kapılmasın diye hangi dosyanın nasıl onarıldığını
/// satır satır gösterir.
/// </summary>
internal sealed class RepairForm : Form
{
    private static readonly Color Background = ColorTranslator.FromHtml("#0b0f14");
    private static readonly Color TrackColor = ColorTranslator.FromHtml("#1b2430");
    private static readonly Color FillColor = ColorTranslator.FromHtml("#e0a83c");
    private static readonly Color TitleColor = ColorTranslator.FromHtml("#e7edf6");
    private static readonly Color SubtitleColor = ColorTranslator.FromHtml("#8ea0b8");

    private readonly Panel _fill;
    private readonly Panel _track;
    private readonly Label _statusLabel;

    internal RepairForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        Width = 460;
        Height = 190;
        BackColor = Background;

        var title = new Label
        {
            Text = "Veritabanı Onarılıyor",
            ForeColor = TitleColor,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 50,
        };

        _statusLabel = new Label
        {
            Text = "Geçen seferki beklenmedik kapanmadan kalan bir sorun düzeltiliyor…",
            ForeColor = SubtitleColor,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(24, 0, 24, 0),
        };

        _track = new Panel
        {
            BackColor = TrackColor,
            Height = 8,
            Margin = new Padding(0),
        };
        _fill = new Panel { BackColor = FillColor, Width = 0, Height = 8 };
        _track.Controls.Add(_fill);

        var trackWrapper = new Panel { Dock = DockStyle.Top, Height = 8, Padding = new Padding(48, 0, 48, 0) };
        trackWrapper.Controls.Add(_track);
        _track.Dock = DockStyle.Fill;

        Controls.Add(trackWrapper);
        Controls.Add(_statusLabel);
        Controls.Add(title);
    }

    /// <summary>Bir onarım adımını ve genel ilerlemeyi (0-100) gösterir. Çağıran taraf her adımdan sonra
    /// mesaj pompalamaktan (Application.DoEvents) sorumludur — bu form kendi mesaj döngüsünü çalıştırmaz.</summary>
    internal void SetStatus(string message, int percent)
    {
        _statusLabel.Text = message;
        _fill.Width = (int)(_track.Width * (Math.Clamp(percent, 0, 100) / 100.0));
    }
}
