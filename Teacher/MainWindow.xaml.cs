// ───────────────────────────────────────────────────────────────────────────
// MainWindow.xaml.cs — Teacher Controller shell
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Hosts WebView2 for the student grid dashboard and bridges WPF buttons
// to the TcpClientManager. WebView2 receives student status + video via
// PostWebMessageAsJson and dispatches commands back via web-to-native msgs.
// ───────────────────────────────────────────────────────────────────────────

using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace TadTeacher;

public partial class MainWindow : Window
{
    private readonly TcpClientManager _clientManager;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    public MainWindow()
    {
        InitializeComponent();

        _clientManager = new TcpClientManager();
        _clientManager.StudentStatusUpdated += OnStudentStatusUpdated;
        _clientManager.VideoFrameReceived += OnVideoFrameReceived;

        InitializeWebView();

        // Poll status every 3 seconds
        _statusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _statusTimer.Tick += (_, _) => UpdateStatusBar();
        _statusTimer.Start();
    }

    // ─── WebView2 Initialization ──────────────────────────────────────

    private async void InitializeWebView()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await DashboardWebView.EnsureCoreWebView2Async(env);

            // Load embedded HTML
            DashboardWebView.CoreWebView2.NavigateToString(LoadEmbeddedHtml());

            // Bridge: JS → C#
            DashboardWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"WebView2 init failed: {ex.Message}";
        }
    }

    private static string LoadEmbeddedHtml()
    {
        var asm = Assembly.GetExecutingAssembly();
        var css = LoadResource(asm, "TadTeacher.Web.dashboard.css");
        var js = LoadResource(asm, "TadTeacher.Web.dashboard.js");
        var html = LoadResource(asm, "TadTeacher.Web.dashboard.html");

        // Inline CSS & JS into the HTML
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
            var msg = JsonSerializer.Deserialize<WebMessage>(e.WebMessageAsJson);
            if (msg == null) return;

            switch (msg.Action)
            {
                case "rv_start":
                    _clientManager.StartRemoteView(msg.Target);
                    break;
                case "rv_stop":
                    _clientManager.StopRemoteView(msg.Target);
                    break;
                case "lock":
                    _clientManager.LockStudent(msg.Target);
                    break;
                case "unlock":
                    _clientManager.UnlockStudent(msg.Target);
                    break;
            }
        }
        catch { /* Ignore malformed messages */ }
    }

    /// <summary>Push student status update to the WebView2 grid.</summary>
    private void OnStudentStatusUpdated(string ip, TadBridge.Shared.StudentStatus status)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var json = JsonSerializer.Serialize(new
            {
                type = "status",
                ip,
                data = status
            });
            DashboardWebView.CoreWebView2?.PostWebMessageAsJson(json);
        });
    }

    /// <summary>Push a raw H.264 frame to the WebView2 video decoder.</summary>
    private void OnVideoFrameReceived(string ip, byte[] frameData, bool isKeyFrame)
    {
        Dispatcher.InvokeAsync(() =>
        {
            // Send as base64 to JS for WebCodecs API decoding
            var json = JsonSerializer.Serialize(new
            {
                type = "video_frame",
                ip,
                keyFrame = isKeyFrame,
                data = Convert.ToBase64String(frameData)
            });
            DashboardWebView.CoreWebView2?.PostWebMessageAsJson(json);
        });
    }

    // ─── Toolbar Buttons ──────────────────────────────────────────────

    private void BtnLockAll_Click(object sender, RoutedEventArgs e)
    {
        _clientManager.BroadcastLock();
        TxtStatus.Text = "Locked all screens";
    }

    private void BtnUnlockAll_Click(object sender, RoutedEventArgs e)
    {
        _clientManager.BroadcastUnlock();
        TxtStatus.Text = "Unlocked all screens";
    }

    private void BtnCollect_Click(object sender, RoutedEventArgs e)
    {
        _clientManager.BroadcastCollectFiles();
        TxtStatus.Text = "File collection started...";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _clientManager.PingAll();
        TxtStatus.Text = "Refreshing...";
    }

    private void UpdateStatusBar()
    {
        int connected = _clientManager.ConnectedCount;
        int total = _clientManager.TotalEndpoints;
        TxtConnected.Text = $"{connected}/{total} connected";
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        _clientManager.Dispose();
        base.OnClosed(e);
    }
}

/// <summary>Messages from WebView2 JS → C#</summary>
file sealed class WebMessage
{
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";  // IP address
}
