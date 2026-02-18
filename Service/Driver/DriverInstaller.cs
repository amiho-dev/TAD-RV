// ───────────────────────────────────────────────────────────────────────────
// DriverInstaller.cs — Simplified kernel driver injection
//
// (C) 2026 TAD Europe — https://tad-it.eu
// TAD.RV — The Greater Brother of the mighty te.comp NET.FX
//
// Provides a one-call API to install, start, stop, and uninstall the
// TAD.RV kernel driver without requiring external scripts.
//
// Preferred method: pnputil with the INF file.
// Fallback method:  sc.exe create/start (no minifilter registration).
//
// Usage from Program.cs:
//   if (DriverInstaller.EnsureInstalled(logger))
//       logger.LogInformation("Driver ready");
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace TadBridge.Driver;

/// <summary>
/// Simplified, one-call kernel driver installation and lifecycle management.
/// All methods are static — no DI required.
/// </summary>
public static class DriverInstaller
{
    private const string DriverName  = "TadRv";
    private const string ServiceName = "TAD.RV";
    private const string SysFileName = "TAD.RV.sys";
    private const string InfFileName = "TAD_RV.inf";

    // ─── High-Level API ─────────────────────────────────────────────

    /// <summary>
    /// Ensures the driver is installed and running.
    /// Returns true if the driver is ready; false if installation failed.
    /// </summary>
    public static bool EnsureInstalled(ILogger? log = null)
    {
        if (IsDriverRunning())
        {
            log?.LogInformation("[DriverInstaller] TAD.RV driver is already running.");
            return true;
        }

        if (IsDriverInstalled())
        {
            log?.LogInformation("[DriverInstaller] Driver installed but not running — starting ...");
            return StartDriver(log);
        }

        // Not installed — try to install
        log?.LogInformation("[DriverInstaller] Driver not found — installing ...");

        // Strategy 1: pnputil + INF (preferred — full minifilter registration)
        string? infPath = LocateFile(InfFileName);
        string? sysPath = LocateFile(SysFileName);

        if (infPath != null)
        {
            log?.LogInformation("[DriverInstaller] Found INF at {Path} — using pnputil", infPath);
            if (InstallViaInf(infPath, log))
                return StartDriver(log);
        }

        // Strategy 2: sc.exe create (fallback — manual load)
        if (sysPath != null)
        {
            log?.LogInformation("[DriverInstaller] Found SYS at {Path} — using sc.exe", sysPath);
            if (InstallViaSc(sysPath, log))
                return StartDriver(log);
        }

        log?.LogError("[DriverInstaller] Could not locate driver files. " +
                      "Place {Inf} and {Sys} next to the service executable or in the Kernel subfolder.",
                      InfFileName, SysFileName);
        return false;
    }

    /// <summary>
    /// Uninstalls the driver completely.
    /// </summary>
    public static bool Uninstall(ILogger? log = null)
    {
        StopDriver(log);

        // Try pnputil removal first
        if (RunProcess("pnputil", $"/delete-driver {InfFileName} /uninstall /force", log) == 0)
        {
            log?.LogInformation("[DriverInstaller] Driver removed via pnputil.");
            return true;
        }

        // Fallback: sc delete
        if (RunProcess("sc.exe", $"delete \"{ServiceName}\"", log) == 0)
        {
            log?.LogInformation("[DriverInstaller] Driver service deleted via sc.exe.");
            return true;
        }

        return false;
    }

    // ─── Query Methods ──────────────────────────────────────────────

    /// <summary>Checks if the driver service is registered (installed).</summary>
    public static bool IsDriverInstalled()
    {
        return RunProcess("sc.exe", $"query \"{ServiceName}\"", null) == 0;
    }

    /// <summary>Checks if the driver is currently running.</summary>
    public static bool IsDriverRunning()
    {
        // sc query returns 0 if the service exists; we parse output for RUNNING
        var (exitCode, output) = RunProcessWithOutput("sc.exe", $"query \"{ServiceName}\"");
        return exitCode == 0 && output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    // ─── Installation Strategies ────────────────────────────────────

    /// <summary>
    /// Install via pnputil — the preferred method.
    /// Handles INF parsing, catalog verification, and minifilter registration.
    /// </summary>
    private static bool InstallViaInf(string infPath, ILogger? log)
    {
        int rc = RunProcess("pnputil", $"/add-driver \"{infPath}\" /install", log);
        if (rc != 0)
        {
            log?.LogWarning("[DriverInstaller] pnputil returned {Code}, falling back to sc.exe", rc);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Install via sc.exe create — fallback for unsigned/test-signed drivers.
    /// Copies .sys to %SystemRoot%\system32\drivers and registers the service.
    /// </summary>
    private static bool InstallViaSc(string sysPath, ILogger? log)
    {
        try
        {
            string driverDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "system32", "drivers");
            string destPath = Path.Combine(driverDir, SysFileName);

            // Copy driver binary
            File.Copy(sysPath, destPath, overwrite: true);
            log?.LogInformation("[DriverInstaller] Copied {Sys} → {Dest}", sysPath, destPath);

            // Register as kernel service
            int rc = RunProcess("sc.exe",
                $"create \"{ServiceName}\" type=kernel binPath=\"{destPath}\" start=demand",
                log);

            return rc == 0;
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[DriverInstaller] sc.exe install failed");
            return false;
        }
    }

    // ─── Start / Stop ───────────────────────────────────────────────

    public static bool StartDriver(ILogger? log)
    {
        int rc = RunProcess("sc.exe", $"start \"{ServiceName}\"", log);
        if (rc == 0)
        {
            log?.LogInformation("[DriverInstaller] Driver started successfully.");
            return true;
        }

        // 1056 = service already running
        if (rc == 1056) return true;

        log?.LogError("[DriverInstaller] Failed to start driver (sc.exe returned {Code})", rc);
        return false;
    }

    public static bool StopDriver(ILogger? log)
    {
        int rc = RunProcess("sc.exe", $"stop \"{ServiceName}\"", log);
        return rc == 0 || rc == 1062; // 1062 = not started
    }

    // ─── File Location ──────────────────────────────────────────────

    /// <summary>
    /// Searches for a driver file in common locations:
    ///   1) Same directory as the running executable
    ///   2) Kernel/ subfolder
    ///   3) Parent/Kernel/ folder
    /// </summary>
    private static string? LocateFile(string fileName)
    {
        string baseDir = AppContext.BaseDirectory;

        string[] candidates =
        [
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "Kernel", fileName),
            Path.Combine(baseDir, "..", "Kernel", fileName),
            Path.Combine(baseDir, "..", "..", "Kernel", fileName),
        ];

        foreach (string path in candidates)
        {
            string full = Path.GetFullPath(path);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    // ─── Process Helpers ────────────────────────────────────────────

    private static int RunProcess(string exe, string arguments, ILogger? log)
    {
        var (exitCode, output) = RunProcessWithOutput(exe, arguments);
        if (!string.IsNullOrEmpty(output))
            log?.LogDebug("[DriverInstaller] {Exe} {Args} → {Output}", exe, arguments, output.Trim());
        return exitCode;
    }

    private static (int exitCode, string output) RunProcessWithOutput(string exe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = arguments,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (-1, "Failed to start process");

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(TimeSpan.FromSeconds(30));

            return (proc.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
