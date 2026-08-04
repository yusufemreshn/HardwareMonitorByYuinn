namespace HardwareMonitorByYuinn.Entity.System;

public sealed class PhysicalDiskSummary
{
    public required string Name { get; init; }
    public double TotalGb { get; init; }
    public required string MediaType { get; init; }
}
