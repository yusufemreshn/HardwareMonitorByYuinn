namespace HardwareMonitorByYuinn.Entity.Hardware;

public sealed class HardwareSnapshot
{
    public required DateTime TimestampUtc { get; init; }
    public CpuSnapshot? Cpu { get; init; }
    public GpuSnapshot? Gpu { get; init; }
    public IReadOnlyList<GpuSnapshot> AllGpus { get; init; } = [];
    public RamSnapshot? Ram { get; init; }
    public FpsSnapshot? Fps { get; init; }
    public IReadOnlyList<StorageSnapshot> Storages { get; init; } = [];
    public NetworkSnapshot? Network { get; init; }
    public IReadOnlyList<ProcessMetric> TopProcesses { get; init; } = [];
}
