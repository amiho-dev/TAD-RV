// ───────────────────────────────────────────────────────────────────────────
// TcpClientManager.cs — Teacher-side TCP connection pool
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Maintains persistent TCP connections to up to 50 student endpoints.
// Each student runs a TadTcpListener on port 17420.
//
// Features:
//   - Auto-reconnect with exponential backoff
//   - Dual-stream: sub (1fps 480p grid) + main (30fps 720p focus)
//   - Per-student command targeting
//   - Broadcast commands (Lock / Unlock / Collect)
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using TadBridge.Shared;

namespace TadTeacher;

public sealed class TcpClientManager : IDisposable
{
    public const int DefaultPort = 17420;
    public const int MaxStudents = 50;
    private const int ReconnectBaseMs = 2000;
    private const int ReconnectMaxMs = 30000;
    private const int ReceiveBufferSize = 256 * 1024; // 256 KB

    // Endpoint registry: IP → connection state
    private readonly ConcurrentDictionary<string, StudentConnection> _connections = new();
    private readonly CancellationTokenSource _cts = new();

    // ─── Events ───────────────────────────────────────────────────────

    /// <summary>Fired when a student reports status (hostname, active window, etc.).</summary>
    public event Action<string, StudentStatus>? StudentStatusUpdated;

    /// <summary>Fired when a sub-stream H.264 frame arrives (1fps 480p grid).</summary>
    public event Action<string, byte[], bool>? VideoFrameReceived;

    /// <summary>Fired when a main-stream H.264 frame arrives (30fps 720p focus).</summary>
    public event Action<string, byte[], bool>? MainFrameReceived;

    // ─── Public Properties ────────────────────────────────────────────

    public int ConnectedCount => _connections.Count(c => c.Value.IsConnected);
    public int TotalEndpoints => _connections.Count;

    // ─── Endpoint Management ──────────────────────────────────────────

    /// <summary>Add a student IP and begin auto-connect loop.</summary>
    public void AddStudent(string ip, int port = DefaultPort)
    {
        if (_connections.ContainsKey(ip)) return;

        var conn = new StudentConnection(ip, port);
        if (_connections.TryAdd(ip, conn))
        {
            _ = ConnectLoopAsync(conn, _cts.Token);
        }
    }

    /// <summary>Remove a student and close the connection.</summary>
    public void RemoveStudent(string ip)
    {
        if (_connections.TryRemove(ip, out var conn))
        {
            conn.Dispose();
        }
    }

    /// <summary>Load student IPs from a list (e.g., AD discovery).</summary>
    public void LoadStudents(IEnumerable<string> ips)
    {
        foreach (var ip in ips.Take(MaxStudents))
            AddStudent(ip);
    }

    // ─── Commands ─────────────────────────────────────────────────────

    public void LockStudent(string ip) => SendCommand(ip, TadCommand.Lock);
    public void UnlockStudent(string ip) => SendCommand(ip, TadCommand.Unlock);

    public void StartRemoteView(string ip) => SendCommand(ip, TadCommand.RvStart);
    public void StopRemoteView(string ip) => SendCommand(ip, TadCommand.RvStop);

    /// <summary>Start focused 30fps 720p main-stream for one student.</summary>
    public void StartFocusStream(string ip) => SendCommand(ip, TadCommand.RvFocusStart);
    /// <summary>Stop focused main-stream (sub-stream keeps running).</summary>
    public void StopFocusStream(string ip) => SendCommand(ip, TadCommand.RvFocusStop);

    public void BroadcastLock() => BroadcastCommand(TadCommand.Lock);
    public void BroadcastUnlock() => BroadcastCommand(TadCommand.Unlock);
    public void BroadcastCollectFiles()
    {
        var request = new CollectFilesRequest();
        var frame = TadFrameCodec.EncodeJson(TadCommand.CollectFiles, request);
        BroadcastRaw(frame);
    }
    public void PingAll() => BroadcastCommand(TadCommand.Ping);

    // ─── Networking Core ──────────────────────────────────────────────

