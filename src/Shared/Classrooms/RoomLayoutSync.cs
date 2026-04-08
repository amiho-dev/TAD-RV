using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

#if WINDOWS
using Microsoft.Win32;
#endif

namespace TADBridge.Shared.Classrooms;

public enum RoomItemKind
{
    Seat,
    Table
}

public sealed class RoomItemDefinition
{
    [JsonPropertyName("row")]
    public int Row { get; set; }

    [JsonPropertyName("col")]
    public int Col { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public RoomItemKind Kind { get; set; } = RoomItemKind.Seat;

    [JsonIgnore]
    public bool IsAssigned => !string.IsNullOrWhiteSpace(Host);
}

public sealed class RoomLayout
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Room";

    [JsonPropertyName("rows")]
    public int Rows { get; set; } = 4;

    [JsonPropertyName("cols")]
    public int Cols { get; set; } = 8;

    [JsonPropertyName("items")]
    public List<RoomItemDefinition> Items { get; set; } = [];

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RoomLayout Load(string? path = null)
    {
        path ??= RoomLayoutSync.ResolveSyncPath();

        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<RoomLayout>(json, s_json) ?? new RoomLayout();
            }
        }
        catch
        {
            // Return a default layout if the sync file is missing/corrupt.
        }

        return new RoomLayout();
    }

    public void Save(string? path = null)
    {
        path ??= RoomLayoutSync.ResolveSyncPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(this, s_json));
    }

    public RoomItemDefinition? GetItem(int row, int col) =>
        Items.FirstOrDefault(s => s.Row == row && s.Col == col);

    public void SetItem(int row, int col, string label, string host, RoomItemKind kind)
    {
        var existing = GetItem(row, col);
        if (existing != null)
        {
            existing.Label = label;
            existing.Host = host;
            existing.Kind = kind;
            return;
        }

        Items.Add(new RoomItemDefinition
        {
            Row = row,
            Col = col,
            Label = label,
            Host = host,
            Kind = kind
        });
    }

    public void ClearItem(int row, int col) =>
        Items.RemoveAll(s => s.Row == row && s.Col == col);

    public IEnumerable<RoomItemDefinition> AssignedItems =>
        Items.Where(s => s.IsAssigned);
}

public static class RoomLayoutSync
{
    private const string ProductName = "TAD-RV";
    private const string EnvPath = "TAD_ROOM_LAYOUT_PATH";

#if WINDOWS
    private const string RegistryRoot = @"SOFTWARE\TAD_RV";
    private const string RegistryValue = "RoomLayoutPath";
#endif

    public static string ResolveSyncPath()
    {
        var envPath = Environment.GetEnvironmentVariable(EnvPath);
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;

#if WINDOWS
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryRoot, false);
            var regPath = key?.GetValue(RegistryValue) as string;
            if (!string.IsNullOrWhiteSpace(regPath))
                return regPath;
        }
        catch
        {
            // Registry lookup is best-effort only.
        }
#endif

        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(root, ProductName, "sync", "room-layout.json");
    }
}
