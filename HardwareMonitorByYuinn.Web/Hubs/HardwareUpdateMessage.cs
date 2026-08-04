using HardwareMonitorByYuinn.Entity.Averages;
using HardwareMonitorByYuinn.Entity.Hardware;

namespace HardwareMonitorByYuinn.Web.Hubs;

public sealed record HardwareUpdateMessage(
    DateTime TimestampUtc,
    CpuSnapshot? Cpu,
    GpuSnapshot? Gpu,
    RamSnapshot? Ram,
    FpsSnapshot? Fps,
    IReadOnlyList<StorageSnapshot> Storages,
    NetworkSnapshot? Network,
    HardwareAverages Averages,
    IReadOnlyList<ProcessMetric> TopProcesses);
