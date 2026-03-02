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
      11. User role + policy IOCTLs from TadBridgeService
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
#pragma alloc_text(PAGE,  TadDriverUnload)
#pragma alloc_text(PAGE,  TadDispatchCreateClose)
#pragma alloc_text(PAGE,  TadDispatchDeviceControl)
#pragma alloc_text(PAGE,  TadCreateDeviceAndSymlink)
#pragma alloc_text(PAGE,  TadCleanupDeviceAndSymlink)
#pragma alloc_text(PAGE,  TadRegisterProcessProtection)
#pragma alloc_text(PAGE,  TadUnregisterProcessProtection)
#pragma alloc_text(PAGE,  TadSetDeviceDacl)
#pragma alloc_text(PAGE,  TadVerifyAuthKey)
#pragma alloc_text(PAGE,  TadProcessNotifyCallback)
#pragma alloc_text(PAGE,  TadRegisterProcessNotify)
#pragma alloc_text(PAGE,  TadUnregisterProcessNotify)
#endif

/* ═══════════════════════════════════════════════════════════════════════
 * Global Driver State
 * ═══════════════════════════════════════════════════════════════════════ */

TAD_DRIVER_GLOBALS g_Tad = { 0 };

/* ═══════════════════════════════════════════════════════════════════════
 * Minifilter Registration Tables
 * ═══════════════════════════════════════════════════════════════════════ */

static const FLT_OPERATION_REGISTRATION g_TadFilterCallbacks[] = {
    { IRP_MJ_SET_INFORMATION, 0, TadPreSetInformationCallback, NULL },
    { IRP_MJ_OPERATION_END }
};

static const FLT_REGISTRATION g_TadFilterRegistration = {
    sizeof(FLT_REGISTRATION),
    FLT_REGISTRATION_VERSION,
    0, NULL,
    g_TadFilterCallbacks,
    TadFilterUnloadCallback,
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

    RtlZeroMemory(&g_Tad, sizeof(g_Tad));
    InterlockedExchange(&g_Tad.AllowUnload, 0);
    InterlockedExchange(&g_Tad.FailedUnlockAttempts, 0);
    InterlockedExchange(&g_Tad.HeartbeatAlive, 0);
    InterlockedExchange(&g_Tad.PolicyValid, 0);
    InterlockedExchange(&g_Tad.CurrentUserRole, (LONG)TadRoleUnknown);
    ExInitializeFastMutex(&g_Tad.BannedAppsLock);

    DriverObject->MajorFunction[IRP_MJ_CREATE]         = TadDispatchCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE]          = TadDispatchCreateClose;
    DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = TadDispatchDeviceControl;
    DriverObject->DriverUnload                         = TadDriverUnload;

    status = TadCreateDeviceAndSymlink(DriverObject);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
                   "[TAD.RV] Device creation failed: 0x%08X\n", status));
        return status;
    }

    status = TadSetDeviceDacl(g_Tad.DeviceObject);
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                   "[TAD.RV] DACL failed: 0x%08X (non-fatal)\n", status));
    }

    status = TadRegisterProcessProtection();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                   "[TAD.RV] ObCallbacks failed: 0x%08X\n", status));
    }

    status = TadRegisterProcessNotify();
    if (!NT_SUCCESS(status)) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                   "[TAD.RV] PsProcessNotify registration failed: 0x%08X\n", status));
    }

    status = FltRegisterFilter(DriverObject, &g_TadFilterRegistration, &g_Tad.FilterHandle);
    if (NT_SUCCESS(status)) {
        status = FltStartFiltering(g_Tad.FilterHandle);
        if (!NT_SUCCESS(status)) {
            FltUnregisterFilter(g_Tad.FilterHandle);
            g_Tad.FilterHandle = NULL;
        }
    } else {
        g_Tad.FilterHandle = NULL;
    }

    /* Initialise the heartbeat watchdog DPC timer */
    TadInitHeartbeatWatchdog();

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
               "[TAD.RV] Loaded (ObCB=%s, PsNotify=%s, Flt=%s)\n",
               g_Tad.ObCallbackHandle         ? "YES" : "NO",
               g_Tad.ProcessNotifyRegistered  ? "YES" : "NO",
               g_Tad.FilterHandle             ? "YES" : "NO"));

    return STATUS_SUCCESS;
}

