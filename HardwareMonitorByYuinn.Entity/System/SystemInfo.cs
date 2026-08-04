namespace HardwareMonitorByYuinn.Entity.System;

public sealed class SystemInfo
{
    public required string MachineName { get; init; }
    public required string OsDescription { get; init; }
    public required string CpuName { get; init; }
    public IReadOnlyList<string> GpuNames { get; init; } = [];

    /// <summary>GPU adına göre eşlenmiş sürücü sürümü. WMI'da bulunamayan kartlar bu sözlükte yer almaz.</summary>
    public IReadOnlyDictionary<string, string> GpuDriverVersions { get; init; } = new Dictionary<string, string>();
    public double TotalRamGb { get; init; }
    public required DateTime AppStartedAtUtc { get; init; }
    public TimeSpan Uptime { get; init; }
    public IReadOnlyList<StorageDriveInfo> Drives { get; init; } = [];
    public IReadOnlyList<PhysicalDiskSummary> PhysicalDisks { get; init; } = [];

    /// <summary>
    /// İşlemcinin MSR/SMU kayıtlarını okuyabilen çekirdek sürücüsünün (PawnIO) kurulu olup olmadığı.
    /// Kurulu değilse işlemci güç tüketimi hiç okunamaz, frekans ve sıcaklık ise yaklaşık
    /// kaynaklardan (Windows performans sayaçları / ACPI) türetilir.
    /// </summary>
    public bool LowLevelSensorsAvailable { get; init; }
}
