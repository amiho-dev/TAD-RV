/*++

Module Name:

    TadShared.h

Abstract:

    Shared data definitions for driver ↔ service communication.
    Included by both the kernel driver (TAD_RV.h) and the C# service
    (via P/Invoke layout-equivalent structures in TadSharedInterop.cs).

    ALL IOCTL codes and their payloads are defined here as the single
    source of truth.

Copyright:

    (C) 2026 TAD Europe — https://tad-it.eu
    All rights reserved.

Environment:

    Kernel mode / User mode

--*/

#pragma once

#ifndef TAD_SHARED_H
#define TAD_SHARED_H

#ifdef _KERNEL_MODE
#include <ntddk.h>
#else
#include <windows.h>
#include <winioctl.h>
#endif

/* ═══════════════════════════════════════════════════════════════════════
 * Version
 * ═══════════════════════════════════════════════════════════════════════ */

#define TAD_VERSION_MAJOR       26500
#define TAD_VERSION_MINOR       181
#define TAD_VERSION_BUILD       0
#define TAD_VERSION_REVISION    0

/* ═══════════════════════════════════════════════════════════════════════
 * Device & IOCTL Definitions
 * ═══════════════════════════════════════════════════════════════════════ */

#define TAD_DEVICE_TYPE         ((ULONG)0x8A00)

/* 0x800 — Register PID for process protection */
#define IOCTL_TAD_PROTECT_PID   CTL_CODE(TAD_DEVICE_TYPE, 0x800, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* 0x801 — Present 256-bit auth key to allow driver unload */
#define IOCTL_TAD_UNLOCK        CTL_CODE(TAD_DEVICE_TYPE, 0x801, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* 0x802 — Driver status heartbeat (output) */
#define IOCTL_TAD_HEARTBEAT     CTL_CODE(TAD_DEVICE_TYPE, 0x802, METHOD_BUFFERED, FILE_READ_ACCESS)

/* 0x803 — Push user role to driver after AD group resolution */
#define IOCTL_TAD_SET_USER_ROLE CTL_CODE(TAD_DEVICE_TYPE, 0x803, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* 0x804 — Push resolved policy from service to driver */
#define IOCTL_TAD_SET_POLICY    CTL_CODE(TAD_DEVICE_TYPE, 0x804, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* 0x805 — Driver → Service notification read (long-poll) */
#define IOCTL_TAD_READ_ALERT    CTL_CODE(TAD_DEVICE_TYPE, 0x805, METHOD_BUFFERED, FILE_READ_ACCESS)

/* 0x806 — Enable/disable kernel-level input blocking (keyboard + mouse) */
#define IOCTL_TAD_HARD_LOCK     CTL_CODE(TAD_DEVICE_TYPE, 0x806, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* 0x807 — Protect a UI process from Alt+F4, Task Manager kill, etc. */
#define IOCTL_TAD_PROTECT_UI    CTL_CODE(TAD_DEVICE_TYPE, 0x807, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* 0x808 — Enable stealth mode: suppress yellow "Screen Recording" border (Win11+)
 *         and hide DXGI Desktop Duplication registration from DWM queries.
 *         Uses undocumented SetWindowDisplayAffinity bypass via kernel callback. */
#define IOCTL_TAD_STEALTH       CTL_CODE(TAD_DEVICE_TYPE, 0x808, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* 0x809 — Push a list of banned application image names to the driver.
 *         The process-creation callback denies any image whose filename
 *         (final path component) matches an entry in this list.          */
#define IOCTL_TAD_SET_BANNED_APPS CTL_CODE(TAD_DEVICE_TYPE, 0x809, METHOD_BUFFERED, FILE_WRITE_ACCESS)

/* ═══════════════════════════════════════════════════════════════════════
 * Enumerations
 * ═══════════════════════════════════════════════════════════════════════ */

typedef enum _TAD_USER_ROLE {
    TadRoleStudent  = 0,
    TadRoleTeacher  = 1,
    TadRoleAdmin    = 2,
    TadRoleUnknown  = 0xFF
} TAD_USER_ROLE;

typedef enum _TAD_ALERT_TYPE {
    TadAlertNone              = 0,
    TadAlertServiceTamper     = 1,   /* ObCallback detected stop attempt       */
    TadAlertHeartbeatLost     = 2,   /* Heartbeat timeout — network kill       */
    TadAlertUnlockBruteForce  = 3,   /* Unlock lockout triggered               */
    TadAlertFileTamper        = 4,   /* Minifilter blocked deletion/rename     */
    TadAlertProcessBlocked    = 5,   /* PsNotify blocked a banned application  */
} TAD_ALERT_TYPE;

/* ═══════════════════════════════════════════════════════════════════════
 * IOCTL Payload Structures
 *
 * All structures are packed to 8-byte alignment to match the C#
 * [StructLayout(LayoutKind.Sequential, Pack = 8)] counterpart.
 * ═══════════════════════════════════════════════════════════════════════ */

#define TAD_AUTH_KEY_BYTES      32
#define TAD_MAX_OU_LENGTH       256     /* WCHAR count including NUL */
#define TAD_MAX_SID_LENGTH      68      /* bytes — covers S-1-5-21-...-RID */
#define TAD_MAX_GROUP_NAME      64      /* WCHAR count */
#define TAD_MAX_GROUPS          16

/* Banned-app list limits */
#define TAD_MAX_BANNED_APPS     32      /* Max entries per IOCTL_TAD_SET_BANNED_APPS call */
#define TAD_MAX_IMAGE_NAME_LEN  64      /* WCHAR count per entry, including NUL */

#pragma pack(push, 8)

/* ── IOCTL_TAD_PROTECT_PID ───────────────────────────────────────────── */

