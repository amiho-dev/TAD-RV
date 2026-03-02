// ───────────────────────────────────────────────────────────────────────────
// ProvisioningManager.cs — First-Boot AD + Policy Provisioning
//
// On service startup, checks HKLM\SOFTWARE\TAD_RV\Provisioned.
// If 0 (or missing), performs:
//   1. Retrieves the machine's Distinguished Name and OU from AD
//   2. Fetches Policy.json from a network share (or AD attribute)
//   3. Stores settings in the registry
//   4. Sets Provisioned = 1
//
// On subsequent boots, loads the cached policy from registry and returns it.
// ───────────────────────────────────────────────────────────────────────────

using System.DirectoryServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TadBridge.Shared;

namespace TadBridge.Provisioning;

/// <summary>
/// Manages first-boot AD provisioning and policy retrieval.
/// </summary>
public class ProvisioningManager
{
    private const string RegistryKeyPath     = @"SOFTWARE\TAD_RV";
    private const string ProvisionedValue    = "Provisioned";
    private const string MachineDnValue      = "MachineDN";
    private const string OuValue             = "OrganizationalUnit";
    private const string PolicyJsonValue     = "PolicyJson";
    private const string PolicyVersionValue  = "PolicyVersion";

    // Hardcoded network share for Policy.json — customise per school district
    private const string PolicyNetworkPath   = @"\\dc01.school.local\NETLOGON\TAD\Policy.json";

    private readonly ILogger<ProvisioningManager> _log;

    public ProvisioningManager(ILogger<ProvisioningManager> logger)
    {
        _log = logger;
    }

    /// <summary>
    /// Ensures the machine is provisioned.  Returns the active policy.
    /// </summary>
    public virtual async Task<TadPolicyBuffer?> EnsureProvisionedAsync(CancellationToken ct)
    {
        bool provisioned = IsProvisioned();

        if (!provisioned)
        {
            _log.LogInformation("First-boot detected — starting AD provisioning…");
            await ProvisionFromAdAsync(ct);
        }
        else
        {
            _log.LogInformation("Machine already provisioned — loading cached policy");
        }

        return LoadPolicyFromRegistry();
    }

    // ─── Registry Helpers ────────────────────────────────────────────

