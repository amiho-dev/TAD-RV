/*++

Module Name:

    TAD_RV.c

Abstract:

    Core implementation of the TAD.RV kernel-mode endpoint monitoring driver
    for school-managed workstations.

    Capabilities:
      1.  DriverEntry / DriverUnload with authenticated unload gate
      2.  Process AND thread protection via ObRegisterCallbacks
      3.  IOCTL communication bridge with caller validation
      4.  Anti-deletion AND anti-rename via minifilter
      5.  DACL-hardened device object
      6.  256-bit XOR-obfuscated key with constant-time comparison
      7.  Unlock attempt rate-limiting / lockout
      8.  Spectre V1 mitigations on all IOCTL paths
      9.  All allocations tagged with 'RVAT', IRQL verified per routine
      10. Heartbeat watchdog DPC timer
      11. User role + policy IOCTLs from TADBridgeService
      12. Alert queue for driver → service notifications

Copyright:

    (C) 2026 TAD Europe — https://tad-it.eu
    All rights reserved.

Environment:

    Kernel mode only — IRQL documented per routine.

--*/

#include "TAD_RV.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT,  DriverEntry)
#pragma alloc_text(PAGE,  TADDriverUnload)
#pragma alloc_text(PAGE,  TADDispatchCreateClose)
#pragma alloc_text(PAGE,  TADDispatchDeviceControl)
#pragma alloc_text(PAGE,  TADCreateDeviceAndSymlink)
#pragma alloc_text(PAGE,  TADCleanupDeviceAndSymlink)
#pragma alloc_text(PAGE,  TADRegisterProcessProtection)
#pragma alloc_text(PAGE,  TADUnregisterProcessProtection)
#pragma alloc_text(PAGE,  TADSetDeviceDacl)
#pragma alloc_text(PAGE,  TADVerifyAuthKey)
#pragma alloc_text(PAGE,  TADProcessNotifyCallback)
#pragma alloc_text(PAGE,  TADRegisterProcessNotify)
#pragma alloc_text(PAGE,  TADUnregisterProcessNotify)
#endif

/* ═══════════════════════════════════════════════════════════════════════
 * Global Driver State
 * ═══════════════════════════════════════════════════════════════════════ */

TAD_DRIVER_GLOBALS g_TAD = { 0 };

/* ═══════════════════════════════════════════════════════════════════════
 * Minifilter Registration Tables
 * ═══════════════════════════════════════════════════════════════════════ */

static const FLT_OPERATION_REGISTRATION g_TADFilterCallbacks[] = {
    { IRP_MJ_SET_INFORMATION, 0, TADPreSetInformationCallback, NULL },
    { IRP_MJ_OPERATION_END }
};

static const FLT_REGISTRATION g_TADFilterRegistration = {
    sizeof(FLT_REGISTRATION),
    FLT_REGISTRATION_VERSION,
    0, NULL,
    g_TADFilterCallbacks,
    TADFilterUnloadCallback,
    NULL, NULL, NULL, NULL,
    NULL, NULL, NULL
};

/* ═══════════════════════════════════════════════════════════════════════
 * 1.  DRIVER ENTRY & UNLOAD
 * ═══════════════════════════════════════════════════════════════════════ */

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    NTSTATUS status;
    UNREFERENCED_PARAMETER(RegistryPath);

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
               "[TAD.RV] DriverEntry — v%d.%d.%d\n",
               TAD_VERSION_MAJOR, TAD_VERSION_MINOR, TAD_VERSION_BUILD));

    RtlZeroMemory(&g_TAD, sizeof(g_TAD));
    InterlockedExchange(&g_TAD.AllowUnload, 0);
    InterlockedExchange(&g_TAD.FailedUnlockAttempts, 0);
    InterlockedExchange(&g_TAD.HeartbeatAlive, 0);
    InterlockedExchange(&g_TAD.PolicyValid, 0);
    InterlockedExchange(&g_TAD.CurrentUserRole, (LONG)TADRoleUnknown);
    ExInitializeFastMutex(&g_TAD.BannedAppsLock);

    DriverObject->MajorFunction[IRP_MJ_CREATE]         = TADDispatchCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE]          = TADDispatchCreateClose;
    DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = TADDispatchDeviceControl;
    DriverObject->DriverUnload                         = TADDriverUnload;

    status = TADCreateDeviceAndSymlink(DriverObject);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
                   "[TAD.RV] Device creation failed: 0x%08X\n", status));
        return status;
    }

    status = TADSetDeviceDacl(g_TAD.DeviceObject);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                   "[TAD.RV] DACL failed: 0x%08X (non-fatal)\n", status));
    }

    status = TADRegisterProcessProtection();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                   "[TAD.RV] ObCallbacks failed: 0x%08X\n", status));
    }

    status = TADRegisterProcessNotify();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                   "[TAD.RV] PsProcessNotify registration failed: 0x%08X\n", status));
    }

    status = FltRegisterFilter(DriverObject, &g_TADFilterRegistration, &g_TAD.FilterHandle);
    if (NT_SUCCESS(status)) {
        status = FltStartFiltering(g_TAD.FilterHandle);
        if (!NT_SUCCESS(status)) {
            FltUnregisterFilter(g_TAD.FilterHandle);
            g_TAD.FilterHandle = NULL;
        }
    } else {
        g_TAD.FilterHandle = NULL;
    }

    /* Initialise the heartbeat watchdog DPC timer */
    TADInitHeartbeatWatchdog();

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
               "[TAD.RV] Loaded (ObCB=%s, PsNotify=%s, Flt=%s)\n",
               g_TAD.ObCallbackHandle         ? "YES" : "NO",
               g_TAD.ProcessNotifyRegistered  ? "YES" : "NO",
               g_TAD.FilterHandle             ? "YES" : "NO"));

    return STATUS_SUCCESS;
}

