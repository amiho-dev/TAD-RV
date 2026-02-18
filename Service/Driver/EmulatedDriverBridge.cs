// ───────────────────────────────────────────────────────────────────────────
// EmulatedDriverBridge.cs — Mock driver for testing without a kernel driver
//
// Activated via: TadBridgeService.exe --emulate
//
// Simulates all IOCTL responses with plausible data so the full Bridge
// Service + Teacher + Console stack can be tested on any Windows machine
// without installing TAD_RV.sys.
// ───────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Logging;
using TadBridge.Shared;

namespace TadBridge.Driver;

/// <summary>
/// Drop-in replacement for <see cref="DriverBridge"/> that responds with
/// simulated data instead of talking to \\.\TadRvLink.
/// </summary>
public sealed class EmulatedDriverBridge : DriverBridge
{
    private readonly ILogger _log;
    private bool _connected;
    private uint _protectedPid;
    private TadUserRole _currentRole = TadUserRole.Student;
    private uint _currentSession;
    private TadPolicyFlags _policyFlags;
    private bool _hardLocked;
    private bool _stealthActive;
    private int _alertCounter;

    public EmulatedDriverBridge(ILogger<DriverBridge> logger) : base(logger)
    {
        _log = logger;
    }

    public override bool IsConnected => _connected;

    public override void Connect()
    {
        _connected = true;
        _log.LogInformation("[EMULATED] Driver bridge connected (no real driver)");
    }

    public override void Disconnect()
    {
        _connected = false;
        _log.LogInformation("[EMULATED] Driver bridge disconnected");
    }

    public override void ProtectPid(uint pid)
    {
        _protectedPid = pid;
        _log.LogInformation("[EMULATED] Registered PID {Pid} for protection", pid);
    }

    public override void UnprotectPid(uint pid)
    {
        if (_protectedPid == pid) _protectedPid = 0;
        _log.LogDebug("[EMULATED] Unprotected PID {Pid}", pid);
    }

    public override bool Unlock()
    {
        _log.LogInformation("[EMULATED] Driver unlock accepted");
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
        _log.LogInformation("[EMULATED] User role set: {Role} for session {Session}",
            role, sessionId);
    }

    public override void SetPolicy(TadPolicyBuffer policy)
    {
        _policyFlags = (TadPolicyFlags)policy.Flags;
        _log.LogInformation("[EMULATED] Policy applied: flags=0x{Flags:X}", policy.Flags);
    }

    public override TadAlertOutput? ReadAlert()
    {
        // Simulate a long-poll: block for 15-45 seconds, then occasionally generate a demo alert
        Thread.Sleep(15000 + Random.Shared.Next(30000));
        _alertCounter++;

        // Generate a demo alert every 3rd cycle
        if (_alertCounter % 3 == 0)
        {
            _log.LogInformation("[EMULATED] Generating demo alert #{Counter}", _alertCounter);
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
        _log.LogInformation("[EMULATED] Hard-lock {State}", enable ? "ENGAGED" : "RELEASED");
    }

    public override void ProtectUiProcess(uint pid, bool protect = true)
    {
        _log.LogInformation("[EMULATED] UI process {Pid} protection {State}",
            pid, protect ? "ON" : "OFF");
    }

    public override void SetStealth(bool enable, TadStealthFlags flags = TadStealthFlags.All)
    {
        _stealthActive = enable;
        _log.LogInformation("[EMULATED] Stealth mode {State} (flags=0x{Flags:X})",
            enable ? "ACTIVE" : "DISABLED", (uint)flags);
    }

    public override void Dispose()
    {
        _connected = false;
    }
}
