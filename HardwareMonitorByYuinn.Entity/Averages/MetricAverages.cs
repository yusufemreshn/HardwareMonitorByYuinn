namespace HardwareMonitorByYuinn.Entity.Averages;

// A null window means not enough samples have been collected yet for that window.
public sealed class MetricAverages
{
    public double? Last1Min { get; init; }
    public double? Last2Min { get; init; }
    public double? Last5Min { get; init; }
    public double? Last10Min { get; init; }
    public double? Last15Min { get; init; }
    public double? Lifetime { get; init; }
}