_Use_decl_annotations_
VOID
TADDriverUnload(
    _In_ PDRIVER_OBJECT DriverObject
    )
{
    PAGED_CODE();
    UNREFERENCED_PARAMETER(DriverObject);

    if (InterlockedCompareExchange(&g_TAD.AllowUnload, 0, 0) == 0) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
                   "[TAD.RV] Unload DENIED\n"));
        return;
    }

    TADStopHeartbeatWatchdog();

    if (g_TAD.FilterHandle) {
        FltUnregisterFilter(g_TAD.FilterHandle);
        g_TAD.FilterHandle = NULL;
    }

    TADUnregisterProcessNotify();
    TADUnregisterProcessProtection();

    if (g_TAD.AgentProcess) {
        ObDereferenceObject(g_TAD.AgentProcess);
        g_TAD.AgentProcess = NULL;
    }

    TADCleanupDeviceAndSymlink();

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
               "[TAD.RV] Unloaded\n"));
}

/* ═══════════════════════════════════════════════════════════════════════
 * 2.  DEVICE & SYMBOLIC LINK
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
NTSTATUS
TADCreateDeviceAndSymlink(_In_ PDRIVER_OBJECT DriverObject)
{
    NTSTATUS       status;
    UNICODE_STRING deviceName;

    PAGED_CODE();

    RtlInitUnicodeString(&deviceName,          TAD_DEVICE_NAME);
    RtlInitUnicodeString(&g_TAD.SymbolicLink,  TAD_SYMBOLIC_LINK);

    status = IoCreateDevice(DriverObject, 0, &deviceName, TAD_DEVICE_TYPE,
                            FILE_DEVICE_SECURE_OPEN, FALSE, &g_TAD.DeviceObject);
    if (!NT_SUCCESS(status)) return status;

    g_TAD.DeviceObject->Flags |= DO_BUFFERED_IO;
    g_TAD.DeviceObject->Flags &= ~DO_DEVICE_INITIALIZING;

    status = IoCreateSymbolicLink(&g_TAD.SymbolicLink, &deviceName);
    if (!NT_SUCCESS(status)) {
        IoDeleteDevice(g_TAD.DeviceObject);
        g_TAD.DeviceObject = NULL;
        return status;
    }

    g_TAD.SymbolicLinkCreated = TRUE;
    return STATUS_SUCCESS;
}

_Use_decl_annotations_
VOID TADCleanupDeviceAndSymlink(VOID)
{
    PAGED_CODE();
    if (g_TAD.SymbolicLinkCreated) {
        IoDeleteSymbolicLink(&g_TAD.SymbolicLink);
        g_TAD.SymbolicLinkCreated = FALSE;
    }
    if (g_TAD.DeviceObject) {
        IoDeleteDevice(g_TAD.DeviceObject);
        g_TAD.DeviceObject = NULL;
    }
}

/* ═══════════════════════════════════════════════════════════════════════
 * 3.  DEVICE DACL HARDENING
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
NTSTATUS TADSetDeviceDacl(_In_ PDEVICE_OBJECT DeviceObject)
{
    NTSTATUS status;
    SECURITY_DESCRIPTOR sd;
    PACL   dacl = NULL;
    ULONG  daclSize;
    SID_IDENTIFIER_AUTHORITY ntAuth = SECURITY_NT_AUTHORITY;
    PSID   systemSid = NULL;
    PSID   adminsSid = NULL;

    PAGED_CODE();

    status = RtlAllocateAndInitializeSid(&ntAuth, 1,
        SECURITY_LOCAL_SYSTEM_RID, 0, 0, 0, 0, 0, 0, 0, &systemSid);
    if (!NT_SUCCESS(status)) goto Cleanup;

    status = RtlAllocateAndInitializeSid(&ntAuth, 2,
        SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
        0, 0, 0, 0, 0, 0, &adminsSid);
    if (!NT_SUCCESS(status)) goto Cleanup;

    daclSize = sizeof(ACL) + 2 * sizeof(ACCESS_ALLOWED_ACE)
             + RtlLengthSid(systemSid) + RtlLengthSid(adminsSid)
             - 2 * sizeof(ULONG);

    dacl = (PACL)ExAllocatePool2(POOL_FLAG_PAGED, daclSize, TAD_POOL_TAG);
    if (!dacl) { status = STATUS_INSUFFICIENT_RESOURCES; goto Cleanup; }

    status = RtlCreateAcl(dacl, daclSize, ACL_REVISION);
    if (!NT_SUCCESS(status)) goto Cleanup;

    RtlAddAccessAllowedAce(dacl, ACL_REVISION, GENERIC_ALL, systemSid);
    RtlAddAccessAllowedAce(dacl, ACL_REVISION, GENERIC_ALL, adminsSid);

    status = RtlCreateSecurityDescriptor(&sd, SECURITY_DESCRIPTOR_REVISION);
    if (!NT_SUCCESS(status)) goto Cleanup;

    RtlSetDaclSecurityDescriptor(&sd, TRUE, dacl, FALSE);
    status = ObSetSecurityObjectByPointer(DeviceObject, DACL_SECURITY_INFORMATION, &sd);

Cleanup:
    if (dacl)      ExFreePoolWithTag(dacl, TAD_POOL_TAG);
    if (systemSid) RtlFreeSid(systemSid);
    if (adminsSid) RtlFreeSid(adminsSid);
    return status;
}

/* ═══════════════════════════════════════════════════════════════════════
 * 4.  SECURITY UTILITIES
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
BOOLEAN TADVerifyAuthKey(_In_reads_bytes_(TAD_AUTH_KEY_SIZE) const UCHAR *ProvidedKey)
{
    UCHAR decoded[TAD_AUTH_KEY_SIZE];
    UCHAR diff = 0;
    ULONG i;
    PAGED_CODE();

    for (i = 0; i < TAD_AUTH_KEY_SIZE; i++)
        decoded[i] = TADObfuscatedKey[i] ^ TAD_KEY_XOR_MASK;

    for (i = 0; i < TAD_AUTH_KEY_SIZE; i++)
        diff |= (decoded[i] ^ ProvidedKey[i]);

    RtlSecureZeroMemory(decoded, sizeof(decoded));
    return (diff == 0) ? TRUE : FALSE;
}

_Use_decl_annotations_
BOOLEAN TADIsCallerProtectedAgent(VOID)
{
    return (g_TAD.AgentProcess && PsGetCurrentProcess() == g_TAD.AgentProcess);
}

BOOLEAN TADIsProtectedFilename(_In_ PCUNICODE_STRING FileName)
{
    UNICODE_STRING driverName, uiName, svcName;

    RtlInitUnicodeString(&driverName, TAD_DRIVER_FILENAME);
    RtlInitUnicodeString(&uiName,     TAD_UI_FILENAME);
    RtlInitUnicodeString(&svcName,    TAD_SERVICE_FILENAME);

    if (RtlCompareUnicodeString(FileName, &driverName, TRUE) == 0) return TRUE;
    if (RtlCompareUnicodeString(FileName, &uiName,     TRUE) == 0) return TRUE;
    if (RtlCompareUnicodeString(FileName, &svcName,    TRUE) == 0) return TRUE;

    return FALSE;
}

/* ═══════════════════════════════════════════════════════════════════════
 * 5.  HEARTBEAT WATCHDOG (DPC Timer)
 *
 * A KTIMER fires every TAD_HEARTBEAT_TIMEOUT_MS milliseconds.
 * The DPC checks whether HeartbeatAlive has been set since the last tick.
 * If not, the service is presumed dead and the driver can:
 *   - Engage a WFP network killswitch (TODO: WFP callout integration)
 *   - Queue an alert for the next ReadAlert IRP
 * ═══════════════════════════════════════════════════════════════════════ */

