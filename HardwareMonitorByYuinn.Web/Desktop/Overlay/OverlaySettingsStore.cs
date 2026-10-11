using System.Drawing;
using Microsoft.Win32;

namespace HardwareMonitorByYuinn.Web.Desktop.Overlay;

/// <summary>
/// Oyun içi izleme panelinin (overlay) ayarlarını HKCU'da tutar; <see cref="DesktopShellSettings"/> ile
/// aynı desen (ayrı bir JSON dosyası yok, kayıt defteri tek doğru kaynak). Hem Ayarlar sayfasının
/// OverlayController'ı (ASP.NET Core istek iş parçacığı) hem OverlayForm'un 1 sn'lik zamanlayıcısı
/// (WinForms STA iş parçacığı) aynı anahtarı okur/yazar; kayıt defteri erişimi işletim sistemi
/// seviyesinde zaten eşzamanlıdır, ek bir kilide gerek yok. Değerlerin ardışık yazımı arasında kısa
/// süreli tutarsız bir okuma teorik olarak mümkün ama kozmetik; bir sonraki saniyelik tur kendini düzeltir.
/// </summary>
internal static class OverlaySettingsStore
{
    private const string KeyPath = @"Software\HardwareMonitorByYuinn\Overlay";

    internal static OverlaySettings Load()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        if (key is null)
        {
            return OverlaySettings.Default;
        }

        OverlaySettings d = OverlaySettings.Default;

        bool enabled = ReadBool(key, "Enabled", d.Enabled);
        bool onlyDuringGame = ReadBool(key, "OnlyShowDuringGame", d.OnlyShowDuringGame);
        int backgroundOpacity = ReadInt(key, "BackgroundOpacity", d.BackgroundOpacity, min: 0, max: 100);

        OverlayCorner corner = Enum.TryParse(key.GetValue("Corner") as string, out OverlayCorner parsedCorner)
            ? parsedCorner
            : d.Corner;

        OverlayFontSize fontSize = Enum.TryParse(key.GetValue("FontSize") as string, out OverlayFontSize parsedFontSize)
            ? parsedFontSize
            : d.FontSize;

        string layoutRaw = key.GetValue("Layout") as string ?? d.Layout;
        string layout = OverlayLayoutFormat.IsValid(layoutRaw) ? layoutRaw : d.Layout;

        double offsetX = ReadPercent(key, "OffsetXPercent", d.OffsetXPercent);
        double offsetY = ReadPercent(key, "OffsetYPercent", d.OffsetYPercent);

        Color backgroundColor = ParseColor(key.GetValue("BackgroundColor") as string) ?? d.BackgroundColor;
        Color labelColor = ParseColor(key.GetValue("LabelColor") as string) ?? d.LabelColor;
        Color valueColor = ParseColor(key.GetValue("ValueColor") as string) ?? d.ValueColor;

        return new OverlaySettings(enabled, layout, corner, offsetX, offsetY, fontSize, onlyDuringGame, backgroundColor, backgroundOpacity, labelColor, valueColor);
    }

    internal static void Save(OverlaySettings settings)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath);

        key.SetValue("Enabled", settings.Enabled ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("OnlyShowDuringGame", settings.OnlyShowDuringGame ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("BackgroundOpacity", Math.Clamp(settings.BackgroundOpacity, 0, 100), RegistryValueKind.DWord);
        key.SetValue("Corner", settings.Corner.ToString(), RegistryValueKind.String);
        key.SetValue("FontSize", settings.FontSize.ToString(), RegistryValueKind.String);
        key.SetValue("Layout", settings.Layout, RegistryValueKind.String);
        key.SetValue("OffsetXPercent", Math.Clamp(settings.OffsetXPercent, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.String);
        key.SetValue("OffsetYPercent", Math.Clamp(settings.OffsetYPercent, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.String);
        key.SetValue("BackgroundColor", ToHex(settings.BackgroundColor), RegistryValueKind.String);
        key.SetValue("LabelColor", ToHex(settings.LabelColor), RegistryValueKind.String);
        key.SetValue("ValueColor", ToHex(settings.ValueColor), RegistryValueKind.String);
    }

    private static bool ReadBool(RegistryKey key, string name, bool defaultValue) =>
        key.GetValue(name) is int value ? value != 0 : defaultValue;

    private static int ReadInt(RegistryKey key, string name, int defaultValue, int min, int max) =>
        key.GetValue(name) is int value ? Math.Clamp(value, min, max) : defaultValue;

    private static double ReadPercent(RegistryKey key, string name, double defaultValue) =>
        double.TryParse(key.GetValue(name) as string, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? Math.Clamp(value, 0, 100)
            : defaultValue;

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch (Exception)
        {
            // Kayıt defterindeki değer elle/bozuk bir araçla değiştirilmiş olabilir; geçersizse
            // sessizce varsayılana düşülür, uygulama çökmez.
            return null;
        }
    }
}
