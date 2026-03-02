// ───────────────────────────────────────────────────────────────────────────
// TadSharedInterop.cs
//
// (C) 2026 TAD Europe — https://tad-it.eu
// All rights reserved.
//
// C# mirror of the structures and constants defined in TadShared.h.
// Used by TadBridgeService to communicate with the TAD.RV kernel driver
// via DeviceIoControl.
//
// IMPORTANT: Field order, sizes, and Pack value MUST match the C header
//            exactly (pack 8, sequential layout).
// ───────────────────────────────────────────────────────────────────────────

using System.Runtime.InteropServices;

namespace TadBridge.Shared;

// ═══════════════════════════════════════════════════════════════════════════
// Constants
// ═══════════════════════════════════════════════════════════════════════════

public static class TadIoctl
{
    public const string DevicePath = @"\\.\TadRvLink";

    public const uint DeviceType = 0x8A00;
    public const int  AuthKeySize = 32;

    // CTL_CODE helper: (DeviceType << 16) | (Access << 14) | (Function << 2) | Method
    private const uint METHOD_BUFFERED  = 0;
    private const uint FILE_READ_ACCESS = 1;
    private const uint FILE_WRITE_ACCESS = 2;

    private static uint CtlCode(uint function, uint method, uint access)
        => (DeviceType << 16) | (access << 14) | (function << 2) | method;

    public static readonly uint IOCTL_TAD_PROTECT_PID   = CtlCode(0x800, METHOD_BUFFERED, FILE_WRITE_ACCESS);
    public static readonly uint IOCTL_TAD_UNLOCK        = CtlCode(0x801, METHOD_BUFFERED, FILE_WRITE_ACCESS);
    public static readonly uint IOCTL_TAD_HEARTBEAT     = CtlCode(0x802, METHOD_BUFFERED, FILE_READ_ACCESS);
    public static readonly uint IOCTL_TAD_SET_USER_ROLE = CtlCode(0x803, METHOD_BUFFERED, FILE_WRITE_ACCESS);
    public static readonly uint IOCTL_TAD_SET_POLICY    = CtlCode(0x804, METHOD_BUFFERED, FILE_WRITE_ACCESS);
    public static readonly uint IOCTL_TAD_READ_ALERT    = CtlCode(0x805, METHOD_BUFFERED, FILE_READ_ACCESS);
    public static readonly uint IOCTL_TAD_HARD_LOCK     = CtlCode(0x806, METHOD_BUFFERED, FILE_WRITE_ACCESS);
    public static readonly uint IOCTL_TAD_PROTECT_UI    = CtlCode(0x807, METHOD_BUFFERED, FILE_WRITE_ACCESS);
    public static readonly uint IOCTL_TAD_STEALTH       = CtlCode(0x808, METHOD_BUFFERED, FILE_WRITE_ACCESS);
    public static readonly uint IOCTL_TAD_SET_BANNED_APPS = CtlCode(0x809, METHOD_BUFFERED, FILE_WRITE_ACCESS);

