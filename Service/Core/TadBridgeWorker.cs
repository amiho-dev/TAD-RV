// ───────────────────────────────────────────────────────────────────────────
// TadBridgeWorker.cs — Primary background service worker
//
// Orchestrates the startup sequence:
//   1. Connect to kernel driver
//   2. Register our PID for protection
//   3. Run first-boot provisioning (AD OU + Policy.json)
//   4. Start the AD group watcher for interactive logons
//   5. Push resolved policy to the driver
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TadBridge.Driver;
using TadBridge.ActiveDirectory;
using TadBridge.Provisioning;
using TadBridge.Shared;
using System.Diagnostics;

namespace TadBridge.Core;

public sealed class TadBridgeWorker : BackgroundService
{
    private readonly ILogger<TadBridgeWorker> _log;
    private readonly DriverBridge             _driver;
    private readonly ProvisioningManager      _provisioning;
    private readonly AdGroupWatcher           _adWatcher;

    private TadUserRole _lastPushedRole = TadUserRole.Unknown;
    private string      _lastPushedSid  = string.Empty;

    public TadBridgeWorker(
        ILogger<TadBridgeWorker> logger,
        DriverBridge             driver,
        ProvisioningManager      provisioning,
        AdGroupWatcher           adWatcher)
    {
        _log          = logger;
        _driver       = driver;
        _provisioning = provisioning;
        _adWatcher    = adWatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("TadBridgeWorker starting…");

        // ── Step 1: Connect to the kernel driver ─────────────────────
        try
        {
            _driver.Connect();
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Cannot connect to TAD.RV driver — aborting");
            return;
        }

        // ── Step 2: Register our PID for process protection ──────────
        uint myPid = (uint)Environment.ProcessId;
        try
        {
            _driver.ProtectPid(myPid);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to register PID {Pid} for protection", myPid);
        }

        // ── Step 3: First-boot provisioning ──────────────────────────
        try
        {
            var policy = await _provisioning.EnsureProvisionedAsync(stoppingToken);
            if (policy != null)
            {
                _driver.SetPolicy(policy.Value);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Provisioning failed");
        }

        // ── Step 4: Monitor interactive logons ───────────────────────
        _log.LogInformation("Starting AD group watcher loop…");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (role, sessionId, sid) = _adWatcher.ResolveCurrentUser();

                if (role != TadUserRole.Unknown && (role != _lastPushedRole || sid != _lastPushedSid))
                {
                    _driver.SetUserRole(role, sessionId, sid);
                    _lastPushedRole = role;
                    _lastPushedSid  = sid;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AD group watcher iteration failed");
            }

            // Poll every 10 seconds for session changes
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("TadBridgeWorker stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("TadBridgeWorker shutting down — unlocking driver…");

        try
        {
            _driver.Unlock();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to unlock driver on shutdown");
        }

        _driver.Disconnect();

        await base.StopAsync(cancellationToken);
    }
}
