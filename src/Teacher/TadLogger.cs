// ───────────────────────────────────────────────────────────────────────────
// TadLogger.cs — Lightweight file logger for TAD.RV Teacher startup diagnosis
//
// Writes to two files simultaneously:
//   %TEMP%\TadTeacher_latest.log   — always overwritten (easy to find)
//   %TEMP%\TadTeacher_YYYYMMDD_HHmmss.log — archived per session
//
// Flushed after every write so the log survives hard crashes.
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.Runtime.CompilerServices;

namespace TadTeacher;

public static class TadLogger
{
    private static StreamWriter? _writer;
    private static readonly object _lock = new();

    public static string LogPath { get; private set; } = "";

    public static void Init()
    {
        try
        {
            var tmp  = Path.GetTempPath();
            var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(tmp, $"TadTeacher_{ts}.log");
            var latest = Path.Combine(tmp, "TadTeacher_latest.log");

            LogPath = path;

            // Keep a session-stamped copy and overwrite "latest" each run
            Directory.CreateDirectory(tmp);
            _writer = new StreamWriter(path, append: false, encoding: System.Text.Encoding.UTF8)
                { AutoFlush = true };

            // Mirror first line to latest.log
            File.WriteAllText(latest,
                $"TAD.RV Teacher Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"Full log: {path}\r\n\r\n");

            Info($"=== TAD.RV Teacher Controller — v26700.192 ===");
            Info($"Log file   : {path}");
            Info($"Process ID : {Environment.ProcessId}");
            Info($"OS         : {Environment.OSVersion}");
            Info($"CLR        : {Environment.Version}");
            Info($"Temp dir   : {tmp}");
        }
        catch
        {
            // If logging itself fails, continue silently — don't crash the app
        }
    }

    public static void Info(string message,
        [CallerFilePath]   string file = "",
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
        => Write("INFO ", message, file, member, line);

    public static void Warn(string message,
        [CallerFilePath]   string file = "",
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
        => Write("WARN ", message, file, member, line);

    public static void Error(string message,
        [CallerFilePath]   string file = "",
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
        => Write("ERROR", message, file, member, line);

    public static void Exception(Exception ex, string context = "",
        [CallerFilePath]   string file = "",
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        Write("CRASH", $"{context}: [{ex.GetType().Name}] {ex.Message}", file, member, line);
        Write("CRASH", $"  Stack: {ex.StackTrace?.Replace(Environment.NewLine, "\n         ")}", file, member, line);
        if (ex.InnerException != null)
            Write("CRASH", $"  Inner: [{ex.InnerException.GetType().Name}] {ex.InnerException.Message}", file, member, line);
    }

    private static void Write(string level, string message, string file, string member, int line)
    {
        if (_writer == null) return;
        try
        {
            var src = $"{Path.GetFileNameWithoutExtension(file)}::{member}:{line}";
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {src,-42} {message}";

            lock (_lock)
            {
                _writer.WriteLine(entry);
                // Also mirror to latest.log (append)
                var latest = Path.Combine(Path.GetTempPath(), "TadTeacher_latest.log");
                File.AppendAllText(latest, entry + "\r\n");
            }
        }
        catch { /* Never throw from logger */ }
    }

    public static void Close()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Close();
            _writer = null;
        }
    }
}
