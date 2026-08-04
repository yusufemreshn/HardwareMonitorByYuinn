using HardwareMonitorByYuinn.Entity.Comparison;

namespace HardwareMonitorByYuinn.DataAccess.Comparison;

public interface ISnapshotFileStore
{
    string Serialize(ComparisonReport report);
    ComparisonReport Parse(string text);
}
