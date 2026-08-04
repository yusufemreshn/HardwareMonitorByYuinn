namespace HardwareMonitorByYuinn.Entity.History;

/// <summary>Kalıcı geçmiş veritabanının genel durumu; Geçmiş sayfasında kullanıcıya özet olarak gösterilir.</summary>
public sealed class HistoryStoreStatus
{
    public required int SampleCount { get; init; }
    public required DateTime? OldestSampleUtc { get; init; }
    public required DateTime? NewestSampleUtc { get; init; }
    public required long DatabaseSizeBytes { get; init; }
}
