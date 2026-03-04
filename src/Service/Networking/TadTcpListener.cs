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
using System.Reflection;
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
    private volatile bool _isFrozen;
    private Process? _lockOverlayProcess;
    private Process? _blankOverlayProcess;
    private Process? _freezeOverlayProcess;

    // Blocklist enforcement
    private BlocklistUpdate _blocklist = new();
    private readonly object _blocklistLock = new();
    private volatile bool _isWebLocked;
    private volatile bool _isProgramLocked;

    // CPU usage tracking via PerformanceCounter-free approach
    private TimeSpan _prevCpuTotal;
    private DateTime _prevCpuTime = DateTime.UtcNow;
    private double _lastCpuUsage;

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
            if (_isFrozen)  ExecuteUnfreeze();
            if (_isWebLocked) ExecuteWebUnlock();
            if (_isProgramLocked) ExecuteProgramUnlock();
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
            var remaining = data.AsSpan(offset, length - offset);
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

            case TadCommand.Freeze:
                ExecuteFreeze();
                break;

            case TadCommand.Unfreeze:
                ExecuteUnfreeze();
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

            case TadCommand.WebLock:
                ExecuteWebLock();
                break;

            case TadCommand.WebUnlock:
                ExecuteWebUnlock();
                break;

            case TadCommand.ProgramLock:
                try
                {
                    var pl = JsonSerializer.Deserialize<BlocklistUpdate>(payload.Span);
                    if (pl != null) ExecuteProgramLock(pl);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Bad ProgramLock payload");
                }
                break;

            case TadCommand.ProgramUnlock:
                ExecuteProgramUnlock();
                break;

            default:
                _log.LogDebug("Unhandled command: {Cmd}", cmd);
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
            _blankOverlayProcess = LaunchOverlay("--blank");
            if (_blankOverlayProcess != null)
                _log.LogInformation("Blank screen activated (PID {Pid})", _blankOverlayProcess.Id);
            else
                _log.LogWarning("Failed to launch blank overlay — TadOverlay.exe not found or CreateProcessAsUser failed");
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
                title,             title.Length * sizeof(char),    // byte count for Unicode
                message,           message.Length * sizeof(char),  // byte count for Unicode
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

    // ─── FREEZE Command ───────────────────────────────────────────────

    private void ExecuteFreeze()
    {
        if (_isFrozen) return;
        _isFrozen = true;

        try
        {
            _freezeOverlayProcess = LaunchOverlay("--freeze");
            if (_freezeOverlayProcess != null)
                _log.LogInformation("Freeze overlay launched (PID {Pid})", _freezeOverlayProcess.Id);
            else
                _log.LogWarning("Failed to launch freeze overlay — TadOverlay.exe not found or CreateProcessAsUser failed");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to launch freeze overlay");
        }
    }

    private void ExecuteUnfreeze()
    {
        if (!_isFrozen) return;
        _isFrozen = false;

        try
        {
            if (_freezeOverlayProcess is { HasExited: false })
            {
                _freezeOverlayProcess.Kill();
                _freezeOverlayProcess.WaitForExit(3000);
                _freezeOverlayProcess.Dispose();
                _freezeOverlayProcess = null;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to kill freeze overlay");
        }
    }

    // ─── LOCK Command ─────────────────────────────────────────────────

    private void ExecuteLock()
    {
        if (_isLocked) return;
        _isLocked = true;

        // 1. Tell the kernel driver to disable keyboard/mouse (if loaded)
        try
        {
            _driver.SendHardLock(true);
            _log.LogInformation("Kernel hard-lock engaged");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Kernel hard-lock unavailable — overlay lock only");
        }

        // 2. Launch fullscreen lock overlay in the USER'S session
        try
        {
            _lockOverlayProcess = LaunchOverlay("--lock");
            if (_lockOverlayProcess != null)
            {
                _log.LogInformation("Lock overlay launched (PID {Pid})", _lockOverlayProcess.Id);
                try { _driver.ProtectPid((uint)_lockOverlayProcess.Id); }
                catch { /* Driver may not be loaded */ }
            }
            else
            {
                _log.LogWarning("Failed to launch lock overlay — TadOverlay.exe not found or CreateProcessAsUser failed");
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

        try
        {
            _ = _capture.StartAsync(ct);
            _log.LogInformation("Sub-stream started (1fps, 480p)");
        }
        catch (Exception ex)
        {
            // DXGI Desktop Duplication fails when the service runs in Session 0
            // because there is no interactive desktop to capture. This is expected
            // for Windows Service mode.  Streaming works in emulation/interactive mode.
            _log.LogWarning(ex, "Screen capture start failed (Session 0?) — streaming unavailable");
            _isStreaming = false;
        }
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
    /// When running as a service in Session 0, GDI+ CopyFromScreen only sees
    /// the empty Session 0 desktop. Use CreateProcessAsUser to capture from
    /// the interactive user session instead.
    /// </summary>
    private async Task SendSnapshotAsync()
    {
        try
        {
            // 1. Attempt user-session capture via CreateProcessAsUser
            var tempFile = Path.Combine(Path.GetTempPath(), $"tad_snap_{Guid.NewGuid():N}.jpg");
            var cmd = "powershell.exe -WindowStyle Hidden -Command \"" +
                "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; " +
                "$b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; " +
                "$bmp=New-Object System.Drawing.Bitmap($b.Width,$b.Height); " +
                "$g=[System.Drawing.Graphics]::FromImage($bmp); " +
                "$g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); " +
                "$c=[System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders()|?{$_.MimeType-eq'image/jpeg'}; " +
                "$p=New-Object System.Drawing.Imaging.EncoderParameters(1); " +
                "$p.Param[0]=New-Object System.Drawing.Imaging.EncoderParameter(" +
                "[System.Drawing.Imaging.Encoder]::Quality,60L); " +
                "$bmp.Save('" + tempFile.Replace("\\", "\\\\") + "',$c,$p); " +
                "$g.Dispose(); $bmp.Dispose()\"";

            var proc = LaunchInUserSession(cmd);
            if (proc != null)
            {
                await Task.Run(() => proc.WaitForExit(5000));
                if (File.Exists(tempFile))
                {
                    var data = await File.ReadAllBytesAsync(tempFile);
                    SendFrame(TadCommand.SnapshotData, data);
                    try { File.Delete(tempFile); } catch { }
                    _log.LogDebug("Snapshot sent via user-session capture ({Bytes} bytes)", data.Length);
                    return;
                }
            }

            // 2. Fallback: direct GDI capture (works in emulation / interactive mode)
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
                _log.LogDebug("Snapshot sent via direct capture ({Bytes} bytes)", ms.Length);
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

        // ── Logged-in user (from interactive session, NOT this service account) ──
        string loggedInUser = GetConsoleSessionUser();

        // ── Collect running programs from the interactive session ──
        var openWindows = new List<OpenWindowInfo>();
        try
        {
            int userSessionId = WTSGetActiveConsoleSessionId();
            // Processes to always ignore — system services, shell, runtime, background agents
            var ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Windows core
                "svchost", "csrss", "wininit", "services", "lsass", "smss",
                "System", "Idle", "Registry", "dwm", "fontdrvhost",
                "sihost", "taskhostw", "ctfmon", "conhost", "dllhost",
                "RuntimeBroker", "SearchHost", "StartMenuExperienceHost",
                "ShellExperienceHost", "TextInputHost", "SecurityHealthSystray",
                "SearchIndexer", "spoolsv", "CompPkgSrv", "uhssvc",
                "MsMpEng", "NisSrv", "SgrmBroker", "WmiPrvSE",
                "TadBridgeService", "audiodg", "powershell",
                // Shell & UWP infrastructure
                "explorer", "ApplicationFrameHost", "SystemSettings",
                "UserOOBEBroker", "WidgetService", "Widgets",
                "LockApp", "LogonUI", "WinLogon", "userinit",
                "backgroundTaskHost", "backgroundTransferHost",
                "PhoneExperienceHost", "YourPhone", "GameBarPresenceWriter",
                // Runtime / hosting
                "cmd", "pwsh", "WindowsTerminal", "OpenConsole",
                "msedgewebview2", "WebViewHost", "crashpad_handler",
                // Windows Defender & security
                "SecurityHealthService", "smartscreen",
                // Windows Update & maintenance
                "TiWorker", "TrustedInstaller", "WerFault", "wermgr",
                "MusNotifyIcon", "musNotification",
                // Graphics / driver
                "igfxCUIService", "igfxEM", "igfxHK", "igfxTray",
                "nvcontainer", "NVIDIA", "amddvr",
                // Input / accessibility
                "TabTip", "InputApp", "Narrator", "Magnify",
                // Lenovo / OEM (common on T431S)
                "Lenovo", "LenovoUtility", "WUDFHost", "PresentationFontCache"
            };

            // Prefixes that indicate non-user background processes
            var ignoredPrefixes = new[] {
                "svchost", "com.docker", "docker", "vmware",
                "Microsoft.SharePoint", "OneDrive"
            };

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    // Filter to the active user session only
                    if (!ProcessIdToSessionId((uint)proc.Id, out uint procSid)) continue;
                    if (procSid != (uint)userSessionId) continue;

                    string name = proc.ProcessName;
                    if (ignoredNames.Contains(name)) continue;
                    if (ignoredPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

                    // Get the window title — from Session 0 this is usually empty
                    string title = "";
                    try { title = proc.MainWindowTitle; } catch { }

                    // Only include processes that have a visible window title
                    // (i.e., actual user applications, not background services)
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    openWindows.Add(new OpenWindowInfo
                    {
                        Title = title,
                        ProcessId = proc.Id,
                        ProcessName = name
                    });
                }
                catch { /* access denied for some processes */ }
            }
        }
        catch { }

        // ── Disk usage (system drive) ──
        long diskUsedGb = 0, diskTotalGb = 0;
        try
        {
            var sysDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            diskTotalGb = sysDrive.TotalSize / (1024 * 1024 * 1024);
            diskUsedGb = (sysDrive.TotalSize - sysDrive.AvailableFreeSpace) / (1024 * 1024 * 1024);
        }
        catch { }

        // ── RAM: system-wide usage (not just this process) ──
        long ramTotalMb = 0;
        long ramUsedMb = 0;
        try
        {
            var ci = new Microsoft.VisualBasic.Devices.ComputerInfo();
            ramTotalMb = (long)(ci.TotalPhysicalMemory / (1024 * 1024));
            long ramAvailMb = (long)(ci.AvailablePhysicalMemory / (1024 * 1024));
            ramUsedMb = ramTotalMb - ramAvailMb;
        }
        catch
        {
            ramTotalMb = Environment.WorkingSet / (1024 * 1024) * 4; // rough fallback
            ramUsedMb = Environment.WorkingSet / (1024 * 1024);
        }

        // ── CPU: system-wide usage via process time delta ──
        double cpuUsage = MeasureCpuUsage();

        return new StudentStatus
        {
            Hostname       = Environment.MachineName,
            Username       = loggedInUser,
            IpAddress      = GetLocalIp(),
            DriverLoaded   = heartbeat != null,
            IsLocked       = _isLocked,
            IsFrozen       = _isFrozen,
            IsStreaming    = _isStreaming,
            IsBlankScreen  = _isBlanked,
            IsWebLocked    = _isWebLocked,
            IsProgramLocked = _isProgramLocked,
            ActiveWindow   = GetForegroundWindowTitle(),
            CpuUsage       = cpuUsage,
            RamUsedMb      = ramUsedMb,
            RamTotalMb     = ramTotalMb,
            DiskUsedGb     = diskUsedGb,
            DiskTotalGb    = diskTotalGb,
            OpenWindows    = openWindows,
            ServiceVersion = GetServiceVersion(),
            Timestamp      = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get the username of the interactively logged-in user (console session).
    /// The service runs as SYSTEM so Environment.UserName returns the service account.
    /// </summary>
    private string GetConsoleSessionUser()
    {
        try
        {
            int sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == unchecked((int)0xFFFFFFFF)) return Environment.MachineName;

            if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTS_INFO_CLASS.WTSUserName,
                    out IntPtr buffer, out int bytesReturned) && bytesReturned > 1)
            {
                string user = Marshal.PtrToStringUni(buffer) ?? "";
                WTSFreeMemory(buffer);
                if (!string.IsNullOrEmpty(user)) return user;
            }
        }
        catch { /* P/Invoke may fail on older Windows */ }

        // Fallback: query explorer.exe owner
        try
        {
            var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
            if (explorer != null)
            {
                // The explorer.exe process runs under the logged-in user
                return GetProcessOwner(explorer.Id) ?? Environment.MachineName;
            }
        }
        catch { }

        return Environment.MachineName;
    }

    private static string? GetProcessOwner(int pid)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var args = new object[] { "", "" };
                if ((uint)obj.InvokeMethod("GetOwner", args) == 0)
                    return args[0]?.ToString();
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Measure system-wide CPU usage by summing all process times over a time delta.
    /// This avoids PerformanceCounter which requires special setup.
    /// </summary>
    private double MeasureCpuUsage()
    {
        try
        {
            TimeSpan totalCpu = TimeSpan.Zero;
            foreach (var proc in Process.GetProcesses())
            {
                try { totalCpu += proc.TotalProcessorTime; }
                catch { /* access denied */ }
            }

            var now = DateTime.UtcNow;
            var elapsed = now - _prevCpuTime;
            if (elapsed.TotalMilliseconds > 500)
            {
                var cpuDelta = totalCpu - _prevCpuTotal;
                int cores = Environment.ProcessorCount;
                _lastCpuUsage = Math.Clamp(
                    cpuDelta.TotalMilliseconds / elapsed.TotalMilliseconds / cores * 100.0,
                    0, 100);
                _prevCpuTotal = totalCpu;
                _prevCpuTime = now;
            }
            return Math.Round(_lastCpuUsage, 1);
        }
        catch { return 0; }
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

    // ─── Web-Lock (Firewall-based internet block) ─────────────────────

    private const string FW_RULE_BLOCK = "TAD-WebLock-Block";
    private const string FW_RULE_ALLOW_PRIVATE = "TAD-WebLock-AllowPrivate";

    private void ExecuteWebLock()
    {
        if (_isWebLocked)
        {
            _log.LogDebug("Web-Lock already active, skipping");
            return;
        }

        try
        {
            // 1) Add outbound allow rule for private/local subnets (evaluated before block)
            RunNetsh($"advfirewall firewall add rule name=\"{FW_RULE_ALLOW_PRIVATE}\" " +
                     "dir=out action=allow " +
                     "remoteip=10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,127.0.0.0/8,169.254.0.0/16,localsubnet " +
                     "enable=yes");

            // 2) Block ALL outbound public internet traffic
            //    Public ranges: everything except private (10/8, 127/8, 172.16/12, 192.168/16, 169.254/16)
            RunNetsh($"advfirewall firewall add rule name=\"{FW_RULE_BLOCK}\" " +
                     "dir=out action=block " +
                     "remoteip=1.0.0.0-9.255.255.255,11.0.0.0-126.255.255.255," +
                     "128.0.0.0-172.15.255.255,172.32.0.0-191.255.255.255," +
                     "192.0.0.0-192.167.255.255,192.169.0.0-223.255.255.255 " +
                     "enable=yes");

            _isWebLocked = true;
            _log.LogInformation("Web-Lock enabled — internet blocked, local/domain preserved");
            SendStatusNow();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to enable Web-Lock");
        }
    }

    private void ExecuteWebUnlock()
    {
        if (!_isWebLocked)
        {
            _log.LogDebug("Web-Lock not active, skipping unlock");
            return;
        }

        try
        {
            RunNetsh($"advfirewall firewall delete rule name=\"{FW_RULE_BLOCK}\"");
            RunNetsh($"advfirewall firewall delete rule name=\"{FW_RULE_ALLOW_PRIVATE}\"");

            _isWebLocked = false;
            _log.LogInformation("Web-Lock disabled — internet restored");
            SendStatusNow();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to disable Web-Lock");
        }
    }

    private static void RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo("netsh", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
    }

    // ─── Program-Lock (per-student program kill) ──────────────────────

    private void ExecuteProgramLock(BlocklistUpdate bl)
    {
        lock (_blocklistLock)
        {
            _blocklist = new BlocklistUpdate
            {
                BlockedPrograms = bl.BlockedPrograms,
                BlockedWebsites = _blocklist.BlockedWebsites
            };
        }
        _isProgramLocked = bl.BlockedPrograms.Count > 0;
        _log.LogInformation("Program-Lock: blocking {Count} programs", bl.BlockedPrograms.Count);
        SendStatusNow();
    }

    private void ExecuteProgramUnlock()
    {
        lock (_blocklistLock)
        {
            _blocklist = new BlocklistUpdate
            {
                BlockedPrograms = new List<string>(),
                BlockedWebsites = _blocklist.BlockedWebsites
            };
        }
        _isProgramLocked = false;
        _log.LogInformation("Program-Lock disabled");
        SendStatusNow();
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

    private static string GetServiceVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var ver = attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0";
            // Strip component suffix (e.g. "v26.3.04.116-client" → "26.3.04.116")
            if (ver.StartsWith('v')) ver = ver[1..];
            var dash = ver.IndexOf('-');
            if (dash > 0) ver = ver[..dash];
            return ver;
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

    private enum WTS_INFO_CLASS { WTSUserName = 5, WTSDomainName = 7 }

    [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass,
        out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("Wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

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

    // ─── CreateProcessAsUser P/Invoke — launch processes in the interactive session ──

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(int SessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes,
        int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken, string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    /// <summary>
    /// Resolve and launch TadOverlay.exe with the given mode argument.
    /// Tries CreateProcessAsUser first (for Session 0 service), falls back to direct launch.
    /// </summary>
    private Process? LaunchOverlay(string modeArg)
    {
        // Locate TadOverlay.exe next to the service binary
        string overlayPath = Path.Combine(AppContext.BaseDirectory, "TadOverlay.exe");
        if (!File.Exists(overlayPath))
        {
            _log.LogWarning("TadOverlay.exe not found at {Path} — overlay unavailable", overlayPath);
            return null;
        }

        string cmdLine = $"\"{overlayPath}\" {modeArg}";

        // Primary: launch in user session via CreateProcessAsUser
        var proc = LaunchInUserSession(cmdLine);
        if (proc != null)
        {
            _log.LogInformation("Overlay {Mode} launched via CreateProcessAsUser (PID {Pid})", modeArg, proc.Id);
            return proc;
        }

        // Fallback: direct start (works in emulation mode / interactive service)
        _log.LogWarning("CreateProcessAsUser unavailable — launching overlay directly");
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = overlayPath,
                Arguments = modeArg,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Direct overlay launch also failed");
            return null;
        }
    }

    /// <summary>
    /// Launch a process in the interactive user's session (not Session 0).
    /// Uses WTSQueryUserToken + CreateProcessAsUser so overlays appear on the user's desktop.
    /// </summary>
    private Process? LaunchInUserSession(string commandLine)
    {
        int sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == unchecked((int)0xFFFFFFFF))
        {
            _log.LogWarning("No active console session — cannot launch in user session");
            return null;
        }

        IntPtr userToken = IntPtr.Zero;
        IntPtr duplicateToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;

        try
        {
            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                _log.LogWarning("WTSQueryUserToken failed (Win32={Err})", Marshal.GetLastWin32Error());
                return null;
            }

            // Duplicate as primary token (MAXIMUM_ALLOWED, SecurityImpersonation, TokenPrimary)
            if (!DuplicateTokenEx(userToken, 0x02000000, IntPtr.Zero, 2, 1, out duplicateToken))
            {
                _log.LogWarning("DuplicateTokenEx failed (Win32={Err})", Marshal.GetLastWin32Error());
                return null;
            }

            CreateEnvironmentBlock(out environment, duplicateToken, false);

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = "winsta0\\default",   // interactive desktop
                dwFlags = 0x00000001,             // STARTF_USESHOWWINDOW
                wShowWindow = 0                   // SW_HIDE — hide the host console
            };

            const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

            if (CreateProcessAsUser(
                duplicateToken, null, commandLine,
                IntPtr.Zero, IntPtr.Zero, false,
                CREATE_UNICODE_ENVIRONMENT, environment, null,
                ref si, out var pi))
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                _log.LogInformation("Launched process in user session {Sid} (PID {Pid})", sessionId, pi.dwProcessId);
                try { return Process.GetProcessById(pi.dwProcessId); }
                catch { return null; }
            }
            else
            {
                _log.LogWarning("CreateProcessAsUser failed (Win32={Err})", Marshal.GetLastWin32Error());
                return null;
            }
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (duplicateToken != IntPtr.Zero) CloseHandle(duplicateToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }
}

file static class NativeForeground
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
}
