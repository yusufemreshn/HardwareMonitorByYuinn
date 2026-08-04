using HardwareMonitorByYuinn.Business.Averaging;
using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.DataAccess.Comparison;
using HardwareMonitorByYuinn.DataAccess.Hardware;
using HardwareMonitorByYuinn.Entity.Comparison;
using HardwareMonitorByYuinn.Entity.System;

namespace HardwareMonitorByYuinn.Business.Comparison;

public sealed class ComparisonService(
    AverageCalculatorService averageCalculator,
    TimeSeriesStore store,
    ISnapshotFileStore fileStore,
    IHardwareReader reader,
    AppSession session)
{
    public ComparisonReport BuildCurrentSessionReport()
    {
        SystemInfo info = reader.GetSystemInfo(session.StartedAtUtc);
        string gpuName = store.GetLatestSnapshot()?.Gpu?.Name ?? info.GpuNames.FirstOrDefault() ?? "Bilinmeyen";

        return new ComparisonReport
        {
            MachineName = info.MachineName,
            CpuName = info.CpuName,
            GpuName = gpuName,
            SessionStartedAtUtc = session.StartedAtUtc,
            ExportedAtUtc = DateTime.UtcNow,
            Averages = averageCalculator.GetHardwareAverages()
        };
    }

    public string ExportCurrentSessionAsText() => fileStore.Serialize(BuildCurrentSessionReport());
}
