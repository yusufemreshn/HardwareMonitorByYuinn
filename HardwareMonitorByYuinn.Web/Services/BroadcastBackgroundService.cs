using HardwareMonitorByYuinn.Business.Averaging;
using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.Entity.Hardware;
using HardwareMonitorByYuinn.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace HardwareMonitorByYuinn.Web.Services;

public sealed class BroadcastBackgroundService(
    IHubContext<HardwareHub> hub,
    TimeSeriesStore store,
    AverageCalculatorService averageCalculator,
    ILogger<BroadcastBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                HardwareSnapshot? snapshot = store.GetLatestSnapshot();
                if (snapshot is null)
                    continue;

                var message = new HardwareUpdateMessage(
                    snapshot.TimestampUtc,
                    snapshot.Cpu,
                    snapshot.Gpu,
                    snapshot.Ram,
                    snapshot.Fps,
                    snapshot.Storages,
                    snapshot.Network,
                    averageCalculator.GetHardwareAverages(),
                    snapshot.TopProcesses);

                await hub.Clients.All.SendAsync("hardwareUpdate", message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bir istemcinin bağlantısı beklenmedik şekilde bozulursa SendAsync istisna
                // fırlatabilir; bu döngüyü (ve dolayısıyla tüm host'u) durdurmamalı, yalnızca
                // o turu loglayıp bir sonraki saniyede devam etmeli.
                logger.LogWarning(ex, "Donanım güncellemesi istemcilere yayınlanamadı");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