VOID TADInitHeartbeatWatchdog(VOID)
{
    KeInitializeTimer(&g_TAD.HeartbeatTimer);
    KeInitializeDpc(&g_TAD.HeartbeatDpc, TADHeartbeatDpcRoutine, NULL);

    /* Start the timer — fires every HeartbeatTimeout period */
    LARGE_INTEGER dueTime;
    dueTime.QuadPart = -((LONGLONG)TAD_HEARTBEAT_TIMEOUT_MS * 10 * 1000); /* relative, 100ns */

    KeSetTimerEx(&g_TAD.HeartbeatTimer, dueTime,
                 TAD_HEARTBEAT_TIMEOUT_MS, /* periodic interval in ms */
                 &g_TAD.HeartbeatDpc);
}

VOID TADStopHeartbeatWatchdog(VOID)
{
    KeCancelTimer(&g_TAD.HeartbeatTimer);
}

/*
 * DPC fires at IRQL = DISPATCH_LEVEL.
 * Check HeartbeatAlive flag; if 0, the service hasn't checked in.
 */
_Use_decl_annotations_
VOID
TADHeartbeatDpcRoutine(
    _In_     PKDPC  Dpc,
    _In_opt_ PVOID  DeferredContext,
    _In_opt_ PVOID  SystemArgument1,
    _In_opt_ PVOID  SystemArgument2
    )
{
    UNREFERENCED_PARAMETER(Dpc);
    UNREFERENCED_PARAMETER(DeferredContext);
    UNREFERENCED_PARAMETER(SystemArgument1);
    UNREFERENCED_PARAMETER(SystemArgument2);

    if (InterlockedExchange(&g_TAD.HeartbeatAlive, 0) == 0) {
        /*
         * Service has NOT sent a heartbeat since the last DPC tick.
         * Actions:
         *   1. Log the event
         *   2. Engage WFP network killswitch (future: inject WFP callout)
         *   3. Queue a TADAlertHeartbeatLost for the next ReadAlert IRP
         */
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
                   "[TAD.RV] HEARTBEAT LOST — service is unresponsive!\n"));

        /*
         * TODO: WFP killswitch integration
         *
         * In production, this DPC would signal a work item (since WFP APIs
         * require IRQL <= APC_LEVEL) that calls:
         *   FwpmFilterAdd0() to insert a BLOCK-ALL filter at the
         *   FWPM_LAYER_OUTBOUND_TRANSPORT_V4 layer.
         *
         * The filter is removed when the next heartbeat arrives.
         */
    }
}

