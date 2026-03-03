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
//   2. Creates a dedicated local service account  (TAD_Service)
//   3. Adds the account to the local Administrators group
//   4. Grants SeServiceLogonRight via LSA P/Invoke
//   5. Denies interactive logon (SeDenyInteractiveLogonRight) for the account
//   6. Registers TADBridgeService as an auto-start Windows service running
//      under .\TAD_Service, so it starts on every boot automatically
//   7. Starts the service immediately
//
// Usage (requires Administrator):
//   TADClientSetup.exe               → interactive install
//   TADClientSetup.exe --install     → silent install
//   TADClientSetup.exe --uninstall   → remove service + account + files
//   TADClientSetup.exe --status      → print current service status
// ───────────────────────────────────────────────────────────────────────────

using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

// ── Constants ────────────────────────────────────────────────────────────

const string ServiceName    = "TADBridgeService";
const string ServiceDisplay = "TAD.RV Bridge Service";
const string ServiceDesc    = "TAD.RV endpoint protection and remote-view agent. "
                            + "Runs as a dedicated least-privilege service account.";
const string ServiceBinary  = "TADBridgeService.exe";
const string InstallSubDir  = "TAD-RV";
const string ServiceAccount = "TAD_Service";
const string ResourceName   = "bundled_service"; // LogicalName in .csproj

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
    Console.WriteLine($"  Service account: .\\{ServiceAccount}  (local Administrators)");
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
    Step(1, 7, "Extracting service binary...");
    if (!ExtractServiceBinary()) return false;

    Step(2, 7, $"Creating service account '{ServiceAccount}'...");
    string password = GeneratePassword();
    if (!CreateServiceAccount(ServiceAccount, password)) return false;

    Step(3, 7, "Granting SeServiceLogonRight...");
    GrantLsaRight(ServiceAccount, "SeServiceLogonRight");

    Step(4, 7, "Restricting service account logon privileges...");
    GrantLsaRight(ServiceAccount, "SeDenyInteractiveLogonRight");
    GrantLsaRight(ServiceAccount, "SeDenyRemoteInteractiveLogonRight");

    Step(5, 7, "Writing registry entries...");
    WriteRegistry();

    Step(6, 7, "Registering Windows service (autostart on boot)...");
    if (!RegisterService(password)) return false;

    Step(7, 7, "Starting service...");
    StartService();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine();
    Console.WriteLine("  +--------------------------------------------------+");
    Console.WriteLine("  |   TAD.RV endpoint installed successfully!         |");
    Console.WriteLine($"  |   Account  : .\\{ServiceAccount,-20}               |");
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
    Console.WriteLine("[1/4] Stopping service...");
    RunExitCode("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(2000);

    Console.WriteLine("[2/4] Deleting service registration...");
    int rc = RunExitCode("sc.exe", $"delete {ServiceName}");
    if (rc != 0 && rc != 1060 /* ERROR_SERVICE_DOES_NOT_EXIST */)
        Warn($"sc delete returned {rc}");

    Console.WriteLine($"[3/4] Removing service account '{ServiceAccount}'...");
    RunExitCode("net", $"user {ServiceAccount} /delete");

    Console.WriteLine("[4/4] Removing registry entries + files...");
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

// ── Service account ───────────────────────────────────────────────────────

static bool CreateServiceAccount(string username, string password)
{
    // Check whether account already exists
    bool exists = RunExitCode("net", $"user {username}") == 0;

    if (exists)
    {
        Console.WriteLine($"  Account '{username}' already exists — resetting password...");
        RunExitCode("net", $"user {username} \"{password}\"");
    }
    else
    {
        int rc = RunExitCode("net",
            $"user {username} \"{password}\" /add /passwordchg:no /expires:never" +
            $" /comment:\"TAD.RV endpoint service account — do not delete\"");

        if (rc != 0)
        {
            Err($"[ERROR] Could not create account '{username}' (net user exit {rc}).");
            return false;
        }

        // Disable password expiry flag via WMIC (belt-and-suspenders with /expires:never)
        RunExitCode("wmic", $"useraccount where name='{username}' set PasswordExpires=false");

        Console.WriteLine($"  Created local account: {username}");
    }

    // Add to local Administrators group → "system-level" local permissions
    int adminRc = RunExitCode("net", $"localgroup Administrators {username} /add");
    switch (adminRc)
    {
        case 0:    Console.WriteLine($"  '{username}' added to Administrators."); break;
        case 1378: Console.WriteLine($"  '{username}' is already in Administrators."); break;
        default:   Warn($"net localgroup returned {adminRc}"); break;
    }

    return true;
}

// ── LSA rights ────────────────────────────────────────────────────────────

static void GrantLsaRight(string username, string rightName)
{
    try
    {
        LsaHelper.AddRight(username, rightName);
        Console.WriteLine($"  {rightName} granted to {username}.");
    }
    catch (Exception ex)
    {
        Warn($"LSA {rightName}: {ex.Message}");
    }
}

// ── Registry ──────────────────────────────────────────────────────────────

static void WriteRegistry()
{
    try
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\TAD_RV", writable: true);
        key.SetValue("InstallDir",     InstallDir(),         RegistryValueKind.String);
        key.SetValue("ServiceBin",     InstallBin(),         RegistryValueKind.String);
        key.SetValue("ServiceAccount", ServiceAccount,     RegistryValueKind.String);
        key.SetValue("Provisioned",    0,                  RegistryValueKind.DWord);
        key.SetValue("UpdateRepo",     "amiho-dev/TAD-RV", RegistryValueKind.String);
        Console.WriteLine($"  HKLM\\SOFTWARE\\TAD_RV written.");
    }
    catch (Exception ex) { Warn($"Registry (non-fatal): {ex.Message}"); }
}

