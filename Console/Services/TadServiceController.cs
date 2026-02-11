// ───────────────────────────────────────────────────────────────────────────
// TadServiceController.cs — Thin wrapper around Windows Service Control
//
// Provides async-friendly methods to query, start, stop, and restart
// both the TAD.RV kernel driver service and TadBridgeService.
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;

namespace TadConsole.Services;

/// <summary>
/// Represents the runtime state of one Windows service.
/// </summary>
public sealed class ServiceStatusInfo
{
    public string Name        { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Status      { get; init; } = "Unknown";
    public string StartType   { get; init; } = "Unknown";
    public int    Pid         { get; init; }
    public bool   Exists      { get; init; }
}

public sealed class TadServiceController
{
    public const string DriverServiceName  = "TAD.RV";
    public const string BridgeServiceName  = "TadBridgeService";

    public event Action<string>? LogMessage;

    // ─── Query ───────────────────────────────────────────────────────

    public async Task<ServiceStatusInfo> QueryServiceAsync(string serviceName)
    {
        try
        {
            var (exitCode, output) = await RunScQueryAsync(serviceName);

            if (exitCode != 0)
                return new ServiceStatusInfo { Name = serviceName, Exists = false, Status = "Not Installed" };

            return ParseScOutput(serviceName, output);
        }
        catch (Exception ex)
        {
            Log($"Query failed for {serviceName}: {ex.Message}");
            return new ServiceStatusInfo { Name = serviceName, Exists = false, Status = "Error" };
        }
    }

    public async Task<(ServiceStatusInfo Driver, ServiceStatusInfo Bridge)> QueryAllAsync()
    {
        var driverTask  = QueryServiceAsync(DriverServiceName);
        var bridgeTask  = QueryServiceAsync(BridgeServiceName);

        await Task.WhenAll(driverTask, bridgeTask);

        return (driverTask.Result, bridgeTask.Result);
    }

    // ─── Control ─────────────────────────────────────────────────────

    public async Task StartServiceAsync(string serviceName)
    {
        Log($"Starting {serviceName}…");
        await RunScCommandAsync("start", serviceName);
        Log($"{serviceName} started");
    }

    public async Task StopServiceAsync(string serviceName)
    {
        Log($"Stopping {serviceName}…");
        await RunScCommandAsync("stop", serviceName);
        Log($"{serviceName} stopped");
    }

    public async Task RestartServiceAsync(string serviceName)
    {
        await StopServiceAsync(serviceName);
        await Task.Delay(2000);
        await StartServiceAsync(serviceName);
    }

    // ─── Internal Helpers ────────────────────────────────────────────

    private static async Task<(int ExitCode, string Output)> RunScQueryAsync(string serviceName)
    {
        var psi = new ProcessStartInfo
        {
            FileName  = "sc.exe",
            Arguments = $"query \"{serviceName}\"",
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Cannot start sc.exe");

        string output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return (proc.ExitCode, output);
    }

    private static async Task RunScCommandAsync(string verb, string serviceName)
    {
        var psi = new ProcessStartInfo
        {
            FileName  = "sc.exe",
            Arguments = $"{verb} \"{serviceName}\"",
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Cannot start sc.exe");

        await proc.WaitForExitAsync();
    }

    private static ServiceStatusInfo ParseScOutput(string serviceName, string output)
    {
        string status      = "Unknown";
        string displayName = serviceName;
        int    pid         = 0;

        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                // STATE : 4  RUNNING
                int colonPos = trimmed.IndexOf(':');
                if (colonPos >= 0)
                {
                    string rest = trimmed[(colonPos + 1)..].Trim();
                    // Extract the text part (e.g. "RUNNING" from "4  RUNNING")
                    string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    status = parts.Length >= 2 ? parts[1] : rest;
                }
            }
            else if (trimmed.StartsWith("DISPLAY_NAME", StringComparison.OrdinalIgnoreCase))
            {
                int colonPos = trimmed.IndexOf(':');
                if (colonPos >= 0)
                    displayName = trimmed[(colonPos + 1)..].Trim();
            }
            else if (trimmed.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
            {
                int colonPos = trimmed.IndexOf(':');
                if (colonPos >= 0)
                    int.TryParse(trimmed[(colonPos + 1)..].Trim(), out pid);
            }
        }

        return new ServiceStatusInfo
        {
            Name        = serviceName,
            DisplayName = displayName,
            Status      = status,
            Pid         = pid,
            Exists      = true,
        };
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);
}
