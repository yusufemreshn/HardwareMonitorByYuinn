namespace HardwareMonitorByYuinn.Entity.Averages;

public sealed class CpuAverages
{
    public required MetricAverages UsagePercent { get; init; }
    public required MetricAverages ClockMhz { get; init; }
    public required MetricAverages PowerWatts { get; init; }
    public required MetricAverages TemperatureC { get; init; }
    public IReadOnlyList<CoreAverages> Cores { get; init; } = [];
}
