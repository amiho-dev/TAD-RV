// ───────────────────────────────────────────────────────────────────────────
// DeploymentService.cs — Handles user-mode service deployment operations
//
// Encapsulates deployment steps:
//   • Copy published service binaries to install dir
//   • Register and start the user-mode Windows service (LocalSystem)
//   • Configure service hardening/recovery settings
//   • Configure registry keys
//   • Verify NETLOGON policy share
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TADDomainController.Services;

/// <summary>
/// Deployment step result for live UI feedback.
/// </summary>
public sealed class DeploymentStepResult
{
    public string   StepName { get; init; } = "";
    public bool     Success  { get; init; }
    public string   Message  { get; init; } = "";
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Complete deployment configuration.
/// </summary>
public sealed class DeploymentConfig
{
    public string ServicePath      { get; set; } = "";
    public string TargetDir        { get; set; } = @"C:\Program Files\TAD";
    public string DomainController { get; set; } = "dc01.school.local";
    public bool   InstallService   { get; set; } = true;
    public bool   BlockUsbStorageForStudents { get; set; }
    public bool   PushClientUpdateAfterDeploy { get; set; }
}

public sealed class DeploymentService
{
    private const string ServiceFolderName = "Service";
    private const string RollbackFolderName = "Service.rollback";

    public event Action<DeploymentStepResult>? StepCompleted;
    public event Action<string>? LogMessage;

    // ─── Full Deployment Pipeline ────────────────────────────────────

    public async Task<List<DeploymentStepResult>> DeployAsync(
        DeploymentConfig config,
        IProgress<int> progress,
        CancellationToken ct)
    {
        var results = new List<DeploymentStepResult>();
        int totalSteps = 0;
        if (config.InstallService) totalSteps += 2;
        totalSteps += 2; // registry + policy check
        int currentStep = 0;

        void ReportProgress() => progress.Report((int)((double)++currentStep / totalSteps * 100));

        // ── 1. Stop existing services ────────────────────────────────
        Log("Stopping existing services…");
        await StopServiceAsync("TADBridgeService");

        // ── 2. Create target directory ───────────────────────────────
        Log($"Ensuring target directory: {config.TargetDir}");
        Directory.CreateDirectory(config.TargetDir);

        // ── 3. Deploy user-mode service ──────────────────────────────
        if (config.InstallService)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RunStepAsync("Copy Service Binaries", async () =>
            {
                if (!Directory.Exists(config.ServicePath))
                    throw new DirectoryNotFoundException($"Service publish path not found: {config.ServicePath}");

                string svcDir = Path.Combine(config.TargetDir, ServiceFolderName);
                Directory.CreateDirectory(svcDir);

                CreateServiceBackup(config.TargetDir);

                CopyDirectory(config.ServicePath, svcDir);
                Log($"  Copied service binaries → {svcDir}");

                string svcExe = Path.Combine(svcDir, "TADBridgeService.exe");
                if (!File.Exists(svcExe))
                    throw new FileNotFoundException("TADBridgeService.exe not found in publish output!");

                await Task.CompletedTask;
            }));
            ReportProgress();

            ct.ThrowIfCancellationRequested();
            results.Add(await RunStepAsync("Register Bridge Service", async () =>
            {
                await EnsureBridgeServiceRegisteredAndRunningAsync(config.TargetDir);
            }));
            ReportProgress();
        }

