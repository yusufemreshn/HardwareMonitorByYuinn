using System.Drawing;
using System.Windows.Forms;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// Uygulama açılırken (WebView2 ortamı kurulup Kestrel'in sunduğu sayfa yüklenene kadar geçen,
/// genelde birkaç saniyelik) bekleme süresini görünür kılan çerçevesiz bir bekleme ekranı.
/// Gerçek ilerlemeyi hızlandırmaz, yalnızca <see cref="SetProgress"/> ile bildirilen kilometre
/// taşları arasında yumuşak bir animasyonla "dolan" bir çubuk gösterir, böylece kullanıcı boş bir
/// ekranla beklemek yerine bir şeylerin olduğunu görür.
/// </summary>
internal sealed class SplashForm : Form
{
    private static readonly Color Background = ColorTranslator.FromHtml("#0b0f14");
    private static readonly Color TrackColor = ColorTranslator.FromHtml("#1b2430");
    private static readonly Color FillColor = ColorTranslator.FromHtml("#35d0ba");
    private static readonly Color TitleColor = ColorTranslator.FromHtml("#e7edf6");
    private static readonly Color SubtitleColor = ColorTranslator.FromHtml("#8ea0b8");

    private readonly Panel _track;
    private readonly Panel _fill;
    private readonly System.Windows.Forms.Timer _animationTimer;

    /// <summary>En son <see cref="SetProgress"/> ile bildirilen gerçek kilometre taşı.</summary>
    private double _target;

    /// <summary>O an ekranda çizilen değer; her tikte <see cref="_target"/>'e doğru yumuşakça ilerler.</summary>
    private double _displayed;

    internal SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        Width = 420;
        Height = 170;
        BackColor = Background;

        var title = new Label
        {
            Text = "HardwareMonitorByYuinn",
            ForeColor = TitleColor,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 60,
        };

        var subtitle = new Label
        {
            Text = Loc.T("Program başlatılıyor…"),
            ForeColor = SubtitleColor,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 32,
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
        Controls.Add(subtitle);
        Controls.Add(title);

        // Segoe UI'ın sistemde bulunmadığı (çok eski/özelleştirilmiş) durumlarda Label constructor'ı
        // yine de en yakın uygun fontu seçer, bu yüzden ayrı bir yedek mantığa gerek yok.

        _animationTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _animationTimer.Tick += (_, _) => Animate();
        _animationTimer.Start();

        FormClosed += (_, _) => _animationTimer.Stop();
    }

    /// <summary>
    /// Gerçek bir kilometre taşına ulaşıldığını bildirir (0-100). Çubuk buraya anında zıplamaz,
    /// <see cref="Animate"/> aracılığıyla yumuşakça yaklaşır, ani sıçramalar yerine akıcı bir
    /// dolma hissi vermek için.
    /// </summary>
    internal void SetProgress(int percent)
    {
        double clamped = Math.Clamp(percent, 0, 100);
        if (IsHandleCreated)
        {
            BeginInvoke(() => _target = clamped);
        }
        else
        {
            _target = clamped;
        }
    }

    /// <summary>
    /// Son kilometre taşına (%100) ulaşıldığında çağrılır: çubuğun görsel olarak dolmasını bekler,
    /// kısa bir duraklamayla tamamlanmış hissi verir, sonra kapanır ve <paramref name="onClosed"/>
    /// çağrılır (genelde asıl pencereyi göstermek için).
    /// </summary>
    internal async void CompleteAndClose(Action onClosed)
    {
        _target = 100;
        while (_displayed < 100 && !IsDisposed)
        {
            await Task.Delay(30);
        }

        if (!IsDisposed)
        {
            await Task.Delay(200);
        }

        if (!IsDisposed)
        {
            Close();
        }

        onClosed();
    }

    private void Animate()
    {
        if (_displayed >= _target)
        {
            // Bir sonraki gerçek kilometre taşı gelene kadar tamamen dursun, ama uzun sürerse
            // ekranın "donmuş" görünmemesi için hedefin biraz ilerisine kadar ufak ufak sürünsün.
            double creepCeiling = Math.Min(100, _target + 6);
            if (_displayed < creepCeiling)
            {
                _displayed += 0.05;
            }
        }
        else
        {
            _displayed += Math.Max(0.4, (_target - _displayed) * 0.12);
            if (_displayed > _target) _displayed = _target;
        }

        _fill.Width = (int)(_track.Width * (_displayed / 100.0));
    }
}
