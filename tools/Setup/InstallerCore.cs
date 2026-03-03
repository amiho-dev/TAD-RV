// InstallerCore.cs — compile-time parameterised for Client / Admin / DC
// Build with:
//   -p:SetupTarget=Client         → installs TADBridgeService (Windows service)
//   -p:SetupTarget=Admin          → installs TADAdmin WPF app + Start Menu shortcut
//   -p:SetupTarget=DomainController → installs TADDomainController WPF app + shortcut

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace TADSetup;

// ── Per-target configuration (resolved at compile time) ──────────────────

public record InstallerConfig(
    string AppDisplayName,      // human-readable product name
    string BinaryName,          // EXE filename to extract
    string ResourceName,        // embedded resource LogicalName
    string SetupBinaryName,     // name to self-copy as (for uninstall)
    string UninstallSubKey,     // HKLM\SOFTWARE\...\Uninstall\<this>
    bool   IsService,           // true  → register Windows service
    string? ServiceName,        // service key name (null for GUI apps)
    string? ServiceDisplay,     // display name
    string? ServiceDesc,        // description
    bool   CreateShortcut       // true  → create Start Menu .lnk
);

// ── Static class ──────────────────────────────────────────────────────────

public static class InstallerCore
{
    // ── Compile-time config ─────────────────────────────────────────────

    public static readonly InstallerConfig Config =
#if SETUP_ADMIN
        new(
            AppDisplayName:   "TAD Admin",
            BinaryName:       "TADAdmin.exe",
            ResourceName:     "bundled_admin",
            SetupBinaryName:  "TADAdminSetup.exe",
            UninstallSubKey:  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD.Admin",
            IsService:        false,
            ServiceName:      null,
            ServiceDisplay:   null,
            ServiceDesc:      null,
            CreateShortcut:   true
        );
#elif SETUP_DC
        new(
            AppDisplayName:   "TAD Domain Controller",
            BinaryName:       "TADDomainController.exe",
            ResourceName:     "bundled_dc",
            SetupBinaryName:  "TADDomainControllerSetup.exe",
            UninstallSubKey:  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD.DomainController",
            IsService:        false,
            ServiceName:      null,
            ServiceDisplay:   null,
            ServiceDesc:      null,
            CreateShortcut:   true
        );
#else   // SETUP_CLIENT (default / fallback)
        new(
            AppDisplayName:   "TAD Client",
            BinaryName:       "TADBridgeService.exe",
            ResourceName:     "bundled_service",
            SetupBinaryName:  "TADClientSetup.exe",
            UninstallSubKey:  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD.Client",
            IsService:        true,
            ServiceName:      "TADBridgeService",
            ServiceDisplay:   "TAD Endpoint Service",
            ServiceDesc:      "TAD endpoint agent — remote view, screen capture and policy enforcement.",
            CreateShortcut:   false
        );
#endif

    // ── Constants ────────────────────────────────────────────────────────

    private const string VirtualAccount  = @"NT SERVICE\TADBridgeService";
    private const string ServiceRegKey   = @"SOFTWARE\TAD_RV";
    private const string StartMenuFolder = "TAD";

    // ── Helpers ──────────────────────────────────────────────────────────

    public static string AppVersion
    {
        get
        {
            string v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0";
            v = v.TrimStart('v');
            int dash = v.IndexOf('-');
            return dash > 0 ? v[..dash] : v;
        }
    }

