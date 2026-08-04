using Microsoft.AspNetCore.Mvc;

namespace HardwareMonitorByYuinn.Web.Controllers;

/// <summary>
/// Bildirim geçmişi tamamen tarayıcıda (localStorage) tutulur; bu sayfa yalnızca listeyi
/// gösterecek statik kabuğu döndürür, veri wwwroot/js/notifications.js tarafından okunur.
/// </summary>
public sealed class NotificationsController : Controller
{
    public IActionResult Index() => View();
}
