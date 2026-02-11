// ───────────────────────────────────────────────────────────────────────────
// EventLogService.cs — Reads TAD.RV related events from Windows Event Log
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.Diagnostics;

namespace TadConsole.Services;

/// <summary>
/// Single event log entry for display in the UI.
/// </summary>
public sealed class TadEventEntry
{
    public DateTime   TimeStamp   { get; init; }
    public string     Level       { get; init; } = "";
    public int        EventId     { get; init; }
    public string     Source      { get; init; } = "";
    public string     Message     { get; init; } = "";
}

public sealed class EventLogService
{
    /// <summary>
    /// Reads the most recent TAD-related events from the Application event log.
    /// </summary>
    public ObservableCollection<TadEventEntry> ReadRecentEvents(int maxCount = 200)
    {
        var entries = new ObservableCollection<TadEventEntry>();

        try
        {
            using var log = new EventLog("Application");

            var tadEntries = log.Entries
                .Cast<EventLogEntry>()
                .Where(e => string.Equals(e.Source, "TadBridgeService", StringComparison.OrdinalIgnoreCase)
                         || (e.Message?.Contains("TAD", StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderByDescending(e => e.TimeGenerated)
                .Take(maxCount);

            foreach (var entry in tadEntries)
            {
                entries.Add(new TadEventEntry
                {
                    TimeStamp = entry.TimeGenerated,
                    Level     = entry.EntryType.ToString(),
                    EventId   = (int)entry.InstanceId,
                    Source    = entry.Source,
                    Message   = entry.Message.Length > 500
                                  ? entry.Message[..500] + "…"
                                  : entry.Message,
                });
            }
        }
        catch
        {
            // Event log access may fail on non-Windows or non-admin
        }

        return entries;
    }
}
