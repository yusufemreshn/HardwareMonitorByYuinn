using System.Drawing;
using System.Runtime.InteropServices;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// Pencerenin başlık çubuğunu/kenarlığını (Windows'un çizdiği, uygulama CSS'inin hiç erişemediği
/// "non-client" alan) sayfanın o anki gerçek arka plan rengiyle boyar. Ayarlanmazsa Windows bu alanı
/// her zaman aydınlık çizer, koyu bir sayfanın üstünde beyaz, uyumsuz bir kenar/başlık çubuğu olarak
/// görünür.
/// </summary>
internal static class DwmHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19; // Windows 10 20H1 öncesi derlemeler
    private const int DwmwaBorderColor = 34; // Yalnızca Windows 11 build 22000+
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>
    /// Windows 11'de (build 22000+) başlık çubuğunu/kenarlığını <paramref name="background"/> ile
    /// BİREBİR aynı renge boyar; kullanıcının özel rengi/hazır teması ne olursa olsun tam eşleşir.
    /// Bu üç öznitelik (DWMWA_*_COLOR) yalnızca Windows 11'de desteklendiği için Windows 10'da
    /// sessizce başarısız olur; o durumda ikili karanlık/aydınlık moda (DWMWA_USE_IMMERSIVE_DARK_MODE)
    /// düşülür, tam renk eşleşmez ama en azından koyu bir sayfada beyaz çakmaz.
    /// </summary>
    internal static void SetTitleBarColor(nint handle, Color background)
    {
        bool dark = RelativeLuminance(background) < 0.5;

        int darkValue = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref darkValue, sizeof(int));
        }

        int colorRef = ToColorRef(background);
        if (DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref colorRef, sizeof(int)) == 0)
        {
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref colorRef, sizeof(int));
            int textColorRef = ToColorRef(dark ? Color.White : Color.Black);
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColorRef, sizeof(int));
        }
    }

    // DWM'nin COLORREF formatı 0x00BBGGRR'dir (System.Drawing.Color.ToArgb()'nin 0xAARRGGBB
    // sırasından FARKLI); bayt sırası ters çevrilmezse renkler kanal karışıklığıyla yanlış çıkar.
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static double RelativeLuminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
