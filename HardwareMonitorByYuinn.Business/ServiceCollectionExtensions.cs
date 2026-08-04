using HardwareMonitorByYuinn.Business.Averaging;
using HardwareMonitorByYuinn.Business.Comparison;
using HardwareMonitorByYuinn.Business.Polling;
using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareMonitorByYuinn.Business;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services) => services
        .AddDataAccessServices()
        .AddSingleton<AppSession>()
        .AddSingleton<TimeSeriesStore>()
        .AddSingleton<AverageCalculatorService>()
        .AddSingleton<ComparisonService>()
        .AddHostedService<HardwareMonitoringBackgroundService>();
}
