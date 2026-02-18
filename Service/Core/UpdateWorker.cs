// ───────────────────────────────────────────────────────────────────────────
// UpdateWorker.cs — Background update checker for TadBridgeService
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Periodically checks GitHub Releases for new versions.
// When an update is found:
//   1. Logs the availability
//   2. Downloads the asset to a staging directory
//   3. Applies the update (swap binaries)
//   4. Signals the host to restart
//
// Check interval: every 6 hours (configurable via registry).
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TadBridge.Shared;

namespace TadBridge.Core;

public sealed class UpdateWorker : BackgroundService
{
    private readonly ILogger<UpdateWorker> _log;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly UpdateManager _updater;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan InitialDelay  = TimeSpan.FromMinutes(2);

    public UpdateWorker(
        ILogger<UpdateWorker> log,
        IHostApplicationLifetime lifetime)
    {
        _log = log;
        _lifetime = lifetime;
        _updater = new UpdateManager("service")
        {
            MinCheckInterval = CheckInterval
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("UpdateWorker started (check every {Interval})", CheckInterval);

        // Wait a bit after startup before first check
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var update = await _updater.CheckForUpdateAsync(stoppingToken);

                if (update != null)
                {
                    _log.LogInformation(
                        "Update available: v{Current} → v{New} ({Title})",
                        _updater.CurrentVersion, update.Version, update.Title);

                    // Auto-download
                    if (!string.IsNullOrEmpty(update.DownloadUrl))
                    {
                        _log.LogInformation("Downloading update asset: {Asset}", update.AssetName);

                        string? downloadPath = await _updater.DownloadUpdateAsync(
                            update, ct: stoppingToken);

                        if (downloadPath != null)
                        {
                            _log.LogInformation("Download complete: {Path}", downloadPath);

                            // Apply update (extract alongside running binaries)
                            bool applied = UpdateManager.ApplyUpdate(downloadPath);

                            if (applied)
                            {
                                _log.LogWarning(
                                    "Update v{Version} applied successfully — requesting service restart",
                                    update.Version);

                                // Write to event log for admin visibility
                                WriteUpdateEventLog(update);

                                // Request graceful restart (service recovery will restart us)
                                _lifetime.StopApplication();
                                return;
                            }
                            else
                            {
                                _log.LogError("Failed to apply update v{Version}", update.Version);
                            }
                        }
                        else
                        {
                            _log.LogWarning("Failed to download update asset");
                        }
                    }
                }
                else
                {
                    _log.LogDebug("No updates available (current: v{Version})", _updater.CurrentVersion);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Update check failed");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _updater.Dispose();
        _log.LogInformation("UpdateWorker stopped");
    }

    private void WriteUpdateEventLog(UpdateInfo update)
    {
        try
        {
            using var eventLog = new System.Diagnostics.EventLog("Application");
            eventLog.Source = "TadBridgeService";
            eventLog.WriteEntry(
                $"[TAD.RV UPDATE] Version {update.Version} applied. " +
                $"Service will restart to complete the update.\n\n" +
                $"Release: {update.Title}\n" +
                $"Notes: {update.ReleaseNotes?[..Math.Min(update.ReleaseNotes.Length, 500)]}",
                System.Diagnostics.EventLogEntryType.Information,
                9002);
        }
        catch { /* Best effort */ }
    }
}