        // ── 4. Registry configuration ────────────────────────────────
        ct.ThrowIfCancellationRequested();
        results.Add(await RunStepAsync("Configure Registry", async () =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\TAD_RV", writable: true);

            key.SetValue("InstallDir",       config.TargetDir,        RegistryValueKind.String);
            key.SetValue("DomainController",  config.DomainController, RegistryValueKind.String);
            key.SetValue("DeployedAt",        DateTime.Now.ToString("o"), RegistryValueKind.String);

            Log($"  Registry configured at HKLM\\SOFTWARE\\TAD_RV");
            await Task.CompletedTask;
        }));
        ReportProgress();

        // ── 5. NETLOGON policy check ─────────────────────────────────
        ct.ThrowIfCancellationRequested();
        results.Add(await RunStepAsync("Verify NETLOGON Policy", async () =>
        {
            string policyPath = $@"\\{config.DomainController}\NETLOGON\TAD\Policy.json";

            if (File.Exists(policyPath))
            {
                Log($"  Policy.json found at {policyPath}");
            }
            else
            {
                Log($"  WARNING: Policy.json NOT found at {policyPath}");
                Log("  The service will use default policy until the file is created.");
            }

            await Task.CompletedTask;
        }));
        ReportProgress();

        // ── 6. Optional domain-wide USB storage policy flag ──────────
        if (config.BlockUsbStorageForStudents)
        {
            results.Add(await RunStepAsync("Set USB Storage Policy", async () =>
            {
                await SetUsbBlockPolicyAsync(true);
                Log("  USB storage block policy flag enabled (domain scope).\n  Clients enforce on next policy sync.");
            }));
        }

        // ── 7. Optional client force-update queue flag ───────────────
        if (config.PushClientUpdateAfterDeploy)
        {
            results.Add(await RunStepAsync("Queue Client Force Update", async () =>
            {
                await QueueClientForceUpdateAsync();
                Log("  Force-update queue flag written. Connected clients will apply updates without user consent.");
            }));
        }

        return results;
    }

    public async Task RollbackLastDeploymentAsync(string targetDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new ArgumentException("Target directory is required for rollback.", nameof(targetDir));

        string serviceDir = Path.Combine(targetDir, ServiceFolderName);
        string rollbackDir = Path.Combine(targetDir, RollbackFolderName);

        if (!Directory.Exists(rollbackDir))
            throw new DirectoryNotFoundException(
                $"Rollback backup not found at {rollbackDir}. Run a deployment first to create a backup.");

        Log("Starting rollback pipeline...");
        ct.ThrowIfCancellationRequested();

        await StopServiceAsync("TADBridgeService");

        if (Directory.Exists(serviceDir))
        {
            Log("  Removing current service binaries...");
            Directory.Delete(serviceDir, recursive: true);
        }

        Log("  Restoring previous service binaries from rollback backup...");
        CopyDirectory(rollbackDir, serviceDir);

        ct.ThrowIfCancellationRequested();
        await EnsureBridgeServiceRegisteredAndRunningAsync(targetDir);

        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TAD_RV", writable: true);
        key?.SetValue("RolledBackAt", DateTime.Now.ToString("o"), RegistryValueKind.String);
        key?.SetValue("DeployedAt", DateTime.Now.ToString("o"), RegistryValueKind.String);

        Log("Rollback completed — previous service version is now active.");
    }

    public Task SetUsbBlockPolicyAsync(bool enabled)
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TAD_RV\Policy", writable: true)
            ?? throw new InvalidOperationException("Cannot open policy registry key");
        key.SetValue("BlockUsbStorage", enabled ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("BlockUsbStorageSetAt", DateTime.UtcNow.ToString("o"), RegistryValueKind.String);
        return Task.CompletedTask;
    }

    public Task QueueClientForceUpdateAsync()
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TAD_RV\Policy", writable: true)
            ?? throw new InvalidOperationException("Cannot open policy registry key");
        key.SetValue("ForceUpdateQueued", 1, RegistryValueKind.DWord);
        key.SetValue("ForceUpdateQueuedAt", DateTime.UtcNow.ToString("o"), RegistryValueKind.String);
        return Task.CompletedTask;
    }

    public List<string> CollectOperationalLogs(int maxEntries = 150)
    {
        var logs = new List<string>();

        try
        {
            using var sec = new EventLog("Security");
            foreach (EventLogEntry entry in sec.Entries.Cast<EventLogEntry>().Reverse())
            {
                // 4624 = logon, 4634 = logoff, 4647 = user initiated logoff
                if (entry.InstanceId is 4624 or 4634 or 4647)
                {
                    logs.Add($"[{entry.TimeGenerated:yyyy-MM-dd HH:mm:ss}] User session event {entry.InstanceId}: {entry.Message.Split('\n').FirstOrDefault()?.Trim()}");
                }
                if (logs.Count >= maxEntries) break;
            }
        }
        catch (Exception ex)
        {
            logs.Add("[WARN] Could not read Security log: " + ex.Message);
        }

        try
        {
            using var app = new EventLog("Application");
            foreach (EventLogEntry entry in app.Entries.Cast<EventLogEntry>().Reverse())
            {
                if (entry.Message.Contains("print", StringComparison.OrdinalIgnoreCase) ||
                    entry.Source.Contains("Print", StringComparison.OrdinalIgnoreCase) ||
                    entry.Source.Contains("TAD", StringComparison.OrdinalIgnoreCase))
                {
                    logs.Add($"[{entry.TimeGenerated:yyyy-MM-dd HH:mm:ss}] {entry.Source}: {entry.Message.Split('\n').FirstOrDefault()?.Trim()}");
                }
                if (logs.Count >= maxEntries) break;
            }
        }
        catch (Exception ex)
        {
            logs.Add("[WARN] Could not read Application log: " + ex.Message);
        }

        return logs;
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private async Task<DeploymentStepResult> RunStepAsync(string name, Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            Log($"[*] {name}…");
            await action();
            sw.Stop();

            var result = new DeploymentStepResult
            {
                StepName = name,
                Success  = true,
                Message  = "OK",
                Duration = sw.Elapsed
            };
            StepCompleted?.Invoke(result);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log($"[!] {name} FAILED: {ex.Message}");

            var result = new DeploymentStepResult
            {
                StepName = name,
                Success  = false,
                Message  = ex.Message,
                Duration = sw.Elapsed
            };
            StepCompleted?.Invoke(result);
            return result;
        }
    }

    private async Task StopServiceAsync(string name)
    {
        try
        {
            await RunScExeAsync("stop", name, "");
            await Task.Delay(2000);
        }
        catch
        {
            // Service may not exist — that's fine
        }
    }

    private async Task RemoveServiceAsync(string name)
    {
        await StopServiceAsync(name);
        try
        {
            await RunScExeAsync("delete", name, "");
        }
        catch { }
    }

    private async Task EnsureBridgeServiceRegisteredAndRunningAsync(string targetDir)
    {
        await RemoveServiceAsync("TADBridgeService");

        string svcExe = Path.Combine(targetDir, ServiceFolderName, "TADBridgeService.exe");
        if (!File.Exists(svcExe))
            throw new FileNotFoundException("TADBridgeService.exe not found in target directory.", svcExe);

        await RunScExeAsync("create", "TADBridgeService",
            $"binPath=\"{svcExe}\" start=auto obj=LocalSystem DisplayName=\"TAD.RV Bridge Service\"");

        await RunScExeAsync("description", "TADBridgeService",
            "\"Runs the TAD.RV user-mode protection service with Active Directory integration.\"");

        await RunScExeAsync("sidtype", "TADBridgeService", "unrestricted");
        await RunScExeAsync("failureflag", "TADBridgeService", "1");

        await RunScExeAsync("failure", "TADBridgeService",
            "reset=86400 actions=restart/5000/restart/10000/restart/30000");

        Log("  TADBridgeService registered with auto-restart");

        await RunScExeAsync("start", "TADBridgeService", "");
        Log("  TADBridgeService started");
    }

    private void CreateServiceBackup(string targetDir)
    {
        string serviceDir = Path.Combine(targetDir, ServiceFolderName);
        string rollbackDir = Path.Combine(targetDir, RollbackFolderName);

        if (!Directory.Exists(serviceDir))
            return;

        if (!Directory.EnumerateFileSystemEntries(serviceDir).Any())
            return;

        if (Directory.Exists(rollbackDir))
            Directory.Delete(rollbackDir, recursive: true);

        CopyDirectory(serviceDir, rollbackDir);
        Log($"  Rollback backup updated → {rollbackDir}");
    }

    private static async Task RunScExeAsync(string verb, string serviceName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName  = "sc.exe",
            Arguments = $"{verb} \"{serviceName}\" {args}".Trim(),
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start sc.exe");

        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0 &&
            verb is not "stop" and not "delete") // stop/delete may fail if not running
        {
            string stderr = await proc.StandardError.ReadToEndAsync();
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            throw new InvalidOperationException(
                $"sc.exe {verb} failed (exit {proc.ExitCode}): {stdout} {stderr}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, dest);
        }
    }

    private void Log(string message) => LogMessage?.Invoke(message);
}
