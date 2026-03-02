// ───────────────────────────────────────────────────────────────────────────
// EmulatedDriverBridge.cs — User-mode protection engine
//
// Provides a driver-compatible contract in pure user mode so the full
// Bridge Service + Teacher + Console stack can run without TAD_RV.sys.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Logging;
using TadBridge.Shared;

namespace TadBridge.Driver;

/// <summary>
/// Drop-in replacement for <see cref="DriverBridge"/> that responds with
/// user-mode state instead of talking to \\.\TadRvLink.
/// </summary>
public sealed class EmulatedDriverBridge : DriverBridge
{
    private readonly ILogger _log;
    private readonly bool _enableSyntheticAlerts;
    private bool _connected;
    private uint _protectedPid;
    private TadUserRole _currentRole = TadUserRole.Student;
    private uint _currentSession;
    private TadPolicyFlags _policyFlags;
    private bool _hardLocked;
    private bool _stealthActive;
    private int _alertCounter;

    public EmulatedDriverBridge(ILogger<DriverBridge> logger, bool enableSyntheticAlerts = false) : base(logger)
    {
        _log = logger;
        _enableSyntheticAlerts = enableSyntheticAlerts;
    }

    public override bool IsConnected => _connected;

    public override void Connect()
    {
        _connected = true;
        _log.LogInformation("[USERMODE] Protection bridge connected");
    }

    public override void Disconnect()
    {
        _connected = false;
        _log.LogInformation("[USERMODE] Protection bridge disconnected");
    }

    public override void ProtectPid(uint pid)
    {
        _protectedPid = pid;
        _log.LogInformation("[USERMODE] Registered PID {Pid} for protection", pid);
    }

    public override void UnprotectPid(uint pid)
    {
        if (_protectedPid == pid) _protectedPid = 0;
        _log.LogDebug("[USERMODE] Unprotected PID {Pid}", pid);
    }

    public override bool Unlock()
    {
        _log.LogInformation("[USERMODE] Unlock request accepted");
        return true;
    }

    public override TadHeartbeatOutput? Heartbeat()
    {
        return new TadHeartbeatOutput
        {
            DriverVersionMajor = 1,
            DriverVersionMinor = 0,
            ProtectedPid = _protectedPid,
            ProcessProtectionActive = 1,
            FileProtectionActive = 1,
            UnlockPermitted = 0,
            HeartbeatAlive = 1,
            FailedUnlockAttempts = 0,
            CurrentUserRole = (uint)_currentRole,
            PolicyValid = _policyFlags != 0 ? 1u : 0u
        };
    }

    public override void SetUserRole(TadUserRole role, uint sessionId, string userSid)
    {
        _currentRole = role;
        _currentSession = sessionId;
        _log.LogInformation("[USERMODE] User role set: {Role} for session {Session}",
            role, sessionId);
    }

    public override void SetPolicy(TadPolicyBuffer policy)
    {
        _policyFlags = (TadPolicyFlags)policy.Flags;
        _log.LogInformation("[USERMODE] Policy applied: flags=0x{Flags:X}", policy.Flags);
    }

    public override TadAlertOutput? ReadAlert()
    {
        if (!_enableSyntheticAlerts)
        {
            Thread.Sleep(10000);
            return null;
        }

        Thread.Sleep(15000 + Random.Shared.Next(30000));
        _alertCounter++;

        if (_alertCounter % 3 == 0)
        {
            _log.LogInformation("[USERMODE] Generating demo alert #{Counter}", _alertCounter);
            return new TadAlertOutput
            {
                AlertType = (uint)TadAlertType.ServiceTamper,
                // Use Windows FILETIME format to match KeQuerySystemTime in the real driver
                Timestamp = DateTime.UtcNow.ToFileTimeUtc(),
                SourcePid = (uint)Random.Shared.Next(1000, 65000),
                Reserved = 0
            };
        }

        return null;
    }

    public override void SendHardLock(bool enable)
    {
        _hardLocked = enable;
        _log.LogInformation("[USERMODE] Hard-lock {State}", enable ? "ENGAGED" : "RELEASED");
    }

    public override void ProtectUiProcess(uint pid, bool protect = true)
    {
        _log.LogInformation("[USERMODE] UI process {Pid} protection {State}",
            pid, protect ? "ON" : "OFF");
    }

    public override void SetStealth(bool enable, TadStealthFlags flags = TadStealthFlags.All)
    {
        _stealthActive = enable;
        _log.LogInformation("[USERMODE] Stealth mode {State} (flags=0x{Flags:X})",
            enable ? "ACTIVE" : "DISABLED", (uint)flags);
    }

    public override void SetBannedApps(IEnumerable<string>? imageNames)
    {
        var list = imageNames?.ToList() ?? [];
        if (list.Count == 0)
            _log.LogInformation("[USERMODE] Banned-app list cleared");
        else
            _log.LogInformation("[USERMODE] Banned apps: {Names}", string.Join(", ", list));
    }

    public override void Dispose()
    {
        _connected = false;
    }
}
