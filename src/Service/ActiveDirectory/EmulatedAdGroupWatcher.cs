// ───────────────────────────────────────────────────────────────────────────
// EmulatedAdGroupWatcher.cs — Mock AD group resolution for demo / emulation
//
// Activated via: TADBridgeService.exe --emulate
//
// Returns plausible user/role data without contacting a Domain Controller.
// Cycles through Student → Teacher → Admin roles every ~60 seconds so the
// full pipeline can be observed during demos.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Logging;
using TADBridge.Cache;
using TADBridge.Shared;

namespace TADBridge.ActiveDirectory;

/// <summary>
/// Drop-in replacement for <see cref="AdGroupWatcher"/> that returns
/// simulated role data without requiring Active Directory.
/// </summary>
public sealed class EmulatedAdGroupWatcher : AdGroupWatcher
{
    private readonly ILogger _log;
    private int _callCount;

    // Demo users that rotate on each resolution cycle
    private static readonly (string Sid, string Name, TADUserRole Role)[] DemoUsers =
    {
        ("S-1-5-21-DEMO-1001", "demo.student",  TADUserRole.Student),
        ("S-1-5-21-DEMO-1002", "demo.teacher",   TADUserRole.Teacher),
        ("S-1-5-21-DEMO-1003", "demo.admin",     TADUserRole.Admin),
    };

    public EmulatedAdGroupWatcher(
        ILogger<AdGroupWatcher> logger,
        OfflineCacheManager cache)
        : base(logger, cache)
    {
        _log = logger;
    }

    /// <summary>
    /// Returns a simulated user that cycles through roles.
    /// Session ID is always 1 (console session).
    /// </summary>
    public override (TADUserRole Role, uint SessionId, string Sid) ResolveCurrentUser()
    {
        int idx = (_callCount / 6) % DemoUsers.Length;   // switch every ~60s at 10s poll
        _callCount++;

        var user = DemoUsers[idx];

        _log.LogInformation("[EMULATED] AD resolution → {Name} ({Role}), SID={Sid}",
            user.Name, user.Role, user.Sid);

        return (user.Role, 1, user.Sid);
    }
}
