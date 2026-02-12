// ───────────────────────────────────────────────────────────────────────────
// DriverBridge.cs — P/Invoke bridge to \\.\TadRvLink
//
// Provides typed wrappers around DeviceIoControl for every IOCTL defined
// in TadShared.h.  Handles safe handle management, buffer marshalling,
// and Win32 error translation.
// ───────────────────────────────────────────────────────────────────────────

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using TadBridge.Shared;

namespace TadBridge.Driver;

/// <summary>
/// Low-level communication channel to the TAD.RV kernel driver.
/// Lifetime managed by DI — Singleton.
/// </summary>
public class DriverBridge : IDisposable
{
    private readonly ILogger<DriverBridge> _log;
    private SafeFileHandle? _deviceHandle;
    private readonly object _lock = new();

    public DriverBridge(ILogger<DriverBridge> logger)
    {
        _log = logger;
    }

    // ─── Device Handle ────────────────────────────────────────────────

    public virtual bool IsConnected => _deviceHandle is { IsInvalid: false, IsClosed: false };

    public virtual void Connect()
    {
        lock (_lock)
        {
            if (IsConnected) return;

            _deviceHandle = NativeMethods.CreateFile(
                TadIoctl.DevicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                0,                  // No sharing
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero
            );

            if (_deviceHandle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                _log.LogError("Failed to connect to driver device. Win32 error {Error}", err);
                throw new InvalidOperationException(
                    $"Cannot open {TadIoctl.DevicePath} — Win32 error {err}");
            }

            _log.LogInformation("Connected to TAD.RV driver @ {Path}", TadIoctl.DevicePath);
        }
    }

    public virtual void Disconnect()
    {
        lock (_lock)
        {
            _deviceHandle?.Dispose();
            _deviceHandle = null;
        }
    }

    // ─── IOCTL Wrappers ──────────────────────────────────────────────

    /// <summary>
    /// Register our PID for kernel-level process protection.
    /// </summary>
    public virtual void ProtectPid(uint pid)
    {
        var input = new TadProtectPidInput { TargetPid = pid, Flags = 0 };
        SendIoctl(TadIoctl.IOCTL_TAD_PROTECT_PID, input);
        _log.LogInformation("Registered PID {Pid} for protection", pid);
    }

    /// <summary>
    /// Present the 256-bit auth key to unlock the driver for unloading.
    /// </summary>
    public virtual bool Unlock()
    {
        var input = new TadUnlockInput { AuthKey = TadIoctl.AuthKey };
        return TrySendIoctl(TadIoctl.IOCTL_TAD_UNLOCK, input);
    }

    /// <summary>
    /// Send a heartbeat and retrieve driver status.
    /// </summary>
    public virtual TadHeartbeatOutput? Heartbeat()
    {
        return ReadIoctl<TadHeartbeatOutput>(TadIoctl.IOCTL_TAD_HEARTBEAT);
    }

    /// <summary>
    /// Push the resolved AD user role to the kernel driver.
    /// </summary>
    public virtual void SetUserRole(TadUserRole role, uint sessionId, string userSid)
    {
        var input = new TadSetUserRoleInput
        {
            Role      = (uint)role,
            SessionId = sessionId,
            UserSid   = userSid
        };
        SendIoctl(TadIoctl.IOCTL_TAD_SET_USER_ROLE, input);
        _log.LogInformation("Pushed role {Role} for session {Session}", role, sessionId);
    }

    /// <summary>
    /// Push the resolved policy to the driver.
    /// </summary>
    public virtual void SetPolicy(TadPolicyBuffer policy)
    {
        SendIoctl(TadIoctl.IOCTL_TAD_SET_POLICY, policy);
        _log.LogInformation("Pushed policy (flags=0x{Flags:X}) to driver", policy.Flags);
    }

    /// <summary>
    /// Long-poll for a driver alert (blocks until the driver completes the IRP).
    /// </summary>
    public virtual TadAlertOutput? ReadAlert()
    {
        return ReadIoctl<TadAlertOutput>(TadIoctl.IOCTL_TAD_READ_ALERT);
    }

