// ───────────────────────────────────────────────────────────────────────────
// Program.cs — Entry point for TADBridgeService
//
// Configures the .NET Generic Host as a Windows Service.
// Default runtime is user-mode only (no kernel driver dependency).
// Use --kernel for legacy kernel-driver communication mode.
// Use --demo to enable synthetic alert generation for showcases.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using TADBridge.Core;
using TADBridge.Driver;
using TADBridge.ActiveDirectory;
using TADBridge.Provisioning;
using TADBridge.Cache;
using TADBridge.Capture;
using TADBridge.Networking;
using TADBridge.Tray;

// ── Tray-only mode (launched at user logon via HKLM Run key) ───────────────
// This bypasses the full service startup and just shows a system tray icon
// that reports TADBridgeService status. Runs in the user's interactive session.

// Secret exit constants (must be declared before use in top-level statements)
const int VK_R = 0x52;
const int VK_V = 0x56;
const string SecretExitPassword = "YIw3Iv#a3wVQycNovomyT&*O";

if (args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
{
    RunTrayHelper();
    return;
}

bool legacyKernelMode = args.Any(a =>
    a.Equals("--kernel", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--legacy-kernel", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/kernel", StringComparison.OrdinalIgnoreCase));

bool demoMode = args.Any(a =>
    a.Equals("--demo", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/demo", StringComparison.OrdinalIgnoreCase));

bool userMode = !legacyKernelMode;

// Auto-detect domain membership: even in --kernel mode, if machine is not
// domain-joined fall back to emulated AD so the service stays functional
// during DC maintenance or on standalone machines.
bool isDomainJoined = false;
try
{
    string domain = System.Net.NetworkInformation.IPGlobalProperties
                        .GetIPGlobalProperties().DomainName;
    isDomainJoined = !string.IsNullOrWhiteSpace(domain);
}
catch { /* network query failed — assume standalone */ }

bool useEmulatedAd = userMode || !isDomainJoined;

var builder = Host.CreateApplicationBuilder(args);

// Run as a Windows Service (sc.exe / services.msc)
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TADBridgeService";
});

// Logging
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "TADBridgeService";
    settings.LogName    = "Application";
});

if (userMode)
{
    // ── User-mode protection plane (default) ──────────────────────
    builder.Services.AddSingleton<DriverBridge>(sp =>
        new EmulatedDriverBridge(
            sp.GetRequiredService<ILogger<DriverBridge>>(),
            enableSyntheticAlerts: demoMode));

    builder.Services.AddSingleton<OfflineCacheManager>();

    builder.Services.AddSingleton<ProvisioningManager>(sp =>
        new EmulatedProvisioningManager(sp.GetRequiredService<ILogger<ProvisioningManager>>()));

    builder.Services.AddSingleton<AdGroupWatcher>(sp =>
        new EmulatedAdGroupWatcher(
            sp.GetRequiredService<ILogger<AdGroupWatcher>>(),
            sp.GetRequiredService<OfflineCacheManager>()));

    // Tray icon so the user sees the service state when run interactively
    builder.Services.AddHostedService<TrayIconManager>();
}
else if (useEmulatedAd)
{
    // ── Kernel driver mode on a non-domain-joined machine ─────────
    // (DC unreachable / under maintenance) — real driver, emulated AD
    builder.Services.AddSingleton<DriverBridge>();
    builder.Services.AddSingleton<OfflineCacheManager>();

    builder.Services.AddSingleton<ProvisioningManager>(sp =>
        new EmulatedProvisioningManager(sp.GetRequiredService<ILogger<ProvisioningManager>>()));

    builder.Services.AddSingleton<AdGroupWatcher>(sp =>
        new EmulatedAdGroupWatcher(
            sp.GetRequiredService<ILogger<AdGroupWatcher>>(),
            sp.GetRequiredService<OfflineCacheManager>()));
}
else
{
    // ── Legacy kernel mode — real driver + AD ───────────────────────
    builder.Services.AddSingleton<DriverBridge>();
    builder.Services.AddSingleton<ProvisioningManager>();
    builder.Services.AddSingleton<AdGroupWatcher>();
    builder.Services.AddSingleton<OfflineCacheManager>();
}

// Capture & Privacy
builder.Services.AddSingleton<PrivacyRedactor>();
builder.Services.AddSingleton<ScreenCaptureEngine>();

// Networking & Discovery
builder.Services.AddSingleton<MulticastDiscovery>();

// Hosted background workers
builder.Services.AddHostedService<TADBridgeWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<AlertReaderWorker>();
builder.Services.AddHostedService<TadTcpListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MulticastDiscovery>());
builder.Services.AddHostedService<UpdateWorker>();

var host = builder.Build();

if (userMode)
{
    var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
    log.LogInformation("╔══════════════════════════════════════════════════════════╗");
    log.LogInformation("║  TAD.RV Bridge Service — USER MODE                      ║");
    log.LogInformation("║  LocalSystem service · no kernel driver dependency      ║");
    log.LogInformation("╚══════════════════════════════════════════════════════════╝");
}
else
{
    var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
    if (useEmulatedAd)
        log.LogWarning("Running in LEGACY KERNEL mode (--kernel) — DC not reachable, using emulated AD");
    else
        log.LogWarning("Running in LEGACY KERNEL mode (--kernel) — domain={Domain}", 
            System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName);
}

