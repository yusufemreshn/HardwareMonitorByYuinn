using HardwareMonitorByYuinn.Web.Desktop;
using Microsoft.AspNetCore.Mvc;

namespace HardwareMonitorByYuinn.Web.Controllers;

/// <summary>
/// Masaüstü penceresinin kapat (X) düğmesi davranışını (sistem tepsisine küçült / uygulamayı kapat)
/// yönetir. Gerçek ayar deposu Desktop/DesktopShellSettings.cs'dedir (HKCU); bu controller yalnızca
/// Ayarlar sayfasının okuma/yazma uç noktalarıdır.
/// </summary>
public sealed class DesktopShellController : Controller
{
    [HttpGet]
    public IActionResult Status() => Json(new { closeBehavior = DesktopShellSettings.GetCloseBehavior() });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Configure(string closeBehavior)
    {
        if (closeBehavior != DesktopShellSettings.MinimizeToTray && closeBehavior != DesktopShellSettings.Exit)
        {
            return BadRequest(new { error = "Geçersiz değer." });
        }

        DesktopShellSettings.SetCloseBehavior(closeBehavior);
        return Ok(new { success = true });
    }
}
