// ───────────────────────────────────────────────────────────────────────────
// DemoTcpClientManager.cs — Simulated student connections for demo mode
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Drop-in replacement for TcpClientManager when --demo is passed.
// Creates 12 fake students that emit periodic status updates, synthetic
// screen thumbnails, and respond to all teacher commands by toggling state.
//
// Thumbnail Strategy:
//   Instead of encoding fake H.264 NAL units (which WebCodecs would reject),
//   we send a 'demo_frame' JSON message with canvas-draw instructions.
//   The JS dashboard renders colourful synthetic desktops with taskbar,
//   window title-bars, and live data (time, app name, CPU, cursor) that
//   update every second — giving a convincing live-screen look.
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Text.Json;
using TadBridge.Shared;

namespace TadTeacher;

public sealed class DemoTcpClientManager : IDisposable
{
    private static readonly string[] DemoApps = [
        "Microsoft Word — Essay.docx",
        "Chrome — Google Classroom",
        "Visual Studio Code — main.py",
        "PowerPoint — Presentation.pptx",
        "Excel — Homework.xlsx",
        "Chrome — Khan Academy",
        "Firefox — Wikipedia",
        "Notepad++ — notes.txt",
        "Chrome — YouTube",
        "File Explorer",
        "Calculator",
        "Paint — drawing.png"
    ];

    private static readonly string[] DemoUsers = [
        "emma.johnson", "liam.smith", "olivia.williams",
        "noah.brown", "ava.jones", "elijah.garcia",
        "sophia.miller", "james.davis", "isabella.rodriguez",
        "benjamin.martinez", "mia.hernandez", "lucas.lopez"
    ];

    // Wallpaper-style background colours per student (hue variety)
    private static readonly string[] WallpaperColors = [
        "#1a1a2e", "#16213e", "#0f3460", "#1b1a2e",
        "#1e3a5f", "#2d132c", "#1a1a40", "#0a1931",
        "#1c1c3c", "#191923", "#1b2838", "#0d1b2a"
    ];

    // Window accent colours
    private static readonly string[] AccentColors = [
        "#58A6FF", "#3FB950", "#D29922", "#F85149",
        "#BC8CFF", "#79C0FF", "#56D364", "#E3B341",
        "#FF7B72", "#D2A8FF", "#A5D6FF", "#7EE787"
    ];

    private readonly ConcurrentDictionary<string, DemoStudent> _students = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _statusTimer;
    private readonly Timer _frameTimer;

    // ─── Events (same signature as TcpClientManager) ─────────────────

    public event Action<string, StudentStatus>? StudentStatusUpdated;
#pragma warning disable CS0067 // Kept for API compat with TcpClientManager
    public event Action<string, byte[], bool>? VideoFrameReceived;
    public event Action<string, byte[], bool>? MainFrameReceived;
#pragma warning restore CS0067

    /// <summary>
    /// Custom event for synthetic demo thumbnails.
    /// Payload: JSON string with canvas-draw instructions.
    /// </summary>
    public event Action<string, string>? DemoFrameReady;

    // ─── Properties ──────────────────────────────────────────────────

    public int ConnectedCount => _students.Count(s => s.Value.IsConnected);
    public int TotalEndpoints => _students.Count;

    public DemoTcpClientManager()
    {
        // Create 12 demo students
        for (int i = 0; i < 12; i++)
        {
            string ip = $"10.0.1.{40 + i}";
            _students[ip] = new DemoStudent
            {
                Ip = ip,
                Index = i,
                Hostname = $"LAB1-PC{(i + 1):D2}",
                Username = DemoUsers[i],
                ActiveWindow = DemoApps[i],
                IsConnected = true,
                CpuUsage = 10 + Random.Shared.Next(50),
                RamUsedMb = 2000 + Random.Shared.Next(6000),
                WallpaperColor = WallpaperColors[i],
                AccentColor = AccentColors[i],
                CursorX = Random.Shared.Next(50, 430),
                CursorY = Random.Shared.Next(30, 240),
            };
        }

        // Periodic status updates every 3 seconds
        _statusTimer = new Timer(EmitStatusUpdates, null, 1000, 3000);

        // Synthetic frame updates every 1.2 seconds (slightly below real 1fps)
        _frameTimer = new Timer(EmitDemoFrames, null, 2000, 1200);
    }