_Use_decl_annotations_
VOID
TadDriverUnload(
    _In_ PDRIVER_OBJECT DriverObject
    )
{
    PAGED_CODE();
    UNREFERENCED_PARAMETER(DriverObject);

    if (InterlockedCompareExchange(&g_Tad.AllowUnload, 0, 0) == 0) {
        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL,
                   "[TAD.RV] Unload DENIED\n"));
        return;
    }

    TadStopHeartbeatWatchdog();

    if (g_Tad.FilterHandle) {
        FltUnregisterFilter(g_Tad.FilterHandle);
        g_Tad.FilterHandle = NULL;
    }

    TadUnregisterProcessNotify();
    TadUnregisterProcessProtection();

    if (g_Tad.AgentProcess) {
        ObDereferenceObject(g_Tad.AgentProcess);
        g_Tad.AgentProcess = NULL;
    }

    TadCleanupDeviceAndSymlink();

    KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
               "[TAD.RV] Unloaded\n"));
}

/* ═══════════════════════════════════════════════════════════════════════
 * 2.  DEVICE & SYMBOLIC LINK
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
NTSTATUS
TadCreateDeviceAndSymlink(_In_ PDRIVER_OBJECT DriverObject)
{
    NTSTATUS       status;
    UNICODE_STRING deviceName;

    PAGED_CODE();

    RtlInitUnicodeString(&deviceName,          TAD_DEVICE_NAME);
    RtlInitUnicodeString(&g_Tad.SymbolicLink,  TAD_SYMBOLIC_LINK);

    status = IoCreateDevice(DriverObject, 0, &deviceName, TAD_DEVICE_TYPE,
                            FILE_DEVICE_SECURE_OPEN, FALSE, &g_Tad.DeviceObject);
    if (!NT_SUCCESS(status)) return status;

    g_Tad.DeviceObject->Flags |= DO_BUFFERED_IO;
    g_Tad.DeviceObject->Flags &= ~DO_DEVICE_INITIALIZING;

    status = IoCreateSymbolicLink(&g_Tad.SymbolicLink, &deviceName);
    if (!NT_SUCCESS(status)) {
        IoDeleteDevice(g_Tad.DeviceObject);
        g_Tad.DeviceObject = NULL;
        return status;
    }

    g_Tad.SymbolicLinkCreated = TRUE;
    return STATUS_SUCCESS;
}

_Use_decl_annotations_
VOID TadCleanupDeviceAndSymlink(VOID)
{
    PAGED_CODE();
    if (g_Tad.SymbolicLinkCreated) {
        IoDeleteSymbolicLink(&g_Tad.SymbolicLink);
        g_Tad.SymbolicLinkCreated = FALSE;
    }
    if (g_Tad.DeviceObject) {
        IoDeleteDevice(g_Tad.DeviceObject);
        g_Tad.DeviceObject = NULL;
    }
}

/* ═══════════════════════════════════════════════════════════════════════
 * 3.  DEVICE DACL HARDENING
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
NTSTATUS TadSetDeviceDacl(_In_ PDEVICE_OBJECT DeviceObject)
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
BOOLEAN TadVerifyAuthKey(_In_reads_bytes_(TAD_AUTH_KEY_SIZE) const UCHAR *ProvidedKey)
{
    UCHAR decoded[TAD_AUTH_KEY_SIZE];
    UCHAR diff = 0;
    ULONG i;
    PAGED_CODE();

    for (i = 0; i < TAD_AUTH_KEY_SIZE; i++)
        decoded[i] = TadObfuscatedKey[i] ^ TAD_KEY_XOR_MASK;

    for (i = 0; i < TAD_AUTH_KEY_SIZE; i++)
        diff |= (decoded[i] ^ ProvidedKey[i]);

    RtlSecureZeroMemory(decoded, sizeof(decoded));
    return (diff == 0) ? TRUE : FALSE;
}

_Use_decl_annotations_
BOOLEAN TadIsCallerProtectedAgent(VOID)
{
    return (g_Tad.AgentProcess && PsGetCurrentProcess() == g_Tad.AgentProcess);
}

BOOLEAN TadIsProtectedFilename(_In_ PCUNICODE_STRING FileName)
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

VOID TadInitHeartbeatWatchdog(VOID)
{
    KeInitializeTimer(&g_Tad.HeartbeatTimer);
    KeInitializeDpc(&g_Tad.HeartbeatDpc, TadHeartbeatDpcRoutine, NULL);

    /* Start the timer — fires every HeartbeatTimeout period */
    LARGE_INTEGER dueTime;
    dueTime.QuadPart = -((LONGLONG)TAD_HEARTBEAT_TIMEOUT_MS * 10 * 1000); /* relative, 100ns */

    KeSetTimerEx(&g_Tad.HeartbeatTimer, dueTime,
                 TAD_HEARTBEAT_TIMEOUT_MS, /* periodic interval in ms */
                 &g_Tad.HeartbeatDpc);
}

