namespace HardwareMonitorByYuinn.Business.TimeSeries;

public readonly record struct TimeSeriesPoint(DateTime TimestampUtc, double Value);