    private void EmitStatusUpdates(object? _)
    {
        foreach (var (ip, student) in _students)
        {
            if (!student.IsConnected) continue;

            // Vary CPU/RAM slightly each tick
            student.CpuUsage = Math.Clamp(student.CpuUsage + Random.Shared.Next(-8, 9), 3, 95);
            student.RamUsedMb = Math.Clamp(student.RamUsedMb + Random.Shared.Next(-200, 201), 1500, 14000);

            // Occasionally change active window
            if (Random.Shared.Next(10) == 0)
                student.ActiveWindow = DemoApps[Random.Shared.Next(DemoApps.Length)];

            // Tick freeze timer
            if (student.IsFrozen && student.FreezeSecondsRemaining > 0)
            {
                student.FreezeSecondsRemaining -= 3;
                if (student.FreezeSecondsRemaining <= 0)
                {
                    student.IsFrozen = false;
                    student.FreezeSecondsRemaining = 0;
                }
            }

            var status = new StudentStatus
            {
                Hostname = student.Hostname,
                Username = student.Username,
                IpAddress = ip,
                DriverLoaded = true,
                IsLocked = student.IsLocked,
                IsStreaming = student.IsStreaming,
                ActiveWindow = student.ActiveWindow,
                CpuUsage = student.CpuUsage,
                RamUsedMb = student.RamUsedMb,
                Role = "Student",
                IsFrozen = student.IsFrozen,
                FreezeSecondsRemaining = student.FreezeSecondsRemaining,
                Timestamp = DateTime.UtcNow,
            };

            StudentStatusUpdated?.Invoke(ip, status);
        }
    }

    private void EmitDemoFrames(object? _)
    {
        foreach (var (ip, student) in _students)
        {
            if (!student.IsConnected) continue;

            // Drift cursor randomly for "live" look
            student.CursorX = Math.Clamp(student.CursorX + Random.Shared.Next(-40, 41), 20, 460);
            student.CursorY = Math.Clamp(student.CursorY + Random.Shared.Next(-30, 31), 20, 250);

            var frame = new
            {
                wallpaper = student.WallpaperColor,
                accent = student.AccentColor,
                hostname = student.Hostname,
                username = student.Username,
                app = student.ActiveWindow,
                cpu = Math.Round(student.CpuUsage),
                ram = student.RamUsedMb,
                locked = student.IsLocked,
                frozen = student.IsFrozen,
                cursorX = student.CursorX,
                cursorY = student.CursorY,
                time = DateTime.Now.ToString("HH:mm:ss"),
                index = student.Index,
            };

            DemoFrameReady?.Invoke(ip, JsonSerializer.Serialize(frame));
        }
    }

    // ─── Endpoint management (no-op for demo, students are pre-loaded) ──

    public void AddStudent(string ip, int port = 17420) { }
    public void RemoveStudent(string ip) => _students.TryRemove(ip, out _);
    public void LoadStudents(IEnumerable<string> ips) { }

    // ─── Commands ────────────────────────────────────────────────────

    public void LockStudent(string ip)
    {
        if (_students.TryGetValue(ip, out var s)) s.IsLocked = true;
    }

    public void UnlockStudent(string ip)
    {
        if (_students.TryGetValue(ip, out var s)) s.IsLocked = false;
    }

    public void StartRemoteView(string ip)
    {
        if (_students.TryGetValue(ip, out var s)) s.IsStreaming = true;
    }

    public void StopRemoteView(string ip)
    {
        if (_students.TryGetValue(ip, out var s)) s.IsStreaming = false;
    }

    public void StartFocusStream(string ip) => StartRemoteView(ip);
    public void StopFocusStream(string ip) => StopRemoteView(ip);

    public void FreezeStudent(string ip, int durationSeconds, string message = "")
    {
        if (_students.TryGetValue(ip, out var s))
        {
            s.IsFrozen = true;
            s.FreezeSecondsRemaining = durationSeconds;
        }
    }

    public void UnfreezeStudent(string ip)
    {
        if (_students.TryGetValue(ip, out var s))
        {
            s.IsFrozen = false;
            s.FreezeSecondsRemaining = 0;
        }
    }

    public void BroadcastLock()
    {
        foreach (var s in _students.Values) s.IsLocked = true;
    }

    public void BroadcastUnlock()
    {
        foreach (var s in _students.Values) s.IsLocked = false;
    }

    public void BroadcastFreeze(int durationSeconds, string message = "")
    {
        foreach (var s in _students.Values)
        {
            s.IsFrozen = true;
            s.FreezeSecondsRemaining = durationSeconds;
        }
    }

    public void BroadcastUnfreeze()
    {
        foreach (var s in _students.Values)
        {
            s.IsFrozen = false;
            s.FreezeSecondsRemaining = 0;
        }
    }

    public void BroadcastCollectFiles() { /* No-op in demo */ }

    public void PingAll()
    {
        // Force a status + frame update cycle
        EmitStatusUpdates(null);
        EmitDemoFrames(null);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _statusTimer.Dispose();
        _frameTimer.Dispose();
        _cts.Dispose();
    }

    // ─── Internal ────────────────────────────────────────────────────

    private sealed class DemoStudent
    {
        public string Ip { get; set; } = "";
        public int Index { get; set; }
        public string Hostname { get; set; } = "";
        public string Username { get; set; } = "";
        public string ActiveWindow { get; set; } = "";
        public bool IsConnected { get; set; }
        public bool IsLocked { get; set; }
        public bool IsStreaming { get; set; }
        public bool IsFrozen { get; set; }
        public int FreezeSecondsRemaining { get; set; }
        public double CpuUsage { get; set; }
        public long RamUsedMb { get; set; }
        public string WallpaperColor { get; set; } = "#1a1a2e";
        public string AccentColor { get; set; } = "#58A6FF";
        public int CursorX { get; set; }
        public int CursorY { get; set; }
    }
}
