// ───────────────────────────────────────────────────────────────────────────
// RecordingService.cs — Automatic screenshot + video recording for the DC
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Discovers endpoints via the same UDP multicast as TADAdmin, opens a
// dedicated TCP connection to each, and periodically requests JPEG
// screenshots (TadCommand.Snapshot).  Optionally activates the sub-stream
// (RvStart) and saves the raw H.264 frames to per-session .h264 files.
//
// Output layout:
//   <SaveFolder>\yyyy-MM-dd\<hostname>\HH-mm-ss.jpg      (screenshots)
//   <SaveFolder>\yyyy-MM-dd\<hostname>\HH-mm-ss.h264     (stream files)
// ───────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TADBridge.Shared;

namespace TADDomainController.Services;

// ═══════════════════════════════════════════════════════════════════════════
// Public data model
// ═══════════════════════════════════════════════════════════════════════════

public sealed class RecordingEntry
{
    public string Hostname  { get; set; } = "";
    public string Ip        { get; set; } = "";
    public string FilePath  { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsVideo     { get; set; }

    public string FileSizeDisplay => FileSizeBytes >= 1024 * 1024
        ? $"{FileSizeBytes / (1024.0 * 1024):F1} MB"
        : $"{FileSizeBytes / 1024.0:F0} KB";

    public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");
}

// ═══════════════════════════════════════════════════════════════════════════
// Service
// ═══════════════════════════════════════════════════════════════════════════

public sealed class RecordingService : IDisposable
{
    // ─── Configuration ────────────────────────────────────────────────

    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "TAD.RV", "Recordings");

    /// <summary>How often to take a screenshot per endpoint (seconds).</summary>
    public int SnapshotIntervalSeconds { get; set; } = 300;

    /// <summary>When true, also saves the H.264 sub-stream to .h264 files.</summary>
    public bool VideoRecordingEnabled { get; set; } = false;

    // ─── Events ───────────────────────────────────────────────────────

    /// <summary>Fired on the thread-pool whenever a file is saved.</summary>
    public event Action<RecordingEntry>? FileSaved;

    /// <summary>Fired when a new endpoint is auto-discovered.</summary>
    public event Action<string, string>? EndpointDiscovered; // (ip, hostname)

    // ─── State ────────────────────────────────────────────────────────

    public bool IsRunning { get; private set; }

    // ip → agent
    private readonly ConcurrentDictionary<string, EndpointAgent> _agents = new();
    private CancellationTokenSource? _cts;
    private Thread? _discoveryThread;

    private static readonly IPAddress MulticastGroup = IPAddress.Parse("239.1.1.1");
    private const int MulticastPort = 17421;

    // ─── Lifecycle ────────────────────────────────────────────────────

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();

        Directory.CreateDirectory(SaveFolder);

        _discoveryThread = new Thread(DiscoveryLoop) { IsBackground = true, Name = "RecordingDiscovery" };
        _discoveryThread.Start();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _cts?.Cancel();
        _discoveryThread?.Join(3000);