    /// <summary>
    /// Enable or disable kernel-level hard-lock (keyboard + mouse input blocked
    /// at the filter-driver level). Used by the Teacher LOCK command.
    /// </summary>
    public virtual void SendHardLock(bool enable)
    {
        var input = new TadHardLockInput { Enable = enable ? 1u : 0u, Flags = 0 };
        SendIoctl(TadIoctl.IOCTL_TAD_HARD_LOCK, input);
        _log.LogInformation("Hard-lock {State}", enable ? "ENGAGED" : "RELEASED");
    }

    /// <summary>
    /// Protect a UI overlay process (e.g., lock screen) from being killed via
    /// Task Manager, Alt+F4, or process termination APIs. Uses ObRegisterCallbacks
    /// in the kernel driver to strip PROCESS_TERMINATE rights.
    /// </summary>
    public virtual void ProtectUiProcess(uint pid, bool protect = true)
    {
        var input = new TadProtectUiInput { TargetPid = pid, Protect = protect ? 1u : 0u };
        SendIoctl(TadIoctl.IOCTL_TAD_PROTECT_UI, input);
        _log.LogInformation("UI process {Pid} protection {State}", pid, protect ? "ON" : "OFF");
    }

    /// <summary>
    /// Enable or disable stealth mode to suppress the Windows 11 yellow
    /// "Screen Recording" border and hide DXGI duplication from DWM queries.
    /// Should be called when the capture engine starts.
    /// </summary>
    public virtual void SetStealth(bool enable, TadStealthFlags flags = TadStealthFlags.All)
    {
        var input = new TadStealthInput
        {
            Enable = enable ? 1u : 0u,
            Flags = (uint)flags
        };
        SendIoctl(TadIoctl.IOCTL_TAD_STEALTH, input);
        _log.LogInformation("Stealth mode {State} (flags=0x{Flags:X})",
            enable ? "ACTIVE" : "DISABLED", (uint)flags);
    }

    // ─── Generic IOCTL Helpers ───────────────────────────────────────

    private void SendIoctl<TInput>(uint ioctlCode, TInput input) where TInput : struct
    {
        EnsureConnected();

        int inputSize = Marshal.SizeOf<TInput>();
        IntPtr inBuf  = Marshal.AllocHGlobal(inputSize);
        try
        {
            Marshal.StructureToPtr(input, inBuf, false);

            bool ok = NativeMethods.DeviceIoControl(
                _deviceHandle!,
                ioctlCode,
                inBuf, (uint)inputSize,
                IntPtr.Zero, 0,
                out _,
                IntPtr.Zero
            );

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"DeviceIoControl 0x{ioctlCode:X} failed — Win32 error {err}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inBuf);
        }
    }

    private bool TrySendIoctl<TInput>(uint ioctlCode, TInput input) where TInput : struct
    {
        try
        {
            SendIoctl(ioctlCode, input);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "IOCTL 0x{Code:X} returned failure", ioctlCode);
            return false;
        }
    }

    private TOutput? ReadIoctl<TOutput>(uint ioctlCode) where TOutput : struct
    {
        EnsureConnected();

        int outputSize = Marshal.SizeOf<TOutput>();
        IntPtr outBuf  = Marshal.AllocHGlobal(outputSize);
        try
        {
            bool ok = NativeMethods.DeviceIoControl(
                _deviceHandle!,
                ioctlCode,
                IntPtr.Zero, 0,
                outBuf, (uint)outputSize,
                out uint bytesReturned,
                IntPtr.Zero
            );

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                _log.LogWarning("ReadIoctl 0x{Code:X} failed — Win32 {Err}", ioctlCode, err);
                return null;
            }

            if (bytesReturned < outputSize)
            {
                _log.LogWarning("ReadIoctl 0x{Code:X}: short read ({Bytes}/{Expected})",
                    ioctlCode, bytesReturned, outputSize);
                return null;
            }

            return Marshal.PtrToStructure<TOutput>(outBuf);
        }
        finally
        {
            Marshal.FreeHGlobal(outBuf);
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            Connect();
    }

    public virtual void Dispose()
    {
        Disconnect();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// P/Invoke Declarations
// ═══════════════════════════════════════════════════════════════════════════

internal static partial class NativeMethods
{
    public const uint GENERIC_READ    = 0x80000000;
    public const uint GENERIC_WRITE   = 0x40000000;
    public const uint OPEN_EXISTING   = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped
    );
}
