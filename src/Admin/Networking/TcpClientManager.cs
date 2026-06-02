// ───────────────────────────────────────────────────────────────────────────
// TcpClientManager.cs — Teacher-side TCP connection pool
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Maintains persistent TCP connections to up to 50 student endpoints.
// Each student runs a TADTcpListener on port 17420.
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
using TADBridge.Shared;

namespace TADAdmin;

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

    /// <summary>Fired when a JPEG snapshot arrives (thumbnail for grid tile).</summary>
    public event Action<string, byte[]>? SnapshotReceived;

    // ─── Public Properties ────────────────────────────────────────────

    public int ConnectedCount => _connections.Count(c => c.Value.IsConnected);
    public int TotalEndpoints => _connections.Count;

    /// <summary>All known student IPs regardless of connection state.</summary>
    public List<string> GetAllEndpointIps() => _connections.Keys.ToList();

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

    public void LockStudent(string ip) => SendCommand(ip, TADCommand.Lock);
    public void UnlockStudent(string ip) => SendCommand(ip, TADCommand.Unlock);

    public void StartRemoteView(string ip) => SendCommand(ip, TADCommand.RvStart);
    public void StopRemoteView(string ip) => SendCommand(ip, TADCommand.RvStop);

    /// <summary>Start focused 30fps 720p main-stream for one student.</summary>
    public void StartFocusStream(string ip) => SendCommand(ip, TADCommand.RvFocusStart);
    /// <summary>Stop focused main-stream (sub-stream keeps running).</summary>
    public void StopFocusStream(string ip) => SendCommand(ip, TADCommand.RvFocusStop);

    public void BroadcastLock() => BroadcastCommand(TADCommand.Lock);
    public void BroadcastUnlock() => BroadcastCommand(TADCommand.Unlock);
    public void FreezeStudent(string ip) => SendCommand(ip, TADCommand.Freeze);
    public void UnfreezeStudent(string ip) => SendCommand(ip, TADCommand.Unfreeze);
    public void BroadcastFreeze() => BroadcastCommand(TADCommand.Freeze);
    public void BroadcastUnfreeze() => BroadcastCommand(TADCommand.Unfreeze);
    public void BroadcastBlankScreen() => BroadcastCommand(TADCommand.BlankScreen);
    public void BroadcastUnblankScreen() => BroadcastCommand(TADCommand.UnblankScreen);
    public void BroadcastPushMessage(string message)
    {
        var frame = TADFrameCodec.EncodeJson(TADCommand.PushMessage, new PushMessageRequest { Message = message });
        BroadcastRaw(frame);
    }
    public void BroadcastCollectFiles()
    {
        var request = new CollectFilesRequest();
        var frame = TADFrameCodec.EncodeJson(TADCommand.CollectFiles, request);
        BroadcastRaw(frame);
    }
    public void PingAll() => BroadcastCommand(TADCommand.Ping);
    public void RequestSnapshot(string ip) => SendCommand(ip, TADCommand.Snapshot);
    public void BroadcastRequestSnapshot() => BroadcastCommand(TADCommand.Snapshot);

    public void KillProcessOnStudent(string ip, int pid)
    {
        var frame = TADFrameCodec.EncodeJson(TADCommand.KillProcess, new KillProcessRequest { ProcessId = pid });
        SendRaw(ip, frame);
    }

    public void BroadcastBlocklist(BlocklistUpdate blocklist)
    {
        var frame = TADFrameCodec.EncodeJson(TADCommand.SetBlocklist, blocklist);
        BroadcastRaw(frame);
    }

    public int BroadcastProgramLock(BlocklistUpdate blocklist)
    {
        var frame = TADFrameCodec.EncodeJson(TADCommand.ProgramLock, blocklist);
        return BroadcastRawCounted(frame);
    }

    public int BroadcastProgramUnlock()
    {
        return BroadcastCommandCounted(TADCommand.ProgramUnlock);
    }

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
            if (!TADFrameCodec.TryDecode(span, out var cmd, out var payload, out int consumed))
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

    private void HandleFrame(string ip, TADCommand cmd, ReadOnlyMemory<byte> payload)
    {
        switch (cmd)
        {
            case TADCommand.Pong:
                // Connection alive
                break;

            case TADCommand.Status:
                try
                {
                    var status = JsonSerializer.Deserialize<StudentStatus>(payload.Span);
                    if (status != null)
                        StudentStatusUpdated?.Invoke(ip, status);
                }
                catch { /* Ignore malformed JSON */ }
                break;

            case TADCommand.VideoFrame:
                VideoFrameReceived?.Invoke(ip, payload.ToArray(), false);
                break;

            case TADCommand.VideoKeyFrame:
                VideoFrameReceived?.Invoke(ip, payload.ToArray(), true);
                break;

            case TADCommand.MainFrame:
                MainFrameReceived?.Invoke(ip, payload.ToArray(), false);
                break;

            case TADCommand.MainKeyFrame:
                MainFrameReceived?.Invoke(ip, payload.ToArray(), true);
                break;

            case TADCommand.SnapshotData:
                SnapshotReceived?.Invoke(ip, payload.ToArray());
                break;

            case TADCommand.FileChunk:
            case TADCommand.FileComplete:
                // File transfer handling (future expansion)
                break;
        }
    }

    // ─── Send Helpers ─────────────────────────────────────────────────

    private void SendCommand(string ip, TADCommand cmd, ReadOnlySpan<byte> payload = default)
    {
        if (!_connections.TryGetValue(ip, out var conn) || !conn.IsConnected) return;
        try
        {
            var frame = TADFrameCodec.Encode(cmd, payload);
            conn.Client?.GetStream().Write(frame);
        }
        catch
        {
            conn.IsConnected = false;
        }
    }

    private void BroadcastCommand(TADCommand cmd)
    {
        var frame = TADFrameCodec.Encode(cmd);
        BroadcastRaw(frame);
    }

    /// <summary>Broadcast a command and return how many students received it.</summary>
    public int BroadcastCommandCounted(TADCommand cmd)
    {
        var frame = TADFrameCodec.Encode(cmd);
        return BroadcastRawCounted(frame);
    }

    private void BroadcastRaw(byte[] frame)
    {
        BroadcastRawCounted(frame);
    }

    private int BroadcastRawCounted(byte[] frame)
    {
        int sent = 0;
        foreach (var conn in _connections.Values.Where(c => c.IsConnected))
        {
            try
            {
                conn.Client?.GetStream().Write(frame);
                sent++;
            }
            catch
            {
                conn.IsConnected = false;
            }
        }
        return sent;
    }

    private bool SendRaw(string ip, byte[] frame)
    {
        if (!_connections.TryGetValue(ip, out var conn) || !conn.IsConnected) return false;
        try { conn.Client?.GetStream().Write(frame); return true; }
        catch { conn.IsConnected = false; return false; }
    }

    /// <summary>Send a pre-encoded frame to a specific student (public access for per-student commands).</summary>
    public bool SendCommandToStudent(string ip, byte[] frame) => SendRaw(ip, frame);

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
