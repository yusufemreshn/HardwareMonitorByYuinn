using HardwareMonitorByYuinn.Business.Averaging;
using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.Entity.Hardware;
using Microsoft.AspNetCore.SignalR;

namespace HardwareMonitorByYuinn.Web.Hubs;

public sealed class HardwareHub(TimeSeriesStore store, AverageCalculatorService averageCalculator) : Hub
{
    public override async Task OnConnectedAsync()
    {
        HardwareSnapshot? snapshot = store.GetLatestSnapshot();
        if (snapshot is not null)
        {
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

            await Clients.Caller.SendAsync("hardwareUpdate", message);
        }

        await base.OnConnectedAsync();
    }
}
