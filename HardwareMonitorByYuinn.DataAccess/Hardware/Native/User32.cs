using System.Runtime.InteropServices;

namespace HardwareMonitorByYuinn.DataAccess.Hardware.Native;

/// <summary>
/// Ekrana o an hangi pencerenin (ve dolayısıyla hangi sürecin) kare sunduğunu belirlemek için
/// kullanılır — kare hızı yalnızca kullanıcının aktif olarak kullandığı uygulama için anlamlıdır.
/// </summary>
internal static class User32
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    /// <summary>Ön plandaki (aktif) pencerenin süreç kimliği. Böyle bir pencere yoksa <c>null</c>.</summary>
    public static int? GetForegroundProcessId()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == nint.Zero)
            return null;

        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == 0 ? null : (int)processId;
    }
}
