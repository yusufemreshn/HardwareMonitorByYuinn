namespace HardwareMonitorByYuinn.Web.Models;

public sealed class ReportViewModel
{
    public required int Days { get; init; }
    public required DateTime GeneratedAtLocal { get; init; }
}
