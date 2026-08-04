namespace HardwareMonitorByYuinn.Entity.Averages;

public sealed class NetworkAverages
{
    public required MetricAverages DownloadBytesPerSec { get; init; }
    public required MetricAverages UploadBytesPerSec { get; init; }
}
