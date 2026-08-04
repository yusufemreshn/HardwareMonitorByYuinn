namespace HardwareMonitorByYuinn.Entity.Hardware;

public sealed class CoreSensor
{
    public required int CoreIndex { get; init; }
    public double? ClockMhz { get; init; }
    public double? LoadPercent { get; init; }
    public double? PowerWatts { get; init; }
}
