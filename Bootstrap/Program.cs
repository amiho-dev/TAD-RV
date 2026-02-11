// ───────────────────────────────────────────────────────────────────────────
// Program.cs — TAD.RV Zero-Install Bootstrap Loader
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// ZERO-INSTALL deployment for non-domain environments:
//   1. Launched via GPO Startup Script from \\Server\TAD\TadBootstrap.exe
//   2. Copies TadBridgeService.exe + TAD_RV.sys to hidden local cache
//   3. Registers + starts a SYSTEM service with auto-recovery
//   4. Installs the kernel driver via sc.exe
//
// Runs as SYSTEM (GPO startup context). No reboot required.
// Supports 50-seat labs with no Domain Controller.
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;

namespace TadBootstrap;

internal static class Program
{
    // ─── Configuration ────────────────────────────────────────────────

    const string ServiceName        = "TadBridgeService";
    const string ServiceDisplayName = "TAD.RV Bridge Service";
    const string DriverName         = "TAD_RV";

    static readonly string CacheDir   = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ".tad_cache");

    static readonly string ServiceExe = Path.Combine(CacheDir, "TadBridgeService.exe");
    static readonly string DriverSys  = Path.Combine(CacheDir, "TAD_RV.sys");
    static readonly string DriverInf  = Path.Combine(CacheDir, "TAD_RV.inf");
    static readonly string VersionFile = Path.Combine(CacheDir, ".version");

    // Files to deploy from the UNC share (relative to bootstrap exe location)
    static readonly string[] DeployFiles = new[]
    {
        "TadBridgeService.exe",
        "TadBridgeService.dll",
        "TadBridgeService.deps.json",
        "TadBridgeService.runtimeconfig.json",
        "TAD_RV.sys",
        "TAD_RV.inf",
        "TAD_RV.cat",
    };

    // ─── Entry Point ──────────────────────────────────────────────────

    static int Main(string[] args)
    {
        try
        {
            Log("TAD.RV Bootstrap Loader starting...");
            Log($"Running as: {Environment.UserName}");
            Log($"Source dir: {AppContext.BaseDirectory}");
            Log($"Cache dir:  {CacheDir}");

            // Ensure we're running as SYSTEM or admin
            if (!IsElevated())
            {
                Log("ERROR: Must run as SYSTEM or Administrator (GPO startup context)");
                return 1;
            }

            // Step 1: Create hidden cache directory
            CreateCacheDirectory();

            // Step 2: Check if update is needed
            string sourceVersion = GetSourceVersion();
            string cachedVersion = GetCachedVersion();

            if (sourceVersion != cachedVersion || !File.Exists(ServiceExe))
            {
                Log($"Updating: {cachedVersion} → {sourceVersion}");

                // Stop existing service before overwriting
                StopServiceIfRunning();

                // Step 3: Copy binaries from UNC share
                CopyBinaries();

                // Write version marker
                File.WriteAllText(VersionFile, sourceVersion);
            }
            else
            {
                Log("Binaries are up to date");
            }

            // Step 4: Install kernel driver (if not already loaded)
            InstallKernelDriver();

            // Step 5: Register and start the SYSTEM service
            RegisterService();
            ConfigureServiceRecovery();
            StartService();

            Log("Bootstrap complete — TAD.RV is active");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex}");
            return 2;
        }
    }

    // ─── Step 1: Cache Directory ──────────────────────────────────────

    static void CreateCacheDirectory()
    {
        if (Directory.Exists(CacheDir))
        {
            Log("Cache directory exists");
            return;
        }

        var di = Directory.CreateDirectory(CacheDir);

        // Set hidden + system attributes
        di.Attributes |= FileAttributes.Hidden | FileAttributes.System;

        // Restrict ACL: SYSTEM + Administrators only
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var ds = new DirectorySecurity();
            ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            ds.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            ds.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            di.SetAccessControl(ds);
        }

        Log("Created hidden cache directory with restricted ACL");
    }

    // ─── Step 2: Version Check ────────────────────────────────────────

    static string GetSourceVersion()
    {
        var sourceExe = Path.Combine(AppContext.BaseDirectory, "TadBridgeService.exe");
        if (File.Exists(sourceExe))
        {
            var vi = FileVersionInfo.GetVersionInfo(sourceExe);
            return vi.FileVersion ?? "0.0.0.0";
        }
        // Fallback: hash the file
        return File.GetLastWriteTimeUtc(
            Path.Combine(AppContext.BaseDirectory, DeployFiles[0])).Ticks.ToString();
    }

    static string GetCachedVersion()
    {
        return File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "";
    }

    // ─── Step 3: Copy Binaries ────────────────────────────────────────

    static void CopyBinaries()
    {
        string srcDir = AppContext.BaseDirectory;
        int copied = 0;

        foreach (string file in DeployFiles)
        {
            string src = Path.Combine(srcDir, file);
            string dst = Path.Combine(CacheDir, file);

            if (!File.Exists(src))
            {
                Log($"  SKIP (not found): {file}");
                continue;
            }

            // Retry up to 3 times (file might be locked by running service)
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.Copy(src, dst, overwrite: true);
                    File.SetAttributes(dst, FileAttributes.Hidden | FileAttributes.System);
                    copied++;
                    break;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(1000);
                }
            }
        }

        // Also copy any .dll dependencies in the source directory
        foreach (var dll in Directory.EnumerateFiles(srcDir, "*.dll"))
        {
            string name = Path.GetFileName(dll);
            string dst = Path.Combine(CacheDir, name);
            try
            {
                File.Copy(dll, dst, overwrite: true);
                copied++;
            }
            catch { /* Best effort for companion DLLs */ }
        }

        Log($"Copied {copied} files to cache");
    }

    // ─── Step 4: Kernel Driver ────────────────────────────────────────

    static void InstallKernelDriver()
    {
        // Check if driver is already loaded
        var sc = RunProcess("sc.exe", $"query {DriverName}");
        if (sc.Contains("RUNNING"))
        {
            Log("Kernel driver already loaded");
            return;
        }

        if (!File.Exists(DriverSys))
        {
            Log("WARNING: TAD_RV.sys not found in cache — skipping driver install");
            return;
        }

        // Create driver service entry
        string sysPath = DriverSys.Replace("/", "\\");
        RunProcess("sc.exe",
            $"create {DriverName} type= kernel start= demand binPath= \"{sysPath}\" " +
            $"DisplayName= \"TAD.RV Kernel Driver\"");

        // Start the driver
        RunProcess("sc.exe", $"start {DriverName}");
        Log("Kernel driver installed and started");
    }

    // ─── Step 5: Service Registration ─────────────────────────────────

    static void RegisterService()
    {
        // Check if service already exists
        var query = RunProcess("sc.exe", $"query {ServiceName}");
        if (query.Contains(ServiceName))
        {
            // Update the binary path in case it moved
            RunProcess("sc.exe",
                $"config {ServiceName} binPath= \"{ServiceExe}\" start= auto");
            Log("Service config updated");
            return;
        }

        // Create a new auto-start service running as LocalSystem
        RunProcess("sc.exe",
            $"create {ServiceName} " +
            $"binPath= \"{ServiceExe}\" " +
            $"start= auto " +
            $"obj= LocalSystem " +
            $"DisplayName= \"{ServiceDisplayName}\"");

        // Set description
        RunProcess("sc.exe",
            $"description {ServiceName} \"TAD.RV endpoint management service — " +
            $"driver bridge, screen capture, and teacher communication.\"");

        Log("Service registered as auto-start LocalSystem");
    }

    static void ConfigureServiceRecovery()
    {
        // sc.exe failure config:
        //   1st failure: restart after 5 seconds
        //   2nd failure: restart after 10 seconds
        //   3rd failure: restart after 30 seconds
        //   Reset failure count after 86400 seconds (24 hours)
        RunProcess("sc.exe",
            $"failure {ServiceName} " +
            "reset= 86400 " +
            "actions= restart/5000/restart/10000/restart/30000");

        // Also enable pre-failure actions for crash recovery
        RunProcess("sc.exe",
            $"failureflag {ServiceName} 1");

        Log("Service recovery configured: restart on crash (5s/10s/30s)");
    }

    static void StartService()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);

            if (sc.Status == ServiceControllerStatus.Running)
            {
                Log("Service already running");
                return;
            }

            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                Log("Service started");
            }
        }
        catch (Exception ex)
        {
            // Fallback to sc.exe
            Log($"ServiceController failed ({ex.Message}), falling back to sc.exe");
            RunProcess("sc.exe", $"start {ServiceName}");
        }
    }

    static void StopServiceIfRunning()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running ||
                sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                Log("Stopped running service for update");
            }
        }
        catch { /* Service may not exist yet */ }
    }

    // ─── Utilities ────────────────────────────────────────────────────

    static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static string RunProcess(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);

            if (proc.ExitCode != 0 && !string.IsNullOrEmpty(error))
                Log($"  [{exe}] exit={proc.ExitCode}: {error.Trim()}");

            return output + error;
        }
        catch (Exception ex)
        {
            Log($"  [{exe}] failed: {ex.Message}");
            return "";
        }
    }

    static void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Console.WriteLine(line);

        try
        {
            string logPath = Path.Combine(CacheDir, "bootstrap.log");
            Directory.CreateDirectory(CacheDir);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch { /* Can't write log — continue anyway */ }
    }
}
