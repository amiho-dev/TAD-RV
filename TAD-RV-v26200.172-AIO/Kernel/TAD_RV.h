/*++

Module Name:

    TAD_RV.h

Abstract:

    Shared header for the TAD.RV kernel-mode endpoint monitoring driver
    deployed on school-managed workstations.

    Contains IOCTL definitions, shared structures, constants, and internal
    prototypes used by both the driver and the TAD.RV management agent.

    This header is consumed by:
      - TAD_RV.sys   (kernel mode, _KERNEL_MODE defined)
      - TAD.RV UI    (user mode management console)

Copyright:

    (C) 2026 TAD Europe — https://tad-it.eu
    All rights reserved.

Environment:

    Kernel mode / User mode (shared definitions)

--*/

#pragma once

#ifndef TAD_RV_H
#define TAD_RV_H

#ifdef _KERNEL_MODE
#include <ntddk.h>
#include <wdm.h>
#include <ntstrsafe.h>
#include <fltKernel.h>
#include <intrin.h>
#else
#include <windows.h>
#include <winioctl.h>
#endif

#include "../Shared/TadShared.h"

/* ═══════════════════════════════════════════════════════════════════════
 * Driver Identity & Build Configuration
 * ═══════════════════════════════════════════════════════════════════════ */

#define TAD_DEVICE_NAME         L"\\Device\\TadRvDevice"
#define TAD_SYMBOLIC_LINK       L"\\DosDevices\\TadRvLink"
#define TAD_DRIVER_ALTITUDE     L"328471"
#define TAD_POOL_TAG            'RVAT'

#define TAD_DRIVER_FILENAME     L"TAD.RV.sys"
#define TAD_UI_FILENAME         L"TAD.RV.exe"
#define TAD_SERVICE_FILENAME    L"TadBridgeService.exe"

/* ═══════════════════════════════════════════════════════════════════════
 * Authentication Key  (256-bit / 32-byte pre-shared secret)
 * ═══════════════════════════════════════════════════════════════════════ */

#define TAD_AUTH_KEY_SIZE       32
#define TAD_KEY_XOR_MASK        ((UCHAR)0xA7)

static const UCHAR TadObfuscatedKey[TAD_AUTH_KEY_SIZE] = {
    0xF3, 0xE6, 0xE3, 0x8A, 0xF5, 0xF1, 0x89, 0xF4,
    0xE2, 0xE4, 0xF2, 0xF5, 0xEE, 0xF3, 0xF2, 0xEC,
    0xE2, 0xFE, 0x97, 0x96, 0x95, 0x94, 0x93, 0x92,
    0xEA, 0xE8, 0xE9, 0xEE, 0xF3, 0xE8, 0xE9, 0x86
};

/* ═══════════════════════════════════════════════════════════════════════
 * Access Rights Stripped from External Handle Requests
 * ═══════════════════════════════════════════════════════════════════════ */

#define TAD_STRIPPED_PROCESS_RIGHTS  ( PROCESS_TERMINATE          \
                                    | PROCESS_VM_WRITE            \
                                    | PROCESS_VM_OPERATION        \
                                    | PROCESS_CREATE_THREAD       \
                                    | PROCESS_SUSPEND_RESUME )

#define TAD_STRIPPED_THREAD_RIGHTS   ( THREAD_TERMINATE            \
                                    | THREAD_SUSPEND_RESUME       \
                                    | THREAD_SET_CONTEXT )

/* ═══════════════════════════════════════════════════════════════════════
 * Security Policy Constants
 * ═══════════════════════════════════════════════════════════════════════ */

#define TAD_MAX_UNLOCK_ATTEMPTS     5
#define TAD_LOCKOUT_DURATION        ((LONGLONG)(-30LL * 10 * 1000 * 1000))

/* Heartbeat timeout: 6 seconds (3 missed beats @ 2s interval) */
#define TAD_HEARTBEAT_TIMEOUT_MS    6000

/* ═══════════════════════════════════════════════════════════════════════
 * Kernel-Only Declarations
 * ═══════════════════════════════════════════════════════════════════════ */

#ifdef _KERNEL_MODE