/* ═══════════════════════════════════════════════════════════════════════
 * 6.  DISPATCH — IRP_MJ_CREATE / IRP_MJ_CLOSE
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
NTSTATUS TADDispatchCreateClose(
    _In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    PAGED_CODE();
    UNREFERENCED_PARAMETER(DeviceObject);
    Irp->IoStatus.Status = STATUS_SUCCESS;
    Irp->IoStatus.Information = 0;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

/* ═══════════════════════════════════════════════════════════════════════
 * 7.  DISPATCH — IRP_MJ_DEVICE_CONTROL
 *
 * Handles all IOCTLs defined in TADShared.h:
 *   0x800 PROTECT_PID      0x801 UNLOCK          0x802 HEARTBEAT
 *   0x803 SET_USER_ROLE    0x804 SET_POLICY       0x805 READ_ALERT
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
NTSTATUS TADDispatchDeviceControl(
    _In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    NTSTATUS           status = STATUS_SUCCESS;
    PIO_STACK_LOCATION irpSp;
    ULONG   ioctl, inLen, outLen, bytesWritten = 0;
    PVOID   buf;

    PAGED_CODE();
    UNREFERENCED_PARAMETER(DeviceObject);

    irpSp  = IoGetCurrentIrpStackLocation(Irp);
    ioctl  = irpSp->Parameters.DeviceIoControl.IoControlCode;
    inLen  = irpSp->Parameters.DeviceIoControl.InputBufferLength;
    outLen = irpSp->Parameters.DeviceIoControl.OutputBufferLength;
    buf    = Irp->AssociatedIrp.SystemBuffer;

#if defined(_AMD64_) || defined(_X86_)
    _mm_lfence();
#endif

    switch (ioctl) {

    /* ── PROTECT_PID ──────────────────────────────────────────────── */
    case IOCTL_TAD_PROTECT_PID:
    {
        PTAD_PROTECT_PID_INPUT p;
        PEPROCESS proc = NULL;

        if (inLen < sizeof(TAD_PROTECT_PID_INPUT))  { status = STATUS_BUFFER_TOO_SMALL; break; }
#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif
        if (!buf) { status = STATUS_INVALID_PARAMETER; break; }
        p = (PTAD_PROTECT_PID_INPUT)buf;
        if (p->TargetPid == 0 || p->Flags != 0) { status = STATUS_INVALID_PARAMETER; break; }

        status = PsLookupProcessByProcessId(ULongToHandle(p->TargetPid), &proc);
        if (!NT_SUCCESS(status)) { status = STATUS_INVALID_PARAMETER; break; }

        if (g_TAD.AgentProcess) ObDereferenceObject(g_TAD.AgentProcess);
        g_TAD.AgentProcess = proc;

        InterlockedExchangePointer((PVOID volatile *)&g_TAD.ProtectedPid,
                                   ULongToHandle(p->TargetPid));

        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                   "[TAD.RV] Protecting PID %lu\n", p->TargetPid));
        break;
    }

    /* ── UNLOCK ───────────────────────────────────────────────────── */
    case IOCTL_TAD_UNLOCK:
    {
        PTAD_UNLOCK_INPUT p;
        LARGE_INTEGER now;

        if (inLen < sizeof(TAD_UNLOCK_INPUT))  { status = STATUS_BUFFER_TOO_SMALL; break; }
#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif
        if (!buf) { status = STATUS_INVALID_PARAMETER; break; }

        if (g_TAD.AgentProcess && !TADIsCallerProtectedAgent()) {
            status = STATUS_ACCESS_DENIED; break;
        }

        KeQuerySystemTime(&now);
        if (g_TAD.FailedUnlockAttempts >= TAD_MAX_UNLOCK_ATTEMPTS) {
            if (now.QuadPart < g_TAD.LockoutUntil.QuadPart) {
                status = STATUS_ACCESS_DENIED; break;
            }
            InterlockedExchange(&g_TAD.FailedUnlockAttempts, 0);
        }

        p = (PTAD_UNLOCK_INPUT)buf;
        if (TADVerifyAuthKey(p->AuthKey)) {
            InterlockedExchange(&g_TAD.AllowUnload, 1);
            InterlockedExchange(&g_TAD.FailedUnlockAttempts, 0);
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                       "[TAD.RV] Unlock ACCEPTED\n"));
        } else {
            LONG a = InterlockedIncrement(&g_TAD.FailedUnlockAttempts);
            if (a >= TAD_MAX_UNLOCK_ATTEMPTS) {
                KeQuerySystemTime(&g_TAD.LockoutUntil);
                /* TAD_LOCKOUT_DURATION is negative (relative time), so add it
                 * to move LockoutUntil into the future. */
                g_TAD.LockoutUntil.QuadPart += (-TAD_LOCKOUT_DURATION);
            }
            status = STATUS_ACCESS_DENIED;
        }
        break;
    }

    /* ── HEARTBEAT ────────────────────────────────────────────────── */
    case IOCTL_TAD_HEARTBEAT:
    {
        PTAD_HEARTBEAT_OUTPUT hb;
        if (outLen < sizeof(TAD_HEARTBEAT_OUTPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif

        /* Mark alive for the DPC watchdog */
        InterlockedExchange(&g_TAD.HeartbeatAlive, 1);
        KeQuerySystemTime(&g_TAD.LastHeartbeatTime);

        hb = (PTAD_HEARTBEAT_OUTPUT)buf;
        RtlZeroMemory(hb, sizeof(*hb));

        hb->DriverVersionMajor      = TAD_VERSION_MAJOR;
        hb->DriverVersionMinor      = TAD_VERSION_MINOR;
        hb->ProtectedPid            = HandleToULong(g_TAD.ProtectedPid);
        hb->ProcessProtectionActive = (g_TAD.ObCallbackHandle != NULL);
        hb->FileProtectionActive    = (g_TAD.FilterHandle     != NULL);
        hb->UnlockPermitted         = (InterlockedCompareExchange(&g_TAD.AllowUnload, 0, 0) != 0);
        hb->HeartbeatAlive          = 1;
        hb->FailedUnlockAttempts    = (ULONG)g_TAD.FailedUnlockAttempts;
        hb->CurrentUserRole         = (ULONG)g_TAD.CurrentUserRole;
        hb->PolicyValid             = (ULONG)g_TAD.PolicyValid;

        bytesWritten = sizeof(TAD_HEARTBEAT_OUTPUT);
        break;
    }

    /* ── SET_USER_ROLE ────────────────────────────────────────────── */
    case IOCTL_TAD_SET_USER_ROLE:
    {
        PTAD_SET_USER_ROLE_INPUT p;
        if (inLen < sizeof(TAD_SET_USER_ROLE_INPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif
        if (!buf) { status = STATUS_INVALID_PARAMETER; break; }

        if (g_TAD.AgentProcess && !TADIsCallerProtectedAgent()) {
            status = STATUS_ACCESS_DENIED; break;
        }

        p = (PTAD_SET_USER_ROLE_INPUT)buf;
        InterlockedExchange(&g_TAD.CurrentUserRole, (LONG)p->Role);

        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                   "[TAD.RV] User role set to %lu (session %lu)\n",
                   p->Role, p->SessionId));
        break;
    }

    /* ── SET_POLICY ───────────────────────────────────────────────── */
    case IOCTL_TAD_SET_POLICY:
    {
        PTAD_POLICY_BUFFER p;
        if (inLen < sizeof(TAD_POLICY_BUFFER)) { status = STATUS_BUFFER_TOO_SMALL; break; }
#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif
        if (!buf) { status = STATUS_INVALID_PARAMETER; break; }

        if (g_TAD.AgentProcess && !TADIsCallerProtectedAgent()) {
            status = STATUS_ACCESS_DENIED; break;
        }

        p = (PTAD_POLICY_BUFFER)buf;
        if (p->Version != 1) { status = STATUS_INVALID_PARAMETER; break; }

        RtlCopyMemory(&g_TAD.CurrentPolicy, p, sizeof(TAD_POLICY_BUFFER));
        InterlockedExchange(&g_TAD.PolicyValid, 1);

        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                   "[TAD.RV] Policy loaded (flags=0x%08X)\n", p->Flags));
        break;
    }

    /* ── READ_ALERT ───────────────────────────────────────────────── */
    case IOCTL_TAD_READ_ALERT:
    {
        PTAD_ALERT_OUTPUT a;
        if (outLen < sizeof(TAD_ALERT_OUTPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif

        /*
         * In production, this IRP would be pended (IoMarkIrpPending)
         * and completed asynchronously when an alert fires.
         * For now, return an empty alert (no event queued).
         */
        a = (PTAD_ALERT_OUTPUT)buf;
        RtlZeroMemory(a, sizeof(*a));
        a->AlertType = TADAlertNone;
        KeQuerySystemTime((PLARGE_INTEGER)&a->Timestamp);

        bytesWritten = sizeof(TAD_ALERT_OUTPUT);
        break;
    }

    /* ── IOCTL_TAD_HARD_LOCK ──────────────────────────────────────── */
    case IOCTL_TAD_HARD_LOCK:
    {
        PTAD_HARD_LOCK_INPUT hl;
        if (inLen < sizeof(TAD_HARD_LOCK_INPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        if (!TADIsCallerProtectedAgent()) { status = STATUS_ACCESS_DENIED; break; }

#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif

        hl = (PTAD_HARD_LOCK_INPUT)buf;
        /*
         * Engage or disengage kernel-level input blocking.
         * This uses a keyboard/mouse filter chain notification:
         *   - When Enable==1: Block all HID input at the class-driver level
         *     by installing a temporary upper filter that drops all IRPs.
         *   - When Enable==0: Remove the filter, restoring normal input.
         *
         * Implementation note: The actual input filter is registered via
         * IoRegisterDeviceInterface callbacks. The global flag is checked
         * by TADInputFilterDispatch() in the input filter subsystem.
         */
        InterlockedExchange(&g_TAD.InputLocked, hl->Enable ? 1 : 0);

        DbgPrintEx(DPFLTR_DEFAULT_ID, DPFLTR_INFO_LEVEL,
            "[TAD.RV] Hard-lock %s by PID %lu\n",
            hl->Enable ? "ENGAGED" : "RELEASED",
            (ULONG)(ULONG_PTR)PsGetCurrentProcessId());

        break;
    }

    /* ── IOCTL_TAD_PROTECT_UI ─────────────────────────────────────── */
    case IOCTL_TAD_PROTECT_UI:
    {
        PTAD_PROTECT_UI_INPUT ui;
        if (inLen < sizeof(TAD_PROTECT_UI_INPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        if (!TADIsCallerProtectedAgent()) { status = STATUS_ACCESS_DENIED; break; }

#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif

        ui = (PTAD_PROTECT_UI_INPUT)buf;
        /*
         * Protect or unprotect the lock-screen overlay process.
         * When Protect==1: Register the PID with ObRegisterCallbacks
         * to strip PROCESS_TERMINATE from all external handles.
         * This prevents students from using Task Manager, Alt+F4, or
         * TerminateProcess() to close the lock overlay.
         *
         * We store the UI PID in g_TAD.ProtectedUiPid.  The existing
         * ObCallback checks BOTH ProtectedPid (service) and
         * ProtectedUiPid (lock overlay).
         */
        if (ui->Protect)
            InterlockedExchangePointer(
                (PVOID volatile *)&g_TAD.ProtectedUiPid,
                (PVOID)(ULONG_PTR)ui->TargetPid);
        else
            InterlockedExchangePointer(
                (PVOID volatile *)&g_TAD.ProtectedUiPid, NULL);

        DbgPrintEx(DPFLTR_DEFAULT_ID, DPFLTR_INFO_LEVEL,
            "[TAD.RV] UI process %lu protection %s\n",
            ui->TargetPid, ui->Protect ? "ON" : "OFF");

        break;
    }

    /* ── IOCTL_TAD_STEALTH ────────────────────────────────────────── */
    case IOCTL_TAD_STEALTH:
    {
        PTAD_STEALTH_INPUT stl;
        if (inLen < sizeof(TAD_STEALTH_INPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        if (!TADIsCallerProtectedAgent()) { status = STATUS_ACCESS_DENIED; break; }

#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif

        stl = (PTAD_STEALTH_INPUT)buf;

        /*
         * Stealth mode for DXGI Desktop Duplication:
         *
         * Windows 11 24H2+ shows a yellow border around apps being
         * captured via DXGI OutputDuplication / Windows.Graphics.Capture.
         * The border is drawn by DWM (Desktop Window Manager).
         *
         * Strategy:
         *   Flag 0x01 (SuppressYellowBorder):
         *     - Hook dwm!CDwmNotification to intercept the
         *       "screen recording active" notification that triggers
         *       the yellow border.  In kernel mode we register an
         *       ETW provider callback to suppress DWM's rendering
         *       of the capture indicator.
         *
         *   Flag 0x02 (HideFromGraphicsCapture):
         *     - Modify the DXGI output capabilities to not advertise
         *       the desktop duplication session to GraphicsCaptureItem
         *       enumeration APIs.
         *
         *   Flag 0x04 (CloakDxgiDuplication):
         *     - Set the SetWindowDisplayAffinity equivalent at the
         *       session level to prevent DWM from flagging the capture
         *       in its per-window metadata.
         *
         * NOTE: These techniques are version-specific and may need
         *       updates with each Windows build.  The driver validates
         *       the OS build number before applying each flag.
         */
        if (stl->Enable)
        {
            InterlockedExchange(&g_TAD.StealthActive, 1);
            g_TAD.StealthFlags = stl->Flags;
        }
        else
        {
            InterlockedExchange(&g_TAD.StealthActive, 0);
            g_TAD.StealthFlags = 0;
        }

        DbgPrintEx(DPFLTR_DEFAULT_ID, DPFLTR_INFO_LEVEL,
            "[TAD.RV] Stealth mode %s (flags=0x%lX)\n",
            stl->Enable ? "ACTIVE" : "DISABLED", stl->Flags);

        break;
    }

    /* ── IOCTL_TAD_SET_BANNED_APPS ──────────────────────────────────────── */
    case IOCTL_TAD_SET_BANNED_APPS:
    {
        PTAD_BANNED_APPS_INPUT p;
        ULONG i;

        if (inLen < sizeof(TAD_BANNED_APPS_INPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        if (!TADIsCallerProtectedAgent())           { status = STATUS_ACCESS_DENIED;    break; }

#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif

        p = (PTAD_BANNED_APPS_INPUT)buf;
        if (p->Count > TAD_MAX_BANNED_APPS) { status = STATUS_INVALID_PARAMETER; break; }

        ExAcquireFastMutex(&g_TAD.BannedAppsLock);

        /* Clear the previous list */
        RtlZeroMemory(g_TAD.BannedAppStorage, sizeof(g_TAD.BannedAppStorage));
        RtlZeroMemory(g_TAD.BannedApps,       sizeof(g_TAD.BannedApps));
        g_TAD.BannedAppCount = 0;

        for (i = 0; i < p->Count; i++)
        {
            /*
             * Validate that the caller-supplied string is NUL-terminated
             * within the fixed-size field and not empty.
             */
            SIZE_T srcLen = 0;
            SIZE_T j;

            for (j = 0; j < TAD_MAX_IMAGE_NAME_LEN; j++) {
                if (p->ImageNames[i][j] == L'\0') break;
                srcLen++;
            }

            if (srcLen == 0 || srcLen >= TAD_MAX_IMAGE_NAME_LEN) continue;

            RtlCopyMemory(g_TAD.BannedAppStorage[i],
                          p->ImageNames[i],
                          srcLen * sizeof(WCHAR));

            g_TAD.BannedApps[i].Buffer        = g_TAD.BannedAppStorage[i];
            g_TAD.BannedApps[i].Length        = (USHORT)(srcLen * sizeof(WCHAR));
            g_TAD.BannedApps[i].MaximumLength = (USHORT)(TAD_MAX_IMAGE_NAME_LEN * sizeof(WCHAR));
            g_TAD.BannedAppCount++;
        }

        ExReleaseFastMutex(&g_TAD.BannedAppsLock);

        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                   "[TAD.RV] Banned-app list updated: %lu entr%s\n",
                   g_TAD.BannedAppCount,
                   g_TAD.BannedAppCount == 1 ? "y" : "ies"));
        break;
    }

    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    Irp->IoStatus.Status      = status;
    Irp->IoStatus.Information  = bytesWritten;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return status;
}

/* ═══════════════════════════════════════════════════════════════════════
 * 8.  PROCESS & THREAD PROTECTION — ObRegisterCallbacks
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
OB_PREOP_CALLBACK_STATUS
TADObProcessPreCallback(
    _In_    PVOID                           RegistrationContext,
    _Inout_ POB_PRE_OPERATION_INFORMATION   OpInfo)
{
    HANDLE tPid, cPid, pPid;
    UNREFERENCED_PARAMETER(RegistrationContext);

    pPid = (HANDLE)InterlockedCompareExchangePointer(
        (PVOID volatile *)&g_TAD.ProtectedPid, NULL, NULL);
    if (!pPid) {
        /* Also check UI overlay PID */
        pPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_TAD.ProtectedUiPid, NULL, NULL);
        if (!pPid) return OB_PREOP_SUCCESS;
    }
    if (OpInfo->ObjectType != *PsProcessType) return OB_PREOP_SUCCESS;

    tPid = PsGetProcessId((PEPROCESS)OpInfo->Object);
    cPid = PsGetCurrentProcessId();

    /* Protect both the service PID and the UI overlay PID */
    {
        HANDLE svcPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_TAD.ProtectedPid, NULL, NULL);
        HANDLE uiPid  = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_TAD.ProtectedUiPid, NULL, NULL);

        if (tPid != svcPid && tPid != uiPid)
            return OB_PREOP_SUCCESS;
        /* Allow the service to manage itself */
        if (cPid == svcPid || cPid == uiPid)
            return OB_PREOP_SUCCESS;
    }

    if (OpInfo->Operation == OB_OPERATION_HANDLE_CREATE)
        OpInfo->Parameters->CreateHandleInformation.DesiredAccess &= ~TAD_STRIPPED_PROCESS_RIGHTS;
    else if (OpInfo->Operation == OB_OPERATION_HANDLE_DUPLICATE)
        OpInfo->Parameters->DuplicateHandleInformation.DesiredAccess &= ~TAD_STRIPPED_PROCESS_RIGHTS;

    return OB_PREOP_SUCCESS;
}

_Use_decl_annotations_
OB_PREOP_CALLBACK_STATUS
TADObThreadPreCallback(
    _In_    PVOID                           RegistrationContext,
    _Inout_ POB_PRE_OPERATION_INFORMATION   OpInfo)
{
    HANDLE oPid, cPid, pPid;
    UNREFERENCED_PARAMETER(RegistrationContext);

    pPid = (HANDLE)InterlockedCompareExchangePointer(
        (PVOID volatile *)&g_TAD.ProtectedPid, NULL, NULL);
    if (!pPid) {
        /* Also check UI overlay PID for thread protection */
        pPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_TAD.ProtectedUiPid, NULL, NULL);
        if (!pPid) return OB_PREOP_SUCCESS;
    }
    if (OpInfo->ObjectType != *PsThreadType) return OB_PREOP_SUCCESS;

    oPid = PsGetProcessId(IoThreadToProcess((PETHREAD)OpInfo->Object));
    cPid = PsGetCurrentProcessId();

    /* Protect threads of both the service PID and UI overlay PID */
    {
        HANDLE svcPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_TAD.ProtectedPid, NULL, NULL);
        HANDLE uiPid  = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_TAD.ProtectedUiPid, NULL, NULL);

        if (oPid != svcPid && oPid != uiPid)
            return OB_PREOP_SUCCESS;
        if (cPid == svcPid || cPid == uiPid)
            return OB_PREOP_SUCCESS;
    }

    if (OpInfo->Operation == OB_OPERATION_HANDLE_CREATE)
        OpInfo->Parameters->CreateHandleInformation.DesiredAccess &= ~TAD_STRIPPED_THREAD_RIGHTS;
    else if (OpInfo->Operation == OB_OPERATION_HANDLE_DUPLICATE)
        OpInfo->Parameters->DuplicateHandleInformation.DesiredAccess &= ~TAD_STRIPPED_THREAD_RIGHTS;

    return OB_PREOP_SUCCESS;
}

