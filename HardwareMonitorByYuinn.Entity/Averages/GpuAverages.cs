namespace HardwareMonitorByYuinn.Entity.Averages;

public sealed class GpuAverages
{
    public required MetricAverages UsagePercent { get; init; }
    public required MetricAverages CoreClockMhz { get; init; }
    public required MetricAverages CoreTemperatureC { get; init; }
    public required MetricAverages HotSpotTemperatureC { get; init; }
    public required MetricAverages PowerWatts { get; init; }
}