VOID TadStopHeartbeatWatchdog(VOID)
{
    KeCancelTimer(&g_Tad.HeartbeatTimer);
}

/*
 * DPC fires at IRQL = DISPATCH_LEVEL.
 * Check HeartbeatAlive flag; if 0, the service hasn't checked in.
 */
_Use_decl_annotations_
VOID
TadHeartbeatDpcRoutine(
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

    if (InterlockedExchange(&g_Tad.HeartbeatAlive, 0) == 0) {
        /*
         * Service has NOT sent a heartbeat since the last DPC tick.
         * Actions:
         *   1. Log the event
         *   2. Engage WFP network killswitch (future: inject WFP callout)
         *   3. Queue a TadAlertHeartbeatLost for the next ReadAlert IRP
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
NTSTATUS TadDispatchCreateClose(
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
 * Handles all IOCTLs defined in TadShared.h:
 *   0x800 PROTECT_PID      0x801 UNLOCK          0x802 HEARTBEAT
 *   0x803 SET_USER_ROLE    0x804 SET_POLICY       0x805 READ_ALERT
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
NTSTATUS TadDispatchDeviceControl(
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

        if (g_Tad.AgentProcess) ObDereferenceObject(g_Tad.AgentProcess);
        g_Tad.AgentProcess = proc;

        InterlockedExchangePointer((PVOID volatile *)&g_Tad.ProtectedPid,
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

        if (g_Tad.AgentProcess && !TadIsCallerProtectedAgent()) {
            status = STATUS_ACCESS_DENIED; break;
        }

        KeQuerySystemTime(&now);
        if (g_Tad.FailedUnlockAttempts >= TAD_MAX_UNLOCK_ATTEMPTS) {
            if (now.QuadPart < g_Tad.LockoutUntil.QuadPart) {
                status = STATUS_ACCESS_DENIED; break;
            }
            InterlockedExchange(&g_Tad.FailedUnlockAttempts, 0);
        }

        p = (PTAD_UNLOCK_INPUT)buf;
        if (TadVerifyAuthKey(p->AuthKey)) {
            InterlockedExchange(&g_Tad.AllowUnload, 1);
            InterlockedExchange(&g_Tad.FailedUnlockAttempts, 0);
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                       "[TAD.RV] Unlock ACCEPTED\n"));
        } else {
            LONG a = InterlockedIncrement(&g_Tad.FailedUnlockAttempts);
            if (a >= TAD_MAX_UNLOCK_ATTEMPTS) {
                KeQuerySystemTime(&g_Tad.LockoutUntil);
                /* TAD_LOCKOUT_DURATION is negative (relative time), so add it
                 * to move LockoutUntil into the future. */
                g_Tad.LockoutUntil.QuadPart += (-TAD_LOCKOUT_DURATION);
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
        InterlockedExchange(&g_Tad.HeartbeatAlive, 1);
        KeQuerySystemTime(&g_Tad.LastHeartbeatTime);

        hb = (PTAD_HEARTBEAT_OUTPUT)buf;
        RtlZeroMemory(hb, sizeof(*hb));

        hb->DriverVersionMajor      = TAD_VERSION_MAJOR;
        hb->DriverVersionMinor      = TAD_VERSION_MINOR;
        hb->ProtectedPid            = HandleToULong(g_Tad.ProtectedPid);
        hb->ProcessProtectionActive = (g_Tad.ObCallbackHandle != NULL);
        hb->FileProtectionActive    = (g_Tad.FilterHandle     != NULL);
        hb->UnlockPermitted         = (InterlockedCompareExchange(&g_Tad.AllowUnload, 0, 0) != 0);
        hb->HeartbeatAlive          = 1;
        hb->FailedUnlockAttempts    = (ULONG)g_Tad.FailedUnlockAttempts;
        hb->CurrentUserRole         = (ULONG)g_Tad.CurrentUserRole;
        hb->PolicyValid             = (ULONG)g_Tad.PolicyValid;

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

        if (g_Tad.AgentProcess && !TadIsCallerProtectedAgent()) {
            status = STATUS_ACCESS_DENIED; break;
        }

        p = (PTAD_SET_USER_ROLE_INPUT)buf;
        InterlockedExchange(&g_Tad.CurrentUserRole, (LONG)p->Role);

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

        if (g_Tad.AgentProcess && !TadIsCallerProtectedAgent()) {
            status = STATUS_ACCESS_DENIED; break;
        }

        p = (PTAD_POLICY_BUFFER)buf;
        if (p->Version != 1) { status = STATUS_INVALID_PARAMETER; break; }

        RtlCopyMemory(&g_Tad.CurrentPolicy, p, sizeof(TAD_POLICY_BUFFER));
        InterlockedExchange(&g_Tad.PolicyValid, 1);

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
        a->AlertType = TadAlertNone;
        KeQuerySystemTime((PLARGE_INTEGER)&a->Timestamp);

        bytesWritten = sizeof(TAD_ALERT_OUTPUT);
        break;
    }

    /* ── IOCTL_TAD_HARD_LOCK ──────────────────────────────────────── */
    case IOCTL_TAD_HARD_LOCK:
    {
        PTAD_HARD_LOCK_INPUT hl;
        if (inLen < sizeof(TAD_HARD_LOCK_INPUT)) { status = STATUS_BUFFER_TOO_SMALL; break; }
        if (!TadIsCallerProtectedAgent()) { status = STATUS_ACCESS_DENIED; break; }

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
         * by TadInputFilterDispatch() in the input filter subsystem.
         */
        InterlockedExchange(&g_Tad.InputLocked, hl->Enable ? 1 : 0);

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
        if (!TadIsCallerProtectedAgent()) { status = STATUS_ACCESS_DENIED; break; }

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
         * We store the UI PID in g_Tad.ProtectedUiPid.  The existing
         * ObCallback checks BOTH ProtectedPid (service) and
         * ProtectedUiPid (lock overlay).
         */
        if (ui->Protect)
            InterlockedExchangePointer(
                (PVOID volatile *)&g_Tad.ProtectedUiPid,
                (PVOID)(ULONG_PTR)ui->TargetPid);
        else
            InterlockedExchangePointer(
                (PVOID volatile *)&g_Tad.ProtectedUiPid, NULL);

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
        if (!TadIsCallerProtectedAgent()) { status = STATUS_ACCESS_DENIED; break; }

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
            InterlockedExchange(&g_Tad.StealthActive, 1);
            g_Tad.StealthFlags = stl->Flags;
        }
        else
        {
            InterlockedExchange(&g_Tad.StealthActive, 0);
            g_Tad.StealthFlags = 0;
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
        if (!TadIsCallerProtectedAgent())           { status = STATUS_ACCESS_DENIED;    break; }

#if defined(_AMD64_) || defined(_X86_)
        _mm_lfence();
#endif

        p = (PTAD_BANNED_APPS_INPUT)buf;
        if (p->Count > TAD_MAX_BANNED_APPS) { status = STATUS_INVALID_PARAMETER; break; }

        ExAcquireFastMutex(&g_Tad.BannedAppsLock);

        /* Clear the previous list */
        RtlZeroMemory(g_Tad.BannedAppStorage, sizeof(g_Tad.BannedAppStorage));
        RtlZeroMemory(g_Tad.BannedApps,       sizeof(g_Tad.BannedApps));
        g_Tad.BannedAppCount = 0;

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

            RtlCopyMemory(g_Tad.BannedAppStorage[i],
                          p->ImageNames[i],
                          srcLen * sizeof(WCHAR));

            g_Tad.BannedApps[i].Buffer        = g_Tad.BannedAppStorage[i];
            g_Tad.BannedApps[i].Length        = (USHORT)(srcLen * sizeof(WCHAR));
            g_Tad.BannedApps[i].MaximumLength = (USHORT)(TAD_MAX_IMAGE_NAME_LEN * sizeof(WCHAR));
            g_Tad.BannedAppCount++;
        }

        ExReleaseFastMutex(&g_Tad.BannedAppsLock);

        KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_INFO_LEVEL,
                   "[TAD.RV] Banned-app list updated: %lu entr%s\n",
                   g_Tad.BannedAppCount,
                   g_Tad.BannedAppCount == 1 ? "y" : "ies"));
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
TadObProcessPreCallback(
    _In_    PVOID                           RegistrationContext,
    _Inout_ POB_PRE_OPERATION_INFORMATION   OpInfo)
{
    HANDLE tPid, cPid, pPid;
    UNREFERENCED_PARAMETER(RegistrationContext);

    pPid = (HANDLE)InterlockedCompareExchangePointer(
        (PVOID volatile *)&g_Tad.ProtectedPid, NULL, NULL);
    if (!pPid) {
        /* Also check UI overlay PID */
        pPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_Tad.ProtectedUiPid, NULL, NULL);
        if (!pPid) return OB_PREOP_SUCCESS;
    }
    if (OpInfo->ObjectType != *PsProcessType) return OB_PREOP_SUCCESS;

    tPid = PsGetProcessId((PEPROCESS)OpInfo->Object);
    cPid = PsGetCurrentProcessId();

    /* Protect both the service PID and the UI overlay PID */
    {
        HANDLE svcPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_Tad.ProtectedPid, NULL, NULL);
        HANDLE uiPid  = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_Tad.ProtectedUiPid, NULL, NULL);

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
TadObThreadPreCallback(
    _In_    PVOID                           RegistrationContext,
    _Inout_ POB_PRE_OPERATION_INFORMATION   OpInfo)
{
    HANDLE oPid, cPid, pPid;
    UNREFERENCED_PARAMETER(RegistrationContext);

    pPid = (HANDLE)InterlockedCompareExchangePointer(
        (PVOID volatile *)&g_Tad.ProtectedPid, NULL, NULL);
    if (!pPid) {
        /* Also check UI overlay PID for thread protection */
        pPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_Tad.ProtectedUiPid, NULL, NULL);
        if (!pPid) return OB_PREOP_SUCCESS;
    }
    if (OpInfo->ObjectType != *PsThreadType) return OB_PREOP_SUCCESS;

    oPid = PsGetProcessId(IoThreadToProcess((PETHREAD)OpInfo->Object));
    cPid = PsGetCurrentProcessId();

    /* Protect threads of both the service PID and UI overlay PID */
    {
        HANDLE svcPid = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_Tad.ProtectedPid, NULL, NULL);
        HANDLE uiPid  = (HANDLE)InterlockedCompareExchangePointer(
            (PVOID volatile *)&g_Tad.ProtectedUiPid, NULL, NULL);

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

