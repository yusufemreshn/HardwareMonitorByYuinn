using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32;

namespace HardwareMonitorByYuinn.Web.Controllers;

/// <summary>
/// Windows açılışında otomatik başlatmayı HKCU\...\Run anahtarı üzerinden yönetir. Kayıt defteri
/// anahtarının kendisi zaten "tek doğru kaynak" olduğundan (RemoteAccess'teki gibi ayrı bir JSON
/// dosyasına gerek yok) — Status her zaman kayıt defterinden okur.
/// </summary>
public sealed class StartupController : Controller
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HardwareMonitorByYuinn";

    [HttpGet]
    public IActionResult Status()
    {
        string exePath = GetExecutablePath();
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        string? savedValue = key?.GetValue(ValueName) as string;

        return Json(new
        {
            enabled = !string.IsNullOrEmpty(savedValue),
            // Uygulama farklı bir klasöre taşındıysa kayıtlı yol artık geçerli değildir; arayüz bu
            // durumda kullanıcıya "yeniden kaydedin" diyebilsin diye ayrıca bildiriyoruz.
            pathMismatch = !string.IsNullOrEmpty(savedValue) && !string.Equals(savedValue, Quote(exePath), StringComparison.OrdinalIgnoreCase),
            exePath
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Configure(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, Quote(GetExecutablePath()));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        return Ok(new { success = true });
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

    private static string Quote(string path) => $"\"{path}\"";
}
