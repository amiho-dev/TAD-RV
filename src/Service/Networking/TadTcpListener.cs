// ───────────────────────────────────────────────────────────────────────────
// TadTcpListener.cs — Student-side TCP listener for teacher commands
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Runs inside TADBridgeService as a hosted background worker.
// Accepts a single TCP connection from the teacher controller and
// routes commands to the appropriate subsystem (lock, RV, collect).
//
// Port: 17420 (TAD default)
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TADBridge.Driver;
using TADBridge.Shared;
using TADBridge.Capture;

namespace TADBridge.Networking;

public sealed class TadTcpListener : BackgroundService
{
    private readonly ILogger<TadTcpListener> _log;
    private readonly DriverBridge _driver;
    private readonly ScreenCaptureEngine _capture;
    private readonly PrivacyRedactor _redactor;
    private readonly IHostApplicationLifetime _lifetime;

    private TcpListener? _listener;
    private NetworkStream? _activeStream;
    private readonly object _streamLock = new();

    public const int ListenPort = 17420;

    // Status state
    private volatile bool _isLocked;
    private volatile bool _isStreaming;
    private volatile bool _isBlanked;
    private Process? _lockOverlayProcess;
    private Process? _blankOverlayProcess;

    // Blocklist enforcement
    private BlocklistUpdate _blocklist = new();
    private readonly object _blocklistLock = new();

    public TadTcpListener(
        ILogger<TadTcpListener> log,
        DriverBridge driver,
        ScreenCaptureEngine capture,
        PrivacyRedactor redactor,
        IHostApplicationLifetime lifetime)
    {
        _log = log;
        _driver = driver;
        _capture = capture;
        _redactor = redactor;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Retry loop: if the port is already in use (e.g., previous instance still shutting down
        // or a race on delayed-auto start) wait and retry rather than crashing the service.
        while (!ct.IsCancellationRequested)
        {
            _listener = new TcpListener(IPAddress.Any, ListenPort);
            try
            {
                _listener.Start();
                break; // bound successfully
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                _log.LogWarning(
                    "Port {Port} is already in use — another instance may still be shutting down. "
                    + "Retrying in 10 s…", ListenPort);
                _listener.Stop();
                try { await Task.Delay(10_000, ct); } catch (OperationCanceledException) { return; }
            }
        }
        if (ct.IsCancellationRequested) return;
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

                // Send an immediate status beacon so the teacher's dashboard
                // shows this student right away, without waiting for the 3-second cycle.
                SendStatusNow();

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
            if (_isLocked)  ExecuteUnlock();
            if (_isBlanked) ExecuteUnblankScreen();
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

            case TadCommand.Snapshot:
                _ = SendSnapshotAsync();
                break;

            case TadCommand.BlankScreen:
                ExecuteBlankScreen();
                break;

            case TadCommand.UnblankScreen:
                ExecuteUnblankScreen();
                break;

            case TadCommand.PushMessage:
                try
                {
                    var req = JsonSerializer.Deserialize<PushMessageRequest>(payload.Span);
                    if (req != null && !string.IsNullOrEmpty(req.Message))
                        ExecutePushMessage(req.Message, req.DurationSeconds);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Bad PushMessage payload");
                }
                break;

            case TadCommand.KillProcess:
                try
                {
                    var req = JsonSerializer.Deserialize<KillProcessRequest>(payload.Span);
                    if (req != null && req.ProcessId > 0) ExecuteKillProcess(req.ProcessId);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Bad KillProcess payload");
                }
                break;

            case TadCommand.SetBlocklist:
                try
                {
                    var bl = JsonSerializer.Deserialize<BlocklistUpdate>(payload.Span);
                    if (bl != null)
                    {
                        lock (_blocklistLock) _blocklist = bl;
                        _log.LogInformation("Blocklist updated: {Progs} programs, {Sites} websites",
                            bl.BlockedPrograms.Count, bl.BlockedWebsites.Count);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Bad SetBlocklist payload");
                }
                break;
        }
    }

    // ─── Blank Screen ─────────────────────────────────────────────────

    private void ExecuteKillProcess(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            proc.Kill();
            proc.WaitForExit(3000);
            _log.LogInformation("Killed process {Name} (PID {Pid}) by teacher request", name, pid);
        }
        catch (ArgumentException)
        {
            _log.LogWarning("KillProcess: PID {Pid} not found", pid);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to kill PID {Pid}", pid);
        }
    }

