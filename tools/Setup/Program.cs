// ───────────────────────────────────────────────────────────────────────────
// Program.cs — TAD Endpoint Installer
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// What this installer does:
//   1. Extracts TADBridgeService.exe → C:\Program Files\TAD\
//   2. Copies itself as TADSetup.exe (for Programs & Features uninstall link)
//   3. Registers app in Programs & Features (Add/Remove Programs)
//   4. Registers TADBridgeService as a DELAYED-AUTO service under
//      NT SERVICE\TADBridgeService (Virtual Account — no password/user needed)
//   5. Adds the virtual account to local Administrators
//   6. Starts the service immediately
//
// Usage (requires Administrator):
//   TADClientSetup.exe               → interactive install
//   TADClientSetup.exe --install     → silent install
//   TADClientSetup.exe --uninstall   → full removal
//   TADClientSetup.exe --status      → service status
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

// ── Constants ────────────────────────────────────────────────────────────

const string AppName         = "TAD Endpoint";
const string AppPublisher    = "TAD Europe";
const string ServiceName     = "TADBridgeService";
const string ServiceDisplay  = "TAD Endpoint Service";
const string ServiceDesc     = "TAD.RV endpoint agent — screen capture, remote view and policy enforcement. "
                             + "Managed by TAD Europe domain controller.";
const string ServiceBinary   = "TADBridgeService.exe";
const string SetupBinary     = "TADSetup.exe";           // installer copy kept in Program Files
const string InstallSubDir   = "TAD";                    // → C:\Program Files\TAD\
const string ResourceName    = "bundled_service";
// Virtual Service Account — SCM creates it automatically, domain-safe.
const string VirtualAccount  = @"NT SERVICE\TADBridgeService";
const string UninstallRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD.RV";

static string InstallDir()     => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallSubDir);
static string InstallBin()     => Path.Combine(InstallDir(), ServiceBinary);
static string InstalledSetup() => Path.Combine(InstallDir(), SetupBinary);

// ── Argument parsing ─────────────────────────────────────────────────────

bool silent     = args.Any(a => a.Equals("--install",   StringComparison.OrdinalIgnoreCase));
bool uninstall  = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
bool statusOnly = args.Any(a => a.Equals("--status",    StringComparison.OrdinalIgnoreCase));

PrintBanner();

// ── Admin check — auto-elevate via UAC ───────────────────────────────────

if (!IsRunningAsAdmin())
{
    string self = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
    try
    {
        Process.Start(new ProcessStartInfo(self)
        {
            Verb            = "runas",
            UseShellExecute = true,
            Arguments       = string.Join(" ", args),
        });
        return 0;
    }
    catch
    {
        Err("[ERROR] Administrator privileges required.");
        if (!silent) Pause();
        return 2;
    }
}

// ── Dispatch ─────────────────────────────────────────────────────────────

if (statusOnly) { PrintServiceStatus(); return 0; }

if (uninstall)
{
    Console.WriteLine("[*] Removing TAD Endpoint...");
    return RunUninstall() ? 0 : 1;
}

