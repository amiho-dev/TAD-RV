// ───────────────────────────────────────────────────────────────────────────
// Program.cs — Entry point for TadBridgeService
//
// Configures the .NET Generic Host as a Windows Service.
// Default runtime is user-mode only (no kernel driver dependency).
// Use --kernel for legacy kernel-driver communication mode.
// Use --demo to enable synthetic alert generation for showcases.
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

bool legacyKernelMode = args.Any(a =>
    a.Equals("--kernel", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--legacy-kernel", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/kernel", StringComparison.OrdinalIgnoreCase));

bool demoMode = args.Any(a =>
    a.Equals("--demo", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/demo", StringComparison.OrdinalIgnoreCase));

bool userMode = !legacyKernelMode;

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
builder.Services.AddHostedService<TadBridgeWorker>();
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
    log.LogWarning("Running in LEGACY KERNEL mode (--kernel)");
}

host.Run();
