// ───────────────────────────────────────────────────────────────────────────
// Program.cs — TAD Unified Installer
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// ONE installer for all TAD components:
//
//   TADBridgeService  — Windows service (runs on every managed endpoint)
//   TADAdmin          — Admin dashboard WPF app  (teacher / IT machines)
//   TADDomainController — DC console WPF app     (domain controller / server)
//
// What this installer does:
//   1. Extracts all bundled executables → C:\Program Files\TAD\
//   2. Copies itself as TADSetup.exe (for Programs & Features uninstall)
//   3. Registers app in Programs & Features
//   4. Installs TADBridgeService as delayed-auto service (Virtual Account)
//   5. Adds the virtual account to local Administrators
//   6. Creates Start Menu shortcuts for TADAdmin and TADDomainController
//   7. Starts the service
//
// Usage (UAC elevation happens automatically):
//   TADSetup.exe               → interactive install (installs everything)
//   TADSetup.exe --install     → silent install
//   TADSetup.exe --uninstall   → full removal
//   TADSetup.exe --status      → service status
// ───────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

// ── Constants ────────────────────────────────────────────────────────────

const string AppName         = "TAD";
const string AppPublisher    = "TAD Europe";
const string InstallSubDir   = "TAD";                    // C:\Program Files\TAD\

// Service
const string ServiceName     = "TADBridgeService";
const string ServiceDisplay  = "TAD Endpoint Service";
const string ServiceDesc     = "TAD endpoint agent — remote view, screen capture and policy enforcement.";
const string ServiceBinary   = "TADBridgeService.exe";
const string VirtualAccount  = @"NT SERVICE\TADBridgeService";

// GUI apps
const string AdminBinary     = "TADAdmin.exe";
const string AdminShortcut   = "TAD Admin.lnk";
const string DcBinary        = "TADDomainController.exe";
const string DcShortcut      = "TAD Domain Controller.lnk";

// Installer self-copy
const string SetupBinary     = "TADSetup.exe";

// Resource names (LogicalName in .csproj)
const string ResService      = "bundled_service";
const string ResAdmin        = "bundled_admin";
const string ResDc           = "bundled_dc";

// Registry
const string UninstallRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD";
const string ServiceRegKey   = @"SOFTWARE\TAD_RV";   // read by ProvisioningManager, AdGroupWatcher

static string InstallDir()     => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), InstallSubDir);
static string InstallBin(string bin) => Path.Combine(InstallDir(), bin);

static string StartMenuDir()   => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
    "Programs", AppName);
static string ShortcutPath(string lnk) => Path.Combine(StartMenuDir(), lnk);

// ── Argument parsing ─────────────────────────────────────────────────────

bool silent     = args.Any(a => a.Equals("--install",   StringComparison.OrdinalIgnoreCase));
bool uninstall  = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
bool statusOnly = args.Any(a => a.Equals("--status",    StringComparison.OrdinalIgnoreCase));

PrintBanner();

// ── Auto-elevate via UAC ─────────────────────────────────────────────────

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
    Console.WriteLine("[*] Removing TAD...");
    return RunUninstall() ? 0 : 1;
}

