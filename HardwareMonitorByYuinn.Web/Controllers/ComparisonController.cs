using System.Text;
using HardwareMonitorByYuinn.Business.Comparison;
using Microsoft.AspNetCore.Mvc;

namespace HardwareMonitorByYuinn.Web.Controllers;

public sealed class ComparisonController(ComparisonService comparisonService) : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    public JsonResult Current() => Json(comparisonService.BuildCurrentSessionReport());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Export()
    {
        string text = comparisonService.ExportCurrentSessionAsText();
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string fileName = $"HardwareMonitor_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        return File(bytes, "text/plain", fileName);
    }
}
