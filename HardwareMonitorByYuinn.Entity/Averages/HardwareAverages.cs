namespace HardwareMonitorByYuinn.Entity.Averages;

public sealed class HardwareAverages
{
    public required CpuAverages Cpu { get; init; }
    public required GpuAverages Gpu { get; init; }
    public required RamAverages Ram { get; init; }
    public required FpsAverages Fps { get; init; }
    public required NetworkAverages Network { get; init; }
}