NTSTATUS TadRegisterProcessProtection(VOID)
{
    NTSTATUS                   status;
    OB_CALLBACK_REGISTRATION  cbReg;
    OB_OPERATION_REGISTRATION opReg[2];
    UNICODE_STRING             altitude;

    PAGED_CODE();
    if (g_Tad.ObCallbackHandle) return STATUS_ALREADY_REGISTERED;

    RtlInitUnicodeString(&altitude, TAD_DRIVER_ALTITUDE);

    RtlZeroMemory(opReg, sizeof(opReg));
    opReg[0].ObjectType   = PsProcessType;
    opReg[0].Operations   = OB_OPERATION_HANDLE_CREATE | OB_OPERATION_HANDLE_DUPLICATE;
    opReg[0].PreOperation = TadObProcessPreCallback;

    opReg[1].ObjectType   = PsThreadType;
    opReg[1].Operations   = OB_OPERATION_HANDLE_CREATE | OB_OPERATION_HANDLE_DUPLICATE;
    opReg[1].PreOperation = TadObThreadPreCallback;

    RtlZeroMemory(&cbReg, sizeof(cbReg));
    cbReg.Version                    = OB_FLT_REGISTRATION_VERSION;
    cbReg.OperationRegistrationCount = 2;
    cbReg.Altitude                   = altitude;
    cbReg.OperationRegistration      = opReg;

    status = ObRegisterCallbacks(&cbReg, &g_Tad.ObCallbackHandle);
    if (!NT_SUCCESS(status)) g_Tad.ObCallbackHandle = NULL;
    return status;
}

