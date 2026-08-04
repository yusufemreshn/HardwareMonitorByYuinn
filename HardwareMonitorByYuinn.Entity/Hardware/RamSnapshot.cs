namespace HardwareMonitorByYuinn.Entity.Hardware;

public sealed class RamSnapshot
{
    public double? UsedPercent { get; init; }
    public double? UsedGb { get; init; }
    public double? TotalGb { get; init; }
    public double? ClockMhz { get; init; }
}