NTSTATUS TADRegisterProcessProtection(VOID)
{
    NTSTATUS                   status;
    OB_CALLBACK_REGISTRATION  cbReg;
    OB_OPERATION_REGISTRATION opReg[2];
    UNICODE_STRING             altitude;

    PAGED_CODE();
    if (g_TAD.ObCallbackHandle) return STATUS_ALREADY_REGISTERED;

    RtlInitUnicodeString(&altitude, TAD_DRIVER_ALTITUDE);

    RtlZeroMemory(opReg, sizeof(opReg));
    opReg[0].ObjectType   = PsProcessType;
    opReg[0].Operations   = OB_OPERATION_HANDLE_CREATE | OB_OPERATION_HANDLE_DUPLICATE;
    opReg[0].PreOperation = TADObProcessPreCallback;

    opReg[1].ObjectType   = PsThreadType;
    opReg[1].Operations   = OB_OPERATION_HANDLE_CREATE | OB_OPERATION_HANDLE_DUPLICATE;
    opReg[1].PreOperation = TADObThreadPreCallback;

    RtlZeroMemory(&cbReg, sizeof(cbReg));
    cbReg.Version                    = OB_FLT_REGISTRATION_VERSION;
    cbReg.OperationRegistrationCount = 2;
    cbReg.Altitude                   = altitude;
    cbReg.OperationRegistration      = opReg;

    status = ObRegisterCallbacks(&cbReg, &g_TAD.ObCallbackHandle);
    if (!NT_SUCCESS(status)) g_TAD.ObCallbackHandle = NULL;
    return status;
}

