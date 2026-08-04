using HardwareMonitorByYuinn.Entity.Averages;

namespace HardwareMonitorByYuinn.Business.TimeSeries;

internal sealed class MetricSeries
{
    private readonly RingBuffer<TimeSeriesPoint> _buffer;
    private readonly object _lifetimeLock = new();
    private double _lifetimeSum;
    private long _lifetimeCount;

    public MetricSeries(int capacity) => _buffer = new RingBuffer<TimeSeriesPoint>(capacity);

    public void Add(DateTime timestampUtc, double value)
    {
        _buffer.Add(new TimeSeriesPoint(timestampUtc, value));
        lock (_lifetimeLock)
        {
            _lifetimeSum += value;
            _lifetimeCount++;
        }
    }

    public IReadOnlyList<TimeSeriesPoint> Snapshot() => _buffer.Snapshot();

    public MetricAverages ComputeAverages(DateTime nowUtc)
    {
        IReadOnlyList<TimeSeriesPoint> points = _buffer.Snapshot();

        double? lifetime;
        lock (_lifetimeLock)
        {
            lifetime = _lifetimeCount > 0 ? _lifetimeSum / _lifetimeCount : null;
        }

        return new MetricAverages
        {
            Last1Min = AverageSince(points, nowUtc, TimeSpan.FromMinutes(1)),
            Last2Min = AverageSince(points, nowUtc, TimeSpan.FromMinutes(2)),
            Last5Min = AverageSince(points, nowUtc, TimeSpan.FromMinutes(5)),
            Last10Min = AverageSince(points, nowUtc, TimeSpan.FromMinutes(10)),
            Last15Min = AverageSince(points, nowUtc, TimeSpan.FromMinutes(15)),
            Lifetime = lifetime
        };
    }

    private static double? AverageSince(IReadOnlyList<TimeSeriesPoint> points, DateTime nowUtc, TimeSpan window)
    {
        DateTime cutoff = nowUtc - window;
        double sum = 0;
        int count = 0;
        foreach (TimeSeriesPoint point in points)
        {
            if (point.TimestampUtc < cutoff)
                continue;

            sum += point.Value;
            count++;
        }

        return count > 0 ? sum / count : null;
    }
}
