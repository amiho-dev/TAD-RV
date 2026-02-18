# TAD.RV — Architecture Reference

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: Technical architecture of the TAD.RV endpoint protection platform —
> component responsibilities, communication flows, and security design.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Component Map](#2-component-map)
3. [Kernel Driver (TAD.RV.sys)](#3-kernel-driver-tadrvsys)
4. [Bridge Service (TadBridgeService.exe)](#4-bridge-service-tadbridgeserviceexe)
5. [Management Console (TadConsole.exe)](#5-management-console-tadconsoleexe)
6. [Teacher Controller (TadTeacher.exe)](#6-teacher-controller-tadteacherexe)
7. [Bootstrap Loader (TadBootstrap.exe)](#7-bootstrap-loader-tadbootstrapexe)
8. [Communication Protocols](#8-communication-protocols)
9. [Security Model](#9-security-model)
10. [Emulation Mode](#10-emulation-mode)

---

## 1. System Overview

TAD.RV is a multi-layered endpoint monitoring platform designed for school environments. It combines a Ring 0 kernel driver with a Ring 3 management service to provide tamper-resistant process protection, policy enforcement, and real-time classroom monitoring.

```
┌─────────────────────────────────────────────────────────────────────┐
│  Active Directory         \\DC\NETLOGON\TAD\Policy.json             │
│  ─────────────────        (Group → Role mappings, policy flags)     │
└──────────┬──────────────────────────────┬───────────────────────────┘
           │ LDAP / Group Policy           │ SMB
           ▼                               ▼
┌──────────────────────────────────────────────────────────────────┐
│  TadBridgeService.exe       (.NET 8 Worker Service / SYSTEM)     │
│  ┌─────────────┐ ┌──────────────┐ ┌────────────────────────┐    │
│  │ Provisioning │ │ AdGroupWatch │ │ OfflineCache (DPAPI)   │    │
│  └──────┬──────┘ └──────┬───────┘ └──────────┬─────────────┘    │
│         │               │                    │                   │
│  ┌──────▼───────────────▼────────────────────▼────────────────┐  │
│  │              DriverBridge (P/Invoke)                        │  │
│  │  PROTECT_PID · UNLOCK · HEARTBEAT · SET_ROLE · SET_POLICY  │  │
│  │  READ_ALERT                                                │  │
│  └────────────────────────┬───────────────────────────────────┘  │
│                           │                                      │
│  ┌────────────────────────▼───────────────────────────────────┐  │
│  │ ScreenCaptureEngine · PrivacyRedactor · TcpListener        │  │
│  │ MulticastDiscovery (LAN broadcast for Teacher discovery)   │  │
│  └────────────────────────────────────────────────────────────┘  │
└───────────────────────────┬──────────────────────────────────────┘
                            │ DeviceIoControl  (\\.\TadRvLink)
┌───────────────────────────┼──────────────────────────────────────┐
│  TAD.RV.sys               │         (Ring 0 — WDM + Minifilter)  │
│  ┌────────────────────────▼──────────────────────────────────┐   │
│  │ ObRegisterCallbacks   (process + thread protection)        │   │
│  │ FltRegisterFilter     (anti-delete, anti-rename)           │   │
│  │ Heartbeat DPC timer   (service liveness watchdog)          │   │
│  │ DACL-hardened device  (SYSTEM + Admins only)               │   │
│  │ 256-bit XOR auth key  (constant-time + rate-limiting)      │   │
│  │ Policy enforcement    (USB, print, screenshots)            │   │
│  └───────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

## 2. Component Map

| Component | Binary | Framework | Role |
|---|---|---|---|
| **Kernel Driver** | `TAD.RV.sys` | C / WDK | Ring 0 protection & enforcement |
| **Bridge Service** | `TadBridgeService.exe` | .NET 8 Worker Service | Ring 3 service — AD, driver comms, screen capture |
| **Management Console** | `TadConsole.exe` | .NET 8 WPF + WebView2 | Admin dashboard, deployment, policy editor |
| **Teacher Controller** | `TadTeacher.exe` | .NET 8 WPF + WebView2 | Classroom monitoring, freeze/unfreeze, RV |
| **Bootstrap Loader** | `TadBootstrap.exe` | .NET 8 Console | GPO startup — zero-touch deployment |

## 3. Kernel Driver (TAD.RV.sys)

Written in C using the Windows Driver Kit (WDK). Operates at Ring 0.

### Capabilities

| Feature | Mechanism |
|---|---|
| Process protection | `ObRegisterCallbacks` — strips TERMINATE, VM_WRITE, VM_OPERATION, CREATE_THREAD, SUSPEND_RESUME |
| Thread protection | Same for THREAD_TERMINATE, THREAD_SUSPEND_RESUME, THREAD_SET_CONTEXT |
| Anti-deletion | Minifilter blocks `FileDispositionInformation(Ex)` on protected filenames |
| Anti-rename | Minifilter blocks `FileRenameInformation(Ex)` to close rename-then-delete bypass |
| Heartbeat watchdog | Kernel DPC timer — fires if no heartbeat within 6 seconds |
| Authenticated unload | 256-bit XOR-obfuscated pre-shared key via `IOCTL_TAD_UNLOCK` |
| Rate limiting | 5 bad unlock attempts → 30-second lockout |
| DACL hardening | Device object restricted to SYSTEM + Administrators |
| Spectre V1 mitigation | `_mm_lfence()` barriers on all IOCTL input paths |

### IOCTL Interface

All IOCTLs use `METHOD_BUFFERED` on device type `0x8000`, defined in `Shared/TadShared.h`.

| Code | Name | Direction | Payload |
|---|---|---|---|
| 0x800 | `IOCTL_TAD_PROTECT_PID` | Svc → Driver | `TAD_PROTECT_PID_INPUT` |
| 0x801 | `IOCTL_TAD_UNLOCK` | Svc → Driver | `TAD_UNLOCK_INPUT` (32 bytes) |
| 0x802 | `IOCTL_TAD_HEARTBEAT` | Svc → Driver | — / `TAD_HEARTBEAT_OUTPUT` |
| 0x803 | `IOCTL_TAD_SET_USER_ROLE` | Svc → Driver | `TAD_SET_USER_ROLE_INPUT` |
| 0x804 | `IOCTL_TAD_SET_POLICY` | Svc → Driver | `TAD_POLICY_BUFFER` |
| 0x805 | `IOCTL_TAD_READ_ALERT` | Driver → Svc | `TAD_ALERT_OUTPUT` |

## 4. Bridge Service (TadBridgeService.exe)

.NET 8 Worker Service running as `LocalSystem`. Five subsystems:

| Worker | Responsibility |
|---|---|
| **TadBridgeWorker** | Primary startup orchestrator — coordinates all subsystems |
| **HeartbeatWorker** | Sends `IOCTL_TAD_HEARTBEAT` every 2 seconds |
| **AlertReaderWorker** | Long-polls `IOCTL_TAD_READ_ALERT`, writes alerts to Event Log |
| **ProvisioningManager** | First-boot AD/OU provisioning, fetches `Policy.json` from NETLOGON |
| **AdGroupWatcher** | Polls AD groups every 10s, resolves `TAD_USER_ROLE` from mappings |

Supporting components:

| Component | Responsibility |
|---|---|
| **OfflineCacheManager** | DPAPI-encrypted offline cache — 7-day TTL when DC is unreachable |
| **ScreenCaptureEngine** | Captures and compresses screen frames for teacher RV |
| **PrivacyRedactor** | Redacts sensitive regions before frames leave the machine |
| **MulticastDiscovery** | UDP multicast for Teacher↔Service LAN discovery |
| **TadTcpListener** | TCP server for Teacher connections (screen streaming, freeze) |
| **TrayIconManager** | System tray icon in emulation/interactive mode |

### Registry Configuration

`HKLM\SOFTWARE\TAD_RV`:

| Value | Type | Description |
|---|---|---|
| `Installed` | DWORD | 1 if deployment completed |
| `PolicyFlags` | DWORD | Active policy bitmask |
| `UserRole` | DWORD | Current user's resolved role |
| `PolicySource` | SZ | UNC path to Policy.json |
| `LastProvision` | SZ | ISO 8601 timestamp of last provisioning |

## 5. Management Console (TadConsole.exe)

WPF shell hosting a WebView2 control. The entire UI is an embedded SPA (HTML/CSS/JS) using a GitHub-dark theme.

### Pages

| Page | Features |
|---|---|
| **Dashboard** | Driver + service status, health checks, system info, registry config, service controls |
| **Deploy** | Driver & service file pickers, install directory, one-click deployment with progress log |
| **Policy** | Visual toggle switches for all policy flags, with descriptions. Save to registry / export JSON |
| **Alerts** | Live alerts from the Event Log, severity filters, search, detail pane |
| **Classrooms** | Room designer canvas, drag-and-drop student placement, connect to Teacher |

### C# ↔ JavaScript Bridge

The Console uses `chrome.webview.hostObjects` to call C# services from JavaScript:

- `DeploymentService` — file copy, driver install, service registration
- `TadServiceController` — start/stop/restart Windows Service
- `RegistryService` — read/write `HKLM\SOFTWARE\TAD_RV`
- `EventLogService` — query Windows Event Log
- `SystemInfoService` — machine info, driver/service status

### Internationalization

All UI text uses the `TAD_I18N` module with `data-i18n` attributes. Seven languages are bundled:
English, German, French, Dutch, Spanish, Italian, Polish.

## 6. Teacher Controller (TadTeacher.exe)

WPF shell with WebView2, designed for teacher use during class. Features:

| Feature | Description |
|---|---|
| **Student grid** | Live thumbnail grid of all connected student desktops |
| **Remote View (RV)** | Full-resolution live view of a single student screen |
| **Freeze** | Freeze all / selected student screens with timed or indefinite lock |
| **Room selector** | Switch between configured classroom layouts |
| **Message broadcast** | Send text messages to all students |

### Network Protocol

Teachers discover student endpoints via **UDP multicast** (239.77.65.68:47100), then establish **TCP connections** (port 47101) for:

- Screen frame streaming (H.264-encoded via WebCodecs API)
- Freeze/unfreeze commands
- Status polling

## 7. Bootstrap Loader (TadBootstrap.exe)

Minimal console application designed for GPO startup scripts:

1. Copies `TadBridgeService.exe` from UNC share to `C:\Program Files\TAD_RV\`
2. Registers Windows Service with automatic start + recovery policy
3. Starts the service immediately

No UI, no .NET runtime required on target — fully self-contained.

## 8. Communication Protocols

### Driver ↔ Service

`DeviceIoControl` via `\\.\TadRvLink` device handle. All payloads are `StructLayout.Sequential` with `Pack = 8`. The C header (`Shared/TadShared.h`) and C# interop (`Shared/TadSharedInterop.cs`) are kept in sync manually.

### Service ↔ Teacher

| Protocol | Port | Purpose |
|---|---|---|
| UDP Multicast | 239.77.65.68:47100 | Service announces presence every 5 seconds |
| TCP | 47101 | Bidirectional command/data channel |

Message framing: 4-byte length prefix + JSON envelope for commands, raw frame data for screen captures.

### Service ↔ AD

| Protocol | Purpose |
|---|---|
| LDAP (via `System.DirectoryServices`) | Group membership queries |
| SMB (UNC path) | Policy.json retrieval from NETLOGON share |

## 9. Security Model

### Authentication
- 256-bit pre-shared key (XOR-obfuscated in binary) for driver unlock
- Rate-limited: 5 failures → 30-second lockout
- Constant-time comparison to prevent timing attacks

### Process Protection
- Kernel driver protects its own PID and the service PID
- `ObRegisterCallbacks` strips dangerous access rights from all callers
- Minifilter prevents deletion/rename of protected files

### Data Protection
- Offline AD cache encrypted with DPAPI (machine-scope)
- PrivacyRedactor masks sensitive screen regions before RV transmission
- All credentials stay on-machine; only role integers traverse the network

### Spectre Mitigation
- `_mm_lfence()` barriers on all IOCTL input-dependent branches

## 10. Emulation Mode

For development and demos, the service supports `--emulate` (or `--demo`):

```bash
TadBridgeService.exe --emulate
```

This activates `EmulatedDriverBridge` which:
- Simulates successful IOCTL responses without a kernel driver
- Generates periodic demo alerts (ServiceTamper every 3rd cycle)
- Provides a tray icon for visual feedback
- Stops any running production service to avoid port conflicts

Emulation mode is ideal for:
- Developer testing without driver signing
- Demo environments on non-domain machines
- CI/CD integration testing

---

*See also: [Deployment-Guide.md](Deployment-Guide.md) · [Signing-Handbook.md](Signing-Handbook.md) · [Teacher-Guide.md](Teacher-Guide.md)*