if (!silent)
{
    Console.WriteLine($"  Install path : {InstallDir()}");
    Console.WriteLine($"  Components   : {ServiceBinary}  |  {AdminBinary}  |  {DcBinary}");
    Console.WriteLine($"  Service      : {ServiceName}  ({VirtualAccount})");
    Console.WriteLine($"  Autostart    : delayed-auto (survives every reboot)");
    Console.WriteLine($"  Shortcuts    : Start Menu → {AppName}");
    Console.WriteLine($"  Apps list    : Settings → Apps → {AppName}");
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
    Step(1, 6, $"Extracting files to C:\\Program Files\\{InstallSubDir}\\...");
    if (!ExtractFiles()) return false;

    Step(2, 6, "Registering app in Programs & Features...");
    WriteUninstallEntry();

    Step(3, 6, "Writing service registry entries...");
    WriteServiceRegistry();

    Step(4, 6, "Registering Windows service (delayed-auto, Virtual Account)...");
    if (!RegisterService()) return false;

    Step(5, 6, "Creating Start Menu shortcuts...");
    CreateShortcuts();

    Step(6, 6, "Starting service...");
    StartService();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine("  +----------------------------------------------------+");
    Console.WriteLine("  |   TAD installed successfully!                      |");
    Console.WriteLine($"  |   Path    : {InstallDir(),-38} |");
    Console.WriteLine("  |   Service : delayed-auto (starts on every reboot)  |");
    Console.WriteLine($"  |   Apps    : Start Menu → {AppName,-26} |");
    Console.WriteLine("  |   Uninstall: Settings → Apps → TAD                |");
    Console.WriteLine("  +----------------------------------------------------+");
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
    Console.WriteLine("[1/5] Stopping service...");
    RunCmd("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(2000);

    Console.WriteLine("[2/5] Deleting service registration...");
    int rc = RunCmd("sc.exe", $"delete {ServiceName}");
    if (rc != 0 && rc != 1060) Warn($"sc delete returned {rc}");

    Console.WriteLine("[3/5] Removing Start Menu shortcuts...");
    RemoveShortcuts();

    Console.WriteLine("[4/5] Removing registry entries...");
    try { Registry.LocalMachine.DeleteSubKeyTree(UninstallRegKey, throwOnMissingSubKey: false); } catch { }
    try { Registry.LocalMachine.DeleteSubKeyTree(ServiceRegKey,   throwOnMissingSubKey: false); } catch { }

    Console.WriteLine($"[5/5] Removing files from {InstallDir()}...");
    RunCmd("taskkill", $"/f /im {ServiceBinary}");
    RunCmd("taskkill", $"/f /im {AdminBinary}");
    RunCmd("taskkill", $"/f /im {DcBinary}");
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
        if (selfPath.StartsWith(InstallDir(), StringComparison.OrdinalIgnoreCase))
        {
            var bat = Path.Combine(Path.GetTempPath(), "tad_cleanup.bat");
            File.WriteAllText(bat,
                "@echo off\r\ntimeout /t 2 /nobreak >nul\r\n" +
                $"rd /s /q \"{InstallDir()}\"\r\ndel \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                { CreateNoWindow = true, UseShellExecute = false });
            Console.WriteLine("  Scheduled folder removal (after this process exits).");
        }
        else
        {
            Directory.Delete(InstallDir(), recursive: true);
            Console.WriteLine($"  Removed: {InstallDir()}");
        }
    }
    catch (Exception ex) { Warn($"Could not remove files: {ex.Message}"); }
}

static void RemoveShortcuts()
{
    try
    {
        if (Directory.Exists(StartMenuDir()))
        {
            Directory.Delete(StartMenuDir(), recursive: true);
            Console.WriteLine($"  Removed: {StartMenuDir()}");
        }
    }
    catch (Exception ex) { Warn($"Shortcut removal: {ex.Message}"); }
}

// ════════════════════════════════════════════════════════════════════════════
// File extraction
// ════════════════════════════════════════════════════════════════════════════

static bool ExtractFiles()
{
    try
    {
        Directory.CreateDirectory(InstallDir());

        // Extract each embedded resource that is present at compile time.
        // Resources may be absent if build.sh didn't copy them (dev scenario).
        var asm = Assembly.GetExecutingAssembly();

        bool ok = ExtractOne(asm, ResService, InstallBin(ServiceBinary), required: true);
        ExtractOne(asm, ResAdmin, InstallBin(AdminBinary),                required: false);
        ExtractOne(asm, ResDc,   InstallBin(DcBinary),                   required: false);

        // Copy installer itself → TADSetup.exe for Programs & Features uninstall
        // NOTE: asm.Location is always empty in single-file EXEs (IL3000); use ProcessPath.
        string self = Environment.ProcessPath ?? AppContext.BaseDirectory;
        if (File.Exists(self) && !string.Equals(self, InstallBin(SetupBinary),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, InstallBin(SetupBinary), overwrite: true);
            Console.WriteLine($"  {SetupBinary}  (uninstaller copy)");
        }

        return ok;
    }
    catch (Exception ex)
    {
        Err($"[ERROR] Extraction failed: {ex.Message}");
        return false;
    }
}

static bool ExtractOne(Assembly asm, string resourceName, string destPath, bool required)
{
    string? name = asm.GetManifestResourceNames()
        .FirstOrDefault(n => n.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

    if (name == null)
    {
        if (required)
        {
            Err($"[ERROR] Required resource '{resourceName}' not found in installer.");
            return false;
        }
        // Optional component not bundled — skip silently
        return true;
    }

    using var src = asm.GetManifestResourceStream(name)!;
    using var dst = File.Create(destPath);
    src.CopyTo(dst);
    Console.WriteLine($"  {Path.GetFileName(destPath)}  ({new FileInfo(destPath).Length / 1024:N0} KB)");
    return true;
}

// ════════════════════════════════════════════════════════════════════════════
// Registry
// ════════════════════════════════════════════════════════════════════════════

static void WriteUninstallEntry()
{
    try
    {
        string version = GetVersion();
        int sizeKb = Directory.Exists(InstallDir())
            ? (int)(new DirectoryInfo(InstallDir())
                .GetFiles("*.exe", SearchOption.TopDirectoryOnly)
                .Sum(f => f.Length) / 1024)
            : 0;

        using var key = Registry.LocalMachine.CreateSubKey(UninstallRegKey, writable: true);
        key.SetValue("DisplayName",         AppName,                                            RegistryValueKind.String);
        key.SetValue("DisplayVersion",       version,                                            RegistryValueKind.String);
        key.SetValue("Publisher",            AppPublisher,                                       RegistryValueKind.String);
        key.SetValue("InstallLocation",      InstallDir(),                                       RegistryValueKind.String);
        key.SetValue("DisplayIcon",          $"{InstallBin(AdminBinary)},0",                    RegistryValueKind.String);
        key.SetValue("UninstallString",      $"\"{InstallBin(SetupBinary)}\" --uninstall",      RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{InstallBin(SetupBinary)}\" --uninstall --install", RegistryValueKind.String);
        key.SetValue("EstimatedSize",        sizeKb,                                             RegistryValueKind.DWord);
        key.SetValue("NoModify",             1,                                                  RegistryValueKind.DWord);
        key.SetValue("NoRepair",             1,                                                  RegistryValueKind.DWord);
        Console.WriteLine($"  '{AppName}' visible in Settings \u2192 Apps");
    }
    catch (Exception ex) { Warn($"Programs & Features (non-fatal): {ex.Message}"); }
}

static void WriteServiceRegistry()
{
    try
    {
        using var key = Registry.LocalMachine.CreateSubKey(ServiceRegKey, writable: true);
        key.SetValue("InstallDir",     InstallDir(),       RegistryValueKind.String);
        key.SetValue("ServiceBin",     InstallBin(ServiceBinary), RegistryValueKind.String);
        key.SetValue("ServiceAccount", VirtualAccount,     RegistryValueKind.String);
        key.SetValue("Provisioned",    0,                  RegistryValueKind.DWord);
        key.SetValue("UpdateRepo",     "amiho-dev/TAD-RV", RegistryValueKind.String);
        Console.WriteLine($@"  HKLM\{ServiceRegKey} written.");
    }
    catch (Exception ex) { Warn($"Service registry (non-fatal): {ex.Message}"); }
}

// ════════════════════════════════════════════════════════════════════════════
// Windows Service
// ════════════════════════════════════════════════════════════════════════════

static bool RegisterService()
{
    RunCmd("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(1000);
    RunCmd("sc.exe", $"delete {ServiceName}");
    System.Threading.Thread.Sleep(500);

    int rc = RunCmd("sc.exe",
        $"create {ServiceName}" +
        $" binPath= \"{InstallBin(ServiceBinary)}\"" +
        $" start= delayed-auto" +
        $" obj= \"{VirtualAccount}\"" +
        $" DisplayName= \"{ServiceDisplay}\"");

    if (rc != 0 && rc != 1073)
    {
        Err($"[ERROR] sc.exe create failed (exit {rc}).");
        return false;
    }

    int adminRc = RunCmd("net", $"localgroup Administrators \"{VirtualAccount}\" /add");
    if      (adminRc == 0)    Console.WriteLine($"  '{VirtualAccount}' added to local Administrators.");
    else if (adminRc == 1378) Console.WriteLine($"  '{VirtualAccount}' already in Administrators.");
    else                      Warn($"net localgroup returned {adminRc}");

    RunCmd("sc.exe", $"description {ServiceName} \"{ServiceDesc}\"");
    RunCmd("sc.exe", $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");
    RunCmd("sc.exe", $"failureflag {ServiceName} 1");
    RunCmd("sc.exe", $"sidtype {ServiceName} unrestricted");

    Console.WriteLine($"  '{ServiceName}' registered (delayed-auto, {VirtualAccount}).");
    return true;
}

static void StartService()
{
    int rc = RunCmd("sc.exe", $"start {ServiceName}");
    switch (rc)
    {
        case    0: Console.WriteLine("  Service started."); break;
        case 1056: Console.WriteLine("  Service already running."); break;
        default:   Warn($"Service start returned {rc} — check Event Log."); break;
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Start Menu shortcuts (via PowerShell WScript.Shell)
// ════════════════════════════════════════════════════════════════════════════

static void CreateShortcuts()
{
    try { Directory.CreateDirectory(StartMenuDir()); }
    catch { }

    if (File.Exists(InstallBin(AdminBinary)))
        MakeShortcut(InstallBin(AdminBinary), ShortcutPath(AdminShortcut), "TAD Admin Dashboard");

    if (File.Exists(InstallBin(DcBinary)))
        MakeShortcut(InstallBin(DcBinary), ShortcutPath(DcShortcut), "TAD Domain Controller Console");
}

static void MakeShortcut(string target, string lnkPath, string description)
{
    // Use PowerShell WScript.Shell COM — no extra dependencies needed
    string ps =
        $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{lnkPath}');" +
        $"$s.TargetPath='{target}';" +
        $"$s.Description='{description}';" +
        $"$s.WorkingDirectory='{Path.GetDirectoryName(target)}';" +
        $"$s.Save()";

    int rc = RunCmd("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"");
    if (rc == 0)
        Console.WriteLine($"  Shortcut: Start Menu \u2192 {AppName} \u2192 {Path.GetFileNameWithoutExtension(lnkPath)}");
    else
        Warn($"Shortcut creation returned {rc} for {Path.GetFileName(lnkPath)}");
}

// ════════════════════════════════════════════════════════════════════════════
// Utilities
// ════════════════════════════════════════════════════════════════════════════

static string GetVersion()
{
    string v = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0";
    v = v.TrimStart('v');
    int di = v.IndexOf('-'); return di > 0 ? v[..di] : v;
}

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

static int RunCmd(string exe, string arguments)
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
        p.WaitForExit(20_000);
        return p.ExitCode;
    }
    catch { return -1; }
}

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"  _____ _   ___  ");
    Console.WriteLine(@" |_   _/_\ |   \ ");
    Console.WriteLine(@"   | |/ _ \| |) |");
    Console.WriteLine(@"   |_/_/ \_\___/ ");
    Console.ResetColor();
    Console.WriteLine($"  Unified Installer  v{GetVersion()}");
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
