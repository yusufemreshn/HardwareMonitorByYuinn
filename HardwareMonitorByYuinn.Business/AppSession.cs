namespace HardwareMonitorByYuinn.Business;

// Singleton olarak kaydedilir; oluşturulduğu an "program açıldığından beri" ortalamalarının başlangıç noktasıdır.
public sealed class AppSession
{
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
}
