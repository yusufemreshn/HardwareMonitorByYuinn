namespace HardwareMonitorByYuinn.Entity.System;

public sealed class StorageDriveInfo
{
    public required string Name { get; init; }
    public string? VolumeLabel { get; init; }
    public string? DriveFormat { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double? TemperatureC { get; init; }
}
