using HardwareMonitorByYuinn.Business.TimeSeries;
using HardwareMonitorByYuinn.Entity.Averages;
using HardwareMonitorByYuinn.Entity.Hardware;

namespace HardwareMonitorByYuinn.Business.Averaging;

public sealed class AverageCalculatorService(TimeSeriesStore store)
{
    public HardwareAverages GetHardwareAverages()
    {
        DateTime now = DateTime.UtcNow;
        HardwareSnapshot? latest = store.GetLatestSnapshot();

        List<CoreAverages> cores = (latest?.Cpu?.Cores ?? [])
            .Select(c => c.CoreIndex)
            .OrderBy(i => i)
            .Select(i => new CoreAverages
            {
                CoreIndex = i,
                ClockMhz = store.GetAverages($"cpu.core.{i}.clock", now),
                LoadPercent = store.GetAverages($"cpu.core.{i}.load", now),
                PowerWatts = store.GetAverages($"cpu.core.{i}.power", now)
            })
            .ToList();

        return new HardwareAverages
        {
            Cpu = new CpuAverages
            {
                UsagePercent = store.GetAverages("cpu.usage", now),
                ClockMhz = store.GetAverages("cpu.clock", now),
                PowerWatts = store.GetAverages("cpu.power", now),
                TemperatureC = store.GetAverages("cpu.temp", now),
                Cores = cores
            },
            Gpu = new GpuAverages
            {
                UsagePercent = store.GetAverages("gpu.usage", now),
                CoreClockMhz = store.GetAverages("gpu.coreClock", now),
                CoreTemperatureC = store.GetAverages("gpu.coreTemp", now),
                HotSpotTemperatureC = store.GetAverages("gpu.hotspot", now),
                PowerWatts = store.GetAverages("gpu.power", now)
            },
            Ram = new RamAverages
            {
                UsagePercent = store.GetAverages("ram.usage", now)
            },
            Fps = new FpsAverages
            {
                FramesPerSecond = store.GetAverages("fps.value", now),
                Low1Percent = store.GetAverages("fps.low1", now),
                LowPoint1Percent = store.GetAverages("fps.low01", now)
            },
            Network = new NetworkAverages
            {
                DownloadBytesPerSec = store.GetAverages("network.download", now),
                UploadBytesPerSec = store.GetAverages("network.upload", now)
            }
        };
    }
}
