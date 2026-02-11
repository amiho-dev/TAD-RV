// ───────────────────────────────────────────────────────────────────────────
// RegistryService.cs — Registry helper for TAD.RV configuration
//
// Reads and writes the HKLM\SOFTWARE\TAD_RV registry key used by
// both the kernel driver and the bridge service.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Win32;

namespace TadConsole.Services;

/// <summary>
/// Current TAD.RV registry configuration snapshot.
/// </summary>
public sealed class TadRegistryConfig
{
    public string  InstallDir        { get; set; } = "";
    public string  DomainController  { get; set; } = "";
    public string  DeployedAt        { get; set; } = "";
    public bool    Provisioned       { get; set; }
    public string  MachineDN         { get; set; } = "";
    public string  OrganizationalUnit { get; set; } = "";
    public string  PolicyJson        { get; set; } = "";
    public int     PolicyVersion     { get; set; }
    public bool    KeyExists         { get; set; }
}

public sealed class RegistryService
{
    private const string RegPath = @"SOFTWARE\TAD_RV";

    /// <summary>
    /// Reads the entire TAD.RV registry configuration.
    /// </summary>
    public TadRegistryConfig ReadConfig()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegPath, writable: false);
            if (key == null)
            {
                return new TadRegistryConfig { KeyExists = false };
            }

            return new TadRegistryConfig
            {
                KeyExists           = true,
                InstallDir          = key.GetValue("InstallDir")?.ToString()       ?? "",
                DomainController    = key.GetValue("DomainController")?.ToString() ?? "",
                DeployedAt          = key.GetValue("DeployedAt")?.ToString()       ?? "",
                Provisioned         = (key.GetValue("Provisioned") is int val) && val == 1,
                MachineDN           = key.GetValue("MachineDN")?.ToString()        ?? "",
                OrganizationalUnit  = key.GetValue("OrganizationalUnit")?.ToString() ?? "",
                PolicyJson          = key.GetValue("PolicyJson")?.ToString()       ?? "",
                PolicyVersion       = key.GetValue("PolicyVersion") is int pv ? pv : 0,
            };
        }
        catch
        {
            return new TadRegistryConfig { KeyExists = false };
        }
    }

    /// <summary>
    /// Writes domain controller and install directory to the registry.
    /// </summary>
    public void WriteConfig(string installDir, string domainController)
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegPath, writable: true);
        key.SetValue("InstallDir",       installDir,       RegistryValueKind.String);
        key.SetValue("DomainController", domainController, RegistryValueKind.String);
        key.SetValue("DeployedAt",       DateTime.Now.ToString("o"), RegistryValueKind.String);
    }

    /// <summary>
    /// Writes a Policy.json string to the registry.
    /// </summary>
    public void WritePolicyJson(string policyJson, int version)
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegPath, writable: true);
        key.SetValue("PolicyJson",    policyJson, RegistryValueKind.String);
        key.SetValue("PolicyVersion", version,    RegistryValueKind.DWord);
    }

    /// <summary>
    /// Resets the provisioning flag to force re-provisioning on next service start.
    /// </summary>
    public void ResetProvisioning()
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegPath, writable: true);
        key.SetValue("Provisioned", 0, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Deletes the entire TAD_RV registry key (full uninstall).
    /// </summary>
    public void DeleteAll()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(RegPath, throwOnMissingSubKey: false);
        }
        catch { }
    }
}
