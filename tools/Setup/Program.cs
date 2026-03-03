// ─────────────────────────────────────────────────────────────────────────────
// Program.cs — TAD Installer  (CLI, single-file, self-contained)
//
// Build THREE times — one EXE per component:
//   -p:SetupTarget=Client          → TADClientSetup.exe       (service)
//   -p:SetupTarget=Admin           → TADAdminSetup.exe        (admin app)
//   -p:SetupTarget=DomainController → TADDomainControllerSetup.exe (DC app)
//
// Usage (installer auto-elevates via UAC on Windows):
//   *Setup.exe               → interactive install
//   *Setup.exe --install     → silent install (no prompts)
//   *Setup.exe --uninstall   → remove
//   *Setup.exe --status      → show service status  (Client only)
// ─────────────────────────────────────────────────────────────────────────────

// Suppress dead-code warnings caused by compile-time const bool flags
// (IsService, CreateShortcut) and target-specific functions only called
// in #if blocks — all intentional.
#pragma warning disable CS0162, CS0219, CS8321

using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

// ── Compile-time target configuration ────────────────────────────────────────

#if SETUP_ADMIN
const string AppDisplayName  = "TAD Admin";
const string BinaryName      = "TADAdmin.exe";
const string ResourceName    = "bundled_admin";
const string SetupBinaryName = "TADAdminSetup.exe";
const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD.Admin";
const bool   IsService       = false;
const bool   CreateShortcut  = true;
const string ShortcutName    = "TAD Admin.lnk";
const string ShortcutDesc    = "TAD Admin Dashboard";
#elif SETUP_DC
const string AppDisplayName  = "TAD Domain Controller";
const string BinaryName      = "TADDomainController.exe";
const string ResourceName    = "bundled_dc";
const string SetupBinaryName = "TADDomainControllerSetup.exe";
const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD.DomainController";
const bool   IsService       = false;
const bool   CreateShortcut  = true;
const string ShortcutName    = "TAD Domain Controller.lnk";
const string ShortcutDesc    = "TAD Domain Controller Console";
#else  // SETUP_CLIENT (default)
const string AppDisplayName  = "TAD Client";
const string BinaryName      = "TADBridgeService.exe";
const string ResourceName    = "bundled_service";
const string SetupBinaryName = "TADClientSetup.exe";
const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TAD.Client";
const bool   IsService       = true;
const bool   CreateShortcut  = false;
const string ShortcutName    = "";
const string ShortcutDesc    = "";
#endif

// ── Service constants (Client only, unused by Admin/DC but must compile) ─────
const string ServiceName     = "TADBridgeService";
const string ServiceDisplay  = "TAD Endpoint Service";
const string ServiceDesc     = "TAD endpoint agent — remote view, screen capture and policy enforcement.";
const string VirtualAccount  = @"NT SERVICE\TADBridgeService";
const string ServiceRegKey   = @"SOFTWARE\TAD_RV";
const string StartMenuFolder = "TAD";

// ── Paths ─────────────────────────────────────────────────────────────────────
static string InstallDir() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TAD");
static string InstallBin(string bin) => Path.Combine(InstallDir(), bin);
static string StartMenuDir() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), StartMenuFolder);

// ── Argument parsing ──────────────────────────────────────────────────────────
bool silent     = args.Any(a => a.Equals("--install",   StringComparison.OrdinalIgnoreCase));
bool uninstall  = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
bool statusOnly = args.Any(a => a.Equals("--status",    StringComparison.OrdinalIgnoreCase));

PrintBanner();

// ── Auto-elevate via UAC ──────────────────────────────────────────────────────
if (!IsAdmin())
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
        Err("Administrator privileges required. Run as Administrator.");
        if (!silent) Pause();
        return 2;
    }
}

// ── Dispatch ──────────────────────────────────────────────────────────────────
if (statusOnly)
{
#if !SETUP_CLIENT
    Warn("--status is only available for the Client installer.");
#else
    PrintServiceStatus();
#endif
    return 0;
}

if (uninstall)
{
    return RunUninstall() ? 0 : 1;
}

