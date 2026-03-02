// ───────────────────────────────────────────────────────────────────────────
// Program.cs — TAD.RV Client Endpoint Installer
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Single-file self-contained installer for the TADBridgeService endpoint.
//
// Usage (must run as Administrator):
//   TADSetup.exe                   → interactive install
//   TADSetup.exe --install         → silent install
//   TADSetup.exe --uninstall       → remove service + files
//   TADSetup.exe --status          → print current service status
//
// Installs to:  %ProgramFiles%\TAD-RV\
// Service name: TADBridgeService
// Runs as:      LocalSystem   (cannot be killed by normal users)
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

const string ServiceName    = "TADBridgeService";
const string ServiceDisplay = "TAD.RV Bridge Service";
const string ServiceDesc    = "TAD.RV endpoint protection and remote-view agent. User-mode runtime — no kernel driver required.";
const string ServiceBinary  = "TADBridgeService.exe";
const string InstallSubDir  = @"TAD-RV";

// ── Argument parsing ─────────────────────────────────────────────────────

bool silent    = args.Any(a => a.Equals("--install",   StringComparison.OrdinalIgnoreCase));
bool uninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
bool statusOnly= args.Any(a => a.Equals("--status",    StringComparison.OrdinalIgnoreCase));

PrintBanner();

// ── Admin check ──────────────────────────────────────────────────────────

if (!IsRunningAsAdmin())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("[ERROR] This installer must be run as Administrator.");
    Console.ResetColor();
    if (!silent) { Console.WriteLine("Press any key to exit..."); Console.ReadKey(true); }
    return 2;
}

// ── Status only ──────────────────────────────────────────────────────────

if (statusOnly)
{
    PrintServiceStatus();
    return 0;
}

// ── Uninstall ────────────────────────────────────────────────────────────

if (uninstall)
{
    Console.WriteLine("[*] Uninstalling TAD.RV endpoint service...");
    return RunUninstall() ? 0 : 1;
}

// ── Interactive install prompt ───────────────────────────────────────────

if (!silent)
{
    string installDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallSubDir);

    Console.WriteLine();
    Console.WriteLine($"Install directory : {installDir}");
    Console.WriteLine($"Service name      : {ServiceName}");
    Console.WriteLine($"Runs as           : LocalSystem");
    Console.WriteLine();
    Console.Write("Proceed with installation? [Y/n]: ");

    var key = Console.ReadLine()?.Trim().ToUpperInvariant();
    if (key is "N" or "NO")
    {
        Console.WriteLine("Installation cancelled.");
        return 0;
    }
}

// ── Install ──────────────────────────────────────────────────────────────

return RunInstall() ? 0 : 1;

// ════════════════════════════════════════════════════════════════════════
// Install / Uninstall logic
// ════════════════════════════════════════════════════════════════════════

static bool RunInstall()
{
    // 1. Find the service binary next to this installer
    string setupDir  = AppContext.BaseDirectory;
    string sourceBin = Path.Combine(setupDir, ServiceBinary);

    if (!File.Exists(sourceBin))
    {
        // Try same directory as running EXE
        sourceBin = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath ?? setupDir) ?? setupDir,
            ServiceBinary);
    }

    if (!File.Exists(sourceBin))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] Cannot find {ServiceBinary} next to the installer.");
        Console.Error.WriteLine($"        Make sure both files are in the same directory.");
        Console.ResetColor();
        return false;
    }

    // 2. Create install directory
    string installDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallSubDir);

    Console.WriteLine($"[1/5] Creating install directory: {installDir}");
    try { Directory.CreateDirectory(installDir); }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] {ex.Message}");
        Console.ResetColor();
        return false;
    }

    // 3. Copy binary
    string destBin = Path.Combine(installDir, ServiceBinary);
    Console.WriteLine($"[2/5] Copying {ServiceBinary} → {destBin}");
    try { File.Copy(sourceBin, destBin, overwrite: true); }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] Copy failed: {ex.Message}");
        Console.ResetColor();
        return false;
    }

    // 4. Write registry entries
    Console.WriteLine("[3/5] Writing registry entries...");
    try
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TAD_RV", writable: true);
        key.SetValue("InstallDir",   installDir,    RegistryValueKind.String);
        key.SetValue("ServiceBin",   destBin,       RegistryValueKind.String);
        key.SetValue("Provisioned",  0,             RegistryValueKind.DWord);
        key.SetValue("UpdateRepo",   "amiho-dev/TAD-RV", RegistryValueKind.String);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"[WARN] Registry write failed (non-fatal): {ex.Message}");
        Console.ResetColor();
    }

    // 5. Register Windows service using sc.exe
    Console.WriteLine("[4/5] Registering Windows service...");

    // Stop + delete existing instance first (idempotent)
    RunSc($"stop {ServiceName}");
    RunSc($"delete {ServiceName}");

    int rc = RunSc($@"create {ServiceName} binPath= ""{destBin}"" start= auto obj= LocalSystem DisplayName= ""{ServiceDisplay}""");
    if (rc != 0 && rc != 1073 /* ERROR_SERVICE_EXISTS */)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] sc.exe create failed (exit {rc})");
        Console.ResetColor();
        return false;
    }

    // Set description
    RunSc($@"description {ServiceName} ""{ServiceDesc}""");

    // Set recovery: restart after 1st/2nd/3rd failure (60s reset)
    RunSc($"failure {ServiceName} reset= 60 actions= restart/5000/restart/10000/restart/30000");

    // sidtype unrestricted (required for some user-mode operations)
    RunSc($"sidtype {ServiceName} unrestricted");

    // 6. Start service
    Console.WriteLine("[5/5] Starting service...");
    int startRc = RunSc($"start {ServiceName}");
    if (startRc != 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"[WARN] Service start returned {startRc} — check Event Log");
        Console.ResetColor();
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine("══════════════════════════════════════════");
    Console.WriteLine("  TAD.RV endpoint installed successfully!");
    Console.WriteLine("══════════════════════════════════════════");
    Console.ResetColor();

    PrintServiceStatus();
    return true;
}

