using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.DataAccess.Hardware;
using HardwareMonitorByYuinn.DataAccess.History;
using HardwareMonitorByYuinn.Entity.Hardware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.Business.Polling;

public sealed class HardwareMonitoringBackgroundService(
    IHardwareReader reader,
    TimeSeriesStore store,
    IHistoryStore historyStore,
    ILogger<HardwareMonitoringBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                HardwareSnapshot snapshot = reader.Poll();
                store.Record(snapshot);
                historyStore.Record(snapshot, snapshot.TimestampUtc);
                historyStore.RecordFpsSample(
                    snapshot.Fps?.SourceProcessName,
                    snapshot.TimestampUtc,
                    snapshot.Fps?.FramesPerSecond,
                    snapshot.Cpu?.PackageTemperatureC,
                    snapshot.Gpu?.CoreTemperatureC);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Donanım verileri okunurken beklenmeyen bir hata oluştu");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
