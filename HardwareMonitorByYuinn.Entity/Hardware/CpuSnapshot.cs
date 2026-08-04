namespace HardwareMonitorByYuinn.Entity.Hardware;

public sealed class CpuSnapshot
{
    public required string Name { get; init; }
    public double? TotalLoadPercent { get; init; }
    public double? PackageClockMhz { get; init; }
    public double? PackagePowerWatts { get; init; }
    public double? PackageTemperatureC { get; init; }

    /// <summary>Sıcaklığın hangi kaynaktan geldiği (çip içi Tctl/Tdie sensörü ya da ACPI termal bölgesi). Değer yoksa null.</summary>
    public string? TemperatureSource { get; init; }

    /// <summary>Frekansın hangi kaynaktan geldiği (donanım sensörü ya da Windows performans sayacı). Değer yoksa null.</summary>
    public string? ClockSource { get; init; }

    public IReadOnlyList<CoreSensor> Cores { get; init; } = [];
}