    private void ExecuteBlankScreen()
    {
        if (_isBlanked) return;
        _isBlanked = true;
        try
        {
            // Try dedicated overlay first; fall back to PowerShell blackout
            var overlayPath = Path.Combine(AppContext.BaseDirectory, "TadBlankOverlay.exe");
            if (File.Exists(overlayPath))
            {
                _blankOverlayProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = overlayPath,
                    WindowStyle = ProcessWindowStyle.Maximized,
                    UseShellExecute = true
                });
            }
            else
            {
                // PowerShell fullscreen black WinForms window
                _blankOverlayProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-WindowStyle Hidden -Command \"" +
                        "Add-Type -AssemblyName System.Windows.Forms; " +
                        "$f=New-Object System.Windows.Forms.Form; " +
                        "$f.BackColor='Black'; $f.FormBorderStyle='None'; " +
                        "$f.WindowState='Maximized'; $f.TopMost=$true; " +
                        "$f.ShowDialog()\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            _log.LogInformation("Blank screen activated (PID {Pid})",
                _blankOverlayProcess?.Id);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to activate blank screen");
        }
    }

    private void ExecuteUnblankScreen()
    {
        if (!_isBlanked) return;
        _isBlanked = false;
        try
        {
            if (_blankOverlayProcess is { HasExited: false })
            {
                _blankOverlayProcess.Kill();
                _blankOverlayProcess.WaitForExit(2000);
            }
            _blankOverlayProcess?.Dispose();
            _blankOverlayProcess = null;
            _log.LogInformation("Blank screen removed");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to remove blank screen");
        }
    }

    // ─── Push Message ─────────────────────────────────────────────────

    private void ExecutePushMessage(string message, int durationSeconds = 10)
    {
        // WTSSendMessage delivers a Win32 message-box directly to the active console session
        // from Session 0 (Windows service), which msg.exe cannot reliably do on Win 10/11.
        try
        {
            int sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == unchecked((int)0xFFFFFFFF))
            {
                _log.LogWarning("WTSSendMessage: no active console session — skipping");
                return;
            }

            const string title = "TAD.RV — Teacher Message";
            bool ok = WTSSendMessage(
                IntPtr.Zero,       // WTS_CURRENT_SERVER_HANDLE
                sessionId,
                title,             title.Length,
                message,           message.Length,
                0x00000040,        // MB_ICONINFORMATION
                durationSeconds,   // auto-close after N seconds
                out _,
                false);            // non-blocking: returns immediately

            if (ok)
                _log.LogInformation("Push message delivered to session {Session} ({Sec}s): {Msg}",
                    sessionId, durationSeconds, message);
            else
            {
                int err = Marshal.GetLastWin32Error();
                _log.LogWarning("WTSSendMessage failed (Win32={Err}) — falling back to msg.exe", err);
                FallbackMsgExe(message, durationSeconds);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send push message — trying msg.exe fallback");
            FallbackMsgExe(message, durationSeconds);
        }
    }

    private void FallbackMsgExe(string message, int durationSeconds)
    {
        try
        {
            var safeMsg = message.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
            Process.Start(new ProcessStartInfo
            {
                FileName        = "msg.exe",
                Arguments       = $"* /TIME:{durationSeconds} \"{safeMsg}\"",
                UseShellExecute = false,
                CreateNoWindow  = true
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "msg.exe fallback also failed");
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

    // ─── Snapshot ─────────────────────────────────────────────────────

    /// <summary>
    /// Capture the primary screen as JPEG and send it back as SnapshotData.
    /// Uses GDI+ BitBlt — fast, works without GPU drivers.
    /// Quality: 75% JPEG (~50–150 KB per screenshot).
    /// </summary>
    private async Task SendSnapshotAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                var bounds = screen.Bounds;
                using var bmp = new System.Drawing.Bitmap(
                    bounds.Width, bounds.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);

                // Encode as JPEG at quality 75
                var jpegCodec = System.Drawing.Imaging.ImageCodecInfo
                    .GetImageEncoders()
                    .FirstOrDefault(e => e.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

                using var ms = new MemoryStream();
                if (jpegCodec != null)
                {
                    var ep = new System.Drawing.Imaging.EncoderParameters(1);
                    ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality, 75L);
                    bmp.Save(ms, jpegCodec, ep);
                }
                else
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                }

                SendFrame(TadCommand.SnapshotData, ms.GetBuffer().AsSpan(0, (int)ms.Length));
                _log.LogDebug("Snapshot sent ({Bytes} bytes)", ms.Length);
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Snapshot capture failed");
        }
    }

    // ─── Status Beacon ────────────────────────────────────────────────

    private StudentStatus BuildStatus()
    {
        var heartbeat = _driver.Heartbeat();

        // Collect open windows from visible top-level processes
        var openWindows = new List<OpenWindowInfo>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(proc.MainWindowTitle) && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        openWindows.Add(new OpenWindowInfo
                        {
                            Title = proc.MainWindowTitle,
                            ProcessId = proc.Id,
                            ProcessName = proc.ProcessName
                        });
                    }
                }
                catch { /* access denied for some processes */ }
            }
        }
        catch { }

        // Disk usage (system drive)
        long diskUsedGb = 0, diskTotalGb = 0;
        try
        {
            var sysDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            diskTotalGb = sysDrive.TotalSize / (1024 * 1024 * 1024);
            diskUsedGb = (sysDrive.TotalSize - sysDrive.AvailableFreeSpace) / (1024 * 1024 * 1024);
        }
        catch { }

        // RAM total (via GC or WMI-free approach)
        long ramTotalMb = 0;
        try
        {
            var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
            ramTotalMb = (long)(ci.TotalPhysicalMemory / (1024 * 1024));
        }
        catch
        {
            ramTotalMb = Environment.WorkingSet / (1024 * 1024) * 4; // rough fallback
        }

        return new StudentStatus
        {
            Hostname      = Environment.MachineName,
            Username      = Environment.UserName,
            IpAddress     = GetLocalIp(),
            DriverLoaded  = heartbeat != null,
            IsLocked      = _isLocked,
            IsStreaming   = _isStreaming,
            IsBlankScreen = _isBlanked,
            ActiveWindow  = GetForegroundWindowTitle(),
            CpuUsage      = 0,
            RamUsedMb     = Environment.WorkingSet / (1024 * 1024),
            RamTotalMb    = ramTotalMb,
            DiskUsedGb    = diskUsedGb,
            DiskTotalGb   = diskTotalGb,
            OpenWindows   = openWindows,
            Timestamp     = DateTime.UtcNow
        };
    }

    private void SendStatusNow()
    {
        try { SendFrame(TadCommand.Status, JsonSerializer.SerializeToUtf8Bytes(BuildStatus())); }
        catch { }
    }

    private async Task StatusBeaconAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { SendFrame(TadCommand.Status, JsonSerializer.SerializeToUtf8Bytes(BuildStatus())); }
            catch { /* Best effort */ }

            // Enforce blocklist — kill blocked programs and browsers showing blocked sites
            try { EnforceBlocklist(); }
            catch { /* best effort */ }

            await Task.Delay(3000, ct);
        }
    }

    /// <summary>Kill processes that match the teacher's blocklist (programs by name, websites by browser title).</summary>
    private void EnforceBlocklist()
    {
        BlocklistUpdate bl;
        lock (_blocklistLock) bl = _blocklist;

        if (bl.BlockedPrograms.Count == 0 && bl.BlockedWebsites.Count == 0) return;

        var browserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "chrome", "msedge", "firefox", "opera", "brave", "iexplore", "ApplicationFrameHost" };

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                string name = proc.ProcessName;

                // Check blocked programs (match process name without .exe)
                foreach (var blocked in bl.BlockedPrograms)
                {
                    if (name.Equals(blocked, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.LogInformation("Blocklist: killing {Name} (PID {Pid}) — blocked program", name, proc.Id);
                        proc.Kill();
                        break;
                    }
                }

                // Check blocked websites (match in browser window titles)
                if (bl.BlockedWebsites.Count > 0 && browserNames.Contains(name))
                {
                    string title = proc.MainWindowTitle ?? "";
                    if (string.IsNullOrEmpty(title)) continue;

                    foreach (var site in bl.BlockedWebsites)
                    {
                        if (title.Contains(site, StringComparison.OrdinalIgnoreCase))
                        {
                            _log.LogInformation("Blocklist: killing {Name} (PID {Pid}) — blocked site '{Site}' in title '{Title}'",
                                name, proc.Id, site, title);
                            proc.Kill();
                            break;
                        }
                    }
                }
            }
            catch { /* access denied or already exited */ }
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

    // ─── WTS P/Invoke — message box in the active user session ───────

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSSendMessage(
        IntPtr  hServer,
        int     SessionId,
        string  pTitle,      int TitleLength,
        string  pMessage,    int MessageLength,
        uint    Style,
        int     Timeout,
        out int pResponse,
        bool    bWait);

    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();
}

file static class NativeForeground
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
}
