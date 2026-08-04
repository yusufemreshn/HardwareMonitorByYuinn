namespace HardwareMonitorByYuinn.Entity.History;

/// <summary>
/// Bir process'in belirli bir dakikadaki ortalama CPU/RAM kullanımı. Yalnızca o process o dakika
/// "üst processler" listesindeyse kaydedilir — sürekli kaydedilmez, yalnızca öne çıktığı anlar.
/// </summary>
public sealed class ProcessHistorySample
{
    public required DateTime TimestampUtc { get; init; }
    public double? AverageCpuPercent { get; init; }
    public double? AverageRamMb { get; init; }
}
