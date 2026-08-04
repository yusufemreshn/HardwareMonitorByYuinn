namespace HardwareMonitorByYuinn.Entity.Hardware;

public sealed class FpsSnapshot
{
    /// <summary>Anlık kare hızı. Ekrana hiçbir şey sunulmuyorsa null.</summary>
    public double? FramesPerSecond { get; init; }

    /// <summary>
    /// Kare hızı ölçülen sürecin (genellikle oyunun) adı. Ölçüm ekran kartından değil, Windows'un
    /// olay izleme (ETW) altyapısı üzerinden doğrudan o süreçten okunur.
    /// </summary>
    public string? SourceProcessName { get; init; }

    /// <summary>Son karenin sunum süresi (milisaniye). FPS'in tersidir; takılmaları ortalama FPS'ten daha net gösterir.</summary>
    public double? FrameTimeMs { get; init; }

    /// <summary>Son ~10 saniyedeki en yavaş %1'lik karenin ortalamasından hesaplanan FPS. Yeterli örnek yoksa null.</summary>
    public double? Low1PercentFps { get; init; }

    /// <summary>Son ~10 saniyedeki en yavaş %0.1'lik karenin ortalamasından hesaplanan FPS. Yeterli örnek yoksa null.</summary>
    public double? LowPoint1PercentFps { get; init; }
}
