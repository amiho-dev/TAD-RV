// ───────────────────────────────────────────────────────────────────────────
// RoomLayout.cs — Room / Class Layout Model
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Serialisable model that describes the physical arrangement of endpoints
// in a room.  Saved to JSON and loaded on the next session.
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TadTeacher.ClassDesigner;

// ══════════════════════════════════════════════════════════════════════════
// Data classes
// ══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Describes one endpoint seat in the room.
/// </summary>
public sealed class SeatDefinition
{
    /// <summary>Zero-based row index (0 = front).</summary>
    [JsonPropertyName("row")]
    public int Row { get; set; }

    /// <summary>Zero-based column index (0 = left).</summary>
    [JsonPropertyName("col")]
    public int Col { get; set; }

    /// <summary>Display label shown on the seat tile (e.g. "A1" or student name).</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Hostname or IP address of the assigned endpoint.</summary>
    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    /// <summary>Whether this seat is currently empty / unassigned.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Host);

    /// <summary>Last known ping result — not persisted to JSON.</summary>
    [JsonIgnore]
    public PingStatus Status { get; set; } = PingStatus.Unknown;
}

/// <summary>Online status of a seat, determined by ICMP ping.</summary>
public enum PingStatus { Unknown, Online, Offline }

/// <summary>
/// Full room layout: grid dimensions + all seat assignments.
/// </summary>
public sealed class RoomLayout
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Room";

    [JsonPropertyName("rows")]
    public int Rows { get; set; } = 4;

    [JsonPropertyName("cols")]
    public int Cols { get; set; } = 8;

    [JsonPropertyName("seats")]
    public List<SeatDefinition> Seats { get; set; } = [];

    // ─── Persistence ─────────────────────────────────────────────────

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Default path: %APPDATA%\TAD-RV\room-layout.json</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TAD-RV", "room-layout.json");

    public static RoomLayout Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<RoomLayout>(json, s_json) ?? new();
            }
        }
        catch { /* return default if corrupt */ }
        return new RoomLayout();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, s_json));
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    /// <summary>Get the seat definition at (row, col), or null if none assigned.</summary>
    public SeatDefinition? GetSeat(int row, int col) =>
        Seats.FirstOrDefault(s => s.Row == row && s.Col == col);

    /// <summary>Set or update a seat. Pass empty host to clear.</summary>
    public void SetSeat(int row, int col, string label, string host)
    {
        var existing = GetSeat(row, col);
        if (existing != null)
        {
            existing.Label = label;
            existing.Host  = host;
        }
        else
        {
            Seats.Add(new SeatDefinition { Row = row, Col = col, Label = label, Host = host });
        }
    }

    /// <summary>Remove a seat assignment (leaves an empty tile).</summary>
    public void ClearSeat(int row, int col) =>
        Seats.RemoveAll(s => s.Row == row && s.Col == col);

    /// <summary>All seats that have a host assigned.</summary>
    public IEnumerable<SeatDefinition> AssignedSeats =>
        Seats.Where(s => !s.IsEmpty);
}
