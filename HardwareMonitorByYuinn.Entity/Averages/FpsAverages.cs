namespace HardwareMonitorByYuinn.Entity.Averages;

public sealed class FpsAverages
{
    public required MetricAverages FramesPerSecond { get; init; }
    public required MetricAverages Low1Percent { get; init; }
    public required MetricAverages LowPoint1Percent { get; init; }
}
