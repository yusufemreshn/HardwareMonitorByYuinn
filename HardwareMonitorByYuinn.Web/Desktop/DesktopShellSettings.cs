using Microsoft.Win32;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// Pencere kapat (X) düğmesine basılınca ne olacağını (sistem tepsisine küçült / uygulamayı kapat)
/// HKCU'da tutar — StartupController'daki Run anahtarı deseniyle aynı: kayıt defteri tek doğru
/// kaynaktır, ayrı bir JSON dosyasına gerek yok. Hem DesktopShellController (Ayarlar sayfasının
/// okuma/yazma uç noktaları) hem ShellForm (kapanma anında gerçek kararı verir) bu sınıfı kullanır.
/// </summary>
internal static class DesktopShellSettings
{
    private const string KeyPath = @"Software\HardwareMonitorByYuinn\DesktopShell";
    private const string ValueName = "CloseBehavior";

    internal const string MinimizeToTray = "Tray";
    internal const string Exit = "Exit";

    internal static string GetCloseBehavior()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        string? value = key?.GetValue(ValueName) as string;
        return value == Exit ? Exit : MinimizeToTray;
    }

    internal static void SetCloseBehavior(string value)
    {
        string normalized = value == Exit ? Exit : MinimizeToTray;
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, normalized);
    }
}
