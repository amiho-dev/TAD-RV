// ───────────────────────────────────────────────────────────────────────────
// Program.cs — TAD.RV Client Endpoint Installer
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Self-contained, single-file installer.  TADBridgeService.exe is bundled
// as an embedded resource and extracted at install time.
//
// What this installer does:
//   1. Extracts the bundled TADBridgeService.exe to %ProgramFiles%\TAD-RV\
//   2. Writes registry entries (HKLM\SOFTWARE\TAD_RV)
//   3. Registers TADBridgeService as an auto-start Windows service running
//      as a Virtual Service Account (NT SERVICE\TADBridgeService) — created
//      automatically by the SCM, no user account or password needed.
//      The virtual account is then added to local Administrators so it has
//      the elevated rights needed for the capture engine and driver bridge.
//   4. Starts the service immediately
//
// Usage (requires Administrator):
//   TADClientSetup.exe               → interactive install
//   TADClientSetup.exe --install     → silent install
//   TADClientSetup.exe --uninstall   → remove service + account + files
//   TADClientSetup.exe --status      → print current service status
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

// ── Constants ────────────────────────────────────────────────────────────

const string ServiceName    = "TADBridgeService";
const string ServiceDisplay = "TAD.RV Bridge Service";
const string ServiceDesc    = "TAD.RV endpoint protection and remote-view agent. "
                            + "Runs as a dedicated least-privilege service account.";
const string ServiceBinary   = "TADBridgeService.exe";
const string InstallSubDir   = "TAD-RV";
const string ResourceName    = "bundled_service"; // LogicalName in .csproj
// Virtual Service Account — automatically managed by Windows SCM.
// No password, no user creation, no LSA rights needed.
// Works on standalone and domain-joined machines alike.
const string VirtualAccount  = @"NT SERVICE\TADBridgeService";

static string InstallDir() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 InstallSubDir);

static string InstallBin() => Path.Combine(InstallDir(), ServiceBinary);

// ── Argument parsing ─────────────────────────────────────────────────────

bool silent     = args.Any(a => a.Equals("--install",   StringComparison.OrdinalIgnoreCase));
bool uninstall  = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
bool statusOnly = args.Any(a => a.Equals("--status",    StringComparison.OrdinalIgnoreCase));

PrintBanner();

// ── Admin check ──────────────────────────────────────────────────────────

if (!IsRunningAsAdmin())
{
    Err("[ERROR] This installer must be run as Administrator.");
    if (!silent) Pause();
    return 2;
}

// ── Dispatch ─────────────────────────────────────────────────────────────

if (statusOnly)
{
    PrintServiceStatus();
    return 0;
}

if (uninstall)
{
    Console.WriteLine("[*] Uninstalling TAD.RV endpoint...");
    return RunUninstall() ? 0 : 1;
}

if (!silent)
{
    Console.WriteLine($"  Install path   : {InstallBin()}");
    Console.WriteLine($"  Service name   : {ServiceName}");
    Console.WriteLine($"  Service account: {VirtualAccount}  (Virtual — managed by SCM)");
    Console.WriteLine($"  Auto-start     : Yes — starts on every boot");
    Console.WriteLine();
    Console.Write("Proceed? [Y/n]: ");
    var k = Console.ReadLine()?.Trim().ToUpperInvariant();
    if (k is "N" or "NO") { Console.WriteLine("Cancelled."); return 0; }
}

return RunInstall() ? 0 : 1;

// ════════════════════════════════════════════════════════════════════════════
// INSTALL
// ════════════════════════════════════════════════════════════════════════════

