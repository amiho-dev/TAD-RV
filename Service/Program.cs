// ───────────────────────────────────────────────────────────────────────────
// Program.cs — Entry point for TadBridgeService
//
// Configures the .NET Generic Host as a Windows Service.
// Pass --emulate or --demo to run without a kernel driver or domain
// controller — uses EmulatedDriverBridge, EmulatedAdGroupWatcher, and
// EmulatedProvisioningManager plus a system-tray icon.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TadBridge.Core;
using TadBridge.Driver;
using TadBridge.ActiveDirectory;
using TadBridge.Provisioning;
using TadBridge.Cache;
using TadBridge.Capture;
using TadBridge.Networking;
using TadBridge.Tray;

// ── Detect emulation mode ────────────────────────────────────────────────
bool emulate = args.Any(a =>
    a.Equals("--emulate", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--demo",    StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/emulate",  StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/demo",     StringComparison.OrdinalIgnoreCase));

bool autoInstallDriver = args.Any(a =>
    a.Equals("--auto-install", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/auto-install",  StringComparison.OrdinalIgnoreCase));

// ── Auto-install kernel driver if requested ──────────────────────────────
if (autoInstallDriver && !emulate)
{
    using var factory = LoggerFactory.Create(b => b.AddConsole());
    var installLog = factory.CreateLogger("DriverInstaller");
    if (!DriverInstaller.EnsureInstalled(installLog))
    {
        installLog.LogError("Driver auto-install failed. Continuing anyway — " +
                            "DriverBridge.Connect() will fail at runtime if driver is needed.");
    }
}

var builder = Host.CreateApplicationBuilder(args);

// Run as a Windows Service (sc.exe / services.msc)
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "TadBridgeService";
});

// Logging
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "TadBridgeService";
    settings.LogName    = "Application";
});

if (emulate)
{
    // ── Emulation mode — no kernel driver, no domain controller ──────
    builder.Services.AddSingleton<DriverBridge>(sp =>
        new EmulatedDriverBridge(sp.GetRequiredService<ILogger<DriverBridge>>()));

    builder.Services.AddSingleton<OfflineCacheManager>();

    builder.Services.AddSingleton<ProvisioningManager>(sp =>
        new EmulatedProvisioningManager(sp.GetRequiredService<ILogger<ProvisioningManager>>()));

    builder.Services.AddSingleton<AdGroupWatcher>(sp =>
        new EmulatedAdGroupWatcher(
            sp.GetRequiredService<ILogger<AdGroupWatcher>>(),
            sp.GetRequiredService<OfflineCacheManager>()));

    // Tray icon so the user sees the emulated service is running
    builder.Services.AddHostedService<TrayIconManager>();
}
else
{
    // ── Production mode — real driver + AD ───────────────────────────
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
builder.Services.AddHostedService<TadBridgeWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<AlertReaderWorker>();
builder.Services.AddHostedService<TadTcpListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MulticastDiscovery>());
builder.Services.AddHostedService<UpdateWorker>();

var host = builder.Build();

if (emulate)
{
    var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
    log.LogWarning("╔══════════════════════════════════════════════════════════╗");
    log.LogWarning("║  TAD.RV Bridge Service — EMULATION MODE                 ║");
    log.LogWarning("║  No kernel driver · No domain controller required       ║");
    log.LogWarning("╚══════════════════════════════════════════════════════════╝");
}

host.Run();