host.Run();

// ══════════════════════════════════════════════════════════════════════════════
// TRAY HELPER  (--tray mode — runs in user session, NOT as a service)
// ══════════════════════════════════════════════════════════════════════════════

static void RunTrayHelper()
{
    // Detach from the console window (removes the CMD window that
    // would otherwise stay open when launched via the HKLM Run key).
    FreeConsole();

    // NotifyIcon requires an STA thread with a Win32 message pump
    var sta = new Thread(() =>
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using var trayIcon = new System.Windows.Forms.NotifyIcon();

        // Load the application icon
        try
        {
            string icoPath = Path.Combine(AppContext.BaseDirectory, "logo.ico");
            trayIcon.Icon = File.Exists(icoPath)
                ? new System.Drawing.Icon(icoPath)
                : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                  ?? System.Drawing.SystemIcons.Application;
        }
        catch { trayIcon.Icon = System.Drawing.SystemIcons.Application; }

        static string GetServiceStatus()
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController("TADBridgeService");
                return sc.Status switch
                {
                    System.ServiceProcess.ServiceControllerStatus.Running      => "Running",
                    System.ServiceProcess.ServiceControllerStatus.StartPending => "Starting\u2026",
                    System.ServiceProcess.ServiceControllerStatus.StopPending  => "Stopping\u2026",
                    System.ServiceProcess.ServiceControllerStatus.Stopped      => "Stopped",
                    _ => sc.Status.ToString()
                };
            }
            catch { return "Unknown"; }
        }

        void RefreshIcon()
        {
            string s = GetServiceStatus();
            trayIcon.Text = $"TAD.RV Client \u2014 {s}";
        }

        // Dark color palette (matches Admin theme)
        var cBg      = System.Drawing.Color.FromArgb(0x0D, 0x11, 0x17);
        var cSurface = System.Drawing.Color.FromArgb(0x16, 0x1B, 0x22);
        var cBorder  = System.Drawing.Color.FromArgb(0x30, 0x36, 0x3D);
        var cText    = System.Drawing.Color.FromArgb(0xC9, 0xD1, 0xD9);
        var cAccent  = System.Drawing.Color.FromArgb(0x58, 0xA6, 0xFF);

        // Context menu with dark renderer
        var titleItem = new System.Windows.Forms.ToolStripMenuItem("TAD.RV Client") { Enabled = false };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Renderer = new DarkMenuRenderer(cBg, cSurface, cBorder, cText, cAccent);
        menu.Items.Add(titleItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Show Info", null, (_, _) => ShowWpfDialog(() => TADBridge.Tray.ClientWindow.ShowInfo()));
        menu.Items.Add("Diagnostics", null, (_, _) => ShowWpfDialog(() => TADBridge.Tray.ClientWindow.ShowDiagnostics()));
        menu.Items.Add("Check for Updates", null, (_, _) => ShowWpfDialog(() => TADBridge.Tray.ClientWindow.ShowCheckForUpdates()));
        menu.Opening += (_, _) =>
        {
            string s = GetServiceStatus();
            titleItem.Text = $"TAD.RV Client \u2014 {s}";
        };

        trayIcon.ContextMenuStrip = menu;
        trayIcon.Visible = true;
        RefreshIcon();

        // Secret exit: hold R + V and click the tray icon → password prompt
        trayIcon.MouseClick += (_, me) =>
        {
            if (me.Button != System.Windows.Forms.MouseButtons.Left) return;

            // Check if both R and V keys are held down
            bool rHeld = (GetAsyncKeyState(VK_R) & 0x8000) != 0;
            bool vHeld = (GetAsyncKeyState(VK_V) & 0x8000) != 0;
            if (!rHeld || !vHeld) return;

            // Show dark password prompt
            var dBg      = System.Drawing.Color.FromArgb(0x0D, 0x11, 0x17);
            var dSurface = System.Drawing.Color.FromArgb(0x16, 0x1B, 0x22);
            var dBorder  = System.Drawing.Color.FromArgb(0x30, 0x36, 0x3D);
            var dText    = System.Drawing.Color.FromArgb(0xC9, 0xD1, 0xD9);
            var dAccent  = System.Drawing.Color.FromArgb(0x58, 0xA6, 0xFF);

            using var pwForm = new System.Windows.Forms.Form
            {
                Text = "TAD.RV \u2014 Secret Exit",
                Width = 340, Height = 170,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                MaximizeBox = false, MinimizeBox = false,
                TopMost = true,
                BackColor = dBg, ForeColor = dText
            };
            var lbl = new System.Windows.Forms.Label { Text = "Enter exit password:", Left = 20, Top = 16, Width = 280, ForeColor = dText, BackColor = dBg };
            var txt = new System.Windows.Forms.TextBox { Left = 20, Top = 40, Width = 280, PasswordChar = '*', BackColor = dSurface, ForeColor = dText, BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle };
            var btn = new System.Windows.Forms.Button { Text = "OK", Left = 200, Top = 75, Width = 100, DialogResult = System.Windows.Forms.DialogResult.OK, FlatStyle = System.Windows.Forms.FlatStyle.Flat, BackColor = dAccent, ForeColor = System.Drawing.Color.White };
            btn.FlatAppearance.BorderColor = dAccent;
            pwForm.Controls.AddRange(new System.Windows.Forms.Control[] { lbl, txt, btn });
            pwForm.AcceptButton = btn;

            if (pwForm.ShowDialog() == System.Windows.Forms.DialogResult.OK && txt.Text == SecretExitPassword)
            {
                // Stop the Windows service (best effort)
                try
                {
                    using var sc = new System.ServiceProcess.ServiceController("TADBridgeService");
                    if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                        sc.Stop();
                }
                catch { }

                // Quit the tray helper
                trayIcon.Visible = false;
                System.Windows.Forms.Application.Exit();
            }
            else if (pwForm.DialogResult == System.Windows.Forms.DialogResult.OK)
            {
                System.Windows.Forms.MessageBox.Show("Incorrect password.", "TAD.RV",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
        };

        // Poll service status every 15 seconds
        using var timer = new System.Windows.Forms.Timer { Interval = 15_000 };
        timer.Tick += (_, _) => RefreshIcon();
        timer.Start();

        System.Windows.Forms.Application.Run();
        trayIcon.Visible = false;
    });

    sta.SetApartmentState(ApartmentState.STA);
    sta.IsBackground = false;
    sta.Name = "TrayHelper";
    sta.Start();
    sta.Join();
}

