// ───────────────────────────────────────────────────────────────────────────
// AlertReaderWorker.cs — Kernel → User notification reader
//
// Long-polls the driver via IOCTL_TAD_READ_ALERT.  When the driver detects
// an unauthorised attempt to stop the service (ObCallback / HandleStrip),
// a forced unlock, or a file tamper, it completes the pending IRP with
// a TadAlertOutput, which this worker then logs and forwards to the
// school's admin alert infrastructure.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TadBridge.Driver;
using TadBridge.Shared;

namespace TadBridge.Core;

public sealed class AlertReaderWorker : BackgroundService
{
    private readonly ILogger<AlertReaderWorker> _log;
    private readonly DriverBridge               _driver;

    public AlertReaderWorker(
        ILogger<AlertReaderWorker> logger,
        DriverBridge               driver)
    {
        _log    = logger;
        _driver = driver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("AlertReaderWorker started");

        // Wait for the main worker to establish driver connection
        while (!_driver.IsConnected && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // This blocks until the driver has an alert to report.
                // In production, use overlapped I/O with a cancellation.
                TadAlertOutput? alert = await Task.Run(
                    () => _driver.ReadAlert(),
                    stoppingToken);

                if (alert.HasValue && alert.Value.AlertType != (uint)TadAlertType.None)
                {
                    HandleAlert(alert.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Alert reader exception — retrying in 5s");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _log.LogInformation("AlertReaderWorker stopped");
    }

    private void HandleAlert(TadAlertOutput alert)
    {
        var type = (TadAlertType)alert.AlertType;

        switch (type)
        {
            case TadAlertType.ServiceTamper:
                _log.LogCritical(
                    "SECURITY: Process PID {Pid} attempted to kill TadBridgeService! Detail: {Detail}",
                    alert.SourcePid, alert.Detail);
                WriteAdminAlertToEventLog(alert);
                break;

            case TadAlertType.HeartbeatLost:
                _log.LogCritical(
                    "SECURITY: Driver lost heartbeat — network killswitch engaged. Detail: {Detail}",
                    alert.Detail);
                WriteAdminAlertToEventLog(alert);
                break;

            case TadAlertType.UnlockBruteForce:
                _log.LogWarning(
                    "SECURITY: Unlock brute-force lockout triggered by PID {Pid}. Detail: {Detail}",
                    alert.SourcePid, alert.Detail);
                WriteAdminAlertToEventLog(alert);
                break;

            case TadAlertType.FileTamper:
                _log.LogWarning(
                    "SECURITY: File tamper blocked for PID {Pid}. Detail: {Detail}",
                    alert.SourcePid, alert.Detail);
                break;

            default:
                _log.LogInformation("Driver alert type={Type}: {Detail}", type, alert.Detail);
                break;
        }
    }

    private void WriteAdminAlertToEventLog(TadAlertOutput alert)
    {
        // In production, this would also:
        //   - Send an email / Teams webhook to the school IT admin
        //   - Write to a central syslog / SIEM endpoint
        //   - Store in HKLM\SOFTWARE\TAD_RV\Alerts for the management console
        try
        {
            using var eventLog = new System.Diagnostics.EventLog("Application");
            eventLog.Source = "TadBridgeService";
            eventLog.WriteEntry(
                $"[TAD.RV ALERT] Type={alert.AlertType}, PID={alert.SourcePid}, " +
                $"Detail={alert.Detail}",
                System.Diagnostics.EventLogEntryType.Error,
                9001);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write to Windows Event Log");
        }
    }
}