static bool RunUninstall()
{
    Console.WriteLine("[1/3] Stopping service...");
    RunSc($"stop {ServiceName}");

    Console.WriteLine("[2/3] Deleting service registration...");
    int rc = RunSc($"delete {ServiceName}");
    if (rc != 0 && rc != 1060 /* ERROR_SERVICE_DOES_NOT_EXIST */)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"[WARN] sc.exe delete returned {rc}");
        Console.ResetColor();
    }

    Console.WriteLine("[3/3] Removing registry entries...");
    try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\TAD_RV", throwOnMissingSubKey: false); }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"[WARN] Registry removal failed: {ex.Message}");
        Console.ResetColor();
    }

    // Optionally remove files — ask unless silent
    Console.Write("Remove installed files from %ProgramFiles%\\TAD-RV? [y/N]: ");
    string? ans = Console.ReadLine()?.Trim().ToUpperInvariant();
    if (ans is "Y" or "YES")
    {
        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallSubDir);
        try
        {
            Directory.Delete(installDir, recursive: true);
            Console.WriteLine($"Removed: {installDir}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine($"[WARN] Could not remove files: {ex.Message}");
            Console.ResetColor();
        }
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Uninstall complete.");
    Console.ResetColor();
    return true;
}

// ════════════════════════════════════════════════════════════════════════
// Helpers
// ════════════════════════════════════════════════════════════════════════

static void PrintBanner()
{
    string version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0";

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"  _____ _   ___       ______   __");
    Console.WriteLine(@" |_   _/_\ |   \     | _ \ \ / /");
    Console.WriteLine(@"   | |/ _ \| |) |    |   /\ V / ");
    Console.WriteLine(@"   |_/_/ \_\___/     |_|_\ \_/  ");
    Console.ResetColor();
    Console.WriteLine($"  Client Endpoint Installer — {version}");
    Console.WriteLine("  (C) 2026 TAD Europe — https://tad-it.eu");
    Console.WriteLine();
}

static bool IsRunningAsAdmin()
{
    try
    {
        using var identity  = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
    catch { return false; }
}

static void PrintServiceStatus()
{
    var psi = new ProcessStartInfo("sc", $"query {ServiceName}")
    {
        RedirectStandardOutput = true,
        UseShellExecute        = false,
        CreateNoWindow         = true
    };

    try
    {
        using var proc = Process.Start(psi)!;
        string output  = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (output.Contains("STATE"))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            foreach (var line in output.Split('\n'))
            {
                if (line.Trim().Length > 0)
                    Console.WriteLine("  " + line.TrimEnd());
            }
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"  Service '{ServiceName}' is not installed.");
        }
    }
    catch { /* sc.exe not available */ }
}

// Returns sc.exe exit code (0 = success)
static int RunSc(string args)
{
    var psi = new ProcessStartInfo("sc", args)
    {
        UseShellExecute = false,
        CreateNoWindow  = true
    };
    try
    {
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        return proc.ExitCode;
    }
    catch { return -1; }
}