    private async Task ConnectLoopAsync(StudentConnection conn, CancellationToken ct)
    {
        int backoff = ReconnectBaseMs;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                conn.Client?.Dispose();
                conn.Client = new TcpClient
                {
                    ReceiveBufferSize = ReceiveBufferSize,
                    NoDelay = true
                };

                await conn.Client.ConnectAsync(conn.Ip, conn.Port, ct);
                conn.IsConnected = true;
                backoff = ReconnectBaseMs; // Reset on success

                await ReceiveLoopAsync(conn, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                conn.IsConnected = false;
            }

            // Reconnect with exponential backoff
            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { break; }
            backoff = Math.Min(backoff * 2, ReconnectMaxMs);
        }
    }

    private async Task ReceiveLoopAsync(StudentConnection conn, CancellationToken ct)
    {
        var stream = conn.Client!.GetStream();
        var buffer = new byte[ReceiveBufferSize];
        var accumulator = new MemoryStream();

        while (!ct.IsCancellationRequested && conn.Client.Connected)
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break; // Disconnected

            accumulator.Write(buffer, 0, read);

            // Parse complete frames from the accumulator
            ProcessAccumulator(conn.Ip, accumulator);
        }

        conn.IsConnected = false;
    }

    private void ProcessAccumulator(string ip, MemoryStream accumulator)
    {
        var data = accumulator.ToArray();
        int offset = 0;

        while (offset < data.Length)
        {
            var span = data.AsSpan(offset);
            if (!TadFrameCodec.TryDecode(span, out var cmd, out var payload, out int consumed))
                break;

            offset += consumed;
            HandleFrame(ip, cmd, payload);
        }

        // Compact: keep unprocessed bytes
        if (offset > 0)
        {
            var remaining = data.AsSpan(offset);
            accumulator.SetLength(0);
            accumulator.Write(remaining);
        }
    }

    private void HandleFrame(string ip, TadCommand cmd, ReadOnlyMemory<byte> payload)
    {
        switch (cmd)
        {
            case TadCommand.Pong:
                // Connection alive
                break;

            case TadCommand.Status:
                try
                {
                    var status = JsonSerializer.Deserialize<StudentStatus>(payload.Span);
                    if (status != null)
                        StudentStatusUpdated?.Invoke(ip, status);
                }
                catch { /* Ignore malformed JSON */ }
                break;

            case TadCommand.VideoFrame:
                VideoFrameReceived?.Invoke(ip, payload.ToArray(), false);
                break;

            case TadCommand.VideoKeyFrame:
                VideoFrameReceived?.Invoke(ip, payload.ToArray(), true);
                break;

            case TadCommand.MainFrame:
                MainFrameReceived?.Invoke(ip, payload.ToArray(), false);
                break;

            case TadCommand.MainKeyFrame:
                MainFrameReceived?.Invoke(ip, payload.ToArray(), true);
                break;

            case TadCommand.FileChunk:
            case TadCommand.FileComplete:
                // File transfer handling (future expansion)
                break;
        }
    }

    // ─── Send Helpers ─────────────────────────────────────────────────

    private void SendCommand(string ip, TadCommand cmd, ReadOnlySpan<byte> payload = default)
    {
        if (!_connections.TryGetValue(ip, out var conn) || !conn.IsConnected) return;
        try
        {
            var frame = TadFrameCodec.Encode(cmd, payload);
            conn.Client?.GetStream().Write(frame);
        }
        catch
        {
            conn.IsConnected = false;
        }
    }

    private void BroadcastCommand(TadCommand cmd)
    {
        var frame = TadFrameCodec.Encode(cmd);
        BroadcastRaw(frame);
    }

    private void BroadcastRaw(byte[] frame)
    {
        foreach (var conn in _connections.Values.Where(c => c.IsConnected))
        {
            try
            {
                conn.Client?.GetStream().Write(frame);
            }
            catch
            {
                conn.IsConnected = false;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var conn in _connections.Values)
            conn.Dispose();
        _connections.Clear();
        _cts.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Internal Connection State
    // ═══════════════════════════════════════════════════════════════════

    private sealed class StudentConnection : IDisposable
    {
        public string Ip { get; }
        public int Port { get; }
        public TcpClient? Client { get; set; }
        public bool IsConnected { get; set; }

        public StudentConnection(string ip, int port)
        {
            Ip = ip;
            Port = port;
        }

        public void Dispose() => Client?.Dispose();
    }
}
