using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.Entity.Hardware;

namespace HardwareMonitorByYuinn.Web.Desktop.Overlay;

/// <summary>
/// Oyun içinde çalışacak izleme paneli: çerçevesiz, tıklamaları oyuna geçiren (click-through),
/// her zaman üstte, köşeye sabitlenmiş küçük bir pencere. Grafik API hook'u / süreç enjeksiyonu
/// YOK (anti-cheat ban riski taşıyan RTSS/Afterburner tarzı yaklaşım bilerek kullanılmadı) —
/// Rainmeter/HWiNFO'nun "her zaman üstte panel"iyle aynı kategoride, düz bir masaüstü penceresi.
/// Bu yüzden gerçek tam-ekran-özel (exclusive fullscreen) modda görünmez; bu bilinen bir sınırlama.
///
/// Çizim <c>UpdateLayeredWindow</c> ile elle yapılıyor (normal <c>OnPaint</c>/<c>Form.Opacity</c>
/// DEĞİL): kullanıcı arka plan opaklığını yazıdan BAĞIMSIZ ayarlayabilsin istendiğinden (bkz.
/// OverlaySettings.BackgroundOpacity'nin doc yorumu). Form.Opacity tüm pencereyi (yazı dahil) tek
/// bir alfa çarpanıyla soluklaştırır; piksel-başına alfa ile sadece arka planı soluklaştırıp yazıyı
/// her zaman tam opak/keskin tutmanın tek yolu bu — bu yüzden <see cref="Render"/> kendi 32bpp
/// premultiplied-ARGB DIB'ini oluşturup doğrudan <c>UpdateLayeredWindow</c>'a veriyor.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const int HotkeyId = 0xA17;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkO = 0x4F;

    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Size
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(nint hWnd, nint hdcDst, ref Win32Point pptDst, ref Win32Size psize, nint hdcSrc, ref Win32Point pptSrc, int crKey, ref BlendFunction pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BitmapInfoHeader header, uint usage, out nint bits, nint hSection, uint offset);

    private const int ContentPadding = 14;
    private const int ItemGap = 20; // aynı satırdaki (yan yana) metrikler arası boşluk
    private const int RowGap = 4;   // satırlar (alt alta) arası ek boşluk
    private const int ScreenMargin = 24;

    private readonly TimeSeriesStore _store;
    private readonly System.Windows.Forms.Timer _timer;

    private OverlaySettings _settings = OverlaySettings.Default;
    private List<List<OverlayMetricKey>> _layoutRows = OverlayLayoutFormat.Parse(OverlaySettings.Default.Layout);
    private List<List<(string Label, string Value)>> _rowTexts = [];
    private Font _font = new("Segoe UI", 11f, FontStyle.Bold);
    private float _lineHeight;
    private float _maxRowWidth;
    private Size _contentSize;
    private bool _hotkeyRegistered;

    /// <summary>
    /// Ctrl+Alt+O ile elle aç/kapat yapıldığında otomatik (oyun algılama) mantığı geçersiz kılınır;
    /// null = otomatik mantık geçerli, true/false = kullanıcı zorladı. Ana anahtar (Enabled)
    /// kapatılınca bu sıfırlanır, tekrar açıldığında temiz bir otomatik durumdan başlar.
    /// </summary>
    private bool? _manualOverride;

    internal OverlayForm(TimeSeriesStore store)
    {
        _store = store;

        Visible = false;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;

        // Handle'ı burada zorla oluştur (Show() çağrılmasını BEKLEMEDEN): Enabled=false ile
        // başlayan bir kurulumda overlay hiç Show() edilmeyebilir, ama Ctrl+Alt+O kısayolunun ilk
        // basıştan itibaren çalışması için OnHandleCreated'daki RegisterHotKey'in en baştan
        // tetiklenmiş olması gerekiyor — görünürlük durumuna bağlı değil.
        _ = Handle;

        RebuildLayout();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Tick();
        Tick();
        _timer.Start();
    }

    // WS_EX_LAYERED burada koşulsuz zorlanıyor çünkü Render() SADECE UpdateLayeredWindow ile
    // çiziyor (Form.Opacity hiç kullanılmıyor, dolayısıyla WinForms'un bu bayrağı kendiliğinden
    // ekleyeceği başka bir yol yok) — UpdateLayeredWindow, pencerede bu stil olmadan başarısız olur.
    // WS_EX_TRANSPARENT: tıklamalar oyuna geçer. WS_EX_TOOLWINDOW: görev çubuğu/Alt-Tab'da hiç
    // görünmez. WS_EX_NOACTIVATE: tıklanamaz/odaklanamaz olduğundan oyunun fokusunu asla çalmaz.
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    // OnHandleCreated, DPI değişimi gibi nedenlerle handle yeniden oluşturulduğunda da tekrar
    // tetiklenir; bu yüzden hotkey kaydı burada kendini otomatik onarır (bkz. DwmHelper'ın
    // ShellForm.OnHandleCreated'daki aynı deseni). Kayıt başarısız olursa (başka bir uygulama —
    // Discord/RTSS vb. — Ctrl+Alt+O'yu zaten almış olabilir) sessizce yok sayılır; overlay yine
    // de Ayarlar sayfasından ve oyun algılamayla normal çalışmaya devam eder.
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, ModControl | ModAlt, VkO);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
        }

        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            bool nowVisible = !Visible;
            _manualOverride = nowVisible;
            Visible = nowVisible;
            return;
        }

        base.WndProc(ref m);
    }

    private void Tick()
    {
        OverlaySettings fresh = OverlaySettingsStore.Load();
        bool geometryChanged = fresh.Corner != _settings.Corner
            || fresh.OffsetXPercent != _settings.OffsetXPercent
            || fresh.OffsetYPercent != _settings.OffsetYPercent
            || fresh.FontSize != _settings.FontSize
            || fresh.Layout != _settings.Layout;
        bool paintSettingsChanged = fresh.BackgroundColor != _settings.BackgroundColor
            || fresh.BackgroundOpacity != _settings.BackgroundOpacity
            || fresh.LabelColor != _settings.LabelColor
            || fresh.ValueColor != _settings.ValueColor;

        if (!fresh.Enabled && _settings.Enabled)
        {
            _manualOverride = null;
        }

        _settings = fresh;

        if (geometryChanged)
        {
            RebuildLayout(); // bu zaten sonunda Render() çağırıyor
        }

        HardwareSnapshot? snapshot = _store.GetLatestSnapshot();
        List<List<(string Label, string Value)>> newRowTexts = BuildRowTexts(snapshot, _layoutRows);
        bool contentChanged = !RowTextsEqual(newRowTexts, _rowTexts);
        _rowTexts = newRowTexts;

        bool isGameActive = snapshot?.Fps?.SourceProcessName is not null;
        bool shouldBeVisible = _settings.Enabled
            && (_manualOverride ?? (!_settings.OnlyShowDuringGame || isGameActive));

        if (Visible != shouldBeVisible)
        {
            Visible = shouldBeVisible;
        }

        if (!geometryChanged && (paintSettingsChanged || contentChanged))
        {
            Render();
        }
    }

    private void RebuildLayout()
    {
        _font.Dispose();
        _font = new Font("Segoe UI", FontSizeToPoints(_settings.FontSize), FontStyle.Bold);
        _layoutRows = OverlayLayoutFormat.Parse(_settings.Layout);

        using Graphics g = CreateGraphics();
        _lineHeight = _font.GetHeight(g) + 4f;

        // Pencere boyutu CANLI değerlerin metin uzunluğuna göre değil, her metriğin "en kötü
        // durum" (worst-case) örnek değerine göre hesaplanır — aksi hâlde "7%" ile "100%" arasında
        // geçiş yapan bir değer her saniye pencereyi büyütüp küçülterek titremeye (jitter) yol açardı.
        _maxRowWidth = 0f;
        foreach (List<OverlayMetricKey> row in _layoutRows)
        {
            float rowWidth = 0f;
            for (int i = 0; i < row.Count; i++)
            {
                rowWidth += g.MeasureString(GetLabel(row[i]) + ": ", _font).Width + g.MeasureString(GetWorstCaseValue(row[i]), _font).Width;
                if (i < row.Count - 1)
                {
                    rowWidth += ItemGap;
                }
            }

            _maxRowWidth = Math.Max(_maxRowWidth, rowWidth);
        }

        RecomputeGeometry();
    }

    private void RecomputeGeometry()
    {
        int rowCount = Math.Max(_layoutRows.Count, 1);
        int width = ContentPadding * 2 + (int)Math.Ceiling(_maxRowWidth);
        int height = ContentPadding * 2 + (int)Math.Ceiling(_lineHeight * rowCount) + RowGap * Math.Max(rowCount - 1, 0);
        _contentSize = new Size(width, height);
        if (Size != _contentSize)
        {
            Size = _contentSize;
        }

        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Point location = _settings.Corner switch
        {
            OverlayCorner.TopLeft => new Point(area.Left + ScreenMargin, area.Top + ScreenMargin),
            OverlayCorner.BottomRight => new Point(area.Right - ScreenMargin - width, area.Bottom - ScreenMargin - height),
            OverlayCorner.BottomLeft => new Point(area.Left + ScreenMargin, area.Bottom - ScreenMargin - height),
            // Serbest modda WorkingArea DEĞİL, ekranın TAM fiziksel sınırları (Bounds) kullanılır:
            // WorkingArea görev çubuğunu dışarıda bırakır, bu yüzden %100 bile görev çubuğu
            // genişliği kadar erken durup kullanıcının "sağa tam gitmiyor" diye algıladığı bir boşluk
            // bırakıyordu. Overlay zaten tıklama geçirgen (click-through) olduğundan görev çubuğunun
            // üzerine gelmesi işlevsel bir sorun değil — kullanıcı serbest modda bilerek tam kontrolü
            // istiyor (gerçek kullanıcı geri bildirimi).
            OverlayCorner.Custom => CustomLocation(Screen.PrimaryScreen?.Bounds ?? area, width, height),
            _ => new Point(area.Right - ScreenMargin - width, area.Top + ScreenMargin), // TopRight
        };
        if (Location != location)
        {
            Location = location;
        }

        Render();
    }

    // OffsetXPercent/OffsetYPercent, kutunun gidebileceği TOPLAM MESAFE içindeki konumunun yüzdesidir
    // (0 = en sola/üste, 100 = en sağa/alta) — ekranın HAM genişliğinin yüzdesi DEĞİL (bkz. aşağıdaki
    // settings.js'teki aynı düzeltme; önizleme küçük sahnesine oranla büyük, gerçek overlay gerçek
    // ekrana oranla küçük olduğundan ham genişliğe göre hesap ikisinde tutarsız sonuç verirdi).
    //
    // Sol/üst/alt kenarlar TAM ekrana yaslı (taşma yok, ilk sürümdeki gibi) — yalnızca SAĞ kenar
    // kutunun %10'u kadar ötesine taşabiliyor (kullanıcı geri bildirimiyle birkaç kez ayarlandı:
    // %50 → %25 → %40 → %5 → son olarak %10'da karar kılındı).
    private Point CustomLocation(Rectangle area, int width, int height)
    {
        int rightOvershoot = (int)Math.Round(width * 0.10);
        int minX = area.Left;
        int maxX = area.Right - width + rightOvershoot;
        int minY = area.Top;
        int maxY = area.Bottom - height;

        int travelX = Math.Max(0, maxX - minX);
        int travelY = Math.Max(0, maxY - minY);
        int x = minX + (int)Math.Round(travelX * _settings.OffsetXPercent / 100.0);
        int y = minY + (int)Math.Round(travelY * _settings.OffsetYPercent / 100.0);
        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);
        return new Point(x, y);
    }

    /// <summary>
    /// 32bpp premultiplied-ARGB bir DIB çizip <c>UpdateLayeredWindow</c> ile doğrudan ekrana
    /// gönderir. <see cref="PixelFormat.Format32bppPArgb"/> formatlı bir <see cref="Bitmap"/>
    /// üzerine normal (straight-alpha) <see cref="Color.FromArgb(int,int,int,int)"/> renkleriyle
    /// çizim yapıldığında GDI+ premultiplied dönüşümü kendisi doğru yapar — bu, piksel-başına alfalı
    /// bir katmanlı pencerede rengin kenarlarda (anti-alias/yazı) sızdırmadan (fringing) doğru
    /// karışması için standart/gerekli teknik.
    /// </summary>
    private void Render()
    {
        if (!IsHandleCreated || _contentSize.Width <= 0 || _contentSize.Height <= 0)
        {
            return;
        }

        int width = _contentSize.Width;
        int height = _contentSize.Height;

        nint screenDc = GetDC(nint.Zero);
        nint hBitmap = nint.Zero;
        nint memDc = nint.Zero;
        nint oldBitmap = nint.Zero;
        try
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // negatif: üstten-alta (top-down), GDI+ Bitmap'in satır sırasıyla eşleşir
                Planes = 1,
                BitCount = 32,
                Compression = 0, // BI_RGB
            };
            hBitmap = CreateDIBSection(screenDc, ref header, 0 /* DIB_RGB_COLORS */, out nint bits, nint.Zero, 0);
            if (hBitmap == nint.Zero || bits == nint.Zero)
            {
                return;
            }

            using (var bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                int bgAlpha = (int)Math.Round(_settings.BackgroundOpacity / 100.0 * 255);
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, width, height), radius: 10))
                using (var bgBrush = new SolidBrush(Color.FromArgb(bgAlpha, _settings.BackgroundColor)))
                {
                    g.FillPath(bgBrush, path);
                }

                float y = ContentPadding;
                using var labelBrush = new SolidBrush(_settings.LabelColor);
                using var valueBrush = new SolidBrush(_settings.ValueColor);
                foreach (List<(string Label, string Value)> row in _rowTexts)
                {
                    float x = ContentPadding;
                    foreach ((string label, string value) in row)
                    {
                        string labelText = label + ": ";
                        g.DrawString(labelText, _font, labelBrush, x, y);
                        x += g.MeasureString(labelText, _font).Width;
                        g.DrawString(value, _font, valueBrush, x, y);
                        x += g.MeasureString(value, _font).Width + ItemGap;
                    }

                    y += _lineHeight + RowGap;
                }
            }

            memDc = CreateCompatibleDC(screenDc);
            oldBitmap = SelectObject(memDc, hBitmap);

            var size = new Win32Size { Cx = width, Cy = height };
            var srcPoint = new Win32Point { X = 0, Y = 0 };
            var dstPoint = new Win32Point { X = Location.X, Y = Location.Y };
            var blend = new BlendFunction { BlendOp = AcSrcOver, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };

            UpdateLayeredWindow(Handle, screenDc, ref dstPoint, ref size, memDc, ref srcPoint, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (memDc != nint.Zero)
            {
                if (oldBitmap != nint.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                }

                DeleteDC(memDc);
            }

            if (hBitmap != nint.Zero)
            {
                DeleteObject(hBitmap);
            }

            ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static List<List<(string Label, string Value)>> BuildRowTexts(HardwareSnapshot? snapshot, List<List<OverlayMetricKey>> layoutRows) =>
        layoutRows.Select(row => row.Select(key => (GetLabel(key), GetLiveValue(snapshot, key))).ToList()).ToList();

    // _rowTexts her zaman _layoutRows'tan (sadece geometryChanged true iken değişir) üretildiğinden,
    // bu karşılaştırmanın çağrıldığı an (bkz. Tick) iki tarafın satır/sütun ŞEKLİ zaten aynıdır;
    // yalnızca değerler değişmiş olabilir. Bu yüzden düzleştirip sırayla karşılaştırmak yeterli ve
    // doğrudur (iç içe List<T>'lerin varsayılan eşitliği referans tabanlı olduğundan SequenceEqual
    // dıştan içe doğrudan uygulanamaz).
    private static bool RowTextsEqual(List<List<(string Label, string Value)>> a, List<List<(string Label, string Value)>> b) =>
        a.SelectMany(row => row).SequenceEqual(b.SelectMany(row => row));

    private static string GetLabel(OverlayMetricKey key) => key switch
    {
        OverlayMetricKey.CpuUsage => "CPU",
        OverlayMetricKey.CpuTemp => "CPU °C",
        OverlayMetricKey.GpuUsage => "GPU",
        OverlayMetricKey.GpuTemp => "GPU °C",
        OverlayMetricKey.Ram => "RAM",
        OverlayMetricKey.Fps => "FPS",
        OverlayMetricKey.CpuClock => "CPU MHz",
        OverlayMetricKey.CpuPower => "CPU W",
        OverlayMetricKey.GpuClock => "GPU MHz",
        OverlayMetricKey.GpuHotSpot => "GPU Hot Spot",
        OverlayMetricKey.GpuPower => "GPU W",
        OverlayMetricKey.GpuVram => "VRAM",
        OverlayMetricKey.RamUsedGb => "RAM GB",
        OverlayMetricKey.FpsLow1 => "%1 Low",
        OverlayMetricKey.FpsLow01 => "%0.1 Low",
        OverlayMetricKey.FrameTime => "Kare Süresi",
        OverlayMetricKey.NetDown => "İndirme",
        OverlayMetricKey.NetUp => "Yükleme",
        _ => key.ToString(),
    };

    // Pencere boyutu hesaplanırken kullanılan, metriğin alabileceği en uzun gerçekçi değeri temsil
    // eden örnek metin (bkz. RebuildLayout'taki jitter açıklaması) — canlı değer değil.
    private static string GetWorstCaseValue(OverlayMetricKey key) => key switch
    {
        OverlayMetricKey.CpuTemp or OverlayMetricKey.GpuTemp or OverlayMetricKey.GpuHotSpot => "100°C",
        OverlayMetricKey.Fps or OverlayMetricKey.FpsLow1 or OverlayMetricKey.FpsLow01 => "999",
        OverlayMetricKey.CpuClock => "5999 MHz",
        OverlayMetricKey.GpuClock => "2999 MHz",
        OverlayMetricKey.CpuPower or OverlayMetricKey.GpuPower => "999 W",
        OverlayMetricKey.GpuVram => "24.0/24.0 GB",
        OverlayMetricKey.RamUsedGb => "128.0/128.0 GB",
        OverlayMetricKey.FrameTime => "999.9 ms",
        OverlayMetricKey.NetDown or OverlayMetricKey.NetUp => "9999 Mbps",
        _ => "100%",
    };

    private static string GetLiveValue(HardwareSnapshot? snapshot, OverlayMetricKey key) => key switch
    {
        OverlayMetricKey.CpuUsage => FormatPercent(snapshot?.Cpu?.TotalLoadPercent),
        OverlayMetricKey.CpuTemp => FormatTemp(snapshot?.Cpu?.PackageTemperatureC),
        OverlayMetricKey.GpuUsage => FormatPercent(snapshot?.Gpu?.LoadPercent),
        OverlayMetricKey.GpuTemp => FormatTemp(snapshot?.Gpu?.CoreTemperatureC),
        OverlayMetricKey.Ram => FormatPercent(snapshot?.Ram?.UsedPercent),
        OverlayMetricKey.Fps => snapshot?.Fps?.FramesPerSecond is double fps ? $"{fps:0}" : "—",
        OverlayMetricKey.CpuClock => FormatMhz(snapshot?.Cpu?.PackageClockMhz),
        OverlayMetricKey.CpuPower => FormatWatts(snapshot?.Cpu?.PackagePowerWatts),
        OverlayMetricKey.GpuClock => FormatMhz(snapshot?.Gpu?.CoreClockMhz),
        OverlayMetricKey.GpuHotSpot => FormatTemp(snapshot?.Gpu?.HotSpotTemperatureC),
        OverlayMetricKey.GpuPower => FormatWatts(snapshot?.Gpu?.PowerWatts),
        OverlayMetricKey.GpuVram => FormatGbPair(snapshot?.Gpu?.MemoryUsedMb / 1024, snapshot?.Gpu?.MemoryTotalMb / 1024),
        OverlayMetricKey.RamUsedGb => FormatGbPair(snapshot?.Ram?.UsedGb, snapshot?.Ram?.TotalGb),
        OverlayMetricKey.FpsLow1 => snapshot?.Fps?.Low1PercentFps is double low1 ? $"{low1:0}" : "—",
        OverlayMetricKey.FpsLow01 => snapshot?.Fps?.LowPoint1PercentFps is double low01 ? $"{low01:0}" : "—",
        OverlayMetricKey.FrameTime => snapshot?.Fps?.FrameTimeMs is double frameTime ? $"{frameTime:0.0} ms" : "—",
        OverlayMetricKey.NetDown => FormatMbps(snapshot?.Network?.DownloadBytesPerSec),
        OverlayMetricKey.NetUp => FormatMbps(snapshot?.Network?.UploadBytesPerSec),
        _ => "—",
    };

    private static string FormatPercent(double? v) => v.HasValue ? $"{v.Value:0}%" : "—";

    private static string FormatTemp(double? v) => v.HasValue ? $"{v.Value:0}°C" : "—";

    private static string FormatMhz(double? v) => v.HasValue ? $"{v.Value:0} MHz" : "—";

    private static string FormatWatts(double? v) => v.HasValue ? $"{v.Value:0} W" : "—";

    private static string FormatGbPair(double? usedGb, double? totalGb) =>
        usedGb.HasValue && totalGb.HasValue ? $"{usedGb.Value:0.0}/{totalGb.Value:0.0} GB" : "—";

    // byte/sn -> Mbps: *8 (bit'e çevir) / 1_000_000 (mega).
    private static string FormatMbps(double? bytesPerSec) =>
        bytesPerSec.HasValue ? $"{bytesPerSec.Value * 8 / 1_000_000:0} Mbps" : "—";

    private static float FontSizeToPoints(OverlayFontSize size) => size switch
    {
        OverlayFontSize.Small => 9f,
        OverlayFontSize.Large => 13f,
        _ => 11f,
    };

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _font.Dispose();
        }

        base.Dispose(disposing);
    }
}