    // Pre-shared key (raw, before XOR on the driver side)
    public static readonly byte[] AuthKey =
    {
        0x54, 0x41, 0x44, 0x2D, 0x52, 0x56, 0x2E, 0x53,
        0x45, 0x43, 0x55, 0x52, 0x49, 0x54, 0x59, 0x4B,
        0x45, 0x59, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35,
        0x4D, 0x4F, 0x4E, 0x49, 0x54, 0x4F, 0x52, 0x21
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// Enumerations
// ═══════════════════════════════════════════════════════════════════════════

public enum TadUserRole : uint
{
    Student = 0,
    Teacher = 1,
    Admin   = 2,
    Unknown = 0xFF
}

public enum TadAlertType : uint
{
    None             = 0,
    ServiceTamper    = 1,
    HeartbeatLost    = 2,
    UnlockBruteForce = 3,
    FileTamper       = 4,
    ProcessBlocked   = 5,   // PsNotify callback denied a banned application
}

// ═══════════════════════════════════════════════════════════════════════════
// Policy Flags
// ═══════════════════════════════════════════════════════════════════════════

[Flags]
public enum TadPolicyFlags : uint
{
    None             = 0,
    BlockUsb         = 0x00000001,
    BlockPrinting    = 0x00000002,
    LogScreenshots   = 0x00000004,
    LogKeystrokes    = 0x00000008,
    BlockApps        = 0x00000010,
    RestrictNetwork  = 0x00000020,
}

// ═══════════════════════════════════════════════════════════════════════════
// IOCTL Payload Structures  (must match C pack(push, 8))
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TadProtectPidInput
{
    public uint TargetPid;
    public uint Flags;          // Reserved — must be 0
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TadHardLockInput
{
    public uint Enable;         // 1 = lock keyboard+mouse, 0 = unlock
    public uint Flags;          // Reserved — must be 0
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TadProtectUiInput
{
    public uint TargetPid;      // PID of the overlay process to protect
    public uint Protect;        // 1 = enable, 0 = remove
}

/// <summary>
/// Stealth mode flags. Bitfield controlling which stealth features are active.
/// </summary>
[Flags]
public enum TadStealthFlags : uint
{
    None                   = 0,
    SuppressYellowBorder   = 0x01,  // Suppress Windows 11 yellow "Screen Recording" border
    HideFromGraphicsCapture = 0x02, // Hide from Windows.Graphics.Capture enumeration
    CloakDxgiDuplication   = 0x04,  // Cloak DXGI duplication session from DWM queries
    All                    = 0x07
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TadStealthInput
{
    public uint Enable;         // 1 = stealth ON, 0 = stealth OFF
    public uint Flags;          // TadStealthFlags bitmask
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TadUnlockInput
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = TadIoctl.AuthKeySize)]
    public byte[] AuthKey;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct TadHeartbeatOutput
{
    public uint DriverVersionMajor;
    public uint DriverVersionMinor;
    public uint ProtectedPid;
    public byte ProcessProtectionActive;
    public byte FileProtectionActive;
    public byte UnlockPermitted;
    public byte HeartbeatAlive;
    public uint FailedUnlockAttempts;
    public uint CurrentUserRole;
    public uint PolicyValid;
}

[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
public struct TadSetUserRoleInput
{
    public uint Role;
    public uint SessionId;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 68)]
    public string UserSid;
}

[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
public struct TadPolicyBuffer
{
    public uint Version;
    public uint Flags;
    public uint HeartbeatIntervalMs;
    public uint HeartbeatTimeoutMs;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string OrganizationalUnit;

    public uint AllowedRoles;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public uint[] Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
public struct TadAlertOutput
{
    public uint  AlertType;
    public long  Timestamp;
    public uint  SourcePid;
    public uint  Reserved;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Detail;
}

// ═══════════════════════════════════════════════════════════════════════════
// Banned-App List  (must match TAD_BANNED_APPS_INPUT in TadShared.h)
// TAD_MAX_BANNED_APPS = 32, TAD_MAX_IMAGE_NAME_LEN = 64
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Sent via IOCTL_TAD_SET_BANNED_APPS.
/// Set Count = 0 and leave ImageNames empty to clear all blocked apps.
/// ImageNames entries are bare filenames only (e.g. "notepad.exe"),
/// not full paths.  Matching in the callback is case-insensitive.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
public struct TadBannedAppsInput
{
    public const int MaxEntries       = 32;
    public const int MaxImageNameLen  = 64;   // WCHARs including NUL

    public uint Count;

    // Fixed-size 2D array: 32 entries × 64 WCHARs = 4096 WCHARs = 8192 bytes.
    // Declared as a flat byte array so Marshal can handle it; callers use
    // TadBannedAppsInput.Encode() to fill it.
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxEntries * MaxImageNameLen)]
    public char[] RawNames;  // layout: Names[i] starts at [i * MaxImageNameLen]

    /// <summary>
    /// Build a <see cref="TadBannedAppsInput"/> from a list of bare image names.
    /// Names are silently truncated to <see cref="MaxImageNameLen"/>-1 characters.
    /// </summary>
    public static TadBannedAppsInput Encode(IEnumerable<string> imageNames)
    {
        var names = imageNames.Take(MaxEntries).ToArray();
        var raw   = new char[MaxEntries * MaxImageNameLen];

        for (int i = 0; i < names.Length; i++)
        {
            var src = names[i].AsSpan(0, Math.Min(names[i].Length, MaxImageNameLen - 1));
            src.CopyTo(raw.AsSpan(i * MaxImageNameLen, MaxImageNameLen));
            // Remaining chars are already \0 (array default)
        }

        return new TadBannedAppsInput
        {
            Count    = (uint)names.Length,
            RawNames = raw
        };
    }
}
