// ───────────────────────────────────────────────────────────────────────────
// AdGroupWatcher.cs — Active Directory Group → Role Resolution
//
// Uses System.DirectoryServices.AccountManagement to:
//   1. Identify the currently logged-in interactive user
//   2. Enumerate their AD group memberships
//   3. Map groups to a TAD_USER_ROLE (Student / Teacher / Admin)
//   4. Return the result for the service to push via IOCTL to the driver
//
// Falls back to the OfflineCacheManager when a Domain Controller is
// unreachable.
// ───────────────────────────────────────────────────────────────────────────

using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TadBridge.Cache;
using TadBridge.Provisioning;
using TadBridge.Shared;

namespace TadBridge.ActiveDirectory;

/// <summary>
/// Resolves the interactive user's AD groups and maps them to a TAD role.
/// </summary>
public sealed class AdGroupWatcher
{
    private readonly ILogger<AdGroupWatcher>  _log;
    private readonly OfflineCacheManager      _cache;

    private string? _lastResolvedSid;
    private TadUserRole _lastRole = TadUserRole.Unknown;
    private Dictionary<string, int>? _groupMappings;

    public AdGroupWatcher(
        ILogger<AdGroupWatcher> logger,
        OfflineCacheManager     cache)
    {
        _log   = logger;
        _cache = cache;
        LoadGroupMappings();
    }

    /// <summary>
    /// Resolves the current interactive user's role from AD groups.
    /// Returns (role, sessionId, userSid).
    /// </summary>
    public (TadUserRole Role, uint SessionId, string Sid) ResolveCurrentUser()
    {
        // Find the active console session
        uint sessionId = NativeSession.WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            return (TadUserRole.Unknown, 0, string.Empty);
        }

        try
        {
            return ResolveFromAd(sessionId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AD resolution failed — falling back to offline cache");
            return ResolveFromCache(sessionId);
        }
    }

    // ─── AD Resolution ───────────────────────────────────────────────

    private (TadUserRole, uint, string) ResolveFromAd(uint sessionId)
    {
        using var context = new PrincipalContext(ContextType.Domain);
        using var user    = UserPrincipal.Current;

        if (user == null)
        {
            _log.LogDebug("No domain user found for session {Session}", sessionId);
            return (TadUserRole.Unknown, sessionId, string.Empty);
        }

        string sid = user.Sid?.Value ?? string.Empty;

        // Get all group memberships (recursive — includes nested groups)
        var groups = new List<string>();
        using var groupCollection = user.GetAuthorizationGroups();
        foreach (var principal in groupCollection)
        {
            if (principal is GroupPrincipal gp && gp.Name != null)
            {
                groups.Add(gp.Name);
            }
            principal.Dispose();
        }

        _log.LogDebug("User {Sid} in session {Session}: groups=[{Groups}]",
            sid, sessionId, string.Join(", ", groups));

        // Map to highest-privilege role
        TadUserRole role = MapGroupsToRole(groups);

        // Cache the result for offline use
        _cache.CacheUserResolution(sid, role, groups);

        _lastResolvedSid = sid;
        _lastRole = role;

        return (role, sessionId, sid);
    }

    // ─── Offline Fallback ────────────────────────────────────────────

    private (TadUserRole, uint, string) ResolveFromCache(uint sessionId)
    {
        // If we had a previous resolution in this session, reuse it
        if (_lastResolvedSid != null)
        {
            _log.LogInformation("Using last-known role {Role} for SID {Sid}",
                _lastRole, _lastResolvedSid);
            return (_lastRole, sessionId, _lastResolvedSid);
        }

        // Otherwise, consult the encrypted offline cache
        var cached = _cache.LoadCachedResolution();
        if (cached != null)
        {
            _log.LogInformation("Offline cache: SID={Sid}, Role={Role}",
                cached.Sid, cached.Role);
            return (cached.Role, sessionId, cached.Sid);
        }

        return (TadUserRole.Unknown, sessionId, string.Empty);
    }

    // ─── Group → Role Mapping ────────────────────────────────────────

    private TadUserRole MapGroupsToRole(List<string> groups)
    {
        if (_groupMappings == null || _groupMappings.Count == 0)
        {
            // Fallback: simple name-based heuristic
            foreach (string g in groups)
            {
                string lower = g.ToLowerInvariant();
                if (lower.Contains("admin")) return TadUserRole.Admin;
                if (lower.Contains("teacher") || lower.Contains("staff"))
                    return TadUserRole.Teacher;
            }
            return TadUserRole.Student;
        }

        TadUserRole highest = TadUserRole.Student;

        foreach (string group in groups)
        {
            if (_groupMappings.TryGetValue(group, out int roleInt))
            {
                var role = (TadUserRole)roleInt;
                if (role > highest) highest = role;
            }
        }

        return highest;
    }

    private void LoadGroupMappings()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\TAD_RV", writable: false);
            string? json = key?.GetValue("PolicyJson") as string;
            if (json == null) return;

            var config = JsonSerializer.Deserialize<TadPolicyConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _groupMappings = config?.GroupRoleMappings;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load group mappings from registry");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// P/Invoke for WTS Session Query
// ═══════════════════════════════════════════════════════════════════════════

internal static partial class NativeSession
{
    [DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();
}
