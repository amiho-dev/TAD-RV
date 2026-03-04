// ───────────────────────────────────────────────────────────────────────────
// MainWindow.xaml.cs — Admin Controller shell
//
// (C) 2026 TAD Europe — https://tad-it.eu
// TAD.RV — The Greater Brother of the mighty te.comp NET.FX
//
// Hosts WebView2 for the student grid dashboard and bridges WPF buttons
// to TcpClientManager (production) or DemoTcpClientManager (demo mode).
// WebView2 receives student status + video via PostWebMessageAsJson and
// dispatches commands back via web-to-native messages.
//
// v26200.172 — Bug-fix release:
//   • Fixed WebMessage JSON case mismatch (JS camelCase → C# PascalCase)
//   • Fixed double-encoding on postMessage → WebMessageAsJson path
//   • Added WebView2 readiness gate — no messages before NavigationCompleted
//   • Throttled demo frame dispatch to prevent Dispatcher overflow
//   • Added system tray icon with minimize-to-tray
// ───────────────────────────────────────────────────────────────────────────

using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using TADAdmin.Networking;
using TADBridge.Shared;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TADAdmin;

public partial class MainWindow : Window
{
    // Shared interface: both managers expose the same events & methods
    private readonly TcpClientManager? _tcpManager;
    private readonly DemoTcpClientManager? _demoManager;
    private readonly DiscoveryListener? _discoveryListener;
    private readonly bool _isDemoMode;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    // WebView2 readiness gate — prevents posting before page is loaded
    private volatile bool _webViewReady;

    // Throttle: only allow one pending demo-frame dispatch per student
    private readonly HashSet<string> _pendingFrameIps = new();
    private readonly object _frameLock = new();

    // System tray icon
    private NotifyIcon? _trayIcon;

    private bool _allFrozen;
    private bool _allBlanked;
    private bool _exitRequested;  // true only when "Exit" from tray — X button hides to tray
    private DateTime? _statusMessageExpiry;

    // Room filter: tracks discovered room IDs → student IPs
    private readonly Dictionary<string, string> _ipToRoom = new();  // ip → roomId
    private string _selectedRoom = "";  // "" = all rooms

    // JSON options for camelCase deserialization
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MainWindow(bool demoMode = false)
    {
        TADLogger.Info($"MainWindow constructor start  demoMode={demoMode}");
        InitializeComponent();
        TADLogger.Info("InitializeComponent() done");

        // Show short version in status bar (strip leading 'v' + component suffix)
        var fullVer = GetRunningVersion().TrimStart('v');
        var shortVer = fullVer;
        var dashIdx = fullVer.IndexOf('-');
        if (dashIdx > 0) shortVer = fullVer[..dashIdx];
        TxtVersionBar.Text = $"v{shortVer}";

        // Load logo into title bar from embedded base64 PNG
        LoadTitleBarLogo();

        _isDemoMode = demoMode;

        if (_isDemoMode)
        {
            TxtDemoTag.Visibility = Visibility.Visible;
            TxtModeBadge.Text = "Demo";
            Title = "TAD.RV — Admin Controller [DEMO]";

            _demoManager = new DemoTcpClientManager();
            _demoManager.StudentStatusUpdated += OnStudentStatusUpdated;
            _demoManager.VideoFrameReceived += OnVideoFrameReceived;
            _demoManager.MainFrameReceived += OnMainFrameReceived;
            _demoManager.DemoFrameReady += OnDemoFrameReady;
            TADLogger.Info("Demo managers wired up");
        }
        else
        {
            TADLogger.Info("Production mode: creating TcpClientManager + DiscoveryListener");
            _tcpManager = new TcpClientManager();
            _tcpManager.StudentStatusUpdated += OnStudentStatusUpdated;
            _tcpManager.VideoFrameReceived += OnVideoFrameReceived;
            _tcpManager.MainFrameReceived += OnMainFrameReceived;

            // Auto-discover student machines via UDP multicast (zero-config)
            _discoveryListener = new DiscoveryListener();
            _discoveryListener.OnStudentDiscovered += OnStudentDiscovered;
            _discoveryListener.Start();
            TADLogger.Info("TcpClientManager and DiscoveryListener started");
        }

        // WebView2 requires the window's HWND to exist before EnsureCoreWebView2Async.
        // Calling it from the constructor crashes because the handle does not exist yet.
        // Defer to the Loaded event, which fires after the window is fully rendered.
        Loaded += (_, _) =>
        {
            TADLogger.Info("Loaded event fired — HWND now exists, starting WebView2 init");
            InitializeWebView();
        };
        InitializeTrayIcon();
        TADLogger.Info($"MainWindow constructor complete  IsVisible={IsVisible}");

        _statusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _statusTimer.Tick += (_, _) =>
        {
            UpdateStatusBar();
            // Belt-and-suspenders: re-flush all known student IPs to WebView every tick.
            // If any add_students message was dropped (race on startup), this recovers it
            // within 3 seconds without waiting for the next multicast heartbeat.
            FlushStudentsToWebView();
        };
        _statusTimer.Start();
    }

