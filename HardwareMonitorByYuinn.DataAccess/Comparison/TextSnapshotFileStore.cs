using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HardwareMonitorByYuinn.Entity.Averages;
using HardwareMonitorByYuinn.Entity.Comparison;

namespace HardwareMonitorByYuinn.DataAccess.Comparison;

public sealed partial class TextSnapshotFileStore : ISnapshotFileStore
{
    public string Serialize(ComparisonReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HardwareMonitorByYuinn Karsilastirma Raporu");
        sb.AppendLine($"MachineName={report.MachineName}");
        sb.AppendLine($"CpuName={report.CpuName}");
        sb.AppendLine($"GpuName={report.GpuName}");
        sb.AppendLine($"SessionStartedAtUtc={report.SessionStartedAtUtc:O}");
        sb.AppendLine($"ExportedAtUtc={report.ExportedAtUtc:O}");
        sb.AppendLine();

        WriteSection(sb, "Cpu.UsagePercent", report.Averages.Cpu.UsagePercent);
        WriteSection(sb, "Cpu.ClockMhz", report.Averages.Cpu.ClockMhz);
        WriteSection(sb, "Cpu.PowerWatts", report.Averages.Cpu.PowerWatts);
        WriteSection(sb, "Cpu.TemperatureC", report.Averages.Cpu.TemperatureC);

        foreach (CoreAverages core in report.Averages.Cpu.Cores.OrderBy(c => c.CoreIndex))
        {
            WriteSection(sb, $"Cpu.Core.{core.CoreIndex}.ClockMhz", core.ClockMhz);
            WriteSection(sb, $"Cpu.Core.{core.CoreIndex}.LoadPercent", core.LoadPercent);
            WriteSection(sb, $"Cpu.Core.{core.CoreIndex}.PowerWatts", core.PowerWatts);
        }

        WriteSection(sb, "Gpu.UsagePercent", report.Averages.Gpu.UsagePercent);
        WriteSection(sb, "Gpu.CoreClockMhz", report.Averages.Gpu.CoreClockMhz);
        WriteSection(sb, "Gpu.CoreTemperatureC", report.Averages.Gpu.CoreTemperatureC);
        WriteSection(sb, "Gpu.HotSpotTemperatureC", report.Averages.Gpu.HotSpotTemperatureC);
        WriteSection(sb, "Gpu.PowerWatts", report.Averages.Gpu.PowerWatts);

        WriteSection(sb, "Ram.UsagePercent", report.Averages.Ram.UsagePercent);

        WriteSection(sb, "Fps.FramesPerSecond", report.Averages.Fps.FramesPerSecond);
        WriteSection(sb, "Fps.Low1Percent", report.Averages.Fps.Low1Percent);
        WriteSection(sb, "Fps.LowPoint1Percent", report.Averages.Fps.LowPoint1Percent);

        WriteSection(sb, "Network.DownloadBytesPerSec", report.Averages.Network.DownloadBytesPerSec);
        WriteSection(sb, "Network.UploadBytesPerSec", report.Averages.Network.UploadBytesPerSec);

        return sb.ToString();
    }

    public ComparisonReport Parse(string text)
    {
        string? machineName = null, cpuName = null, gpuName = null;
        DateTime sessionStart = default, exported = default;
        var sections = new Dictionary<string, Dictionary<string, double?>>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1];
                sections[currentSection] = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            string key = line[..eq];
            string rawValue = line[(eq + 1)..];

            if (currentSection is null)
            {
                switch (key)
                {
                    case "MachineName": machineName = rawValue; break;
                    case "CpuName": cpuName = rawValue; break;
                    case "GpuName": gpuName = rawValue; break;
                    case "SessionStartedAtUtc":
                        sessionStart = DateTime.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                        break;
                    case "ExportedAtUtc":
                        exported = DateTime.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                        break;
                }
            }
            else
            {
                double? value = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
                sections[currentSection][key] = value;
            }
        }

        MetricAverages ReadSection(string name)
        {
            if (!sections.TryGetValue(name, out Dictionary<string, double?>? values))
                return new MetricAverages();

            return new MetricAverages
            {
                Last1Min = values.GetValueOrDefault("Last1Min"),
                Last2Min = values.GetValueOrDefault("Last2Min"),
                Last5Min = values.GetValueOrDefault("Last5Min"),
                Last10Min = values.GetValueOrDefault("Last10Min"),
                Last15Min = values.GetValueOrDefault("Last15Min"),
                Lifetime = values.GetValueOrDefault("Lifetime")
            };
        }

        List<CoreAverages> cores = sections.Keys
            .Select(k => CoreSectionRegex().Match(k))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(i => i)
            .Select(i => new CoreAverages
            {
                CoreIndex = i,
                ClockMhz = ReadSection($"Cpu.Core.{i}.ClockMhz"),
                LoadPercent = ReadSection($"Cpu.Core.{i}.LoadPercent"),
                PowerWatts = ReadSection($"Cpu.Core.{i}.PowerWatts")
            })
            .ToList();

        return new ComparisonReport
        {
            MachineName = machineName ?? "Bilinmeyen",
            CpuName = cpuName ?? "Bilinmeyen",
            GpuName = gpuName ?? "Bilinmeyen",
            SessionStartedAtUtc = sessionStart,
            ExportedAtUtc = exported,
            Averages = new HardwareAverages
            {
                Cpu = new CpuAverages
                {
                    UsagePercent = ReadSection("Cpu.UsagePercent"),
                    ClockMhz = ReadSection("Cpu.ClockMhz"),
                    PowerWatts = ReadSection("Cpu.PowerWatts"),
                    TemperatureC = ReadSection("Cpu.TemperatureC"),
                    Cores = cores
                },
                Gpu = new GpuAverages
                {
                    UsagePercent = ReadSection("Gpu.UsagePercent"),
                    CoreClockMhz = ReadSection("Gpu.CoreClockMhz"),
                    CoreTemperatureC = ReadSection("Gpu.CoreTemperatureC"),
                    HotSpotTemperatureC = ReadSection("Gpu.HotSpotTemperatureC"),
                    PowerWatts = ReadSection("Gpu.PowerWatts")
                },
                Ram = new RamAverages
                {
                    UsagePercent = ReadSection("Ram.UsagePercent")
                },
                Fps = new FpsAverages
                {
                    FramesPerSecond = ReadSection("Fps.FramesPerSecond"),
                    Low1Percent = ReadSection("Fps.Low1Percent"),
                    LowPoint1Percent = ReadSection("Fps.LowPoint1Percent")
                },
                Network = new NetworkAverages
                {
                    DownloadBytesPerSec = ReadSection("Network.DownloadBytesPerSec"),
                    UploadBytesPerSec = ReadSection("Network.UploadBytesPerSec")
                }
            }
        };
    }

    private static void WriteSection(StringBuilder sb, string name, MetricAverages avg)
    {
        sb.AppendLine($"[{name}]");
        sb.AppendLine($"Last1Min={Format(avg.Last1Min)}");
        sb.AppendLine($"Last2Min={Format(avg.Last2Min)}");
        sb.AppendLine($"Last5Min={Format(avg.Last5Min)}");
        sb.AppendLine($"Last10Min={Format(avg.Last10Min)}");
        sb.AppendLine($"Last15Min={Format(avg.Last15Min)}");
        sb.AppendLine($"Lifetime={Format(avg.Lifetime)}");
        sb.AppendLine();
    }

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : string.Empty;

    [GeneratedRegex(@"^Cpu\.Core\.(\d+)\.")]
    private static partial Regex CoreSectionRegex();
}
