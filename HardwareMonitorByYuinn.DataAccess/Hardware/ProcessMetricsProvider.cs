using System.Diagnostics;
using HardwareMonitorByYuinn.Entity.Hardware;

namespace HardwareMonitorByYuinn.DataAccess.Hardware;

/// <summary>
/// CPU yüzdesi Process sınıfında doğrudan yoktur; iki ölçüm arasındaki TotalProcessorTime farkının
/// geçen süreye ve çekirdek sayısına oranlanmasıyla (Task Manager'ın yaptığı gibi) hesaplanır.
/// İlk ölçümde önceki değer olmadığından o turda CPU% için 0 döner.
/// </summary>
internal sealed class ProcessMetricsProvider
{
    private readonly object _gate = new();
    private Dictionary<int, (TimeSpan CpuTime, DateTime AtUtc)> _lastCpuTime = [];

    /// <summary>
    /// Panel'de kullanıcı sıralama ölçütünü (CPU/GPU/RAM) değiştirebildiği için, her ölçüte göre en
    /// yüksek <paramref name="topPerMetric"/> süreç ayrı ayrı toplanıp birleştirilir; böylece hangi
    /// ölçüt seçilirse seçilsin istemci tarafında gerçek "en çok kullanan" listesi elde edilir
    /// (yalnızca CPU'ya göre kesip göndersek, RAM'de en üstte olan ama CPU'su düşük bir süreç hiç
    /// gelmezdi).
    /// </summary>
    public IReadOnlyList<ProcessMetric> Read(int topPerMetric, IReadOnlyDictionary<int, double> gpuPercentByPid)
    {
        lock (_gate)
        {
            DateTime now = DateTime.UtcNow;
            var current = new Dictionary<int, (TimeSpan CpuTime, DateTime AtUtc)>();
            var results = new List<ProcessMetric>();

            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        TimeSpan cpuTime = process.TotalProcessorTime;
                        current[process.Id] = (cpuTime, now);

                        double cpuPercent = 0;
                        if (_lastCpuTime.TryGetValue(process.Id, out (TimeSpan CpuTime, DateTime AtUtc) previous))
                        {
                            double elapsedSeconds = (now - previous.AtUtc).TotalSeconds;
                            if (elapsedSeconds > 0)
                                cpuPercent = (cpuTime - previous.CpuTime).TotalSeconds / elapsedSeconds / Environment.ProcessorCount * 100;
                        }

                        results.Add(new ProcessMetric
                        {
                            Name = process.ProcessName,
                            Pid = process.Id,
                            CpuPercent = Math.Clamp(cpuPercent, 0, 100),
                            RamMb = process.WorkingSet64 / 1024.0 / 1024.0,
                            GpuPercent = gpuPercentByPid.GetValueOrDefault(process.Id)
                        });
                    }
                    catch
                    {
                        // Süreç ölçüm sırasında kapanmış veya erişim engellenmiş olabilir; atla.
                    }
                }
            }

            _lastCpuTime = current;

            var topByPid = new Dictionary<int, ProcessMetric>();
            void AddTop(IEnumerable<ProcessMetric> ordered)
            {
                foreach (ProcessMetric p in ordered.Take(topPerMetric))
                    topByPid.TryAdd(p.Pid, p);
            }

            AddTop(results.OrderByDescending(p => p.CpuPercent));
            AddTop(results.OrderByDescending(p => p.GpuPercent));
            AddTop(results.OrderByDescending(p => p.RamMb));

            return topByPid.Values.OrderByDescending(p => p.CpuPercent).ToList();
        }
    }
}
