using HardwareMonitorByYuinn.Business;
using HardwareMonitorByYuinn.Business.Averaging;
using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.DataAccess.Hardware;
using HardwareMonitorByYuinn.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace HardwareMonitorByYuinn.Web.Controllers;

public sealed class DetailsController(
    TimeSeriesStore store,
    AverageCalculatorService averageCalculator,
    IHardwareReader reader,
    AppSession session) : Controller
{
    public IActionResult Index()
    {
        var model = new DetailsViewModel
        {
            SystemInfo = reader.GetSystemInfo(session.StartedAtUtc),
            Latest = store.GetLatestSnapshot(),
            Averages = averageCalculator.GetHardwareAverages()
        };

        return View(model);
    }

    [HttpGet]
    public JsonResult Averages() => Json(averageCalculator.GetHardwareAverages());
}
