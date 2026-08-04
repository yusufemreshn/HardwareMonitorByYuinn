using HardwareMonitorByYuinn.Entity.History;

namespace HardwareMonitorByYuinn.Web.Models;

public sealed class HistoryViewModel
{
    public required HistoryStoreStatus Status { get; init; }
    public required HistoryStoreStatus LoginAttemptsStatus { get; init; }
    public required HistoryStoreStatus GameSessionsStatus { get; init; }
    public required HistoryStoreStatus ProcessSamplesStatus { get; init; }
    public required DateTime DefaultFromLocal { get; init; }
    public required DateTime DefaultToLocal { get; init; }
    public required int MaxDisplayRows { get; init; }
}
