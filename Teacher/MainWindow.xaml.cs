// ───────────────────────────────────────────────────────────────────────────
// MainWindow.xaml.cs — Teacher Controller shell
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
using TadTeacher.Networking;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TadTeacher;

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

    // JSON options for camelCase deserialization
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MainWindow(bool demoMode = false)
    {
        InitializeComponent();

        _isDemoMode = demoMode;

        if (_isDemoMode)
        {
            TxtDemoTag.Visibility = Visibility.Visible;
            Title = "TAD.RV — Teacher Controller [DEMO]";

            _demoManager = new DemoTcpClientManager();
            _demoManager.StudentStatusUpdated += OnStudentStatusUpdated;
            _demoManager.VideoFrameReceived += OnVideoFrameReceived;
            _demoManager.MainFrameReceived += OnMainFrameReceived;
            _demoManager.DemoFrameReady += OnDemoFrameReady;
        }
        else
        {
            _tcpManager = new TcpClientManager();
            _tcpManager.StudentStatusUpdated += OnStudentStatusUpdated;
            _tcpManager.VideoFrameReceived += OnVideoFrameReceived;
            _tcpManager.MainFrameReceived += OnMainFrameReceived;

            // Auto-discover student machines via UDP multicast (zero-config)
            _discoveryListener = new DiscoveryListener();
            _discoveryListener.OnStudentDiscovered += OnStudentDiscovered;
            _discoveryListener.Start();
        }

        InitializeWebView();
        InitializeTrayIcon();

        _statusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _statusTimer.Tick += (_, _) => UpdateStatusBar();
        _statusTimer.Start();
    }

    // ─── System Tray Icon ─────────────────────────────────────────────

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new NotifyIcon
            {
                Text = _isDemoMode ? "TAD.RV Teacher [DEMO]" : "TAD.RV Teacher Controller",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };

            // Try to load icon from embedded logo
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("TadTeacher.Assets.logo32.b64");
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
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.InvokeAsync(Close));
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

    /// <summary>Minimize to system tray instead of taskbar.</summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _trayIcon?.ShowBalloonTip(2000, "TAD.RV", "Minimized to system tray", ToolTipIcon.Info);
        }
    }

    /// <summary>Called by DiscoveryListener when a new student IP appears on the network.</summary>
    private void OnStudentDiscovered(string ip, int port, string hostname, string roomId)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _tcpManager?.AddStudent(ip, port);
            TxtStatus.Text = $"Discovered: {hostname} ({ip})";
        });
    }

    // ─── WebView2 Initialization ──────────────────────────────────────

    private async void InitializeWebView()
    {
        try
        {
            // Use a temp folder for WebView2 user data to avoid permission issues
            var userDataFolder = Path.Combine(Path.GetTempPath(), "TadTeacher_WV2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await DashboardWebView.EnsureCoreWebView2Async(env);

            // Wire up readiness gate — NavigationCompleted fires after the page is fully loaded
            DashboardWebView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                _webViewReady = args.IsSuccess;
                if (_webViewReady && _isDemoMode)
                {
                    PostJsonMessage(new { type = "config", demoMode = true, version = "26200.172" });
                }
                else if (_webViewReady)
                {
                    PostJsonMessage(new { type = "config", demoMode = false, version = "26200.172" });
                }
            };

            DashboardWebView.CoreWebView2.NavigateToString(LoadEmbeddedHtml());
            DashboardWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"WebView2 init failed: {ex.Message}";
            // Show a helpful message if WebView2 runtime is missing
            if (ex.Message.Contains("not found") || ex.Message.Contains("not installed") ||
                ex is System.Runtime.InteropServices.COMException || ex is FileNotFoundException)
            {
                MessageBox.Show(
                    "The Microsoft Edge WebView2 Runtime is required but was not found.\n\n" +
                    "Please install it from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/\n\n" +
                    $"Technical details: {ex.Message}",
                    "TAD.RV — WebView2 Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

    private static string LoadEmbeddedHtml()
    {
        var asm = Assembly.GetExecutingAssembly();
        var css = LoadResource(asm, "TadTeacher.Web.dashboard.css");
        var js = LoadResource(asm, "TadTeacher.Web.dashboard.js");
        var html = LoadResource(asm, "TadTeacher.Web.dashboard.html");

        html = html.Replace("<!-- INLINE_CSS -->", $"<style>{css}</style>");
        html = html.Replace("<!-- INLINE_JS -->", $"<script>{js}</script>");
        return html;
    }

    private static string LoadResource(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return $"/* resource {name} not found */";
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
                    TxtStatus.Text = $"Message sent: {msg.Payload}";
                    break;
            }
        }
        catch { /* Ignore malformed messages */ }
    }

    /// <summary>Push student status update to the WebView2 grid.</summary>
    private void OnStudentStatusUpdated(string ip, TadBridge.Shared.StudentStatus status)
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
        if (_isDemoMode) _demoManager!.BroadcastLock();
        else _tcpManager!.BroadcastLock();
        TxtStatus.Text = "Locked all screens";
    }

    private void BtnUnlockAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isDemoMode) _demoManager!.BroadcastUnlock();
        else _tcpManager!.BroadcastUnlock();
        TxtStatus.Text = "Unlocked all screens";
    }

    private void BtnFreezeAll_Click(object sender, RoutedEventArgs e)
    {
        _allFrozen = !_allFrozen;
        if (_allFrozen)
        {
            if (_isDemoMode) _demoManager!.BroadcastFreeze(300, "Eyes on the teacher!");
            TxtStatus.Text = "Froze all screens — Eyes on the teacher!";
        }
        else
        {
            if (_isDemoMode) _demoManager!.BroadcastUnfreeze();
            TxtStatus.Text = "Unfroze all screens";
        }

        PostJsonMessage(new { type = "freeze_all", frozen = _allFrozen });
    }

    private void BtnBlankAll_Click(object sender, RoutedEventArgs e)
    {
        _allBlanked = !_allBlanked;
        TxtStatus.Text = _allBlanked ? "Blanked all screens" : "Restored all screens";
        PostJsonMessage(new { type = "blank_all", blanked = _allBlanked });
    }

    private void BtnMessage_Click(object sender, RoutedEventArgs e)
    {
        PostJsonMessage(new { type = "show_message_dialog" });
    }

    private void BtnCollect_Click(object sender, RoutedEventArgs e)
    {
        if (_isDemoMode) _demoManager!.BroadcastCollectFiles();
        else _tcpManager!.BroadcastCollectFiles();
        TxtStatus.Text = "File collection started...";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_isDemoMode) _demoManager!.PingAll();
        else _tcpManager!.PingAll();
        TxtStatus.Text = "Refreshing...";
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
        TxtConnected.Text = $"{connected}/{total} connected";
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
