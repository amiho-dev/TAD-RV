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
        menu.Items.Add("Show Info", null, (_, _) => ShowClientInfoDialog(trayIcon));
        menu.Items.Add("Diagnostics", null, (_, _) => ShowDiagnosticsDialog(trayIcon));
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

/// <summary>Shows a small info dialog with recording settings, logged-on user, service status.</summary>
static void ShowClientInfoDialog(System.Windows.Forms.NotifyIcon tray)
{
    string svcStatus;
    try
    {
        using var sc = new System.ServiceProcess.ServiceController("TADBridgeService");
        svcStatus = sc.Status.ToString();
    }
    catch { svcStatus = "Unknown"; }

    string user = Environment.UserName;
    string machine = Environment.MachineName;
    string domain = Environment.UserDomainName;
    string os = Environment.OSVersion.ToString();

    // Check if screen capture is active (service is running)
    string captureStatus = svcStatus == "Running" ? "Active (service running)" : "Inactive (service not running)";

    // Installed version
    string version;
    try
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var attr = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
        version = attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "Unknown";
    }
    catch { version = "Unknown"; }

    // Check if admin/teacher is connected (port 17420 listener active)
    string adminStatus;
    try
    {
        var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners();
        bool listening = listeners.Any(ep => ep.Port == 17420);
        adminStatus = listening ? "Listening on port 17420" : "Not listening";
    }
    catch { adminStatus = "Unable to determine"; }

    var info = $"""
        TAD.RV Client Information
        ─────────────────────────────────
        
        Version:           {version}
        
        Recording / Capture:
          Status:          {captureStatus}
          Service:         TADBridgeService ({svcStatus})
          TCP Listener:    {adminStatus}
        
        User / System:
          Logged-on User:  {domain}\{user}
          Machine:         {machine}
          OS:              {os}
        
        Server / Admin Status:
          Service:         {svcStatus}
          Admin Port:      {adminStatus}
        """;

    // Dark info dialog (matches admin theme)
    var fBg      = System.Drawing.Color.FromArgb(0x0D, 0x11, 0x17);
    var fSurface = System.Drawing.Color.FromArgb(0x16, 0x1B, 0x22);
    var fText    = System.Drawing.Color.FromArgb(0xC9, 0xD1, 0xD9);
    var fAccent  = System.Drawing.Color.FromArgb(0x58, 0xA6, 0xFF);

    using var infoForm = new System.Windows.Forms.Form
    {
        Text = "TAD.RV \u2014 Client Info",
        Width = 520, Height = 420,
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
        MaximizeBox = false, MinimizeBox = false,
        BackColor = fBg, ForeColor = fText
    };

    var infoTxt = new System.Windows.Forms.TextBox
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = System.Windows.Forms.ScrollBars.Vertical,
        Dock = System.Windows.Forms.DockStyle.Fill,
        Font = new System.Drawing.Font("Consolas", 9.5f),
        Text = info.Trim(),
        BackColor = fSurface,
        ForeColor = fText,
        BorderStyle = System.Windows.Forms.BorderStyle.None
    };

    var okBtn = new System.Windows.Forms.Button
    {
        Text = "OK",
        Dock = System.Windows.Forms.DockStyle.Bottom,
        Height = 34,
        FlatStyle = System.Windows.Forms.FlatStyle.Flat,
        BackColor = fAccent,
        ForeColor = System.Drawing.Color.White,
        DialogResult = System.Windows.Forms.DialogResult.OK
    };
    okBtn.FlatAppearance.BorderColor = fAccent;

    infoForm.Controls.Add(infoTxt);
    infoForm.Controls.Add(okBtn);
    infoForm.AcceptButton = okBtn;
    infoForm.ShowDialog();
}

