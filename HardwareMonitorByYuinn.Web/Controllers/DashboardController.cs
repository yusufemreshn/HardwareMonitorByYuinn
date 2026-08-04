using System.Diagnostics;
using HardwareMonitorByYuinn.Business;
using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.DataAccess.Hardware;
using HardwareMonitorByYuinn.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace HardwareMonitorByYuinn.Web.Controllers;

public sealed class DashboardController(TimeSeriesStore store, IHardwareReader reader, AppSession session) : Controller
{
    // TimeSeriesStore en fazla 15 dk'lık ince (saniyelik) çözünürlüklü veri tutuyor (bkz.
    // TimeSeriesStore.RetentionMinutes); burada da aynı üst sınırı istiyoruz ki Panel ilk açıldığında
    // "1 saat" gibi daha geniş bir aralığa geçilse bile bellekteki en ince veri hiç sorgu yapmadan
    // hazır olsun — yalnızca 15 dk'dan eski kısım (varsa) dashboard.js tarafından /History/Rows'tan
    // (dakikalık) ayrıca çekilip tamamlanıyor.
    private static readonly TimeSpan ChartWindow = TimeSpan.FromMinutes(15);

    public IActionResult Index()
    {
        DateTime now = DateTime.UtcNow;
        var model = new DashboardViewModel
        {
            CpuHistory = store.GetRecentPoints("cpu.usage", ChartWindow, now),
            GpuHistory = store.GetRecentPoints("gpu.usage", ChartWindow, now),
            VramHistory = store.GetRecentPoints("gpu.vramUsage", ChartWindow, now),
            RamHistory = store.GetRecentPoints("ram.usage", ChartWindow, now),
            FpsHistory = store.GetRecentPoints("fps.value", ChartWindow, now),
            FrameTimeHistory = store.GetRecentPoints("fps.frameTime", ChartWindow, now),
            Fps1PercentLowHistory = store.GetRecentPoints("fps.low1", ChartWindow, now),
            Fps01PercentLowHistory = store.GetRecentPoints("fps.low01", ChartWindow, now),
            NetworkDownloadHistory = store.GetRecentPoints("network.download", ChartWindow, now),
            NetworkUploadHistory = store.GetRecentPoints("network.upload", ChartWindow, now),
            SystemInfo = reader.GetSystemInfo(session.StartedAtUtc)
        };

        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