    private static bool IsProvisioned()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, writable: false);
        if (key == null) return false;

        object? val = key.GetValue(ProvisionedValue);
        return val is int i && i == 1;
    }

    private static void SetProvisioned()
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath, writable: true);
        key.SetValue(ProvisionedValue, 1, RegistryValueKind.DWord);
    }

    // ─── AD Lookup ───────────────────────────────────────────────────

    private async Task ProvisionFromAdAsync(CancellationToken ct)
    {
        string machineDn = string.Empty;
        string ouDn      = string.Empty;

        try
        {
            // Bind to the local computer's AD object using ADSI
            using var rootDse = new DirectoryEntry("LDAP://RootDSE");
            string defaultNc = rootDse.Properties["defaultNamingContext"].Value?.ToString()
                               ?? string.Empty;

            // Search for this computer object
            string hostname = Environment.MachineName;
            using var searcher = new DirectorySearcher(
                new DirectoryEntry($"LDAP://{defaultNc}"))
            {
                Filter = $"(&(objectClass=computer)(cn={hostname}))",
                PropertiesToLoad = { "distinguishedName" }
            };

            SearchResult? result = searcher.FindOne();
            if (result != null)
            {
                machineDn = result.Properties["distinguishedName"][0]?.ToString()
                            ?? string.Empty;

                // Extract the OU from the DN (everything after the first comma)
                int commaIndex = machineDn.IndexOf(',');
                if (commaIndex >= 0)
                {
                    ouDn = machineDn[(commaIndex + 1)..];
                }
            }

            _log.LogInformation("AD lookup: Machine DN = {Dn}, OU = {Ou}", machineDn, ouDn);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AD lookup failed — machine may not be domain-joined");
        }

        // ── Fetch Policy.json ────────────────────────────────────────
        string policyJson = string.Empty;
        TadPolicyConfig? policyConfig = null;

        try
        {
            // Try network path first
            if (File.Exists(PolicyNetworkPath))
            {
                policyJson = await File.ReadAllTextAsync(PolicyNetworkPath, ct);
                _log.LogInformation("Loaded Policy.json from {Path}", PolicyNetworkPath);
            }
            else
            {
                _log.LogWarning("Policy.json not found at {Path} — using defaults", PolicyNetworkPath);
                policyConfig = TadPolicyConfig.Default;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read Policy.json — using defaults");
            policyConfig = TadPolicyConfig.Default;
        }

        if (policyConfig == null && !string.IsNullOrEmpty(policyJson))
        {
            try
            {
                policyConfig = JsonSerializer.Deserialize<TadPolicyConfig>(policyJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _log.LogError(ex, "Invalid Policy.json — using defaults");
                policyConfig = TadPolicyConfig.Default;
            }
        }

        policyConfig ??= TadPolicyConfig.Default;

        // ── Store in registry ────────────────────────────────────────
        using (var key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath, writable: true))
        {
            key.SetValue(MachineDnValue,     machineDn,  RegistryValueKind.String);
            key.SetValue(OuValue,            ouDn,       RegistryValueKind.String);
            key.SetValue(PolicyJsonValue,    JsonSerializer.Serialize(policyConfig), RegistryValueKind.String);
            key.SetValue(PolicyVersionValue, policyConfig.Version, RegistryValueKind.DWord);
        }

        SetProvisioned();

        _log.LogInformation("Provisioning complete — stored in HKLM\\{Key}", RegistryKeyPath);
    }

    // ─── Load Cached Policy ──────────────────────────────────────────

    private TadPolicyBuffer? LoadPolicyFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, writable: false);
            if (key == null) return null;

            string? json = key.GetValue(PolicyJsonValue) as string;
            string? ou   = key.GetValue(OuValue) as string;

            if (string.IsNullOrEmpty(json)) return null;

            var config = JsonSerializer.Deserialize<TadPolicyConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config == null) return null;

            return new TadPolicyBuffer
            {
                Version              = (uint)config.Version,
                Flags                = (uint)config.Flags,
                HeartbeatIntervalMs  = (uint)config.HeartbeatIntervalMs,
                HeartbeatTimeoutMs   = (uint)config.HeartbeatTimeoutMs,
                OrganizationalUnit   = ou ?? string.Empty,
                AllowedRoles         = (uint)config.AllowedUnloadRoles,
                Reserved             = new uint[8]
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load policy from registry");
            return null;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Policy.json Schema
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Maps to the Policy.json file on the NETLOGON share.
/// </summary>
public sealed class TadPolicyConfig
{
    public int    Version             { get; set; } = 1;
    public int    Flags               { get; set; } = 0;
    public int    HeartbeatIntervalMs { get; set; } = 2000;
    public int    HeartbeatTimeoutMs  { get; set; } = 6000;
    public int    AllowedUnloadRoles  { get; set; } = 0x04;  // Admin only

    /// <summary>AD group → role mapping.</summary>
    public Dictionary<string, int> GroupRoleMappings { get; set; } = new()
    {
        ["Domain Students"]      = 0,   // TadRoleStudent
        ["Domain Teachers"]      = 1,   // TadRoleTeacher
        ["Domain Admins"]        = 2,   // TadRoleAdmin
        ["TAD-Administrators"]   = 2,
    };

    /// <summary>Blocked executable names (when BLOCK_APPS flag is set).</summary>
    public List<string> BlockedApplications { get; set; } = new()
    {
        "taskmgr.exe",
        "cmd.exe",
        "powershell.exe",
        "pwsh.exe",
        "regedit.exe",
    };

    public static TadPolicyConfig Default => new();
}
