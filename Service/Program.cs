// ───────────────────────────────────────────────────────────────────────────
// Program.cs — Entry point for TadBridgeService
//
// Configures the .NET Generic Host as a Windows Service.
// Pass --emulate to run without a kernel driver (mock mode for testing).
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

bool emulate = args.Any(a => a.Equals("--emulate", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--demo", StringComparison.OrdinalIgnoreCase));

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

// Core services (DI registration)
if (emulate)
{
    // Emulated driver — no kernel driver required
    builder.Services.AddSingleton<DriverBridge>(sp =>
        new EmulatedDriverBridge(sp.GetRequiredService<ILogger<DriverBridge>>()));
    Console.WriteLine("[TAD.RV] *** EMULATION MODE — no kernel driver required ***");

    // Stop the real service if it's running so we can bind the port
    try
    {
        using var sc = new System.ServiceProcess.ServiceController("TadBridgeService");
        if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
        {
            Console.WriteLine("[TAD.RV] Stopping existing TadBridgeService...");
            sc.Stop();
            sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            Console.WriteLine("[TAD.RV] Existing service stopped.");
        }
    }
    catch { /* Service may not be installed — that's fine */ }
}
else
{
    builder.Services.AddSingleton<DriverBridge>();
}

builder.Services.AddSingleton<ProvisioningManager>();
builder.Services.AddSingleton<AdGroupWatcher>();
builder.Services.AddSingleton<OfflineCacheManager>();

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

var host = builder.Build();
host.Run();