typedef struct _TAD_DRIVER_GLOBALS {

    PDEVICE_OBJECT  DeviceObject;
    UNICODE_STRING  SymbolicLink;
    BOOLEAN         SymbolicLinkCreated;

    /* Process / thread protection */
    HANDLE          ProtectedPid;
    PVOID           ObCallbackHandle;

    /* Unload gate */
    volatile LONG   AllowUnload;

    /* Input hard-lock (teacher LOCK command) */
    volatile LONG   InputLocked;

    /* UI overlay protection (lock screen PID) */
    HANDLE          ProtectedUiPid;

    /* Stealth mode — suppress yellow recording border (Win11+) */
    volatile LONG   StealthActive;
    ULONG           StealthFlags;

    /* Unlock throttle */
    volatile LONG   FailedUnlockAttempts;
    LARGE_INTEGER   LockoutUntil;

    /* Minifilter */
    PFLT_FILTER     FilterHandle;

    /* Caller validation */
    PEPROCESS       AgentProcess;

    /* Heartbeat watchdog */
    LARGE_INTEGER   LastHeartbeatTime;
    KTIMER          HeartbeatTimer;
    KDPC            HeartbeatDpc;
    volatile LONG   HeartbeatAlive;

    /* Active policy from service */
    TAD_POLICY_BUFFER   CurrentPolicy;
    volatile LONG       PolicyValid;

    /* Current user role pushed by service */
    volatile LONG   CurrentUserRole;    /* TAD_USER_ROLE enum */

} TAD_DRIVER_GLOBALS, *PTAD_DRIVER_GLOBALS;

extern TAD_DRIVER_GLOBALS g_Tad;

/* ── Driver Entry / Unload ───────────────────────────────────────────── */

DRIVER_INITIALIZE   DriverEntry;
DRIVER_UNLOAD       TadDriverUnload;

/* ── Dispatch Routines ───────────────────────────────────────────────── */

_Dispatch_type_(IRP_MJ_CREATE)
_Dispatch_type_(IRP_MJ_CLOSE)
DRIVER_DISPATCH     TadDispatchCreateClose;

_Dispatch_type_(IRP_MJ_DEVICE_CONTROL)
DRIVER_DISPATCH     TadDispatchDeviceControl;

/* ── ObRegisterCallbacks ─────────────────────────────────────────────── */

OB_PREOP_CALLBACK_STATUS
TadObProcessPreCallback(
    _In_    PVOID                           RegistrationContext,
    _Inout_ POB_PRE_OPERATION_INFORMATION   OperationInformation
    );

OB_PREOP_CALLBACK_STATUS
TadObThreadPreCallback(
    _In_    PVOID                           RegistrationContext,
    _Inout_ POB_PRE_OPERATION_INFORMATION   OperationInformation
    );

NTSTATUS TadRegisterProcessProtection(VOID);
VOID     TadUnregisterProcessProtection(VOID);

/* ── Minifilter ──────────────────────────────────────────────────────── */

FLT_PREOP_CALLBACK_STATUS
TadPreSetInformationCallback(
    _Inout_ PFLT_CALLBACK_DATA              Data,
    _In_    PCFLT_RELATED_OBJECTS           FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID    *CompletionContext
    );

NTSTATUS FLTAPI
TadFilterUnloadCallback(
    _In_ FLT_FILTER_UNLOAD_FLAGS Flags
    );

/* ── Heartbeat Watchdog ──────────────────────────────────────────────── */

VOID TadInitHeartbeatWatchdog(VOID);
VOID TadStopHeartbeatWatchdog(VOID);

KDEFERRED_ROUTINE TadHeartbeatDpcRoutine;

/* ── Utilities ───────────────────────────────────────────────────────── */

_IRQL_requires_max_(PASSIVE_LEVEL)
NTSTATUS TadCreateDeviceAndSymlink(_In_ PDRIVER_OBJECT DriverObject);

_IRQL_requires_max_(PASSIVE_LEVEL)
VOID TadCleanupDeviceAndSymlink(VOID);

_IRQL_requires_max_(APC_LEVEL)
BOOLEAN TadVerifyAuthKey(_In_reads_bytes_(TAD_AUTH_KEY_SIZE) const UCHAR *ProvidedKey);

_IRQL_requires_max_(PASSIVE_LEVEL)
NTSTATUS TadSetDeviceDacl(_In_ PDEVICE_OBJECT DeviceObject);

_IRQL_requires_max_(APC_LEVEL)
BOOLEAN TadIsCallerProtectedAgent(VOID);

BOOLEAN TadIsProtectedFilename(_In_ PCUNICODE_STRING FileName);

#endif /* _KERNEL_MODE */

#endif /* TAD_RV_H */
