// ───────────────────────────────────────────────────────────────────────────
// TadTcpListener.cs — Student-side TCP listener for teacher commands
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Runs inside TadBridgeService as a hosted background worker.
// Accepts a single TCP connection from the teacher controller and
// routes commands to the appropriate subsystem (lock, RV, collect).
//
// Port: 17420 (TAD default)
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TadBridge.Driver;
using TadBridge.Shared;
using TadBridge.Capture;

namespace TadBridge.Networking;

public sealed class TadTcpListener : BackgroundService
{
    private readonly ILogger<TadTcpListener> _log;
    private readonly DriverBridge _driver;
    private readonly ScreenCaptureEngine _capture;
    private readonly PrivacyRedactor _redactor;

    private TcpListener? _listener;
    private NetworkStream? _activeStream;
    private readonly object _streamLock = new();

    public const int ListenPort = 17420;

    // Status state
    private volatile bool _isLocked;
    private volatile bool _isStreaming;
    private Process? _lockOverlayProcess;

    public TadTcpListener(
        ILogger<TadTcpListener> log,
        DriverBridge driver,
        ScreenCaptureEngine capture,
        PrivacyRedactor redactor)
    {
        _log = log;
        _driver = driver;
        _capture = capture;
        _redactor = redactor;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _listener = new TcpListener(IPAddress.Any, ListenPort);
        _listener.Start();
        _log.LogInformation("TCP listener started on port {Port}", ListenPort);

        // Periodic status beacon
        _ = StatusBeaconAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                client.ReceiveBufferSize = 256 * 1024;

                _log.LogInformation("Teacher connected from {Ep}", client.Client.RemoteEndPoint);

                lock (_streamLock)
                    _activeStream = client.GetStream();

                await HandleConnectionAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "TCP accept/handle error");
                await Task.Delay(1000, ct);
            }
        }
    }

    // ─── Connection Handler ───────────────────────────────────────────

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();
        var buffer = new byte[64 * 1024];
        using var accumulator = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;

                accumulator.Write(buffer, 0, read);
                ProcessFrames(accumulator, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Connection lost");
        }
        finally
        {
            lock (_streamLock) _activeStream = null;
            client.Dispose();

            // Auto-cleanup on disconnect
            if (_isStreaming) StopStreaming();
            if (_isLocked) ExecuteUnlock();
        }
    }

    private void ProcessFrames(MemoryStream accumulator, CancellationToken ct)
    {
        var data = accumulator.GetBuffer();
        int length = (int)accumulator.Length;
        int offset = 0;

        while (offset < length)
        {
            if (!TadFrameCodec.TryDecode(data.AsSpan(offset, length - offset), out var cmd, out var payload, out int consumed))
                break;

            offset += consumed;
            RouteCommand(cmd, payload, ct);
        }

        if (offset > 0)
        {
            var remaining = data.AsSpan(offset);
            accumulator.SetLength(0);
            accumulator.Write(remaining);
        }
    }

    // ─── Command Router ───────────────────────────────────────────────

    private void RouteCommand(TadCommand cmd, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        _log.LogDebug("Received command: {Cmd}", cmd);

        switch (cmd)
        {
            case TadCommand.Ping:
                SendFrame(TadCommand.Pong);
                break;

            case TadCommand.Lock:
                ExecuteLock();
                break;

            case TadCommand.Unlock:
                ExecuteUnlock();
                break;

            case TadCommand.RvStart:
                StartStreaming(ct);
                break;

            case TadCommand.RvStop:
                StopStreaming();
                break;

            case TadCommand.RvFocusStart:
                StartMainStream();
                break;

            case TadCommand.RvFocusStop:
                StopMainStream();
                break;

            case TadCommand.CollectFiles:
                try
                {
                    var req = JsonSerializer.Deserialize<CollectFilesRequest>(payload.Span);
                    if (req != null) _ = CollectFilesAsync(req, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Bad CollectFiles payload");
                }
                break;

            case TadCommand.PushMessage:
                // Future: show message on student screen
                break;
        }
    }

    // ─── LOCK Command ─────────────────────────────────────────────────

    private void ExecuteLock()
    {
        if (_isLocked) return;
        _isLocked = true;

        // 1. Tell the kernel driver to disable keyboard/mouse
        try
        {
            _driver.SendHardLock(true);
            _log.LogInformation("Kernel hard-lock engaged");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to engage kernel hard-lock");
        }

        // 2. Launch the fullscreen lock overlay (WebView2 or WPF window)
        try
        {
            var overlayPath = Path.Combine(AppContext.BaseDirectory, "TadLockOverlay.exe");
            if (File.Exists(overlayPath))
            {
                _lockOverlayProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = overlayPath,
                    WindowStyle = ProcessWindowStyle.Maximized,
                    UseShellExecute = true
                });

                // 3. Protect the overlay process from being killed
                if (_lockOverlayProcess != null)
                {
                    _driver.ProtectPid((uint)_lockOverlayProcess.Id);
                    _log.LogInformation("Lock overlay PID {Pid} protected", _lockOverlayProcess.Id);
                }
            }
            else
            {
                _log.LogWarning("Lock overlay not found at {Path}", overlayPath);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to launch lock overlay");
        }
    }

    private void ExecuteUnlock()
    {
        if (!_isLocked) return;
        _isLocked = false;

        // 1. Release kernel hard-lock
        try
        {
            _driver.SendHardLock(false);
            _log.LogInformation("Kernel hard-lock released");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to release kernel hard-lock");
        }

        // 2. Kill lock overlay and release PID protection
        try
        {
            if (_lockOverlayProcess is { HasExited: false })
            {
                uint overlayPid = (uint)_lockOverlayProcess.Id;
                _lockOverlayProcess.Kill();
                _lockOverlayProcess.WaitForExit(3000);
                _lockOverlayProcess.Dispose();
                _lockOverlayProcess = null;

                // Unprotect the stale PID so the driver doesn't block reuse
                try { _driver.UnprotectPid(overlayPid); }
                catch { /* Best effort — PID already gone */ }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to kill lock overlay");
        }
    }

    // ─── Remote View Streaming ────────────────────────────────────────

    private void StartStreaming(CancellationToken ct)
    {
        if (_isStreaming) return;
        _isStreaming = true;

        // Sub-stream callback: 1fps 480p → VideoFrame/VideoKeyFrame
        _capture.OnSubFrameEncoded = (frameData, isKeyFrame) =>
        {
            var cmd = isKeyFrame ? TadCommand.VideoKeyFrame : TadCommand.VideoFrame;
            SendFrame(cmd, frameData);
        };

        // Legacy callback for backwards compatibility
        _capture.OnFrameEncoded = (frameData, isKeyFrame) =>
        {
            // Handled by OnSubFrameEncoded above
        };

        // Main-stream callback: 30fps 720p → MainFrame/MainKeyFrame
        _capture.OnMainFrameEncoded = (frameData, isKeyFrame) =>
        {
            var cmd = isKeyFrame ? TadCommand.MainKeyFrame : TadCommand.MainFrame;
            SendFrame(cmd, frameData);
        };

        _ = _capture.StartAsync(ct);
        _log.LogInformation("Sub-stream started (1fps, 480p)");
    }

    private void StopStreaming()
    {
        if (!_isStreaming) return;
        _isStreaming = false;

        _capture.Stop();
        _log.LogInformation("Screen streaming stopped");
    }

    private void StartMainStream()
    {
        if (!_isStreaming) return;
        _capture.StartMainStream();
        _log.LogInformation("Main-stream started (30fps, 720p)");
    }

    private void StopMainStream()
    {
        _capture.StopMainStream();
        _log.LogInformation("Main-stream stopped");
    }

    // ─── File Collection ──────────────────────────────────────────────

    private async Task CollectFilesAsync(CollectFilesRequest request, CancellationToken ct)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(
            request.SourcePath.Replace("%USERNAME%", Environment.UserName));

        if (!Directory.Exists(expandedPath))
        {
            _log.LogWarning("Collect path not found: {Path}", expandedPath);
            return;
        }

        var files = Directory.EnumerateFiles(expandedPath, request.FilePattern, SearchOption.AllDirectories)
            .Where(f => new FileInfo(f).Length <= request.MaxFileSizeBytes)
            .Take(100); // Safety cap

        foreach (var filePath in files)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var data = await File.ReadAllBytesAsync(filePath, ct);
                var meta = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    path = Path.GetRelativePath(expandedPath, filePath),
                    size = data.Length
                });

                // Send file in chunks (1 MB max per chunk)
                const int chunkSize = 1024 * 1024;
                for (int offset = 0; offset < data.Length; offset += chunkSize)
                {
                    int len = Math.Min(chunkSize, data.Length - offset);
                    SendFrame(TadCommand.FileChunk, data.AsSpan(offset, len));
                }

                SendFrame(TadCommand.FileComplete, meta);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to collect {File}", filePath);
            }
        }
    }

    // ─── Status Beacon ────────────────────────────────────────────────

    private async Task StatusBeaconAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var heartbeat = _driver.Heartbeat();
                var status = new StudentStatus
                {
                    Hostname = Environment.MachineName,
                    Username = Environment.UserName,
                    IpAddress = GetLocalIp(),
                    DriverLoaded = heartbeat != null,
                    IsLocked = _isLocked,
                    IsStreaming = _isStreaming,
                    ActiveWindow = GetForegroundWindowTitle(),
                    CpuUsage = 0,   // Populated by perf counter in production
                    RamUsedMb = Environment.WorkingSet / (1024 * 1024),
                    Timestamp = DateTime.UtcNow
                };

                SendFrame(TadCommand.Status, JsonSerializer.SerializeToUtf8Bytes(status));
            }
            catch { /* Best effort */ }

            await Task.Delay(3000, ct);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private void SendFrame(TadCommand cmd, ReadOnlySpan<byte> payload = default)
    {
        lock (_streamLock)
        {
            if (_activeStream == null) return;
            try
            {
                var frame = TadFrameCodec.Encode(cmd, payload);
                _activeStream.Write(frame);
            }
            catch { /* Connection may be lost */ }
        }
    }

    private static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 80);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch { return "unknown"; }
    }

    private static string GetForegroundWindowTitle()
    {
        try
        {
            var hwnd = NativeForeground.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            var sb = new System.Text.StringBuilder(256);
            NativeForeground.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch { return ""; }
    }
}

file static class NativeForeground
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
}
