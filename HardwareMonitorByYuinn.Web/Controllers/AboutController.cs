using HardwareMonitorByYuinn.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace HardwareMonitorByYuinn.Web.Controllers;

public sealed class AboutController : Controller
{
    private const string LicensesFileName = "LICENSES.txt";
    private readonly IStringLocalizer<SharedResource> _localizer;

    public AboutController(IStringLocalizer<SharedResource> localizer)
    {
        _localizer = localizer;
    }

    public IActionResult Index() => View();

    public IActionResult PawnIo() => View();

    /// <summary>
    /// Üçüncü taraf lisans metinlerinin tamamını gösterir. MPL-2.0, bileşenin lisansının dağıtımla
    /// birlikte verilmesini şart koştuğu için bu dosya uygulamanın yanında bulunmak zorundadır.
    /// </summary>
    [HttpGet]
    public IActionResult Licenses()
    {
        string path = Path.Combine(AppContext.BaseDirectory, LicensesFileName);
        if (!System.IO.File.Exists(path))
            return NotFound($"{LicensesFileName} {_localizer["bulunamadı."].Value}");

        return PhysicalFile(path, "text/plain; charset=utf-8");
    }
}
