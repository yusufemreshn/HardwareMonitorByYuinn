using System.Diagnostics.Eventing.Reader;

namespace HardwareMonitorByYuinn.DataAccess.History;

public sealed record SystemEventEntry(DateTime Timestamp, string Type, string Description);

/// <summary>
/// Sistem Olayı Zaman Çizelgesi (Geçmiş sayfası) için Windows Olay Günlüğü'nden (System log)
/// uyku/uyanma/başlatma/kapatma/güncelleme olaylarını okur. SQLite'a kalıcı olarak KAYDEDİLMEZ —
/// Windows zaten bu olayları kendi günlüğünde tutuyor, her istekte doğrudan oradan okunur.
/// </summary>
public sealed class SystemEventReader
{
    private static readonly (string? ProviderName, int EventId, string Type)[] Queries =
    [
        ("EventLog", 6005, "Başlatma"),
        ("EventLog", 6006, "Kapatma"),
        ("Microsoft-Windows-Kernel-Power", 42, "Uykuya Geçiş"),
        ("Microsoft-Windows-Power-Troubleshooter", 1, "Uyanma"),
        ("Microsoft-Windows-WindowsUpdateClient", 19, "Güncelleme Yüklendi"),
        ("User32", 1074, "Kullanıcı İsteğiyle Kapatma/Yeniden Başlatma")
    ];

    /// <summary>fromUtc/toUtc UTC olmalı — Windows Olay Günlüğü'ndeki @SystemTime her zaman UTC'dir;
    /// dönen <see cref="SystemEventEntry.Timestamp"/> ise (EventRecord.TimeCreated) yerel saattir.</summary>
    public IReadOnlyList<SystemEventEntry> Read(DateTime fromUtc, DateTime toUtc)
    {
        var results = new List<SystemEventEntry>();

        foreach ((string? providerName, int eventId, string type) in Queries)
        {
            string providerFilter = providerName is null ? "" : $" and Provider[@Name='{providerName}']";
            string xpath = $"*[System[EventID={eventId}{providerFilter} and " +
                $"TimeCreated[@SystemTime>='{fromUtc:o}' and @SystemTime<='{toUtc:o}']]]";

            try
            {
                var query = new EventLogQuery("System", PathType.LogName, xpath);
                using var reader = new EventLogReader(query);

                EventRecord? record;
                while ((record = reader.ReadEvent()) is not null)
                {
                    using (record)
                    {
                        if (record.TimeCreated is not { } created) continue;

                        string description;
                        try { description = record.FormatDescription() ?? type; }
                        catch { description = type; }

                        results.Add(new SystemEventEntry(created, type, Truncate(description, 200)));
                    }
                }
            }
            catch (EventLogNotFoundException)
            {
                // Bu sağlayıcı/günlük bu sistemde yok; yok say, diğer sorgulara devam edilir.
            }
            catch (UnauthorizedAccessException)
            {
                // Olay günlüğüne erişim reddedildi (nadir); bu sorguyu atla.
            }
        }

        return results.OrderByDescending(e => e.Timestamp).ToList();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
