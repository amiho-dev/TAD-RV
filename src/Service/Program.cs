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

        // Context menu
        var titleItem = new System.Windows.Forms.ToolStripMenuItem("TAD.RV Client") { Enabled = false };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(titleItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Forms.Application.ExitThread());
        menu.Opening += (_, _) =>
        {
            string s = GetServiceStatus();
            titleItem.Text = $"TAD.RV Client \u2014 {s}";
        };

        trayIcon.ContextMenuStrip = menu;
        trayIcon.Visible = true;
        RefreshIcon();

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

[System.Runtime.InteropServices.DllImport("kernel32.dll")]
static extern bool FreeConsole();