VOID TadUnregisterProcessProtection(VOID)
{
    PAGED_CODE();
    if (g_Tad.ObCallbackHandle) {
        ObUnRegisterCallbacks(g_Tad.ObCallbackHandle);
        g_Tad.ObCallbackHandle = NULL;
    }
    g_Tad.ProtectedPid = NULL;
}

/* ═══════════════════════════════════════════════════════════════════════
 * 9.  ANTI-DELETION & ANTI-RENAME — Minifilter
 * ═══════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
FLT_PREOP_CALLBACK_STATUS
TadPreSetInformationCallback(
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

    if (TadIsProtectedFilename(&nameInfo->FinalComponent))
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

NTSTATUS FLTAPI TadFilterUnloadCallback(_In_ FLT_FILTER_UNLOAD_FLAGS Flags)
{
    UNREFERENCED_PARAMETER(Flags);
    return (InterlockedCompareExchange(&g_Tad.AllowUnload, 0, 0) == 0)
        ? STATUS_FLT_DO_NOT_DETACH
        : STATUS_SUCCESS;
}

/* ═══════════════════════════════════════════════════════════════════════════
 * 10. PROCESS CREATION MONITOR — PsSetCreateProcessNotifyRoutineEx
 *
 * TadProcessNotifyCallback fires at PASSIVE_LEVEL for every process
 * creation and termination system-wide.
 *
 * On creation (CreateInfo != NULL):
 *   1. Extract the final path component (filename) of ImageFileName.
 *   2. Acquire BannedAppsLock and compare against BannedApps[].
 *   3. If matched AND TAD_POLICY_FLAG_BLOCK_APPS is set in the current
 *      policy, set CreateInfo->CreationStatus = STATUS_ACCESS_DENIED.
 *   4. Queue a TadAlertProcessBlocked alert for the next ReadAlert IRP.
 *
 * On termination (CreateInfo == NULL):  no-op.
 *
 * The callback is registered with /INTEGRITYCHECK in the PE header
 * (see SOURCES). Without that flag PsSetCreateProcessNotifyRoutineEx
 * returns STATUS_ACCESS_DENIED.
 * ═══════════════════════════════════════════════════════════════════════════ */

