// ───────────────────────────────────────────────────────────────────────────
// EmulatedProvisioningManager.cs — Mock provisioning for demo / emulation
//
// Activated via: TadBridgeService.exe --emulate
//
// Returns a sensible default policy without contacting a Domain Controller
// or touching the registry.  Perfect for demos and integration testing.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Logging;
using TadBridge.Shared;

namespace TadBridge.Provisioning;

/// <summary>
/// Drop-in replacement for <see cref="ProvisioningManager"/> that returns
/// default policy data without contacting AD or reading the registry.
/// </summary>
public sealed class EmulatedProvisioningManager : ProvisioningManager
{
    private readonly ILogger _log;

    public EmulatedProvisioningManager(ILogger<ProvisioningManager> logger)
        : base(logger)
    {
        _log = logger;
    }

    /// <summary>
    /// Returns a default policy immediately — no AD, no LDAP, no registry.
    /// </summary>
    public override Task<TadPolicyBuffer?> EnsureProvisionedAsync(CancellationToken ct)
    {
        _log.LogInformation("[EMULATED] Provisioning skipped — returning default policy");

        var defaultConfig = TadPolicyConfig.Default;

        var policy = new TadPolicyBuffer
        {
            Version             = (uint)defaultConfig.Version,
            Flags               = (uint)defaultConfig.Flags,
            HeartbeatIntervalMs = (uint)defaultConfig.HeartbeatIntervalMs,
            HeartbeatTimeoutMs  = (uint)defaultConfig.HeartbeatTimeoutMs,
            OrganizationalUnit  = "OU=Demo,OU=TAD,DC=school,DC=local",
            AllowedRoles        = (uint)defaultConfig.AllowedUnloadRoles,
            Reserved            = new uint[8]
        };

        return Task.FromResult<TadPolicyBuffer?>(policy);
    }
}