// ── Service registration ──────────────────────────────────────────────────

static bool RegisterService(string password)
{
    // Idempotent — stop + delete existing instance first
    RunExitCode("sc.exe", $"stop {ServiceName}");
    System.Threading.Thread.Sleep(1000);
    RunExitCode("sc.exe", $"delete {ServiceName}");

    // sc.exe create — auto-start, logon as .\TAD_Service
    // The SCM stores the password in its encrypted registry hive
    int rc = RunExitCode("sc.exe",
        $"create {ServiceName}" +
        $" binPath= \"{InstallBin()}\"" +
        $" start= auto" +
        $" obj= \".\\{ServiceAccount}\"" +
        $" password= \"{password}\"" +
        $" DisplayName= \"{ServiceDisplay}\"");

    if (rc != 0 && rc != 1073 /* ERROR_SERVICE_EXISTS */)
    {
        Err($"[ERROR] sc.exe create failed (exit {rc}). Run 'sc.exe query {ServiceName}' for details.");
        return false;
    }

    RunExitCode("sc.exe", $"description {ServiceName} \"{ServiceDesc}\"");

    // Auto-recovery: restart after 5 s / 10 s / 30 s on failure
    RunExitCode("sc.exe",
        $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");
    RunExitCode("sc.exe", $"failureflag {ServiceName} 1");

    // Unrestricted SID type
    RunExitCode("sc.exe", $"sidtype {ServiceName} unrestricted");

    Console.WriteLine($"  Service '{ServiceName}' registered (start= auto, obj= .\\{ServiceAccount}).");
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

static string GeneratePassword()
{
    const string upper   = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    const string lower   = "abcdefghjkmnpqrstuvwxyz";
    const string digits  = "23456789";
    const string symbols = "!@#$%&*";
    const string all     = upper + lower + digits + symbols;

    using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
    var raw = new byte[32];
    rng.GetBytes(raw);

    char[] pwd = new char[24];
    for (int i = 0; i < pwd.Length; i++)
        pwd[i] = all[raw[i] % all.Length];

    // Guarantee at least one char from each policy category
    rng.GetBytes(raw);
    pwd[0] = upper  [raw[0] % upper.Length];
    pwd[1] = lower  [raw[1] % lower.Length];
    pwd[2] = digits [raw[2] % digits.Length];
    pwd[3] = symbols[raw[3] % symbols.Length];

    // Fisher-Yates shuffle
    rng.GetBytes(raw);
    for (int i = pwd.Length - 1; i > 0; i--)
    {
        int j = raw[i % raw.Length] % (i + 1);
        (pwd[i], pwd[j]) = (pwd[j], pwd[i]);
    }

    return new string(pwd);
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

// ════════════════════════════════════════════════════════════════════════════
// LSA Account Rights  (P/Invoke — advapi32.dll)
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Wraps the Windows LSA policy API to grant privilege rights to a local account.
/// Needed because Windows requires SeServiceLogonRight for any account that a
/// Windows service logs on as — even members of Administrators.
/// </summary>
internal static class LsaHelper
{
    // ── Win32 types ───────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public uint   Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint   Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    // ── P/Invoke declarations ─────────────────────────────────────────────

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaOpenPolicy(
        IntPtr                   SystemName,
        ref LSA_OBJECT_ATTRIBUTES ObjectAttributes,
        uint                     DesiredAccess,
        out IntPtr               PolicyHandle);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaAddAccountRights(
        IntPtr              PolicyHandle,
        IntPtr              AccountSid,
        LSA_UNICODE_STRING[] UserRights,
        uint                CountOfRights);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaClose(IntPtr ObjectHandle);

    [DllImport("advapi32.dll")]
    private static extern uint LsaNtStatusToWinError(uint Status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupAccountName(
        string?       SystemName,
        string        AccountName,
        IntPtr        Sid,
        ref int       SidSize,
        StringBuilder DomainName,
        ref int       DomainNameSize,
        out int       SidNameUse);

    // ── Access masks ──────────────────────────────────────────────────────

    // POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES
    private const uint PolicyWrite = 0x00000010 | 0x00000800;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Grants the named privilege (e.g. "SeServiceLogonRight") to the local account.
    /// Throws <see cref="Win32Exception"/> on failure.
    /// </summary>
    public static void AddRight(string accountName, string privilegeName)
    {
        // ── 1. Resolve account name → SID ────────────────────────────────

        int sidSize   = 0;
        int domainLen = 256;
        var domain    = new StringBuilder(256);

        // First call: get required SID buffer size
        LookupAccountName(null, accountName, IntPtr.Zero, ref sidSize,
                          domain, ref domainLen, out _);

        if (sidSize == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"LookupAccountName(\"{accountName}\") failed");

        IntPtr sid = Marshal.AllocHGlobal(sidSize);
        try
        {
            domainLen = 256;
            if (!LookupAccountName(null, accountName, sid, ref sidSize,
                                   domain, ref domainLen, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"LookupAccountName(\"{accountName}\") failed");

            // ── 2. Open the local LSA policy ──────────────────────────────

            var attrs = new LSA_OBJECT_ATTRIBUTES
            {
                Length = (uint)Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>()
            };

            uint st = LsaOpenPolicy(IntPtr.Zero, ref attrs, PolicyWrite, out IntPtr policy);
            if (st != 0)
                throw new Win32Exception((int)LsaNtStatusToWinError(st),
                    $"LsaOpenPolicy failed (NTSTATUS 0x{st:X8})");

            try
            {
                // ── 3. Build LSA_UNICODE_STRING for the right name ────────

                IntPtr buf = Marshal.StringToHGlobalUni(privilegeName);
                try
                {
                    var rights = new LSA_UNICODE_STRING[]
                    {
                        new()
                        {
                            Length        = (ushort)(privilegeName.Length * 2),
                            MaximumLength = (ushort)((privilegeName.Length + 1) * 2),
                            Buffer        = buf,
                        }
                    };

                    // ── 4. Grant the right ────────────────────────────────

                    st = LsaAddAccountRights(policy, sid, rights, 1);
                    if (st != 0)
                        throw new Win32Exception((int)LsaNtStatusToWinError(st),
                            $"LsaAddAccountRights(\"{privilegeName}\") NTSTATUS 0x{st:X8}");
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { LsaClose(policy); }
        }
        finally { Marshal.FreeHGlobal(sid); }
    }
}
