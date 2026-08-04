namespace HardwareMonitorByYuinn.Entity.Hardware;

/// <summary>
/// Tek bir fiziksel disk (SSD/HDD) için canlı sensör değerleri. Windows sürücü harfleriyle
/// (C:, D: ...) eşleştirilmez; LibreHardwareMonitor'ın gördüğü fiziksel diski temsil eder.
/// </summary>
public sealed class StorageSnapshot
{
    public required string Name { get; init; }
    public double? UsedPercent { get; init; }
    public double? TemperatureC { get; init; }
    public double? ReadRateBytesPerSec { get; init; }
    public double? WriteRateBytesPerSec { get; init; }
    public double? TotalGb { get; init; }

    /// <summary>SMART genel durumu ("İyi"/"Dikkat"/"Kötü"); sürücü SMART desteklemiyorsa null.</summary>
    public string? SmartStatus { get; init; }

    /// <summary>Kalan ömür yüzdesi (100 = en iyi, 0 = en kötü); tüm sürücüler desteklemez.</summary>
    public int? SmartLifePercent { get; init; }

    public ulong? PowerOnHours { get; init; }
    public double? HostReadsGb { get; init; }
    public double? HostWritesGb { get; init; }
}