    // ─── System Tray Icon ─────────────────────────────────────────────

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new NotifyIcon
            {
                Text = _isDemoMode ? "TAD.RV Admin [DEMO]" : "TAD.RV Admin Controller",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };

            // Try to load icon from embedded logo
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("TADAdmin.Assets.logo32.b64");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string base64 = reader.ReadToEnd().Trim();
                    byte[] bytes = Convert.FromBase64String(base64);
                    using var ms = new MemoryStream(bytes);
                    using var bmp = new Bitmap(ms);
                    _trayIcon.Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        }
        catch { /* Tray icon is optional — don't crash */ }
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show TAD.RV", null, (_, _) => ShowFromTray());
        menu.Items.Add("-");
        menu.Items.Add("Lock All Screens", null, (_, _) => Dispatcher.InvokeAsync(() => BtnLockAll_Click(this, new RoutedEventArgs())));
        menu.Items.Add("Unlock All Screens", null, (_, _) => Dispatcher.InvokeAsync(() => BtnUnlockAll_Click(this, new RoutedEventArgs())));
        menu.Items.Add("-");
        menu.Items.Add("Diagnostics", null, (_, _) => Dispatcher.InvokeAsync(ShowDiagnostics));
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.InvokeAsync(() => { _exitRequested = true; Close(); }));
        return menu;
    }

    private void ShowFromTray()
    {
        Dispatcher.InvokeAsync(() =>
        {
            Show();
            WindowState = WindowState.Maximized;
            Activate();
        });
    }

    /// <summary>Load the logo PNG from embedded base64 resource into the title bar Image.</summary>
    private void LoadTitleBarLogo()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("TADAdmin.Assets.logo32.b64");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            string base64 = reader.ReadToEnd().Trim();
            byte[] bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            TitleBarLogo.Source = bmp;
        }
        catch { /* Logo is cosmetic — don't crash */ }
    }

    /// <summary>Minimize to system tray instead of taskbar.</summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _trayIcon?.ShowBalloonTip(2000, "TAD.RV", "Minimized to system tray", ToolTipIcon.Info);
        }
        else if (WindowState == WindowState.Maximized)
        {
            // WindowChrome bypasses WM_GETMINMAXINFO, which normally keeps
            // the maximized window within the WorkArea (above the taskbar).
            // Clamping MaxHeight to the logical work-area height fixes this.
            MaxHeight = SystemParameters.WorkArea.Height;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
        }
    }

    /// <summary>Called by DiscoveryListener when a new student IP appears on the network.</summary>
    private void OnStudentDiscovered(string ip, int port, string hostname, string roomId)
    {
        Dispatcher.InvokeAsync(() =>
        {
            // Track the room this IP belongs to
            if (!string.IsNullOrEmpty(roomId))
            {
                _ipToRoom[ip] = roomId;
                UpdateRoomDropdown();
            }

            // If a room filter is active, skip students not in that room
            if (!string.IsNullOrEmpty(_selectedRoom) &&
                !roomId.Equals(_selectedRoom, StringComparison.OrdinalIgnoreCase))
                return;

            _tcpManager?.AddStudent(ip, port);
            SetStatus($"Discovered: {hostname} ({ip}){(string.IsNullOrEmpty(roomId) ? "" : $" [{roomId}]")}");

            // Notify the WebView immediately so a tile appears even before the
            // first status beacon (fired every 3 s) has time to arrive.
            if (_webViewReady)
                PostJsonMessage(new { type = "add_students", ips = new[] { ip } });
        });
    }

    private void UpdateRoomDropdown()
    {
        var rooms = _ipToRoom.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToList();
        var currentItems = new HashSet<string>();
        foreach (System.Windows.Controls.ComboBoxItem item in CmbRoomFilter.Items)
        {
            var s = item.Content?.ToString() ?? "";
            if (s != "All Rooms") currentItems.Add(s);
        }

        foreach (var room in rooms)
        {
            if (!currentItems.Contains(room))
                CmbRoomFilter.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = room });
        }
    }

    private void CmbRoomFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CmbRoomFilter.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            var selected = item.Content?.ToString() ?? "";
            _selectedRoom = (selected == "All Rooms") ? "" : selected;
            ApplyRoomFilter();
        }
    }

    private void ApplyRoomFilter()
    {
        if (_isDemoMode || _tcpManager == null) return;

        // Remove students that don't match the new filter
        foreach (var ip in _tcpManager.GetAllEndpointIps())
        {
            if (string.IsNullOrEmpty(_selectedRoom)) continue; // show all
            if (_ipToRoom.TryGetValue(ip, out var room) &&
                !room.Equals(_selectedRoom, StringComparison.OrdinalIgnoreCase))
            {
                _tcpManager.RemoveStudent(ip);
                if (_webViewReady)
                    PostJsonMessage(new { type = "remove_student", ip });
            }
        }

        // Re-add students that match the filter but were previously filtered out
        foreach (var (ip, room) in _ipToRoom)
        {
            if (string.IsNullOrEmpty(_selectedRoom) ||
                room.Equals(_selectedRoom, StringComparison.OrdinalIgnoreCase))
            {
                _tcpManager.AddStudent(ip); // AddStudent is idempotent
                if (_webViewReady)
                    PostJsonMessage(new { type = "add_students", ips = new[] { ip } });
            }
        }

        SetStatus(string.IsNullOrEmpty(_selectedRoom)
            ? "Showing all rooms"
            : $"Filtering room: {_selectedRoom}");
    }

    // ─── WebView2 Initialization ──────────────────────────────────────

    private async void InitializeWebView()
    {
        TADLogger.Info("InitializeWebView entered");
        try
        {
            var userDataFolder = Path.Combine(Path.GetTempPath(), "TADAdmin_WV2");
            TADLogger.Info($"WebView2 user-data folder: {userDataFolder}");

            TADLogger.Info("Calling CoreWebView2Environment.CreateAsync");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            TADLogger.Info("CoreWebView2Environment created");

            TADLogger.Info("Calling DashboardWebView.EnsureCoreWebView2Async");
            await DashboardWebView.EnsureCoreWebView2Async(env);
            TADLogger.Info("EnsureCoreWebView2Async completed — CoreWebView2 is ready");

            // Wire up readiness gate — NavigationCompleted fires after the page is fully loaded
            DashboardWebView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                TADLogger.Info($"NavigationCompleted: IsSuccess={args.IsSuccess} HttpStatus={args.HttpStatusCode}");
                _webViewReady = args.IsSuccess;
                if (_webViewReady && _isDemoMode)
                {
                    TADLogger.Info("Posting config (demo mode)");
                    PostJsonMessage(new { type = "config", demoMode = true, version = GetRunningVersion() });
                }
                else if (_webViewReady)
                {
                    TADLogger.Info("Posting config (production mode)");
                    PostJsonMessage(new { type = "config", demoMode = false, version = GetRunningVersion() });

                    // Flush any students already discovered before the WebView was ready.
                    // Without this, students that connected before NavigationCompleted
                    // never produce a tile (their status updates were silently dropped).
                    var knownIps = new HashSet<string>(
                        _discoveryListener?.KnownStudentIps ?? []);
                    // Also include any IPs already in TcpManager (e.g., manual adds)
                    foreach (var tip in _tcpManager?.GetAllEndpointIps() ?? [])
                        knownIps.Add(tip);
                    if (knownIps.Count > 0)
                    {
                        TADLogger.Info($"Flushing {knownIps.Count} already-known student(s) to WebView");
                        PostJsonMessage(new { type = "add_students", ips = knownIps.ToArray() });
                    }
                }
                else
                {
                    TADLogger.Warn($"NavigationCompleted with failure — WebView2 page did not load (HttpStatus={args.HttpStatusCode})");
                }
            };

            TADLogger.Info("Loading embedded HTML");
            var html = LoadEmbeddedHtml();
            TADLogger.Info($"Embedded HTML loaded: {html.Length} chars");

            DashboardWebView.CoreWebView2.NavigateToString(html);
            TADLogger.Info("NavigateToString called — waiting for NavigationCompleted");

            DashboardWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            TADLogger.Info("WebView2 init complete");
        }
        catch (Exception ex)
        {
            TADLogger.Exception(ex, "InitializeWebView");
            SetStatus($"WebView2 init failed: {ex.Message}", 60);

            var isMissing =
                ex is System.Runtime.InteropServices.COMException ||
                ex is FileNotFoundException ||
                ex.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("not installed", StringComparison.OrdinalIgnoreCase);

            if (isMissing)
            {
                App.ShowCrashDialog(
                    "The Microsoft Edge WebView2 Runtime is required but was not found on this machine.\n\n" +
                    "Download it from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/\n\n" +
                    $"Technical detail: {ex.Message}");
            }
            else
            {
                App.ShowCrashDialog(
                    $"WebView2 initialisation failed unexpectedly.\n\n{ex}");
            }
        }
    }

    /// <summary>Thread-safe helper to post a JSON message to WebView2.</summary>
    private void PostJsonMessage(object msg)
    {
        if (!_webViewReady) return;
        try
        {
            var json = JsonSerializer.Serialize(msg);
            DashboardWebView.CoreWebView2?.PostWebMessageAsJson(json);
        }
        catch { /* WebView2 may be closing */ }
    }

    /// <summary>Thread-safe helper to post a raw string message to WebView2.</summary>
    private void PostStringMessage(string raw)
    {
        if (!_webViewReady) return;
        try
        {
            DashboardWebView.CoreWebView2?.PostWebMessageAsString(raw);
        }
        catch { /* WebView2 may be closing */ }
    }

    /// <summary>Returns the InformationalVersion from the running assembly (e.g. v26.3.02.003-admin).</summary>
    private static string GetRunningVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var attr = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0";
    }

    private static string LoadEmbeddedHtml()
    {
        var asm = Assembly.GetExecutingAssembly();
        TADLogger.Info("LoadEmbeddedHtml: loading CSS");
        var css  = LoadResource(asm, "TADAdmin.Web.dashboard.css");
        TADLogger.Info($"LoadEmbeddedHtml: CSS {css.Length} chars");
        TADLogger.Info("LoadEmbeddedHtml: loading JS");
        var js   = LoadResource(asm, "TADAdmin.Web.dashboard.js");
        TADLogger.Info($"LoadEmbeddedHtml: JS {js.Length} chars");
        TADLogger.Info("LoadEmbeddedHtml: loading HTML");
        var html = LoadResource(asm, "TADAdmin.Web.dashboard.html");
        TADLogger.Info($"LoadEmbeddedHtml: HTML {html.Length} chars");

        html = html.Replace("<!-- INLINE_CSS -->", $"<style>{css}</style>");
        html = html.Replace("<!-- INLINE_JS -->",  $"<script>{js}</script>");
        return html;
    }

    private static string LoadResource(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null)
        {
            TADLogger.Error($"Embedded resource NOT FOUND: {name}");
            TADLogger.Info($"Available resources: {string.Join(", ", asm.GetManifestResourceNames())}");
            return $"/* resource {name} not found */";
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ─── WebView2 ↔ C# Bridge ────────────────────────────────────────

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            // The JS side sends: window.chrome.webview.postMessage(JSON.stringify(msg))
            // WebView2 wraps that string in JSON again for WebMessageAsJson, causing double-encoding.
            // Fix: use TryGetWebMessageAsString() which gives us the raw string to parse ourselves.
            string? raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(raw)) return;

            var msg = JsonSerializer.Deserialize<WebMessage>(raw, s_jsonOptions);
            if (msg == null || string.IsNullOrEmpty(msg.Action)) return;

            switch (msg.Action)
            {
                case "rv_start":
                    if (_isDemoMode) _demoManager!.StartRemoteView(msg.Target);
                    else _tcpManager!.StartRemoteView(msg.Target);
                    break;
                case "rv_stop":
                    if (_isDemoMode) _demoManager!.StopRemoteView(msg.Target);
                    else _tcpManager!.StopRemoteView(msg.Target);
                    break;
                case "focus_start":
                    if (_isDemoMode) _demoManager!.StartFocusStream(msg.Target);
                    else _tcpManager!.StartFocusStream(msg.Target);
                    break;
                case "focus_stop":
                    if (_isDemoMode) _demoManager!.StopFocusStream(msg.Target);
                    else _tcpManager!.StopFocusStream(msg.Target);
                    break;
                case "lock":
                    if (_isDemoMode) _demoManager!.LockStudent(msg.Target);
                    else _tcpManager!.LockStudent(msg.Target);
                    break;
                case "unlock":
                    if (_isDemoMode) _demoManager!.UnlockStudent(msg.Target);
                    else _tcpManager!.UnlockStudent(msg.Target);
                    break;
                case "freeze":
                    if (_isDemoMode) _demoManager!.FreezeStudent(msg.Target, 300);
                    else _tcpManager!.LockStudent(msg.Target); // Freeze via lock in production
                    break;
                case "unfreeze":
                    if (_isDemoMode) _demoManager!.UnfreezeStudent(msg.Target);
                    else _tcpManager!.UnlockStudent(msg.Target);
                    break;
                case "message":
                    if (!string.IsNullOrWhiteSpace(msg.Payload))
                    {
                        if (_isDemoMode) _demoManager!.BroadcastPushMessage(msg.Payload);
                        else _tcpManager!.BroadcastPushMessage(msg.Payload);
                        SetStatus($"Message sent: \"{msg.Payload.Substring(0, Math.Min(msg.Payload.Length, 50))}\"");;
                    }
                    break;
                case "kill_process":
                    if (!_isDemoMode && int.TryParse(msg.Payload, out int pid))
                    {
                        _tcpManager!.KillProcessOnStudent(msg.Target, pid);
                        SetStatus($"Killed process (PID {pid}) on {msg.Target}");
                    }
                    break;
                case "set_blocklist":
                    if (!_isDemoMode && !string.IsNullOrWhiteSpace(msg.Payload))
                    {
                        try
                        {
                            var bl = System.Text.Json.JsonSerializer.Deserialize<BlocklistUpdate>(msg.Payload);
                            if (bl != null)
                            {
                                _tcpManager!.BroadcastBlocklist(bl);
                                SetStatus($"Blocklist sent: {bl.BlockedPrograms.Count} programs, {bl.BlockedWebsites.Count} websites");
                            }
                        }
                        catch { }
                    }
                    break;
                case "lock_all_confirmed":
                    if (_isDemoMode) _demoManager!.BroadcastLock();
                    else _tcpManager!.BroadcastLock();
                    SetStatus("Locked all screens");
                    break;
                case "unlock_all":
                    if (_isDemoMode) _demoManager!.BroadcastUnlock();
                    else _tcpManager!.BroadcastUnlock();
                    _allFrozen = false;
                    _allBlanked = false;
                    PostJsonMessage(new { type = "freeze_all", frozen = false });
                    PostJsonMessage(new { type = "blank_all", blanked = false });
                    SetStatus("Unlocked all screens");
                    break;
                case "freeze_all_confirmed":
                    _allFrozen = true;
                    if (_isDemoMode) _demoManager!.BroadcastFreeze(300, "Eyes on the teacher!");
                    else _tcpManager!.BroadcastFreeze();
                    PostJsonMessage(new { type = "freeze_all", frozen = true });
                    SetStatus("Froze all screens");
                    break;
                case "unfreeze_all":
                    _allFrozen = false;
                    if (_isDemoMode) _demoManager!.BroadcastUnfreeze();
                    else _tcpManager!.BroadcastUnfreeze();
                    PostJsonMessage(new { type = "freeze_all", frozen = false });
                    SetStatus("Unfroze all screens");
                    break;
                case "blank_all_confirmed":
                    _allBlanked = true;
                    if (_isDemoMode) _demoManager!.BroadcastBlankScreen();
                    else _tcpManager!.BroadcastBlankScreen();
                    PostJsonMessage(new { type = "blank_all", blanked = true });
                    SetStatus("Blanked all screens");
                    break;
                case "unblank_all":
                    _allBlanked = false;
                    if (_isDemoMode) _demoManager!.BroadcastUnblankScreen();
                    else _tcpManager!.BroadcastUnblankScreen();
                    PostJsonMessage(new { type = "blank_all", blanked = false });
                    SetStatus("Restored all screens");
                    break;

                // ── Per-student Internetsperre / Programmsperre ───────────
                case "internet_block":
                    if (!_isDemoMode)
                    {
                        var ibFrame = TadFrameCodec.EncodeJson(TadCommand.SetBlocklist,
                            new BlocklistUpdate { BlockedWebsites = new List<string> { "*" } });
                        _tcpManager!.SendCommandToStudent(msg.Target, ibFrame);
                    }
                    SetStatus($"Internet blocked for {msg.Target}");
                    break;
                case "internet_unblock":
                    if (!_isDemoMode)
                    {
                        var iuFrame = TadFrameCodec.EncodeJson(TadCommand.SetBlocklist,
                            new BlocklistUpdate { BlockedWebsites = new List<string>() });
                        _tcpManager!.SendCommandToStudent(msg.Target, iuFrame);
                    }
                    SetStatus($"Internet unblocked for {msg.Target}");
                    break;
                case "program_block":
                    if (!_isDemoMode)
                    {
                        var pbFrame = TadFrameCodec.EncodeJson(TadCommand.SetBlocklist,
                            new BlocklistUpdate { BlockedPrograms = new List<string> { "*" } });
                        _tcpManager!.SendCommandToStudent(msg.Target, pbFrame);
                    }
                    SetStatus($"Programs blocked for {msg.Target}");
                    break;
                case "program_unblock":
                    if (!_isDemoMode)
                    {
                        var puFrame = TadFrameCodec.EncodeJson(TadCommand.SetBlocklist,
                            new BlocklistUpdate { BlockedPrograms = new List<string>() });
                        _tcpManager!.SendCommandToStudent(msg.Target, puFrame);
                    }
                    SetStatus($"Programs unblocked for {msg.Target}");
                    break;
            }
        }
        catch { /* Ignore malformed messages */ }
    }

    /// <summary>Push student status update to the WebView2 grid.</summary>
    private void OnStudentStatusUpdated(string ip, TADBridge.Shared.StudentStatus status)
    {
        if (!_webViewReady) return;
        Dispatcher.InvokeAsync(() =>
        {
            PostJsonMessage(new { type = "status", ip, data = status });
        });
    }

    /// <summary>Push a raw H.264 sub-stream frame to the WebView2 video decoder.</summary>
    private void OnVideoFrameReceived(string ip, byte[] frameData, bool isKeyFrame)
    {
        if (!_webViewReady) return;
        Dispatcher.InvokeAsync(() =>
        {
            PostJsonMessage(new
            {
                type = "video_frame",
                ip,
                keyFrame = isKeyFrame,
                data = Convert.ToBase64String(frameData)
            });
        });
    }

    /// <summary>Push a synthetic demo-frame (JSON canvas instructions) to the dashboard.</summary>
    private void OnDemoFrameReady(string ip, string frameJson)
    {
        if (!_webViewReady) return;

        // Backpressure: skip if there's already a pending frame for this IP
        lock (_frameLock)
        {
            if (!_pendingFrameIps.Add(ip)) return; // Already queued — drop this frame
        }

        Dispatcher.InvokeAsync(() =>
        {
            lock (_frameLock) { _pendingFrameIps.Remove(ip); }

            var envelope = $"{{\"type\":\"demo_frame\",\"ip\":\"{ip}\",\"frame\":{frameJson}}}";
            PostStringMessage(envelope);
        });
    }

    /// <summary>Push a raw H.264 main-stream frame (30fps 720p) for focused view.</summary>
    private void OnMainFrameReceived(string ip, byte[] frameData, bool isKeyFrame)
    {
        if (!_webViewReady) return;
        Dispatcher.InvokeAsync(() =>
        {
            PostJsonMessage(new
            {
                type = "main_frame",
                ip,
                keyFrame = isKeyFrame,
                data = Convert.ToBase64String(frameData)
            });
        });
    }

    // ─── Toolbar Buttons ──────────────────────────────────────────────

    private void BtnLockAll_Click(object sender, RoutedEventArgs e)
    {
        PostJsonMessage(new { type = "confirm_action", action = "lock_all" });
    }

    private void BtnUnlockAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isDemoMode) _demoManager!.BroadcastUnlock();
        else _tcpManager!.BroadcastUnlock();
        _allFrozen = false;
        _allBlanked = false;
        PostJsonMessage(new { type = "freeze_all", frozen = false });
        PostJsonMessage(new { type = "blank_all", blanked = false });
        SetStatus("Unlocked all screens");
    }

    private void BtnFreezeAll_Click(object sender, RoutedEventArgs e)
    {
        if (_allFrozen)
        {
            _allFrozen = false;
            if (_isDemoMode) _demoManager!.BroadcastUnfreeze();
            else _tcpManager!.BroadcastUnfreeze();
            PostJsonMessage(new { type = "freeze_all", frozen = false });
            SetStatus("Unfroze all screens");
        }
        else
        {
            PostJsonMessage(new { type = "confirm_action", action = "freeze_all" });
        }
    }

    private void BtnBlankAll_Click(object sender, RoutedEventArgs e)
    {
        if (_allBlanked)
        {
            _allBlanked = false;
            if (_isDemoMode) _demoManager!.BroadcastUnblankScreen();
            else _tcpManager!.BroadcastUnblankScreen();
            PostJsonMessage(new { type = "blank_all", blanked = false });
            SetStatus("Restored all screens");
        }
        else
        {
            PostJsonMessage(new { type = "confirm_action", action = "blank_all" });
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_isDemoMode) _demoManager!.PingAll();
        else _tcpManager!.PingAll();
        SetStatus("Refreshing...");
    }

    private void BtnRoomDesigner_Click(object sender, RoutedEventArgs e)
    {
        var designer = new TADAdmin.ClassDesigner.ClassDesignerWindow { Owner = this };
        designer.Show();
    }

    // ── Manual endpoint add (workgroup / no-DC environments) ─────────
    private const string IpPlaceholder = "IP or hostname…";

    private void TxtManualIp_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtManualIp.Text == IpPlaceholder)
        {
            TxtManualIp.Text = string.Empty;
            TxtManualIp.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC9, 0xD1, 0xD9));
        }
    }

    private void TxtManualIp_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtManualIp.Text))
        {
            TxtManualIp.Text = IpPlaceholder;
            TxtManualIp.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x48, 0x4F, 0x58));
        }
    }

    private void TxtManualIp_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            AddManualEndpoint();
    }

    private void BtnAddEndpoint_Click(object sender, RoutedEventArgs e)
        => AddManualEndpoint();

    private void AddManualEndpoint()
    {
        var raw = TxtManualIp.Text.Trim();
        if (string.IsNullOrEmpty(raw) || raw == IpPlaceholder) return;

        // Optional port suffix:  192.168.1.5:17420
        string ip = raw;
        int port  = 17420;
        if (raw.Contains(':'))
        {
            var parts = raw.Split(':', 2);
            ip = parts[0].Trim();
            if (!int.TryParse(parts[1], out port)) port = 17420;
        }

        if (_isDemoMode)
            _demoManager!.AddStudent(ip, port);
        else
            _tcpManager!.AddStudent(ip, port);

        SetStatus($"Connecting to {ip}:{port}…");
        TxtManualIp.Text = IpPlaceholder;
        TxtManualIp.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x48, 0x4F, 0x58));
    }

    private void UpdateStatusBar()
    {
        int connected, total;
        if (_isDemoMode)
        {
            connected = _demoManager!.ConnectedCount;
            total = _demoManager.TotalEndpoints;
        }
        else
        {
            connected = _tcpManager!.ConnectedCount;
            total = _tcpManager.TotalEndpoints;
        }
        TxtConnected.Text = $"{connected} / {total}";

        // Keep the bottom-left status text live with endpoint stats.
        // Overwrite temporary action messages (lock/unlock/etc.) after 5 s
        // so the bar doesn't stay frozen on the last click forever.
        if (_statusMessageExpiry == null || DateTime.UtcNow >= _statusMessageExpiry)
        {
            _statusMessageExpiry = null;
            int offline = total - connected;
            TxtStatus.Text = total == 0
                ? "Waiting for endpoints…"
                : $"{connected} connected  ·  {offline} offline";
        }
    }

    /// <summary>Re-post ALL known student IPs to the WebView every status-timer tick.
    /// This is the belt-and-suspenders recovery for any add_students message that was
    /// silently dropped due to a startup race (_webViewReady was false at the time).
    /// JS's ensureStudentTile() is idempotent — it never creates duplicate tiles.</summary>
    private void FlushStudentsToWebView()
    {
        if (!_webViewReady || _isDemoMode) return;
        var ips = _tcpManager?.GetAllEndpointIps();
        if (ips is { Count: > 0 })
            PostJsonMessage(new { type = "add_students", ips = ips.ToArray() });
    }

    /// <summary>Set a temporary status message that auto-clears after <paramref name="seconds"/> seconds.</summary>
    private void SetStatus(string message, int seconds = 5)
    {
        TxtStatus.Text = message;
        _statusMessageExpiry = DateTime.UtcNow.AddSeconds(seconds);
    }

    // ─── Custom Caption Buttons ───────────────────────────────────────

    private void CaptionMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CaptionMaxRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        BtnCaptionMaxRestore.Content = WindowState == WindowState.Maximized
            ? "\uE923"   // ChromeRestore
            : "\uE922";  // ChromeMaximize
    }

    private void CaptionClose_Click(object sender, RoutedEventArgs e)
    {
        // X button hides to tray — use tray "Exit" to truly quit
        Hide();
        _trayIcon?.ShowBalloonTip(2000, "TAD.RV", "Still running in the system tray", ToolTipIcon.Info);
    }

    /// <summary>Only actually close when _exitRequested is true (tray → Exit).</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(2000, "TAD.RV", "Still running in the system tray", ToolTipIcon.Info);
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _webViewReady = false;
        _statusTimer.Stop();
        _discoveryListener?.Dispose();
        if (_isDemoMode) _demoManager?.Dispose();
        else _tcpManager?.Dispose();

        // Clean up tray icon
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnClosed(e);
    }

    // ─── Software Update Notification ─────────────────────────────────

    /// <summary>
    /// Called from App.xaml.cs when a newer version is available.
    /// Sends a message to the dashboard JS to display an update banner.
    /// </summary>
    public async Task NotifyUpdateAvailable(string version, string releaseNotes, string htmlUrl)
    {
        if (!_webViewReady) return;

        try
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                var payload = JsonSerializer.Serialize(new
                {
                    type = "updateAvailable",
                    version,
                    releaseNotes,
                    htmlUrl
                });
                DashboardWebView.CoreWebView2?.PostWebMessageAsJson(payload);
            });
        }
        catch { /* Best effort */ }
    }

    // ─── Diagnostics ──────────────────────────────────────────────────

    private void ShowDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ TAD.RV Admin Diagnostics ═══");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Version:   {GetRunningVersion()}");
        sb.AppendLine($"Mode:      {(_isDemoMode ? "Demo" : "Production")}");
        sb.AppendLine();

        sb.AppendLine("── Connections ──");
        if (_isDemoMode)
        {
            sb.AppendLine($"  Connected: {_demoManager!.ConnectedCount} / {_demoManager.TotalEndpoints}");
        }
        else
        {
            sb.AppendLine($"  Connected: {_tcpManager!.ConnectedCount} / {_tcpManager.TotalEndpoints}");
            sb.AppendLine($"  Known IPs: {string.Join(", ", _tcpManager.GetAllEndpointIps())}");
        }

        if (_discoveryListener != null)
        {
            sb.AppendLine($"  Discovery: {_discoveryListener.KnownStudentIps?.Count ?? 0} discovered");
        }

        sb.AppendLine();
        sb.AppendLine("── System ──");
        sb.AppendLine($"  Machine:    {Environment.MachineName}");
        sb.AppendLine($"  User:       {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"  OS:         {Environment.OSVersion}");
        sb.AppendLine($"  .NET:       {Environment.Version}");
        sb.AppendLine($"  Processors: {Environment.ProcessorCount}");
        sb.AppendLine($"  WebView2:   {(_webViewReady ? "Ready" : "Not ready")}");

        sb.AppendLine();
        sb.AppendLine("── Network ──");
        try
        {
            foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback))
            {
                var addrs = iface.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString());
                sb.AppendLine($"  {iface.Name}: {string.Join(", ", addrs)}");
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

        MessageBox.Show(sb.ToString(), "TAD.RV — Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

/// <summary>Messages from WebView2 JS → C#. Uses camelCase to match JS property names.</summary>
file sealed class WebMessage
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";
}