if (!silent)
{
    Console.WriteLine($"  Install path   : {InstallDir()}");
    Console.WriteLine($"  Service name   : {ServiceName}");
    Console.WriteLine($"  Service account: {VirtualAccount}  (Virtual — managed by SCM)");
    Console.WriteLine($"  Auto-start     : delayed-auto (survives every reboot)");
    Console.WriteLine($"  Visible in     : Settings → Apps → {AppName}");
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
    Step(1, 5, $"Installing files to C:\\Program Files\\{InstallSubDir}\\...");
    if (!ExtractFiles()) return false;

    Step(2, 5, "Registering app in Programs & Features...");
    WriteUninstallEntry();

    Step(3, 5, "Writing service registry entries...");
    WriteServiceRegistry();

    Step(4, 5, "Registering Windows service (delayed-auto, Virtual Account)...");
    if (!RegisterService()) return false;

    Step(5, 5, "Starting service...");
    StartService();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine("  +--------------------------------------------------+");
    Console.WriteLine("  |   TAD Endpoint installed successfully!            |");
    Console.WriteLine($"  |   Path     : {InstallDir(),-35} |");
    Console.WriteLine("  |   Autostart: delayed-auto (every reboot)         |");
    Console.WriteLine("  |   Uninstall: Settings -> Apps -> TAD Endpoint    |");
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
    Console.WriteLine("[1/4] Stopping service...");
    RunExitCode("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(2000);

    Console.WriteLine("[2/4] Deleting service registration...");
    int rc = RunExitCode("sc.exe", $"delete {ServiceName}");
    if (rc != 0 && rc != 1060) Warn($"sc delete returned {rc}");

    Console.WriteLine("[3/4] Removing registry entries...");
    try { Registry.LocalMachine.DeleteSubKeyTree(UninstallRegKey,       throwOnMissingSubKey: false); } catch { }
    try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\TAD_RV",   throwOnMissingSubKey: false); } catch { }

    Console.WriteLine($"[4/4] Removing files from {InstallDir()}...");
    RunExitCode("taskkill", $"/f /im {ServiceBinary}");
    System.Threading.Thread.Sleep(500);
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
        if (!Directory.Exists(InstallDir())) return;

        string selfPath = Environment.ProcessPath ?? "";
        bool runningFromInstallDir = selfPath.StartsWith(InstallDir(), StringComparison.OrdinalIgnoreCase);

        if (runningFromInstallDir)
        {
            // Schedule via cmd bat after this process exits
            var bat = Path.Combine(Path.GetTempPath(), "tad_cleanup.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"rd /s /q \"{InstallDir()}\"\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                { CreateNoWindow = true, UseShellExecute = false });
            Console.WriteLine($"  Scheduled removal after exit.");
        }
        else
        {
            Directory.Delete(InstallDir(), recursive: true);
            Console.WriteLine($"  Removed: {InstallDir()}");
        }
    }
    catch (Exception ex) { Warn($"Could not remove files: {ex.Message}"); }
}

// ════════════════════════════════════════════════════════════════════════════
// Step implementations
// ════════════════════════════════════════════════════════════════════════════

static bool ExtractFiles()
{
    var asm = Assembly.GetExecutingAssembly();

    string? resourceName =
        asm.GetManifestResourceNames()
           .FirstOrDefault(n => n.Equals(ResourceName, StringComparison.OrdinalIgnoreCase))
        ?? asm.GetManifestResourceNames()
              .FirstOrDefault(n => n.Contains("TADBridgeService", StringComparison.OrdinalIgnoreCase));

    if (resourceName == null)
    {
        Err("[ERROR] Bundled TADBridgeService.exe not found in installer.");
        Err($"        Resources: {string.Join(", ", asm.GetManifestResourceNames())}");
        return false;
    }

    try
    {
        Directory.CreateDirectory(InstallDir());

        // 1. Extract service binary
        using (var src = asm.GetManifestResourceStream(resourceName)!)
        using (var dst = File.Create(InstallBin()))
            src.CopyTo(dst);
        Console.WriteLine($"  {ServiceBinary}  ({new FileInfo(InstallBin()).Length / 1024:N0} KB)");

        // 2. Copy installer itself → TADSetup.exe so uninstall works from Programs & Features
        string self = Environment.ProcessPath ?? asm.Location;
        if (File.Exists(self) && !string.Equals(self, InstalledSetup(), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, InstalledSetup(), overwrite: true);
            Console.WriteLine($"  {SetupBinary}  (uninstaller copy)");
        }

        return true;
    }
    catch (Exception ex)
    {
        Err($"[ERROR] Extraction failed: {ex.Message}");
        return false;
    }
}

// ── Programs & Features entry ─────────────────────────────────────────────

static void WriteUninstallEntry()
{
    try
    {
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0";
        version = version.TrimStart('v');
        int di = version.IndexOf('-'); if (di > 0) version = version[..di];

        int estimatedKb = File.Exists(InstallBin())
            ? (int)(new FileInfo(InstallBin()).Length / 1024) : 0;

        using var key = Registry.LocalMachine.CreateSubKey(UninstallRegKey, writable: true);
        key.SetValue("DisplayName",         AppName,                                          RegistryValueKind.String);
        key.SetValue("DisplayVersion",       version,                                          RegistryValueKind.String);
        key.SetValue("Publisher",            AppPublisher,                                     RegistryValueKind.String);
        key.SetValue("InstallLocation",      InstallDir(),                                     RegistryValueKind.String);
        key.SetValue("DisplayIcon",          $"{InstallBin()},0",                             RegistryValueKind.String);
        key.SetValue("UninstallString",      $"\"{InstalledSetup()}\" --uninstall",           RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{InstalledSetup()}\" --uninstall --install", RegistryValueKind.String);
        key.SetValue("EstimatedSize",        estimatedKb,                                      RegistryValueKind.DWord);
        key.SetValue("NoModify",             1,                                                RegistryValueKind.DWord);
        key.SetValue("NoRepair",             1,                                                RegistryValueKind.DWord);
        key.SetValue("SystemComponent",      0,                                                RegistryValueKind.DWord);
        Console.WriteLine($"  '{AppName}' visible in Settings \u2192 Apps");
    }
    catch (Exception ex) { Warn($"Programs & Features (non-fatal): {ex.Message}"); }
}

// ── Service registry ──────────────────────────────────────────────────────

static void WriteServiceRegistry()
{
    try
    {
        // Write under SOFTWARE\TAD_RV — this is the key read by
        // ProvisioningManager, AdGroupWatcher and the DC's RegistryService.
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TAD_RV", writable: true);
        key.SetValue("InstallDir",     InstallDir(),       RegistryValueKind.String);
        key.SetValue("ServiceBin",     InstallBin(),       RegistryValueKind.String);
        key.SetValue("ServiceAccount", VirtualAccount,     RegistryValueKind.String);
        key.SetValue("Provisioned",    0,                  RegistryValueKind.DWord);
        key.SetValue("UpdateRepo",     "amiho-dev/TAD-RV", RegistryValueKind.String);
        Console.WriteLine(@"  HKLM\SOFTWARE\TAD_RV written.");
    }
    catch (Exception ex) { Warn($"Registry (non-fatal): {ex.Message}"); }
}

// ── Service registration ──────────────────────────────────────────────────

static bool RegisterService()
{
    RunExitCode("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(1000);
    RunExitCode("sc.exe", $"delete {ServiceName}");
    System.Threading.Thread.Sleep(500);

    // delayed-auto: starts after all core Windows service groups are ready.
    // More reliable than plain 'auto' for virtual service accounts on first
    // boot and after Windows Updates that reorder service startup.
    int rc = RunExitCode("sc.exe",
        $"create {ServiceName}" +
        $" binPath= \"{InstallBin()}\"" +
        $" start= delayed-auto" +
        $" obj= \"{VirtualAccount}\"" +
        $" DisplayName= \"{ServiceDisplay}\"");

    if (rc != 0 && rc != 1073)
    {
        Err($"[ERROR] sc.exe create failed (exit {rc}).");
        return false;
    }

    int adminRc = RunExitCode("net", $"localgroup Administrators \"{VirtualAccount}\" /add");
    if      (adminRc == 0)    Console.WriteLine($"  '{VirtualAccount}' added to local Administrators.");
    else if (adminRc == 1378) Console.WriteLine($"  '{VirtualAccount}' already in Administrators.");
    else                      Warn($"net localgroup returned {adminRc}");

    RunExitCode("sc.exe", $"description {ServiceName} \"{ServiceDesc}\"");
    RunExitCode("sc.exe", $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");
    RunExitCode("sc.exe", $"failureflag {ServiceName} 1");
    RunExitCode("sc.exe", $"sidtype {ServiceName} unrestricted");

    Console.WriteLine($"  '{ServiceName}' registered (delayed-auto, {VirtualAccount}).");
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
    Console.WriteLine(@"  _____ _   ___  ");
    Console.WriteLine(@" |_   _/_\ |   \ ");
    Console.WriteLine(@"   | |/ _ \| |) |");
    Console.WriteLine(@"   |_/_/ \_\___/ ");
    Console.ResetColor();
    Console.WriteLine($"  Endpoint Installer  v{version}");
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
    catch { }
}