typedef struct _TAD_PROTECT_PID_INPUT {
    ULONG   TargetPid;
    ULONG   Flags;              /* Reserved — must be zero */
} TAD_PROTECT_PID_INPUT, *PTAD_PROTECT_PID_INPUT;

/* ── IOCTL_TAD_UNLOCK ────────────────────────────────────────────────── */

typedef struct _TAD_UNLOCK_INPUT {
    UCHAR   AuthKey[TAD_AUTH_KEY_BYTES];
} TAD_UNLOCK_INPUT, *PTAD_UNLOCK_INPUT;

/* ── IOCTL_TAD_HEARTBEAT ─────────────────────────────────────────────── */

typedef struct _TAD_HEARTBEAT_OUTPUT {
    ULONG       DriverVersionMajor;
    ULONG       DriverVersionMinor;
    ULONG       ProtectedPid;
    UCHAR       ProcessProtectionActive;   /* BOOLEAN */
    UCHAR       FileProtectionActive;
    UCHAR       UnlockPermitted;
    UCHAR       HeartbeatAlive;
    ULONG       FailedUnlockAttempts;
    ULONG       CurrentUserRole;            /* TAD_USER_ROLE */
    ULONG       PolicyValid;
} TAD_HEARTBEAT_OUTPUT, *PTAD_HEARTBEAT_OUTPUT;

/* ── IOCTL_TAD_SET_USER_ROLE ─────────────────────────────────────────── */

typedef struct _TAD_SET_USER_ROLE_INPUT {
    ULONG       Role;               /* TAD_USER_ROLE */
    ULONG       SessionId;          /* Windows session ID of interactive logon */
    WCHAR       UserSid[TAD_MAX_SID_LENGTH];
} TAD_SET_USER_ROLE_INPUT, *PTAD_SET_USER_ROLE_INPUT;

/* ── IOCTL_TAD_SET_POLICY ────────────────────────────────────────────── */

/* ── IOCTL_TAD_HARD_LOCK ─────────────────────────────────────────────── */

typedef struct _TAD_HARD_LOCK_INPUT {
    ULONG   Enable;             /* 1 = lock input, 0 = unlock */
    ULONG   Flags;              /* Reserved — must be zero */
} TAD_HARD_LOCK_INPUT, *PTAD_HARD_LOCK_INPUT;

/* ── IOCTL_TAD_PROTECT_UI ────────────────────────────────────────────── */

typedef struct _TAD_PROTECT_UI_INPUT {
    ULONG   TargetPid;          /* PID of the UI overlay to protect */
    ULONG   Protect;            /* 1 = enable protection, 0 = remove */
} TAD_PROTECT_UI_INPUT, *PTAD_PROTECT_UI_INPUT;

/* ── IOCTL_TAD_STEALTH ──────────────────────────────────────────────── */

typedef struct _TAD_STEALTH_INPUT {
    ULONG   Enable;             /* 1 = stealth ON, 0 = stealth OFF */
    ULONG   Flags;              /* Bit 0: suppress yellow border
                                   Bit 1: hide from GraphicsCapture enumeration
                                   Bit 2: cloak DXGI duplication session */
} TAD_STEALTH_INPUT, *PTAD_STEALTH_INPUT;

#define TAD_POLICY_FLAG_BLOCK_USB           0x00000001
#define TAD_POLICY_FLAG_BLOCK_PRINTING      0x00000002
#define TAD_POLICY_FLAG_LOG_SCREENSHOTS     0x00000004
#define TAD_POLICY_FLAG_LOG_KEYSTROKES      0x00000008
#define TAD_POLICY_FLAG_BLOCK_APPS          0x00000010
#define TAD_POLICY_FLAG_RESTRICT_NETWORK    0x00000020

typedef struct _TAD_POLICY_BUFFER {
    ULONG   Version;                            /* Must be 1                */
    ULONG   Flags;                              /* Bitmask of TAD_POLICY_FLAG_* */
    ULONG   HeartbeatIntervalMs;                /* Service heartbeat rate   */
    ULONG   HeartbeatTimeoutMs;                 /* Driver kill-switch delay */
    WCHAR   OrganizationalUnit[TAD_MAX_OU_LENGTH]; /* AD OU DN              */
    ULONG   AllowedRoles;                       /* Mask: which roles may unload */
    ULONG   Reserved[8];
} TAD_POLICY_BUFFER, *PTAD_POLICY_BUFFER;

/* ── IOCTL_TAD_SET_BANNED_APPS ──────────────────────────────────────── */

/*
 * Send a new banned-app list to the driver.  Set Count==0 to clear all entries.
 * ImageNames contains the bare filename only (e.g. L"notepad.exe", not a full
 * path).  Matching in the callback is case-insensitive against the final path
 * component of CreateInfo->ImageFileName.
 */
typedef struct _TAD_BANNED_APPS_INPUT {
    ULONG   Count;                                                  /* 0 = clear list */
    WCHAR   ImageNames[TAD_MAX_BANNED_APPS][TAD_MAX_IMAGE_NAME_LEN];
} TAD_BANNED_APPS_INPUT, *PTAD_BANNED_APPS_INPUT;

/* ── IOCTL_TAD_READ_ALERT ────────────────────────────────────────────── */

typedef struct _TAD_ALERT_OUTPUT {
    ULONG           AlertType;      /* TAD_ALERT_TYPE */
    LARGE_INTEGER   Timestamp;      /* KeQuerySystemTime */
    ULONG           SourcePid;      /* PID that triggered the alert */
    ULONG           Reserved;
    WCHAR           Detail[128];    /* Human-readable context */
} TAD_ALERT_OUTPUT, *PTAD_ALERT_OUTPUT;

#pragma pack(pop)

#endif /* TAD_SHARED_H */