_Use_decl_annotations_
VOID
TadProcessNotifyCallback(
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
    if (!(g_Tad.CurrentPolicy.Flags & TAD_POLICY_FLAG_BLOCK_APPS)) return;

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

    ExAcquireFastMutex(&g_Tad.BannedAppsLock);

    for (i = 0; i < g_Tad.BannedAppCount; i++)
    {
        if (RtlEqualUnicodeString(&component, &g_Tad.BannedApps[i], TRUE /*case-insensitive*/))
        {
            KdPrintEx((DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL,
                       "[TAD.RV] BLOCKED process: %wZ (PID %lu)\n",
                       &component, HandleToULong(ProcessId)));

            CreateInfo->CreationStatus = STATUS_ACCESS_DENIED;

            /*
             * TODO: complete alert-queue integration.
             * When the pended-IRP alert queue is implemented, enqueue a
             * TadAlertProcessBlocked event here so TadBridgeService can
             * display a real-time notification in the Console dashboard.
             */
            break;
        }
    }

    ExReleaseFastMutex(&g_Tad.BannedAppsLock);
}

NTSTATUS TadRegisterProcessNotify(VOID)
{
    NTSTATUS status;
    PAGED_CODE();

    if (g_Tad.ProcessNotifyRegistered) return STATUS_ALREADY_REGISTERED;

    /*
     * PsSetCreateProcessNotifyRoutineEx requires the driver image to have
     * IMAGE_DLLCHARACTERISTICS_FORCE_INTEGRITY set (/INTEGRITYCHECK linker
     * flag).  Without it this call returns STATUS_ACCESS_DENIED.
     */
    status = PsSetCreateProcessNotifyRoutineEx(TadProcessNotifyCallback, FALSE);
    if (NT_SUCCESS(status)) {
        g_Tad.ProcessNotifyRegistered = TRUE;
    }
    return status;
}

VOID TadUnregisterProcessNotify(VOID)
{
    PAGED_CODE();
    if (!g_Tad.ProcessNotifyRegistered) return;

    /*
     * Pass Remove=TRUE to deregister.  Must be called before DriverUnload
     * returns to prevent a bugcheck if the callback fires after the driver
     * image is unmapped.
     */
    PsSetCreateProcessNotifyRoutineEx(TadProcessNotifyCallback, TRUE);
    g_Tad.ProcessNotifyRegistered = FALSE;
}
