namespace HardwareMonitorByYuinn.Entity.Hardware;

public sealed class ProcessMetric
{
    public required string Name { get; init; }
    public int Pid { get; init; }
    public double CpuPercent { get; init; }
    public double RamMb { get; init; }
    public double GpuPercent { get; init; }
}