    public static string DefaultInstallDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "TAD");

    private static string InstallBin(string installDir, string bin) =>
        Path.Combine(installDir, bin);

    private static string StartMenuDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            StartMenuFolder);

    public static bool IsInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(Config.UninstallSubKey);
        return key is not null;
    }

    // ── INSTALL ──────────────────────────────────────────────────────────

    public static bool RunInstall(
        string installDir,
        Action<string, int> report)
    {
        if (Config.IsService)
            return RunInstallService(installDir, report);
        else
            return RunInstallApp(installDir, report);
    }

    private static bool RunInstallService(string installDir, Action<string, int> report)
    {
        report("[1/5] Extracting files...", 5);
        if (!ExtractBinary(installDir, out string binPath)) return false;
        CopySelf(installDir, report);

        report("[2/5] Writing Programs & Features entry...", 25);
        WriteUninstallEntry(installDir);

        report("[3/5] Writing service registry entries...", 40);
        WriteServiceRegistry(installDir, binPath);

        report("[4/5] Registering Windows service (delayed-auto)...", 55);
        if (!RegisterService(binPath, report)) return false;

        report("[5/5] Starting service...", 85);
        StartService(report);

        report("Done.", 100);
        return true;
    }

    private static bool RunInstallApp(string installDir, Action<string, int> report)
    {
        report("[1/3] Extracting files...", 5);
        if (!ExtractBinary(installDir, out _)) return false;
        CopySelf(installDir, report);

        report("[2/3] Writing Programs & Features entry...", 50);
        WriteUninstallEntry(installDir);

        report("[3/3] Creating Start Menu shortcut...", 80);
        CreateShortcut(installDir);

        report("Done.", 100);
        return true;
    }

    // ── UNINSTALL ────────────────────────────────────────────────────────

    public static bool RunUninstall(Action<string, int> report)
    {
        string installDir = GetInstalledDir() ?? DefaultInstallDir();

        if (Config.IsService)
        {
            report("[1/4] Stopping service...", 10);
            RunCmd("sc.exe", $"stop {Config.ServiceName}");
            Thread.Sleep(2000);

            report("[2/4] Deleting service registration...", 30);
            int rc = RunCmd("sc.exe", $"delete {Config.ServiceName}");
            if (rc != 0 && rc != 1060)
                report($"  [warn] sc delete returned {rc}", 35);
        }
        else
        {
            report("[1/4] Stopping application...", 10);
            RunCmd("taskkill", $"/f /im {Config.BinaryName}");
        }

        report("[2/4] Removing Start Menu shortcuts...", 45);
        RemoveShortcuts();

        report("[3/4] Removing registry entries...", 60);
        try { Registry.LocalMachine.DeleteSubKeyTree(Config.UninstallSubKey, throwOnMissingSubKey: false); } catch { }
        if (Config.IsService)
            try { Registry.LocalMachine.DeleteSubKeyTree(ServiceRegKey, throwOnMissingSubKey: false); } catch { }

        report("[4/4] Removing files...", 75);
        RemoveInstallDir(installDir, report);

        report("Uninstall complete.", 100);
        return true;
    }

    private static string? GetInstalledDir()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Config.UninstallSubKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    // ── File extraction ──────────────────────────────────────────────────

    private static bool ExtractBinary(string installDir, out string binPath)
    {
        binPath = InstallBin(installDir, Config.BinaryName);
        try
        {
            Directory.CreateDirectory(installDir);
            var asm  = Assembly.GetExecutingAssembly();
            string? name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.Equals(Config.ResourceName, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                // Fallback: resource not embedded (dev build without binary staged)
                // Still allow install to write registry / shortcuts if binary already exists
                if (!File.Exists(binPath))
                {
                    LogError($"Resource '{Config.ResourceName}' not embedded and target binary not found.");
                    return false;
                }
                return true;
            }
            using var src = asm.GetManifestResourceStream(name)!;
            using var dst = File.Create(binPath);
            src.CopyTo(dst);
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Extraction failed: {ex.Message}");
            return false;
        }
    }

    private static void CopySelf(string installDir, Action<string, int> report)
    {
        try
        {
            string? self = Environment.ProcessPath;
            if (self is null) return;
            string dest = InstallBin(installDir, Config.SetupBinaryName);
            if (!string.Equals(self, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            report($"  [warn] Could not copy installer for uninstall: {ex.Message}", -1);
        }
    }

    // ── Programs & Features ──────────────────────────────────────────────

    private static void WriteUninstallEntry(string installDir)
    {
        try
        {
            string setupExe = InstallBin(installDir, Config.SetupBinaryName);
            using var key = Registry.LocalMachine.CreateSubKey(Config.UninstallSubKey, writable: true);
            key.SetValue("DisplayName",          Config.AppDisplayName,                                    RegistryValueKind.String);
            key.SetValue("DisplayVersion",        AppVersion,                                               RegistryValueKind.String);
            key.SetValue("Publisher",             "TAD Europe",                                             RegistryValueKind.String);
            key.SetValue("InstallLocation",       installDir,                                               RegistryValueKind.String);
            key.SetValue("DisplayIcon",           $"{InstallBin(installDir, Config.BinaryName)},0",        RegistryValueKind.String);
            key.SetValue("UninstallString",       $"\"{setupExe}\" --uninstall",                           RegistryValueKind.String);
            key.SetValue("QuietUninstallString",  $"\"{setupExe}\" --uninstall --quiet",                   RegistryValueKind.String);
            key.SetValue("NoModify",              1,                                                        RegistryValueKind.DWord);
            key.SetValue("NoRepair",              1,                                                        RegistryValueKind.DWord);
        }
        catch (Exception ex) { LogError($"Programs & Features (non-fatal): {ex.Message}"); }
    }

    // ── Service registry entries (Client only) ───────────────────────────

    private static void WriteServiceRegistry(string installDir, string binPath)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(ServiceRegKey, writable: true);
            key.SetValue("InstallDir",      installDir,      RegistryValueKind.String);
            key.SetValue("ServiceBin",      binPath,         RegistryValueKind.String);
            key.SetValue("ServiceAccount",  VirtualAccount,  RegistryValueKind.String);
            key.SetValue("Provisioned",     0,               RegistryValueKind.DWord);
            key.SetValue("UpdateRepo",      "amiho-dev/TAD-RV", RegistryValueKind.String);
        }
        catch (Exception ex) { LogError($"Service registry (non-fatal): {ex.Message}"); }
    }

    // ── Windows service registration (Client only) ───────────────────────

    private static bool RegisterService(string binPath, Action<string, int> report)
    {
        RunCmd("sc.exe", $"stop {Config.ServiceName}");
        Thread.Sleep(1000);
        RunCmd("sc.exe", $"delete {Config.ServiceName}");
        Thread.Sleep(500);

        int rc = RunCmd("sc.exe",
            $"create {Config.ServiceName}" +
            $" binPath= \"{binPath}\"" +
            $" start= delayed-auto" +
            $" obj= \"{VirtualAccount}\"" +
            $" DisplayName= \"{Config.ServiceDisplay}\"");

        if (rc != 0 && rc != 1073)
        {
            report($"  [error] sc.exe create failed (exit {rc}).", -1);
            return false;
        }

        int addRc = RunCmd("net", $"localgroup Administrators \"{VirtualAccount}\" /add");
        if (addRc != 0 && addRc != 1378)
            report($"  [warn] net localgroup returned {addRc}", -1);

        RunCmd("sc.exe", $"description {Config.ServiceName} \"{Config.ServiceDesc}\"");
        RunCmd("sc.exe", $"failure {Config.ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");
        RunCmd("sc.exe", $"failureflag {Config.ServiceName} 1");
        RunCmd("sc.exe", $"sidtype {Config.ServiceName} unrestricted");
        return true;
    }

    private static void StartService(Action<string, int> report)
    {
        int rc = RunCmd("sc.exe", $"start {Config.ServiceName}");
        if (rc != 0 && rc != 1056)
            report($"  [warn] Service start returned {rc} — check Event Log.", -1);
    }

    // ── Start Menu shortcut (Admin / DC) ─────────────────────────────────

    private static void CreateShortcut(string installDir)
    {
        if (!Config.CreateShortcut) return;
        try
        {
            string lnkDir  = StartMenuDir();
            string lnkPath = Path.Combine(lnkDir, Path.GetFileNameWithoutExtension(Config.BinaryName) + ".lnk");
            string target  = InstallBin(installDir, Config.BinaryName);
            Directory.CreateDirectory(lnkDir);

            string ps =
                $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{lnkPath}');" +
                $"$s.TargetPath='{target}';" +
                $"$s.WorkingDirectory='{installDir}';" +
                $"$s.Description='{Config.AppDisplayName}';" +
                "$s.Save()";

            RunCmd("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"");
        }
        catch (Exception ex) { LogError($"Shortcut (non-fatal): {ex.Message}"); }
    }

    private static void RemoveShortcuts()
    {
        try
        {
            if (Directory.Exists(StartMenuDir()))
                Directory.Delete(StartMenuDir(), recursive: true);
        }
        catch { }
    }

    // ── File removal ─────────────────────────────────────────────────────

    private static void RemoveInstallDir(string installDir, Action<string, int> report)
    {
        RunCmd("taskkill", $"/f /im {Config.BinaryName}");
        Thread.Sleep(500);
        try
        {
            if (!Directory.Exists(installDir)) return;

            // If we're running from inside the install dir, schedule a deferred cleanup
            string? self = Environment.ProcessPath;
            if (self is not null &&
                self.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
            {
                string bat = Path.Combine(Path.GetTempPath(), "tad_cleanup.bat");
                File.WriteAllText(bat,
                    "@echo off\r\ntimeout /t 2 /nobreak >nul\r\n" +
                    $"rd /s /q \"{installDir}\"\r\ndel \"%~f0\"\r\n");
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                    { CreateNoWindow = true, UseShellExecute = false });
                report("  Folder removal scheduled (runs after this window closes).", -1);
            }
            else
            {
                Directory.Delete(installDir, recursive: true);
            }
        }
        catch (Exception ex) { report($"  [warn] {ex.Message}", -1); }
    }

    // ── Utilities ────────────────────────────────────────────────────────

    private static int RunCmd(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(20_000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    private static void LogError(string msg) =>
        Debug.WriteLine("[TADSetup] " + msg);
}
