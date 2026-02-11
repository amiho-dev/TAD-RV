// ───────────────────────────────────────────────────────────────────────────
// Program.cs — Entry point for TadBridgeService
//
// Configures the .NET Generic Host as a Windows Service.
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
builder.Services.AddSingleton<DriverBridge>();
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
