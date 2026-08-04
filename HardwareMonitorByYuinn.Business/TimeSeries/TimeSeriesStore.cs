using HardwareMonitorByYuinn.Entity.Averages;
using HardwareMonitorByYuinn.Entity.Hardware;

namespace HardwareMonitorByYuinn.Business.TimeSeries;

public sealed class TimeSeriesStore
{
    private const int RetentionMinutes = 15;
    private static readonly int Capacity = RetentionMinutes * 60;

    private readonly Dictionary<string, MetricSeries> _series = [];
    private readonly object _seriesLock = new();
    private readonly object _latestLock = new();
    private HardwareSnapshot? _latest;

    public void Record(HardwareSnapshot snapshot)
    {
        lock (_latestLock)
        {
            _latest = snapshot;
        }

        DateTime t = snapshot.TimestampUtc;

        RecordValue("cpu.usage", t, snapshot.Cpu?.TotalLoadPercent);
        RecordValue("cpu.clock", t, snapshot.Cpu?.PackageClockMhz);
        RecordValue("cpu.power", t, snapshot.Cpu?.PackagePowerWatts);
        RecordValue("cpu.temp", t, snapshot.Cpu?.PackageTemperatureC);

        foreach (CoreSensor core in snapshot.Cpu?.Cores ?? [])
        {
            RecordValue($"cpu.core.{core.CoreIndex}.clock", t, core.ClockMhz);
            RecordValue($"cpu.core.{core.CoreIndex}.load", t, core.LoadPercent);
            RecordValue($"cpu.core.{core.CoreIndex}.power", t, core.PowerWatts);
        }

        RecordValue("gpu.usage", t, snapshot.Gpu?.LoadPercent);
        RecordValue("gpu.coreClock", t, snapshot.Gpu?.CoreClockMhz);
        RecordValue("gpu.coreTemp", t, snapshot.Gpu?.CoreTemperatureC);
        RecordValue("gpu.hotspot", t, snapshot.Gpu?.HotSpotTemperatureC);
        RecordValue("gpu.power", t, snapshot.Gpu?.PowerWatts);

        // VRAM kullanım yüzdesi eskiden hiç kaydedilmiyordu; grafikteki VRAM serisi yalnızca o anki
        // tarayıcı sekmesinde SignalR ile biriken canlı noktalara dayanıyordu — sayfa yenilenince
        // (ya da yeni bir sekme açılınca) geçmiş sıfırlanıyor, kullanıcı yalnızca sekmeyi açtığından
        // beri geçen (bazen 30-40 saniye gibi kısa) kısmı görüyordu. Diğer metrikler gibi burada da
        // kaydedilerek Panel'in ilk yüklemede sunucudan geçmişle "seed" edilmesi (bkz. DashboardController,
        // Model.VramHistory) mümkün hâle gelir.
        double? memUsedMb = snapshot.Gpu?.MemoryUsedMb;
        double? memTotalMb = snapshot.Gpu?.MemoryTotalMb;
        RecordValue("gpu.vramUsage", t, memTotalMb is > 0 && memUsedMb.HasValue ? memUsedMb.Value / memTotalMb.Value * 100 : null);

        RecordValue("ram.usage", t, snapshot.Ram?.UsedPercent);

        RecordValue("fps.value", t, snapshot.Fps?.FramesPerSecond);
        RecordValue("fps.frameTime", t, snapshot.Fps?.FrameTimeMs);
        RecordValue("fps.low1", t, snapshot.Fps?.Low1PercentFps);
        RecordValue("fps.low01", t, snapshot.Fps?.LowPoint1PercentFps);

        RecordValue("network.download", t, snapshot.Network?.DownloadBytesPerSec);
        RecordValue("network.upload", t, snapshot.Network?.UploadBytesPerSec);
    }

    public HardwareSnapshot? GetLatestSnapshot()
    {
        lock (_latestLock)
        {
            return _latest;
        }
    }

    public MetricAverages GetAverages(string metricKey, DateTime nowUtc) =>
        GetOrCreateSeries(metricKey).ComputeAverages(nowUtc);

    public IReadOnlyList<TimeSeriesPoint> GetRecentPoints(string metricKey, TimeSpan window, DateTime nowUtc)
    {
        DateTime cutoff = nowUtc - window;
        return GetOrCreateSeries(metricKey).Snapshot().Where(p => p.TimestampUtc >= cutoff).ToList();
    }

    private void RecordValue(string metricKey, DateTime timestampUtc, double? value)
    {
        if (!value.HasValue)
            return;

        GetOrCreateSeries(metricKey).Add(timestampUtc, value.Value);
    }

    private MetricSeries GetOrCreateSeries(string metricKey)
    {
        lock (_seriesLock)
        {
            if (!_series.TryGetValue(metricKey, out MetricSeries? series))
            {
                series = new MetricSeries(Capacity);
                _series[metricKey] = series;
            }

            return series;
        }
    }
}
