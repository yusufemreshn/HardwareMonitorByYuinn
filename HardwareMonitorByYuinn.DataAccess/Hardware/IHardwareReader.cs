using HardwareMonitorByYuinn.Entity.Hardware;
using HardwareMonitorByYuinn.Entity.System;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

public interface IHardwareReader : IDisposable
{
    HardwareSnapshot Poll();
    SystemInfo GetSystemInfo(DateTime appStartedAtUtc);
}
