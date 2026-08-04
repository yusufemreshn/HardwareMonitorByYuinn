using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.Entity.System;

namespace HardwareMonitorByYuinn.Web.Models;

public sealed class DashboardViewModel
{
    public required IReadOnlyList<TimeSeriesPoint> CpuHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> GpuHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> VramHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> RamHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> FpsHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> FrameTimeHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> Fps1PercentLowHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> Fps01PercentLowHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> NetworkDownloadHistory { get; init; }
    public required IReadOnlyList<TimeSeriesPoint> NetworkUploadHistory { get; init; }
    public required SystemInfo SystemInfo { get; init; }
}
