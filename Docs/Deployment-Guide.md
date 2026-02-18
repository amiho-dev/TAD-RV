# TAD.RV — Deployment Guide

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: Step-by-step instructions for deploying TAD.RV to school
> workstations — from initial setup through AD integration and verification.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Build Artifacts](#2-build-artifacts)
3. [Deployment Methods](#3-deployment-methods)
   - [GUI Console (Recommended)](#31-gui-console-recommended)
   - [PowerShell Script](#32-powershell-script)
   - [Manual Installation](#33-manual-installation)
4. [GPO Startup Script (Mass Deployment)](#4-gpo-startup-script-mass-deployment)
5. [Active Directory Policy Setup](#5-active-directory-policy-setup)
6. [Verification](#6-verification)
7. [Uninstallation](#7-uninstallation)

---

## 1. Prerequisites

| Requirement | Details |
|---|---|
| **OS** | Windows 10/11 (64-bit) |
| **Runtime** | None — all binaries are self-contained (.NET 8 bundled) |
| **Privileges** | Local Administrator (or SYSTEM for GPO deployment) |
| **Driver signing** | See [Signing-Handbook.md](Signing-Handbook.md) |
| **Network** | Domain join required for AD policy provisioning |

## 2. Build Artifacts

After running `./build.sh`, the release folders contain:

| Folder | Contents | Target |
|---|---|---|
| `release-client/` | `TadConsole.exe` | Admin workstation (Management Console) |
| `release-teacher/` | `TadTeacher.exe` | Teacher workstation (Classroom Controller) |
| `release-addc/` | `TadBridgeService.exe`, `TadBootstrap.exe` | Student endpoints (via GPO) |

All executables are single-file, self-contained — no .NET installation required on target machines.

## 3. Deployment Methods

### 3.1 GUI Console (Recommended)

1. Run `TadConsole.exe` on an admin workstation (requests UAC elevation automatically)
2. Navigate to the **Deploy** page via the sidebar
3. Fill in:
   - **Driver path**: path to `TAD.RV.sys`
   - **Service path**: path to `TadBridgeService.exe`
   - **Install directory**: target folder (default `C:\Program Files\TAD_RV`)
4. Click **Deploy Now**
5. Monitor real-time progress in the deployment log

### 3.2 One-Click Driver Install (New in v26200.172)

The simplest way to install the kernel driver on a student machine:

```powershell
# Right-click → Run with PowerShell (as Admin)
.\Scripts\Install-Driver.ps1
```

This script auto-detects the driver files, installs via `pnputil` (preferred)
or `sc.exe` (fallback), and starts the driver. No parameters required.

**Auto-install from the service** — pass `--auto-install` to the bridge service
and it will install the driver automatically at startup:

```batch
TadBridgeService.exe --auto-install
```

### 3.3 PowerShell Script (Full Deployment)

```powershell
.\Scripts\Deploy-TadRV.ps1 `
    -DriverPath  ".\Kernel\TAD.RV.sys" `
    -ServicePath ".\release-addc\TadBridgeService.exe"
```

The script copies files, installs the driver, registers the Windows Service, and starts it.

### 3.4 Manual Installation

```batch
:: 1. Install the kernel driver
pnputil /add-driver Kernel\TAD_RV.inf /install
:: -or- (direct service registration):
sc create TAD.RV type=kernel binPath=%SystemRoot%\system32\drivers\TAD.RV.sys start=auto
sc start TAD.RV

:: 2. Copy the service binary
mkdir "C:\Program Files\TAD_RV"
copy release-addc\TadBridgeService.exe "C:\Program Files\TAD_RV\"

:: 3. Register and start the Windows Service
sc create TadBridgeService binPath="C:\Program Files\TAD_RV\TadBridgeService.exe" start=auto obj=LocalSystem
sc failure TadBridgeService reset=86400 actions=restart/5000/restart/10000/restart/30000
sc start TadBridgeService
```

## 4. GPO Startup Script (Mass Deployment)

For domain-wide deployment, use `TadBootstrap.exe` as a GPO startup script:

1. Copy `TadBootstrap.exe` and `TadBridgeService.exe` to a NETLOGON share:
   ```
   \\dc01.school.local\NETLOGON\TAD\TadBootstrap.exe
   \\dc01.school.local\NETLOGON\TAD\TadBridgeService.exe
   ```

2. In Group Policy Management → Computer Configuration → Policies → Windows Settings → Scripts → Startup:
   - Add `\\dc01.school.local\NETLOGON\TAD\TadBootstrap.exe`

3. `TadBootstrap.exe` will:
   - Copy `TadBridgeService.exe` to `C:\Program Files\TAD_RV\`
   - Register a SYSTEM service with automatic recovery
   - Start the service immediately

## 5. Active Directory Policy Setup

Create `Policy.json` on the NETLOGON share:

```
\\dc01.school.local\NETLOGON\TAD\Policy.json
```

```json
{
  "GroupRoleMappings": {
    "TAD-Admins":   3,
    "TAD-Teachers": 2,
    "TAD-Students": 1,
    "Domain Users": 0
  },
  "PolicyFlags": 31,
  "BlockedApplications": [
    "taskmgr.exe",
    "regedit.exe",
    "cmd.exe",
    "powershell.exe"
  ]
}
```

**Role values:**

| Value | Role | Description |
|---|---|---|
| 0 | Unknown | Default / no permissions |
| 1 | Student | Fully restricted, all policies enforced |
| 2 | Teacher | Can view student screens, freeze/unfreeze |
| 3 | Admin | Full control, can bypass all restrictions |

**Policy flags (bitmask):**

| Bit | Value | Flag | Description |
|---|---|---|---|
| 0 | 0x01 | `BLOCK_USB` | Block USB storage devices |
| 1 | 0x02 | `BLOCK_PRINTING` | Disable printing |
| 2 | 0x04 | `LOG_SCREENSHOTS` | Enable screenshot capture logging |
| 3 | 0x08 | `BLOCK_TASK_MANAGER` | Prevent Task Manager access |
| 4 | 0x10 | `ENFORCE_WEB_FILTER` | Activate web content filter |

## 6. Verification

After deployment, verify on the target machine:

```batch
:: Check driver is loaded
sc query TAD.RV

:: Check service is running
sc query TadBridgeService

:: Check registry configuration
reg query "HKLM\SOFTWARE\TAD_RV"

:: Check Event Log for TAD.RV events
wevtutil qe Application /q:"*[System[Provider[@Name='TadBridgeService']]]" /c:5 /f:text
```

In the Management Console, the **Dashboard** page shows:
- Driver status (loaded / not loaded)
- Service status (running / stopped)
- Current policy flags
- Client heartbeat status

## 7. Uninstallation

```batch
:: 1. Stop and remove the service
sc stop TadBridgeService
sc delete TadBridgeService

:: 2. Stop and remove the driver  
sc stop TAD.RV
sc delete TAD.RV
:: -or-
pnputil /delete-driver TAD_RV.inf /uninstall

:: 3. Clean up files
rmdir /s /q "C:\Program Files\TAD_RV"

:: 4. Clean up registry
reg delete "HKLM\SOFTWARE\TAD_RV" /f
```

---

*See also: [Signing-Handbook.md](Signing-Handbook.md) · [Architecture.md](Architecture.md) · [Teacher-Guide.md](Teacher-Guide.md)*