if (!silent)
{
    Console.WriteLine($"  Component    : {AppDisplayName}");
    Console.WriteLine($"  Install path : {InstallDir()}");
    if (IsService)
        Console.WriteLine($"  Service      : {ServiceName}  →  {VirtualAccount}  (delayed-auto)");
    if (CreateShortcut)
        Console.WriteLine($"  Start Menu   : {StartMenuFolder} → {Path.GetFileNameWithoutExtension(ShortcutName)}");
    Console.WriteLine();
    Console.Write("Proceed? [Y/n]: ");
    var answer = Console.ReadLine()?.Trim().ToUpperInvariant();
    if (answer is "N" or "NO") { Console.WriteLine("Cancelled."); return 0; }
    Console.WriteLine();
}

return RunInstall() ? 0 : 1;

// ═════════════════════════════════════════════════════════════════════════════
// INSTALL
// ═════════════════════════════════════════════════════════════════════════════

static bool RunInstall()
{
#if SETUP_CLIENT
    int totalSteps = 7;
#else
    int totalSteps = CreateShortcut ? 3 : 2;
#endif

    Step(1, totalSteps, $"Extracting  {BinaryName}  →  {InstallDir()}");
    if (!ExtractBinary()) return false;
    CopySelf();

    Step(2, totalSteps, "Registering in Programs & Features (Add/Remove Programs)...");
    WriteUninstallEntry();

#if SETUP_CLIENT
    Step(3, totalSteps, "Writing service registry entries...");
    WriteServiceRegistry();

    Step(4, totalSteps, $"Registering Windows service  '{ServiceName}'  (delayed-auto / {VirtualAccount})...");
    if (!RegisterService()) return false;

    Step(5, totalSteps, $"Starting  '{ServiceName}'...");
    StartService();

    Step(6, totalSteps, "Configuring Windows Firewall rules...");
    RegisterFirewallRules();

    Step(7, totalSteps, "Registering tray icon (auto-start at login)...");
    AddTrayRunKey();
#else
    if (CreateShortcut)
    {
        Step(3, totalSteps, $"Creating Start Menu shortcut  →  {StartMenuFolder}\\{Path.GetFileNameWithoutExtension(ShortcutName)}");
        CreateStartMenuShortcut();
    }
#endif

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  {AppDisplayName} installed successfully.");
    Console.ResetColor();
    Console.WriteLine($"  Location : {InstallDir()}");
    if (IsService)
    {
        Console.WriteLine();
        PrintServiceStatus();
    }
    return true;
}

// ═════════════════════════════════════════════════════════════════════════════
// UNINSTALL
// ═════════════════════════════════════════════════════════════════════════════

