namespace HardwareMonitorByYuinn.Entity.Hardware;

public sealed class GpuSnapshot
{
    public required string Name { get; init; }
    public double? LoadPercent { get; init; }
    public double? CoreClockMhz { get; init; }
    public double? MemoryClockMhz { get; init; }
    public double? CoreTemperatureC { get; init; }
    public double? HotSpotTemperatureC { get; init; }
    public double? PowerWatts { get; init; }
    public double? MemoryUsedMb { get; init; }
    public double? MemoryTotalMb { get; init; }
}
