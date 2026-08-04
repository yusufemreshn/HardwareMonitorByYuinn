using HardwareMonitorByYuinn.Entity.Averages;
using HardwareMonitorByYuinn.Entity.Hardware;
using HardwareMonitorByYuinn.Entity.System;

namespace HardwareMonitorByYuinn.Web.Models;

public sealed class DetailsViewModel
{
    public required SystemInfo SystemInfo { get; init; }
    public HardwareSnapshot? Latest { get; init; }
    public required HardwareAverages Averages { get; init; }
}