static bool RunInstall()
{
    Step(1, 4, "Extracting service binary...");
    if (!ExtractServiceBinary()) return false;

    Step(2, 4, "Writing registry entries...");
    WriteRegistry();

    Step(3, 4, "Registering Windows service as Virtual Account (autostart on boot)...");
    if (!RegisterService()) return false;

    Step(4, 4, "Starting service...");
    StartService();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine("  +--------------------------------------------------+");
    Console.WriteLine("  |   TAD.RV endpoint installed successfully!         |");
    Console.WriteLine($"  |   Account  : {VirtualAccount}");
    Console.WriteLine("  |   Auto-start on boot: SERVICE_AUTO_START          |");
    Console.WriteLine("  +--------------------------------------------------+");
    Console.ResetColor();
    Console.WriteLine();
    PrintServiceStatus();
    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// UNINSTALL
// ════════════════════════════════════════════════════════════════════════════

static bool RunUninstall()
{
    Console.WriteLine("[1/3] Stopping service...");
    RunExitCode("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(2000);

    Console.WriteLine("[2/3] Deleting service registration...");
    int rc = RunExitCode("sc.exe", $"delete {ServiceName}");
    if (rc != 0 && rc != 1060 /* ERROR_SERVICE_DOES_NOT_EXIST */)
        Warn($"sc delete returned {rc}");

    Console.WriteLine("[3/3] Removing registry entries + files...");
    try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\TAD_RV", false); }
    catch (Exception ex) { Warn($"Registry removal: {ex.Message}"); }

    Console.Write($"Remove installed files from {InstallDir()}? [y/N]: ");
    string? ans = Console.ReadLine()?.Trim().ToUpperInvariant();
    if (ans is "Y" or "YES")
        RemoveInstallDir();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Uninstall complete.");
    Console.ResetColor();
    return true;
}

static void RemoveInstallDir()
{
    try
    {
        Directory.Delete(InstallDir(), recursive: true);
        Console.WriteLine($"Removed: {InstallDir()}");
    }
    catch (Exception ex) { Warn($"Could not remove files: {ex.Message}"); }
}

// ════════════════════════════════════════════════════════════════════════════
// Step implementations
// ════════════════════════════════════════════════════════════════════════════

// ── Extract embedded binary ───────────────────────────────────────────────

static bool ExtractServiceBinary()
{
    var asm = Assembly.GetExecutingAssembly();

    // Try exact LogicalName first, then any resource containing the binary name
    string? resourceName =
        asm.GetManifestResourceNames()
           .FirstOrDefault(n => n.Equals(ResourceName, StringComparison.OrdinalIgnoreCase))
        ?? asm.GetManifestResourceNames()
              .FirstOrDefault(n => n.Contains("TADBridgeService", StringComparison.OrdinalIgnoreCase));

    if (resourceName == null)
    {
        Err("[ERROR] Bundled TADBridgeService.exe not found in embedded resources.");
        Err($"        Available: {string.Join(", ", asm.GetManifestResourceNames())}");
        return false;
    }

    try
    {
        Directory.CreateDirectory(InstallDir());

        using var src = asm.GetManifestResourceStream(resourceName)!;
        using var dst = File.Create(InstallBin());
        src.CopyTo(dst);

        Console.WriteLine($"  Extracted to {InstallBin()}  ({new FileInfo(InstallBin()).Length / 1024:N0} KB)");
        return true;
    }
    catch (Exception ex)
    {
        Err($"[ERROR] Extraction failed: {ex.Message}");
        return false;
    }
}

// ── Registry ──────────────────────────────────────────────────────────────

static void WriteRegistry()
{
    try
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TAD_RV", writable: true);
        key.SetValue("InstallDir",     InstallDir(),       RegistryValueKind.String);
        key.SetValue("ServiceBin",     InstallBin(),       RegistryValueKind.String);
        key.SetValue("ServiceAccount", VirtualAccount,     RegistryValueKind.String);
        key.SetValue("Provisioned",    0,                  RegistryValueKind.DWord);
        key.SetValue("UpdateRepo",     "amiho-dev/TAD-RV", RegistryValueKind.String);
        Console.WriteLine($"  HKLM\\SOFTWARE\\TAD_RV written.");
    }
    catch (Exception ex) { Warn($"Registry (non-fatal): {ex.Message}"); }
}

// ── Service registration ──────────────────────────────────────────────────

static bool RegisterService()
{
    // Idempotent — stop + delete existing instance first
    RunExitCode("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(1000);
    RunExitCode("sc.exe", $"delete {ServiceName}");

    // Register as Virtual Service Account (NT SERVICE\<ServiceName>).
    // The SCM creates the virtual identity automatically — no password, no
    // separate user account, no domain policy conflicts.
    int rc = RunExitCode("sc.exe",
        $"create {ServiceName}" +
        $" binPath= \"{InstallBin()}\"" +
        $" start= auto" +
        $" obj= \"{VirtualAccount}\"" +
        $" DisplayName= \"{ServiceDisplay}\"");

    if (rc != 0 && rc != 1073 /* ERROR_SERVICE_EXISTS */)
    {
        Err($"[ERROR] sc.exe create failed (exit {rc}). Run 'sc.exe query {ServiceName}' for details.");
        return false;
    }

    // Grant the virtual account local admin privileges so it can interact
    // with the driver bridge, capture engine, and protected resources.
    // The virtual account only exists after sc.exe create, so this must come after.
    int adminRc = RunExitCode("net", $"localgroup Administrators \"{VirtualAccount}\" /add");
    switch (adminRc)
    {
        case    0: Console.WriteLine($"  '{VirtualAccount}' added to Administrators."); break;
        case 1378: Console.WriteLine($"  '{VirtualAccount}' is already in Administrators."); break;
        default:   Warn($"net localgroup returned {adminRc}"); break;
    }

    RunExitCode("sc.exe", $"description {ServiceName} \"{ServiceDesc}\"");

    // Auto-recovery: restart after 5 s / 10 s / 30 s on failure
    RunExitCode("sc.exe",
        $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");
    RunExitCode("sc.exe", $"failureflag {ServiceName} 1");

    // Unrestricted SID type
    RunExitCode("sc.exe", $"sidtype {ServiceName} unrestricted");

    Console.WriteLine($"  Service '{ServiceName}' registered (start= auto, obj= {VirtualAccount}).");
    return true;
}

// ── Start service ─────────────────────────────────────────────────────────

static void StartService()
{
    int rc = RunExitCode("sc.exe", $"start {ServiceName}");
    switch (rc)
    {
        case    0: Console.WriteLine("  Service started."); break;
        case 1056: Console.WriteLine("  Service already running."); break;
        default:   Warn($"Service start returned {rc} — check Event Log."); break;
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Utilities
// ════════════════════════════════════════════════════════════════════════════

static void Step(int n, int total, string desc)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"[{n}/{total}] ");
    Console.ResetColor();
    Console.WriteLine(desc);
}

static void Err(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(msg);
    Console.ResetColor();
}

static void Warn(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine($"  [WARN] {msg}");
    Console.ResetColor();
}

static void Pause()
{
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
}

static bool IsRunningAsAdmin()
{
    try
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
    catch { return false; }
}

static int RunExitCode(string exe, string arguments)
{
    try
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(15_000);
        return p.ExitCode;
    }
    catch { return -1; }
}

static void PrintBanner()
{
    string version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0";
    version = version.TrimStart('v');
    int di = version.IndexOf('-'); if (di > 0) version = version[..di];

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"  _____ _   ___       ______   __");
    Console.WriteLine(@" |_   _/_\ |   \     | _ \ \ / /");
    Console.WriteLine(@"   | |/ _ \| |) |    |   /\ V / ");
    Console.WriteLine(@"   |_/_/ \_\___/     |_|_\ \_/  ");
    Console.ResetColor();
    Console.WriteLine($"  Client Endpoint Installer v{version}");
    Console.WriteLine("  (C) 2026 TAD Europe — https://tad-it.eu");
    Console.WriteLine();
}

static void PrintServiceStatus()
{
    try
    {
        var psi = new ProcessStartInfo("sc.exe", $"query {ServiceName}")
        {
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        Console.ForegroundColor = ConsoleColor.Cyan;
        foreach (var line in output.Split('\n').Where(l => l.Trim().Length > 0))
            Console.WriteLine("  " + line.TrimEnd());
        Console.ResetColor();
    }
    catch { /* sc.exe not available */ }
}

// LSA P/Invoke removed — replaced by Virtual Service Account approach.
// No SeServiceLogonRight or user creation needed with NT SERVICE\<name>.