/// <summary>Collects diagnostics and shows them in a scrollable dialog.</summary>
static void ShowDiagnosticsDialog(System.Windows.Forms.NotifyIcon tray)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("═══ TAD.RV Client Diagnostics ═══");
    sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine();

    // Version
    try
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var attr = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
        sb.AppendLine($"Version: {attr?.InformationalVersion ?? "?"}");
    }
    catch (Exception ex) { sb.AppendLine($"Version: Error — {ex.Message}"); }

    // Service status
    sb.AppendLine();
    sb.AppendLine("── Service ──");
    try
    {
        using var sc = new System.ServiceProcess.ServiceController("TADBridgeService");
        sb.AppendLine($"  Status:     {sc.Status}");
        sb.AppendLine($"  StartType:  {sc.StartType}");
    }
    catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

    // Network
    sb.AppendLine();
    sb.AppendLine("── Network ──");
    try
    {
        var props = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
        sb.AppendLine($"  Hostname:   {props.HostName}");
        sb.AppendLine($"  Domain:     {(string.IsNullOrEmpty(props.DomainName) ? "(none)" : props.DomainName)}");

        var listeners = props.GetActiveTcpListeners();
        bool has17420 = listeners.Any(ep => ep.Port == 17420);
        bool has17421 = listeners.Any(ep => ep.Port == 17421);
        sb.AppendLine($"  TCP 17420:  {(has17420 ? "LISTENING" : "not listening")}");
        sb.AppendLine($"  UDP 17421:  {(has17421 ? "LISTENING" : "not listening")}");

        foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback))
        {
            var ipProps = iface.GetIPProperties();
            var addrs = ipProps.UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString());
            sb.AppendLine($"  {iface.Name}: {string.Join(", ", addrs)}");
        }
    }
    catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

    // System
    sb.AppendLine();
    sb.AppendLine("── System ──");
    sb.AppendLine($"  User:       {Environment.UserDomainName}\\{Environment.UserName}");
    sb.AppendLine($"  Machine:    {Environment.MachineName}");
    sb.AppendLine($"  OS:         {Environment.OSVersion}");
    sb.AppendLine($"  .NET:       {Environment.Version}");
    sb.AppendLine($"  Processors: {Environment.ProcessorCount}");
    sb.AppendLine($"  64-bit OS:  {Environment.Is64BitOperatingSystem}");

    // Install path
    sb.AppendLine();
    sb.AppendLine("── Installation ──");
    sb.AppendLine($"  Base Dir:   {AppContext.BaseDirectory}");
    sb.AppendLine($"  Process:    {Environment.ProcessPath}");

    // Recent log entries (if log file exists)
    sb.AppendLine();
    sb.AppendLine("── Recent Log ──");
    try
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TAD-RV", "logs");
        if (Directory.Exists(logDir))
        {
            var latestLog = Directory.GetFiles(logDir, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latestLog != null)
            {
                var lines = File.ReadLines(latestLog).TakeLast(20).ToArray();
                sb.AppendLine($"  File: {latestLog}");
                foreach (var line in lines) sb.AppendLine($"  {line}");
            }
            else sb.AppendLine("  No log files found");
        }
        else sb.AppendLine($"  Log directory not found: {logDir}");
    }
    catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }

    // Show in a dark-themed form
    var gBg      = System.Drawing.Color.FromArgb(0x0D, 0x11, 0x17);
    var gSurface = System.Drawing.Color.FromArgb(0x16, 0x1B, 0x22);
    var gText    = System.Drawing.Color.FromArgb(0xC9, 0xD1, 0xD9);
    var gAccent  = System.Drawing.Color.FromArgb(0x58, 0xA6, 0xFF);

    var form = new System.Windows.Forms.Form
    {
        Text = "TAD.RV \u2014 Diagnostics",
        Width = 600,
        Height = 500,
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable,
        BackColor = gBg,
        ForeColor = gText
    };

    var txt = new System.Windows.Forms.TextBox
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = System.Windows.Forms.ScrollBars.Both,
        Dock = System.Windows.Forms.DockStyle.Fill,
        Font = new System.Drawing.Font("Consolas", 9f),
        Text = sb.ToString(),
        BackColor = gSurface,
        ForeColor = gText,
        BorderStyle = System.Windows.Forms.BorderStyle.None
    };

    var copyBtn = new System.Windows.Forms.Button
    {
        Text = "Copy to Clipboard",
        Dock = System.Windows.Forms.DockStyle.Bottom,
        Height = 32,
        FlatStyle = System.Windows.Forms.FlatStyle.Flat,
        BackColor = gAccent,
        ForeColor = System.Drawing.Color.White
    };
    copyBtn.FlatAppearance.BorderColor = gAccent;
    copyBtn.Click += (_, _) =>
    {
        System.Windows.Forms.Clipboard.SetText(sb.ToString());
        copyBtn.Text = "\u2714 Copied!";
    };

    form.Controls.Add(txt);
    form.Controls.Add(copyBtn);
    form.ShowDialog();
    form.Dispose();
}

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
