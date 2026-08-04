namespace HardwareMonitorByYuinn.Entity.Averages;

public sealed class CoreAverages
{
    public required int CoreIndex { get; init; }
    public required MetricAverages ClockMhz { get; init; }
    public required MetricAverages LoadPercent { get; init; }
    public required MetricAverages PowerWatts { get; init; }
}
