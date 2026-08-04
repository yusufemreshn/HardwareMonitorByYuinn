namespace HardwareMonitorByYuinn.Entity.History;

/// <summary>
/// Bir oyunun (ön planda kesintisiz kare sunduğu) tek bir oturumunun özeti. Oturum, işlem adı
/// değişene (oyun kapanana/başka bir uygulama ön plana gelene) kadar sürer.
/// </summary>
public sealed class GameSessionSummary
{
    public required string ProcessName { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
    public required double AverageFps { get; init; }
    public double? AverageCpuTemperatureC { get; init; }
    public double? AverageGpuTemperatureC { get; init; }
}