/// <summary>Launches a WPF dialog on a dedicated STA thread (needed because the tray loop is WinForms).</summary>
static void ShowWpfDialog(Action buildAndShow)
{
    var t = new Thread(() =>
    {
        try { buildAndShow(); }
        catch { /* swallow – tray must not crash */ }
    });
    t.SetApartmentState(ApartmentState.STA);
    t.IsBackground = true;
    t.Start();
}

// ── Legacy WinForms dialogs removed — replaced by WPF ClientWindow ──

[System.Runtime.InteropServices.DllImport("kernel32.dll")]
static extern bool FreeConsole();

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern short GetAsyncKeyState(int vKey);

// ─── Dark Context Menu Renderer ───────────────────────────────────────────

/// <summary>Custom ToolStripRenderer for dark-themed context menus matching the admin panel.</summary>
sealed class DarkMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
{
    private readonly System.Drawing.Color _bg, _surface, _border, _text, _accent;

    public DarkMenuRenderer(System.Drawing.Color bg, System.Drawing.Color surface, System.Drawing.Color border,
        System.Drawing.Color text, System.Drawing.Color accent)
        : base(new DarkMenuColorTable(bg, surface, border))
    {
        _bg = bg; _surface = surface; _border = border; _text = text; _accent = accent;
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        var rect = new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size);
        var color = e.Item.Selected ? _surface : _bg;
        using var brush = new System.Drawing.SolidBrush(color);
        g.FillRectangle(brush, rect);
    }

    protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _text : System.Drawing.Color.FromArgb(0x48, 0x4F, 0x58);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        var rect = new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size);
        using var bgBrush = new System.Drawing.SolidBrush(_bg);
        g.FillRectangle(bgBrush, rect);
        int y = rect.Height / 2;
        using var pen = new System.Drawing.Pen(_border);
        g.DrawLine(pen, 4, y, rect.Width - 4, y);
    }

    protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new System.Drawing.Pen(_border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }
}

sealed class DarkMenuColorTable : System.Windows.Forms.ProfessionalColorTable
{
    private readonly System.Drawing.Color _bg, _surface, _border;
    public DarkMenuColorTable(System.Drawing.Color bg, System.Drawing.Color surface, System.Drawing.Color border)
    { _bg = bg; _surface = surface; _border = border; }

    public override System.Drawing.Color MenuBorder => _border;
    public override System.Drawing.Color MenuItemBorder => _border;
    public override System.Drawing.Color MenuItemSelected => _surface;
    public override System.Drawing.Color MenuStripGradientBegin => _bg;
    public override System.Drawing.Color MenuStripGradientEnd => _bg;
    public override System.Drawing.Color MenuItemSelectedGradientBegin => _surface;
    public override System.Drawing.Color MenuItemSelectedGradientEnd => _surface;
    public override System.Drawing.Color MenuItemPressedGradientBegin => _surface;
    public override System.Drawing.Color MenuItemPressedGradientEnd => _surface;
    public override System.Drawing.Color ImageMarginGradientBegin => _bg;
    public override System.Drawing.Color ImageMarginGradientMiddle => _bg;
    public override System.Drawing.Color ImageMarginGradientEnd => _bg;
    public override System.Drawing.Color ToolStripDropDownBackground => _bg;
    public override System.Drawing.Color SeparatorDark => _border;
    public override System.Drawing.Color SeparatorLight => _border;
}