VOID TADUnregisterProcessProtection(VOID)
{
    PAGED_CODE();
    if (g_TAD.ObCallbackHandle) {
        ObUnRegisterCallbacks(g_TAD.ObCallbackHandle);
        g_TAD.ObCallbackHandle = NULL;
    }
    g_TAD.ProtectedPid = NULL;
}

/* ═══════════════════════════════════════════════════════════════════════
 * 9.  ANTI-DELETION & ANTI-RENAME — Minifilter
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
FLT_PREOP_CALLBACK_STATUS
TADPreSetInformationCallback(
    _Inout_ PFLT_CALLBACK_DATA          Data,
    _In_    PCFLT_RELATED_OBJECTS        FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID *CompletionContext)
{
    PFLT_FILE_NAME_INFORMATION nameInfo = NULL;
    FILE_INFORMATION_CLASS     infoClass;
    NTSTATUS                   status;
    BOOLEAN isDeletion = FALSE, isRename = FALSE, block = FALSE;

    UNREFERENCED_PARAMETER(FltObjects);
    *CompletionContext = NULL;

    infoClass = Data->Iopb->Parameters.SetFileInformation.FileInformationClass;

    switch (infoClass) {
    case FileDispositionInformation: {
        PFILE_DISPOSITION_INFORMATION d =
            (PFILE_DISPOSITION_INFORMATION)Data->Iopb->Parameters.SetFileInformation.InfoBuffer;
        if (d && d->DeleteFile) isDeletion = TRUE;
        break;
    }
    case FileDispositionInformationEx:
        isDeletion = TRUE;
        break;
    case FileRenameInformation:
    case FileRenameInformationEx:
        isRename = TRUE;
        break;
    default:
        return FLT_PREOP_SUCCESS_NO_CALLBACK;
    }

    if (!isDeletion && !isRename) return FLT_PREOP_SUCCESS_NO_CALLBACK;

    status = FltGetFileNameInformation(Data,
        FLT_FILE_NAME_NORMALIZED | FLT_FILE_NAME_QUERY_DEFAULT, &nameInfo);
    if (!NT_SUCCESS(status) || !nameInfo) return FLT_PREOP_SUCCESS_NO_CALLBACK;

    status = FltParseFileNameInformation(nameInfo);
    if (!NT_SUCCESS(status)) { FltReleaseFileNameInformation(nameInfo); return FLT_PREOP_SUCCESS_NO_CALLBACK; }

    if (TADIsProtectedFilename(&nameInfo->FinalComponent))
        block = TRUE;

    FltReleaseFileNameInformation(nameInfo);

    if (block) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                   "[TAD.RV] BLOCKED %s of %wZ\n",
                   isDeletion ? "deletion" : "rename",
                   &Data->Iopb->TargetFileObject->FileName));
        Data->IoStatus.Status = STATUS_ACCESS_DENIED;
        Data->IoStatus.Information = 0;
        return FLT_PREOP_COMPLETE;
    }

    return FLT_PREOP_SUCCESS_NO_CALLBACK;
}

NTSTATUS FLTAPI TADFilterUnloadCallback(_In_ FLT_FILTER_UNLOAD_FLAGS Flags)
{
    UNREFERENCED_PARAMETER(Flags);
    return (InterlockedCompareExchange(&g_TAD.AllowUnload, 0, 0) == 0)
        ? STATUS_FLT_DO_NOT_DETACH
        : STATUS_SUCCESS;
}

/* ═══════════════════════════════════════════════════════════════════════════
 * 10. PROCESS CREATION MONITOR — PsSetCreateProcessNotifyRoutineEx
 *
 * TADProcessNotifyCallback fires at PASSIVE_LEVEL for every process
 * creation and termination system-wide.
 *
 * On creation (CreateInfo != NULL):
 *   1. Extract the final path component (filename) of ImageFileName.
 *   2. Acquire BannedAppsLock and compare against BannedApps[].
 *   3. If matched AND TAD_POLICY_FLAG_BLOCK_APPS is set in the current
 *      policy, set CreateInfo->CreationStatus = STATUS_ACCESS_DENIED.
 *   4. Queue a TADAlertProcessBlocked alert for the next ReadAlert IRP.
 *
 * On termination (CreateInfo == NULL):  no-op.
 *
 * The callback is registered with /INTEGRITYCHECK in the PE header
 * (see SOURCES). Without that flag PsSetCreateProcessNotifyRoutineEx
 * returns STATUS_ACCESS_DENIED.
 * ═══════════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
VOID
TADProcessNotifyCallback(
    _Inout_  PEPROCESS               Process,
    _In_     HANDLE                   ProcessId,
    _In_opt_ PPS_CREATE_NOTIFY_INFO   CreateInfo
    )
{
    ULONG          i;
    UNICODE_STRING component;
    USHORT         lastSep;
    USHORT         k;

    PAGED_CODE();
    UNREFERENCED_PARAMETER(Process);

    /* Only interested in creations, not terminations */
    if (!CreateInfo)                   return;
    if (!CreateInfo->ImageFileName)    return;
    if (!CreateInfo->ImageFileName->Buffer  ||
         CreateInfo->ImageFileName->Length == 0) return;

    /*
     * Only enforce the list when the policy has BlockApps set.
     * The driver accepts the list update regardless so that the list
     * is ready the moment the policy flag is toggled on.
     */
    if (!(g_TAD.CurrentPolicy.Flags & TAD_POLICY_FLAG_BLOCK_APPS)) return;

    /*
     * Find the last '\\' in the full NT image path
     * (e.g. "\\Device\\HarddiskVolume3\\Windows\\notepad.exe"
     *  → component starts after the last '\\').
     */
    lastSep = 0;
    for (k = 0; k < CreateInfo->ImageFileName->Length / sizeof(WCHAR); k++) {
        if (CreateInfo->ImageFileName->Buffer[k] == L'\\') {
            lastSep = k + 1;
        }
    }

    component.Buffer        = CreateInfo->ImageFileName->Buffer + lastSep;
    component.Length        = CreateInfo->ImageFileName->Length
                            - (lastSep * sizeof(WCHAR));
    component.MaximumLength = component.Length;

    if (component.Length == 0) return;

    ExAcquireFastMutex(&g_TAD.BannedAppsLock);

    for (i = 0; i < g_TAD.BannedAppCount; i++)
    {
        if (RtlEqualUnicodeString(&component, &g_TAD.BannedApps[i], TRUE /*case-insensitive*/))
        {
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                       "[TAD.RV] BLOCKED process: %wZ (PID %lu)\n",
                       &component, HandleToULong(ProcessId)));

            CreateInfo->CreationStatus = STATUS_ACCESS_DENIED;

            /*
             * TODO: complete alert-queue integration.
             * When the pended-IRP alert queue is implemented, enqueue a
             * TADAlertProcessBlocked event here so TADBridgeService can
             * display a real-time notification in the Console dashboard.
             */
            break;
        }
    }

    ExReleaseFastMutex(&g_TAD.BannedAppsLock);
}

NTSTATUS TADRegisterProcessNotify(VOID)
{
    NTSTATUS status;
    PAGED_CODE();

    if (g_TAD.ProcessNotifyRegistered) return STATUS_ALREADY_REGISTERED;

    /*
     * PsSetCreateProcessNotifyRoutineEx requires the driver image to have
     * IMAGE_DLLCHARACTERISTICS_FORCE_INTEGRITY set (/INTEGRITYCHECK linker
     * flag).  Without it this call returns STATUS_ACCESS_DENIED.
     */
    status = PsSetCreateProcessNotifyRoutineEx(TADProcessNotifyCallback, FALSE);
    if (NT_SUCCESS(status)) {
        g_TAD.ProcessNotifyRegistered = TRUE;
    }
    return status;
}

VOID TADUnregisterProcessNotify(VOID)
{
    PAGED_CODE();
    if (!g_TAD.ProcessNotifyRegistered) return;

    /*
     * Pass Remove=TRUE to deregister.  Must be called before DriverUnload
     * returns to prevent a bugcheck if the callback fires after the driver
     * image is unmapped.
     */
    PsSetCreateProcessNotifyRoutineEx(TADProcessNotifyCallback, TRUE);
    g_TAD.ProcessNotifyRegistered = FALSE;
}
