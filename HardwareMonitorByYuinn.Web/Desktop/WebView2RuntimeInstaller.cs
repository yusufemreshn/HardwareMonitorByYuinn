using System.Diagnostics;
using Microsoft.Win32;

namespace HardwareMonitorByYuinn.Web.Desktop;

/// <summary>
/// WebView2 Runtime'ın (Evergreen) makinede kurulu olup olmadığını Microsoft'un belgelediği
/// registry anahtarından kontrol eder, eksikse Runtime\MicrosoftEdgeWebView2Setup.exe
/// bootstrapper'ını sessizce çalıştırır. Bu uygulama zaten admin yetkisiyle çalıştığından
/// (app.manifest → requireAdministrator) kurulum otomatik olarak makine geneli (per-machine)
/// yapılır, ekstra bir UAC istemi çıkmaz.
/// </summary>
internal static class WebView2RuntimeInstaller
{
    // Microsoft'un tüm WebView2/Edge Evergreen kurulumları için sabit istemci kimliği
    // (bkz. learn.microsoft.com/microsoft-edge/webview2/concepts/distribution).
    private const string ClientId = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    internal static bool IsInstalled() =>
        HasVersion(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + ClientId)
        || HasVersion(Registry.LocalMachine, @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + ClientId)
        || HasVersion(Registry.CurrentUser, @"Software\Microsoft\EdgeUpdate\Clients\" + ClientId);

    private static bool HasVersion(RegistryKey root, string subKeyPath)
    {
        using RegistryKey? key = root.OpenSubKey(subKeyPath);
        string? version = key?.GetValue("pv") as string;
        return !string.IsNullOrEmpty(version) && version != "0.0.0.0";
    }

    /// <summary>
    /// İnternet bağlantısı gerektirir (bootstrapper, runtime'ın kendisini Microsoft'un
    /// sunucularından indirir). Bağlantı yoksa/kurulum başarısız olursa false döner;
    /// çağıran taraf kullanıcıya bunu bildirmeli.
    /// </summary>
    internal static bool TryInstallSilently()
    {
        string bootstrapperPath = Path.Combine(AppContext.BaseDirectory, "Runtime", "MicrosoftEdgeWebView2Setup.exe");
        if (!File.Exists(bootstrapperPath))
        {
            return false;
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(bootstrapperPath)
            {
                Arguments = "/silent /install",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            process?.WaitForExit(120_000);
            return IsInstalled();
        }
        catch
        {
            return false;
        }
    }
}
