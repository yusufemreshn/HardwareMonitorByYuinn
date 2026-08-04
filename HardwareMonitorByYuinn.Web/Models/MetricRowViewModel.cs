using HardwareMonitorByYuinn.Entity.Averages;

namespace HardwareMonitorByYuinn.Web.Models;

public sealed record MetricRowViewModel(string MetricName, MetricAverages Averages, string Unit, int Digits = 1);
