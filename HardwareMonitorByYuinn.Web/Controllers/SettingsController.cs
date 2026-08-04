using Microsoft.AspNetCore.Mvc;

namespace HardwareMonitorByYuinn.Web.Controllers;

public sealed class SettingsController : Controller
{
    public IActionResult Index() => View();
}
