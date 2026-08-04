using HardwareMonitorByYuinn.DataAccess.Comparison;
using HardwareMonitorByYuinn.DataAccess.Hardware;
using HardwareMonitorByYuinn.DataAccess.History;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareMonitorByYuinn.DataAccess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccessServices(this IServiceCollection services) => services
        .AddSingleton<IHardwareReader, LibreHardwareReader>()
        .AddSingleton<ISnapshotFileStore, TextSnapshotFileStore>()
        .AddSingleton<IHistoryStore, SqliteHistoryStore>()
        .AddSingleton<SystemEventReader>();
}