        foreach (var agent in _agents.Values)
            agent.Dispose();
        _agents.Clear();
    }

    /// <summary>Manually add an endpoint (for workgroup / no-multicast scenarios).</summary>
    public void AddEndpoint(string ip, int port = 17420, string hostname = "")
    {
        if (_cts == null || _cts.IsCancellationRequested) return;
        if (_agents.ContainsKey(ip)) return;

        var agent = new EndpointAgent(ip, port, hostname, this);
        if (_agents.TryAdd(ip, agent))
        {
            EndpointDiscovered?.Invoke(ip, hostname);
            agent.Start(_cts.Token);
        }
    }

    // ─── UDP Multicast Discovery Loop ─────────────────────────────────

    private void DiscoveryLoop()
    {
        var ct = _cts!.Token;

        while (!ct.IsCancellationRequested)
        {
            UdpClient? udp = null;
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
                udp.JoinMulticastGroup(MulticastGroup);
                udp.Client.ReceiveTimeout = 3000;

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref ep);
                        if (data.Length == 0) continue;

                        var pkt = JsonSerializer.Deserialize<DiscoveryPkt>(Encoding.UTF8.GetString(data));
                        if (pkt == null || pkt.Version < 1) continue;
                        if (string.Equals(pkt.Role, "admin", StringComparison.OrdinalIgnoreCase)) continue;

                        string ip = !string.IsNullOrEmpty(pkt.IpAddress) ? pkt.IpAddress : ep.Address.ToString();
                        int port = pkt.TcpPort > 0 ? pkt.TcpPort : 17420;

                        if (!_agents.ContainsKey(ip))
                        {
                            AddEndpoint(ip, port, pkt.Hostname);
                        }
                    }
                    catch (SocketException) { /* timeout */ }
                    catch (JsonException) { /* bad packet */ }
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                try { Task.Delay(2000, ct).Wait(ct); } catch { break; }
            }
            finally
            {
                try { udp?.DropMulticastGroup(MulticastGroup); } catch { }
                udp?.Dispose();
            }
        }
    }

    // ─── Internal helpers for agents ──────────────────────────────────

    internal void NotifyFileSaved(RecordingEntry entry) => FileSaved?.Invoke(entry);

    internal string BuildFilePath(string hostname, string ip, bool video)
    {
        var safeHost = string.IsNullOrEmpty(hostname) ? ip : hostname;
        foreach (var c in Path.GetInvalidFileNameChars())
            safeHost = safeHost.Replace(c, '_');

        var dateDir = Path.Combine(SaveFolder, DateTime.Now.ToString("yyyy-MM-dd"), safeHost);
        Directory.CreateDirectory(dateDir);

        var stamp = DateTime.Now.ToString("HH-mm-ss");
        return Path.Combine(dateDir, stamp + (video ? ".h264" : ".jpg"));
    }

    public void Dispose() => Stop();

    // ─── Internal discovery packet ────────────────────────────────────

    private sealed class DiscoveryPkt
    {
        [JsonPropertyName("v")]   public int Version { get; set; }
        [JsonPropertyName("room")] public string RoomId { get; set; } = "";
        [JsonPropertyName("host")] public string Hostname { get; set; } = "";
        [JsonPropertyName("ip")]   public string IpAddress { get; set; } = "";
        [JsonPropertyName("port")] public int TcpPort { get; set; } = 17420;
        [JsonPropertyName("role")] public string Role { get; set; } = "student";
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Per-endpoint agent: single TCP connection, snapshot timer, optional stream
// ═══════════════════════════════════════════════════════════════════════════

internal sealed class EndpointAgent : IDisposable
{
    private readonly string _ip;
    private readonly int _port;
    private string _hostname;
    private readonly RecordingService _svc;
    private CancellationTokenSource? _cts;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly object _writeLock = new();

    // Video recording state
    private FileStream? _videoFile;
    private string? _videoPath;

    // Snapshot scheduling
    private DateTime _nextSnapshot = DateTime.MinValue;

    public EndpointAgent(string ip, int port, string hostname, RecordingService svc)
    {
        _ip = ip;
        _port = port;
        _hostname = hostname;
        _svc = svc;
    }

    public void Start(CancellationToken parentCt)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _tcp = new TcpClient();
                _tcp.NoDelay = true;
                await _tcp.ConnectAsync(_ip, _port, ct);
                _stream = _tcp.GetStream();

                // Start video recording if enabled
                if (_svc.VideoRecordingEnabled)
                    OpenVideoFile();

                // Start reading frames and schedule snapshot timer in parallel
                var readTask = ReadLoopAsync(ct);
                var snapshotTask = SnapshotTimerAsync(ct);
                await Task.WhenAny(readTask, snapshotTask);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Reconnect after delay
                CloseVideoFile();
                try { await Task.Delay(10_000, ct); } catch { break; }
            }
            finally
            {
                CloseVideoFile();
                _stream?.Dispose();
                _tcp?.Dispose();
                _stream = null;
                _tcp = null;
            }
        }
    }

    // ─── Snapshot Timer ───────────────────────────────────────────────

    private async Task SnapshotTimerAsync(CancellationToken ct)
    {
        // Take first snapshot immediately
        _nextSnapshot = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            if (DateTime.UtcNow >= _nextSnapshot)
            {
                SendCommand(TadCommand.Snapshot);
                _nextSnapshot = DateTime.UtcNow.AddSeconds(_svc.SnapshotIntervalSeconds);
            }

            await Task.Delay(1000, ct);
        }
    }

    // ─── Inbound frame reader ─────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        using var acc = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int read = await _stream.ReadAsync(buf, ct);
                if (read == 0) break;

                acc.Write(buf, 0, read);
                ProcessFrames(acc);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* connection lost — let RunAsync reconnect */ }
    }

    private void ProcessFrames(MemoryStream acc)
    {
        var data = acc.GetBuffer();
        int len = (int)acc.Length;
        int offset = 0;

        while (offset < len)
        {
            if (!TadFrameCodec.TryDecode(data.AsSpan(offset, len - offset),
                out var cmd, out var payload, out int consumed))
                break;

            offset += consumed;
            HandleFrame(cmd, payload);
        }

        if (offset > 0)
        {
            var remaining = data.AsSpan(offset);
            acc.SetLength(0);
            acc.Write(remaining);
        }
    }

    private void HandleFrame(TadCommand cmd, ReadOnlyMemory<byte> payload)
    {
        switch (cmd)
        {
            case TadCommand.Status:
                try
                {
                    var s = JsonSerializer.Deserialize<StudentStatus>(payload.Span);
                    if (s != null && !string.IsNullOrEmpty(s.Hostname))
                        _hostname = s.Hostname;
                }
                catch { /* malformed */ }
                break;

            case TadCommand.SnapshotData:
                SaveSnapshot(payload.ToArray());
                break;

            case TadCommand.VideoFrame:
            case TadCommand.VideoKeyFrame:
                if (_svc.VideoRecordingEnabled && _videoFile != null)
                    AppendVideoFrame(payload.ToArray());
                break;
        }
    }

    // ─── Screenshot save ──────────────────────────────────────────────

    private void SaveSnapshot(byte[] jpegBytes)
    {
        try
        {
            var path = _svc.BuildFilePath(_hostname, _ip, video: false);
            File.WriteAllBytes(path, jpegBytes);

            _svc.NotifyFileSaved(new RecordingEntry
            {
                Hostname      = _hostname,
                Ip            = _ip,
                FilePath      = path,
                Timestamp     = DateTime.Now,
                FileSizeBytes = jpegBytes.Length,
                IsVideo       = false
            });
        }
        catch { /* disk full / access denied — skip */ }
    }

    // ─── Video recording ──────────────────────────────────────────────

    private void OpenVideoFile()
    {
        try
        {
            _videoPath = _svc.BuildFilePath(_hostname, _ip, video: true);
            _videoFile = new FileStream(_videoPath, FileMode.Create, FileAccess.Write, FileShare.Read);

            // Enable RvStart to get H.264 frames
            SendCommand(TadCommand.RvStart);
        }
        catch { _videoFile = null; }
    }

    private void AppendVideoFrame(byte[] frameData)
    {
        try { _videoFile?.Write(frameData); }
        catch { CloseVideoFile(); }
    }

    private void CloseVideoFile()
    {
        if (_videoFile == null) return;

        try
        {
            _videoFile.Flush();
            long size = _videoFile.Length;
            _videoFile.Dispose();
            _videoFile = null;

            if (_videoPath != null && size > 0)
            {
                _svc.NotifyFileSaved(new RecordingEntry
                {
                    Hostname      = _hostname,
                    Ip            = _ip,
                    FilePath      = _videoPath,
                    Timestamp     = DateTime.Now,
                    FileSizeBytes = size,
                    IsVideo       = true
                });
            }
        }
        catch { }
        finally { _videoFile = null; }
    }

    // ─── Send helpers ─────────────────────────────────────────────────

    private void SendCommand(TadCommand cmd)
    {
        lock (_writeLock)
        {
            if (_stream == null) return;
            try { _stream.Write(TadFrameCodec.Encode(cmd)); }
            catch { }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        CloseVideoFile();
        _stream?.Dispose();
        _tcp?.Dispose();
        _cts?.Dispose();
    }
}
