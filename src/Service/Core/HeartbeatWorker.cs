// ───────────────────────────────────────────────────────────────────────────
// HeartbeatWorker.cs — Bidirectional Watchdog (User → Kernel)
//
// Sends IOCTL_TAD_HEARTBEAT every 2 seconds.  If the driver stops
// receiving heartbeats (because this service was killed), the driver's
// built-in DPC timer fires and triggers the WFP network killswitch.
//
// The returned TadHeartbeatOutput is logged and can be used to detect
// driver-side anomalies.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TadBridge.Driver;
using TadBridge.Shared;

namespace TadBridge.Core;

public sealed class HeartbeatWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly ILogger<HeartbeatWorker> _log;
    private readonly DriverBridge             _driver;

    private int _consecutiveFailures;

    public HeartbeatWorker(
        ILogger<HeartbeatWorker> logger,
        DriverBridge             driver)
    {
        _log    = logger;
        _driver = driver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("HeartbeatWorker started (interval={Interval})", Interval);

        // Wait for the main worker to connect first
        try
        {
            while (!_driver.IsConnected && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(500, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("HeartbeatWorker stopped (cancelled while waiting for connection)");
            return;
        }

        using var timer = new PeriodicTimer(Interval);

        try
        {
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                TadHeartbeatOutput? hb = _driver.Heartbeat();

                if (hb.HasValue)
                {
                    _consecutiveFailures = 0;

                    // Log at Trace level to avoid spamming
                    _log.LogTrace(
                        "Heartbeat OK — PID={Pid}, ObCB={Ob}, Flt={Flt}, Role={Role}",
                        hb.Value.ProtectedPid,
                        hb.Value.ProcessProtectionActive != 0,
                        hb.Value.FileProtectionActive != 0,
                        (TadUserRole)hb.Value.CurrentUserRole);
                }
                else
                {
                    _consecutiveFailures++;
                    _log.LogWarning("Heartbeat returned null (failure #{Count})",
                        _consecutiveFailures);
                }

                // If the driver consistently fails, attempt reconnect
                if (_consecutiveFailures >= 5)
                {
                    _log.LogError("5 consecutive heartbeat failures — reconnecting…");
                    _driver.Disconnect();
                    try { await Task.Delay(2000, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    _driver.Connect();
                    _driver.ProtectPid((uint)Environment.ProcessId);
                    _consecutiveFailures = 0;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Heartbeat exception");
                _consecutiveFailures++;
            }
        }
        }
        catch (OperationCanceledException) { /* host shutting down */ }

        _log.LogInformation("HeartbeatWorker stopped");
    }
}
