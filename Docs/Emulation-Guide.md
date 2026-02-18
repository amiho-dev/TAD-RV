# TAD.RV — Emulation & Demo Mode

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: Running TAD.RV without a kernel driver for development, demos,
> and integration testing.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Quick Start](#2-quick-start)
3. [What Gets Emulated](#3-what-gets-emulated)
4. [Demo Alerts](#4-demo-alerts)
5. [Tray Icon](#5-tray-icon)
6. [Running the Full Demo Stack](#6-running-the-full-demo-stack)
7. [Limitations](#7-limitations)

---

## 1. Overview

Emulation mode allows you to run the complete TAD.RV stack **without installing a kernel driver**. This is useful for:

- **Developer testing** — no driver signing or test-signing mode required
- **Sales demos** — show the full product on any Windows machine
- **CI/CD testing** — automated tests in containers or VMs without driver support
- **Training** — let new team members explore without production risk

## 2. Quick Start

```bash
# Run the service in emulation mode
TadBridgeService.exe --emulate

# Alternative flag
TadBridgeService.exe --demo
```

The service will:
1. Print `*** EMULATION MODE — no kernel driver required ***` to console
2. Stop any existing production `TadBridgeService` (to free the TCP port)
3. Start all subsystems with the `EmulatedDriverBridge` instead of real P/Invoke calls
4. Show a system tray icon (when running interactively)

## 3. What Gets Emulated

The `EmulatedDriverBridge` implements the same interface as the real `DriverBridge`:

| IOCTL | Emulated Behavior |
|---|---|
| `PROTECT_PID` | Logs the PID, no actual protection |
| `UNLOCK` | Always succeeds (accepts any key) |
| `HEARTBEAT` | Returns OK status with simulated metrics |
| `SET_USER_ROLE` | Logs the role change |
| `SET_POLICY` | Logs the policy flags, stores in memory |
| `READ_ALERT` | Generates a demo `ServiceTamper` alert every 3rd poll cycle |

### Simulated Heartbeat Output

```
ServiceRunning = true
DriverLoaded   = true  (emulated)
PolicyFlags    = <last set value>
UserRole       = <last set value>
HeartbeatCount = <incrementing>
```

## 4. Demo Alerts

In emulation mode, `ReadAlert()` generates **synthetic security alerts** to demonstrate the alerting pipeline:

- **Type**: `ServiceTamper` (severity: Critical)
- **Frequency**: Every 3rd polling cycle (~45–135 seconds apart)
- **Source PID**: Random (1000–65000)
- **Detail**: Timestamp and cycle counter

These alerts flow through the same pipeline as real alerts:
1. `AlertReaderWorker` reads the alert from `EmulatedDriverBridge`
2. Alert is written to the Windows Event Log (source `TadBridgeService`, event ID 9001)
3. The Management Console's **Alerts** page picks them up on its next refresh

## 5. Tray Icon

When running in emulation mode (interactive), a **system tray icon** appears:

- **Icon**: TAD.RV logo
- **Tooltip**: "TAD.RV Bridge Service — Emulation Mode"
- **Context menu**:
  - Service name (disabled label)
  - **Exit** — cleanly shuts down the service

The tray icon only appears in emulation mode (not when running as a real Windows Service, since SYSTEM services have no interactive desktop).

## 6. Running the Full Demo Stack

To demonstrate the complete system:

### Terminal 1 — Bridge Service (Emulated)

```bash
cd release-addc
.\TadBridgeService.exe --emulate
```

### Terminal 2 — Management Console

```bash
cd release-client
.\TadConsole.exe
```

Navigate through:
- **Dashboard** → See simulated driver & service status
- **Alerts** → Watch demo alerts appear
- **Policy** → Toggle flags (changes are reflected in the emulated driver)
- **Classrooms** → Design a room layout

### Terminal 3 — Teacher Controller

```bash
cd release-teacher
.\TadTeacher.exe
```

The Teacher will discover the emulated service via multicast and show it in the student grid.

## 7. Limitations

| Feature | Real Mode | Emulation Mode |
|---|---|---|
| Process protection | ✅ Kernel-enforced | ❌ Not enforced |
| File protection | ✅ Minifilter blocks delete/rename | ❌ Not enforced |
| Heartbeat watchdog | ✅ Kernel DPC timer | ⚠️ Simulated (no actual timeout) |
| Screen capture | ✅ Full capture | ✅ Full capture (uses GDI, not driver) |
| AD provisioning | ✅ Real LDAP/SMB | ⚠️ Works if domain-joined, else skips |
| Security alerts | ✅ Real kernel alerts | ⚠️ Synthetic demo alerts only |
| USB/Print blocking | ✅ Driver-enforced | ❌ Not enforced |
| Service protection | ✅ Cannot be killed | ❌ Can be killed normally |

> **Key point**: Emulation mode is for **demonstration and testing only**. It does not provide any actual endpoint protection.

---

*See also: [Architecture.md](Architecture.md) · [Build-Guide.md](Build-Guide.md) · [Deployment-Guide.md](Deployment-Guide.md)*