static bool RunUninstall()
{
    string installDir = GetInstalledDir() ?? InstallDir();

#if SETUP_CLIENT
    Step(1, 5, $"Stopping service  '{ServiceName}'...");
    RunVerbose("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(2000);

    Step(2, 5, $"Deleting service registration  '{ServiceName}'...");
    int rc = RunVerbose("sc.exe", $"delete {ServiceName}");
    if (rc != 0 && rc != 1060) Warn($"sc delete returned exit {rc}");
#else
    Step(1, 4, $"Terminating {BinaryName} (if running)...");
    RunVerbose("taskkill", $"/f /im {BinaryName}");

    Step(2, 4, "Removing Start Menu shortcuts...");
    RemoveShortcuts();
#endif

#if SETUP_CLIENT
    Step(3, 5, "Removing firewall rules + tray Run key...");
    RunVerbose("netsh", "advfirewall firewall delete rule name=\"TAD.RV Client (TCP 17420)\"");
    RunVerbose("netsh", "advfirewall firewall delete rule name=\"TAD.RV Client (UDP 17421)\"");
    try
    {
        using var runKey = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        runKey?.DeleteValue("TAD.RV Tray", false);
        Ok("Tray Run key removed.");
    }
    catch (Exception ex) { Warn($"Tray Run key removal: {ex.Message}"); }

    Step(4, 5, "Removing registry entries...");
    try { Registry.LocalMachine.DeleteSubKeyTree(UninstallSubKey, throwOnMissingSubKey: false); Ok("Uninstall registry key removed."); } catch (Exception ex) { Warn(ex.Message); }
    try { Registry.LocalMachine.DeleteSubKeyTree(ServiceRegKey, throwOnMissingSubKey: false); Ok("Service registry key removed."); } catch (Exception ex) { Warn(ex.Message); }
    RemoveShortcuts(); // clean up if Admin/DC were also here

    Step(5, 5, $"Removing files from  {installDir}...");
#else
    Step(3, 4, "Removing registry entries...");
    try { Registry.LocalMachine.DeleteSubKeyTree(UninstallSubKey, throwOnMissingSubKey: false); Ok("Uninstall registry key removed."); } catch (Exception ex) { Warn(ex.Message); }

    Step(4, 4, $"Removing files from  {installDir}...");
#endif
    RunVerbose("taskkill", $"/f /im {BinaryName}");
    System.Threading.Thread.Sleep(500);
    RemoveInstallDir(installDir);

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  {AppDisplayName} uninstalled.");
    Console.ResetColor();
    return true;
}

// ═════════════════════════════════════════════════════════════════════════════
// FILE EXTRACTION
// ═════════════════════════════════════════════════════════════════════════════

static bool ExtractBinary()
{
    try
    {
        Directory.CreateDirectory(InstallDir());
        var asm = Assembly.GetExecutingAssembly();
        string? name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.Equals(ResourceName, StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            // Fallback: binary already present (dev build without staged resource)
            string existing = InstallBin(BinaryName);
            if (!File.Exists(existing))
            {
                Err($"Resource '{ResourceName}' not embedded and '{BinaryName}' not found at install path.");
                return false;
            }
            Warn($"Resource '{ResourceName}' not embedded — using existing binary at install path.");
            return true;
        }

        string dest = InstallBin(BinaryName);
        using var src = asm.GetManifestResourceStream(name)!;
        using var dst = File.Create(dest);
        src.CopyTo(dst);
        long kb = new FileInfo(dest).Length / 1024;
        Ok($"{BinaryName}  →  {dest}  ({kb:N0} KB)");
        return true;
    }
    catch (Exception ex)
    {
        Err($"Extraction failed: {ex.Message}");
        return false;
    }
}

static void CopySelf()
{
    try
    {
        string? self = Environment.ProcessPath;
        if (self is null) return;
        string dest = InstallBin(SetupBinaryName);
        if (!string.Equals(self, dest, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, dest, overwrite: true);
            Ok($"{SetupBinaryName}  →  {dest}  (uninstaller copy)");
        }
    }
    catch (Exception ex) { Warn($"Could not copy installer for uninstall: {ex.Message}"); }
}

// ═════════════════════════════════════════════════════════════════════════════
// REGISTRY
// ═════════════════════════════════════════════════════════════════════════════

static void WriteUninstallEntry()
{
    try
    {
        string version = GetVersion();
        using var key = Registry.LocalMachine.CreateSubKey(UninstallSubKey, writable: true);
        key.SetValue("DisplayName",         AppDisplayName,                                         RegistryValueKind.String);
        key.SetValue("DisplayVersion",       version,                                                RegistryValueKind.String);
        key.SetValue("Publisher",            "TAD Europe",                                           RegistryValueKind.String);
        key.SetValue("InstallLocation",      InstallDir(),                                           RegistryValueKind.String);
        key.SetValue("DisplayIcon",          $"{InstallBin(BinaryName)},0",                         RegistryValueKind.String);
        key.SetValue("UninstallString",      $"\"{InstallBin(SetupBinaryName)}\" --uninstall",      RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{InstallBin(SetupBinaryName)}\" --uninstall --install", RegistryValueKind.String);
        key.SetValue("NoModify",             1,                                                      RegistryValueKind.DWord);
        key.SetValue("NoRepair",             1,                                                      RegistryValueKind.DWord);
        Ok($"HKLM\\{UninstallSubKey}  written.");
    }
    catch (Exception ex) { Warn($"Programs & Features entry (non-fatal): {ex.Message}"); }
}

static void WriteServiceRegistry()
{
    try
    {
        using var key = Registry.LocalMachine.CreateSubKey(ServiceRegKey, writable: true);
        key.SetValue("InstallDir",     InstallDir(),          RegistryValueKind.String);
        key.SetValue("ServiceBin",     InstallBin(BinaryName), RegistryValueKind.String);
        key.SetValue("ServiceAccount", VirtualAccount,        RegistryValueKind.String);
        key.SetValue("Provisioned",    0,                     RegistryValueKind.DWord);
        key.SetValue("UpdateRepo",     "amiho-dev/TAD-RV",   RegistryValueKind.String);
        Ok($@"HKLM\{ServiceRegKey}  written.");
    }
    catch (Exception ex) { Warn($"Service registry (non-fatal): {ex.Message}"); }
}

// ═════════════════════════════════════════════════════════════════════════════
// WINDOWS SERVICE  (Client only)
// ═════════════════════════════════════════════════════════════════════════════

static bool RegisterService()
{
    // Clean up any existing stale registration
    RunVerbose("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(1000);
    RunVerbose("sc.exe", $"delete {ServiceName}");
    System.Threading.Thread.Sleep(500);

    int rc = RunVerbose("sc.exe",
        $"create {ServiceName}" +
        $" binPath= \"{InstallBin(BinaryName)}\"" +
        $" start= auto" +
        $" obj= \"{VirtualAccount}\"" +
        $" DisplayName= \"{ServiceDisplay}\"");

    if (rc != 0 && rc != 1073)
    {
        Err($"sc.exe create failed (exit {rc}) — service not registered.");
        return false;
    }

    int adminRc = RunVerbose("net", $"localgroup Administrators \"{VirtualAccount}\" /add");
    if      (adminRc == 0)    Ok($"'{VirtualAccount}'  added to local Administrators.");
    else if (adminRc == 1378) Ok($"'{VirtualAccount}'  already in local Administrators.");
    else                      Warn($"net localgroup returned exit {adminRc}");

    RunVerbose("sc.exe", $"description {ServiceName} \"{ServiceDesc}\"");
    RunVerbose("sc.exe", $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");
    RunVerbose("sc.exe", $"failureflag {ServiceName} 1");
    RunVerbose("sc.exe", $"sidtype {ServiceName} unrestricted");
    Ok($"'{ServiceName}'  registered  (delayed-auto / {VirtualAccount}).");
    return true;
}

static void StartService()
{
    int rc = RunVerbose("sc.exe", $"start {ServiceName}");
    switch (rc)
    {
        case    0: Ok("Service started."); break;
        case 1056: Ok("Service already running."); break;
        default:   Warn($"sc start returned exit {rc} — check Event Viewer."); break;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// FIREWALL RULES  (Client only)
// ═════════════════════════════════════════════════════════════════════════════

#if SETUP_CLIENT
static void RegisterFirewallRules()
{
    // Remove any stale rules from previous installs first (ignore errors)
    RunVerbose("netsh", "advfirewall firewall delete rule name=\"TAD.RV Client (TCP 17420)\"");
    RunVerbose("netsh", "advfirewall firewall delete rule name=\"TAD.RV Client (UDP 17421)\"");

    // TCP 17420 inbound — teacher → student command channel
    int rc1 = RunVerbose("netsh",
        "advfirewall firewall add rule " +
        "name=\"TAD.RV Client (TCP 17420)\" " +
        "protocol=TCP dir=in localport=17420 " +
        "action=allow enable=yes profile=any " +
        $"program=\"{InstallBin(BinaryName)}\" " +
        "description=\"TAD.RV student endpoint — teacher command channel\"");
    if (rc1 == 0) Ok("Firewall rule added: TCP 17420 inbound (teacher commands).");
    else          Warn($"netsh TCP rule returned exit {rc1}");

    // UDP 17421 inbound — multicast discovery heartbeats
    int rc2 = RunVerbose("netsh",
        "advfirewall firewall add rule " +
        "name=\"TAD.RV Client (UDP 17421)\" " +
        "protocol=UDP dir=in localport=17421 " +
        "action=allow enable=yes profile=any " +
        $"program=\"{InstallBin(BinaryName)}\" " +
        "description=\"TAD.RV student endpoint — multicast discovery\"");
    if (rc2 == 0) Ok("Firewall rule added: UDP 17421 inbound (multicast discovery).");
    else          Warn($"netsh UDP rule returned exit {rc2}");
}

static void AddTrayRunKey()
{
    // HKLM Run key → runs TADBridgeService.exe --tray at every user logon
    // This shows a tray icon in the user's taskbar reporting the service status.
    // Runs in the user's interactive session (not Session 0 like the service).
    try
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        string value = $"\"{InstallBin(BinaryName)}\" --tray";
        key.SetValue("TAD.RV Tray", value, RegistryValueKind.String);
        Ok(@"HKLM\...\Run  →  TAD.RV Tray  (tray icon at user logon).");
    }
    catch (Exception ex) { Warn($"Tray Run key (non-fatal): {ex.Message}"); }
}
#endif


#if !SETUP_CLIENT
static void CreateStartMenuShortcut()
{
    try
    {
        Directory.CreateDirectory(StartMenuDir());
        string lnkPath = Path.Combine(StartMenuDir(), ShortcutName);
        string target  = InstallBin(BinaryName);
        string workDir = InstallDir();
        string desc    = ShortcutDesc;

        // Build the PS script first, then Base64-encode it.
        // This completely avoids quoting/backslash issues with -Command "...".
        string psScript =
            $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut([string]'{EscapePs(lnkPath)}');\n" +
            $"$s.TargetPath      = [string]'{EscapePs(target)}';\n" +
            $"$s.WorkingDirectory = [string]'{EscapePs(workDir)}';\n" +
            $"$s.Description     = [string]'{EscapePs(desc)}';\n" +
            "$s.Save();\n";

        // PowerShell -EncodedCommand expects UTF-16LE Base64
        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
        int rc = RunVerbose("powershell", $"-NoProfile -NonInteractive -EncodedCommand {encoded}");
        if (rc == 0)
            Ok($"Start Menu shortcut  →  {lnkPath}");
        else
            Warn($"Shortcut creation returned exit {rc}");
    }
    catch (Exception ex) { Warn($"Shortcut (non-fatal): {ex.Message}"); }
}

// Escape single quotes inside a PS single-quoted string
static string EscapePs(string s) => s.Replace("'", "''");
#endif

static void RemoveShortcuts()
{
    try
    {
        string dir = StartMenuDir();
        if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); Ok($"Removed: {dir}"); }
    }
    catch (Exception ex) { Warn($"Shortcut removal: {ex.Message}"); }
}

// ═════════════════════════════════════════════════════════════════════════════
// FILE REMOVAL
// ═════════════════════════════════════════════════════════════════════════════

static void RemoveInstallDir(string installDir)
{
    try
    {
        if (!Directory.Exists(installDir)) return;
        string? self = Environment.ProcessPath;
        if (self is not null && self.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
        {
            string bat = Path.Combine(Path.GetTempPath(), "tad_cleanup.bat");
            File.WriteAllText(bat,
                "@echo off\r\ntimeout /t 2 /nobreak >nul\r\n" +
                $"rd /s /q \"{installDir}\"\r\ndel \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                { CreateNoWindow = true, UseShellExecute = false });
            Ok("Folder removal scheduled — runs after this window closes.");
        }
        else
        {
            Directory.Delete(installDir, recursive: true);
            Ok($"Removed: {installDir}");
        }
    }
    catch (Exception ex) { Warn($"File removal: {ex.Message}"); }
}

// ═════════════════════════════════════════════════════════════════════════════
// UTILITIES
// ═════════════════════════════════════════════════════════════════════════════

static string? GetInstalledDir()
{
    try
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallSubKey);
        return key?.GetValue("InstallLocation") as string;
    }
    catch { return null; }
}

static string GetVersion()
{
    string v = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0";
    v = v.TrimStart('v');
    int d = v.IndexOf('-');
    return d > 0 ? v[..d] : v;
}

// ── RunVerbose: runs a process and prints its stdout/stderr line by line ──────
static int RunVerbose(string exe, string arguments)
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

        // Read both streams to avoid deadlocks
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(20_000);

        // Print any output indented
        foreach (var line in (stdout + "\n" + stderr).Split('\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    " + line);
            Console.ResetColor();
        }

        return p.ExitCode;
    }
    catch (Exception ex)
    {
        Warn($"{exe}: {ex.Message}");
        return -1;
    }
}

static void PrintServiceStatus()
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Service status — '{ServiceName}':");
    Console.ResetColor();
    RunVerbose("sc.exe", $"query {ServiceName}");
}

static void Step(int n, int total, string desc)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"[{n}/{total}] ");
    Console.ResetColor();
    Console.WriteLine(desc);
}

static void Ok(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  ✓ " + msg);
    Console.ResetColor();
}

static void Err(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("  [ERROR] " + msg);
    Console.ResetColor();
}

static void Warn(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine("  [WARN]  " + msg);
    Console.ResetColor();
}

static void Pause()
{
    Console.WriteLine("  Press any key to exit...");
    Console.ReadKey(true);
}

static bool IsAdmin()
{
    try
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
    catch { return false; }
}

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"  _____ _   ___  ");
    Console.WriteLine(@" |_   _/_\ |   \ ");
    Console.WriteLine(@"   | |/ _ \| |) |");
    Console.WriteLine(@"   |_/_/ \_\___/ ");
    Console.ResetColor();
    Console.WriteLine($"  {AppDisplayName} Setup  v{GetVersion()}");
    Console.WriteLine("  (C) 2026 TAD Europe");
    Console.WriteLine();
}
