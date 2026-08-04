using HardwareMonitorByYuinn.Entity.Averages;

namespace HardwareMonitorByYuinn.Entity.Comparison;

public sealed class ComparisonReport
{
    public required string MachineName { get; init; }
    public required string CpuName { get; init; }
    public required string GpuName { get; init; }
    public required DateTime SessionStartedAtUtc { get; init; }
    public required DateTime ExportedAtUtc { get; init; }
    public required HardwareAverages Averages { get; init; }
}
