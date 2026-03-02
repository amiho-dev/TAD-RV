# TAD.RV — Management Console Guide

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: Administrator guide for the TAD.RV Management Console —
> deployment, monitoring, policy configuration, and alert management.

---

## Table of Contents

1. [Getting Started](#1-getting-started)
2. [Dashboard](#2-dashboard)
3. [Deployment](#3-deployment)
4. [Policy Editor](#4-policy-editor)
5. [Alerts & Logs](#5-alerts--logs)
6. [Classroom Designer](#6-classroom-designer)
7. [Language Settings](#7-language-settings)
8. [Keyboard Shortcuts](#8-keyboard-shortcuts)

---

## 1. Getting Started

### System Requirements

| Requirement | Details |
|---|---|
| **OS** | Windows 10/11 (64-bit) |
| **Privileges** | Local Administrator (UAC prompt on launch) |
| **Display** | 1280×800 minimum |

### Launch

Run `TadConsole.exe` — the application will request UAC elevation because it needs access to:
- Windows Service management (`sc.exe`)
- Registry (`HKLM\SOFTWARE\TAD_RV`)
- Windows Event Log
- Driver installation (`pnputil`)

## 2. Dashboard

The default landing page provides a real-time overview:

### Status Cards

| Card | Shows |
|---|---|
| **Driver Status** | Whether `TAD.RV.sys` is loaded in the kernel |
| **Service Status** | Whether `TadBridgeService` is running |
| **Policy Flags** | Currently active policy bitmask value |
| **Uptime** | Service uptime since last start |

### Health Checks

Automated checks run every 5 seconds:
- Driver loaded and responding
- Service running and heartbeat active
- Registry configuration valid
- AD connectivity (if domain-joined)

### System Info

Machine details: hostname, OS version, .NET runtime, CPU, RAM, domain.

### Registry Configuration

Live view of all values under `HKLM\SOFTWARE\TAD_RV` — editable from the Policy page.

### Service Controls

| Button | Action |
|---|---|
| **Start** | Start the TadBridgeService |
| **Stop** | Stop the service (requires driver unlock) |
| **Restart** | Stop + Start with 3-second delay |

## 3. Deployment

Full-featured deployment wizard replacing the legacy PowerShell script.

### Steps

1. **Select driver** — Browse to `TAD.RV.sys`
2. **Select service binary** — Browse to `TadBridgeService.exe`
3. **Choose install directory** — Default: `C:\Program Files\TAD_RV`
4. **Click Deploy Now**

### Deployment Pipeline

The wizard executes these steps automatically:
1. Copy driver to `%SystemRoot%\System32\drivers\`
2. Run `pnputil /add-driver TAD_RV.inf /install`
3. Copy service binary to install directory
4. Register Windows Service (`sc create`)
5. Set recovery policy (restart on failure)
6. Start the service

Progress and detailed logs are shown in real-time.

## 4. Policy Editor

Visual editor for all TAD.RV policy flags.

### Policy Flags

Each flag is a toggle switch with a description:

| Flag | Description |
|---|---|
| **Block USB** | Prevent USB mass storage access |
| **Block Printing** | Disable print spooler for student sessions |
| **Log Screenshots** | Enable periodic screen capture logging |
| **Block Task Manager** | Prevent `taskmgr.exe` from running |
| **Enforce Web Filter** | Activate URL-based content filtering |
| **Block CMD** | Prevent `cmd.exe` and `powershell.exe` |
| **Block App Install** | Prevent new application installation |
| **Block Bluetooth** | Disable Bluetooth adapters |
| **Block Network Share** | Prevent access to network shares |
| **Enable Watermark** | Overlay tamper-detection watermark |
| **Stealth** | Hide TAD.RV tray icon from end users |
| **Hard Lock** | Enable hardware-level lock-down mode |

### Actions

| Button | Description |
|---|---|
| **Save to Registry** | Write flags to `HKLM\SOFTWARE\TAD_RV\PolicyFlags` |
| **Reset to Default** | Reset all flags to factory defaults |

## 5. Alerts & Logs

Real-time view of TAD.RV security events from the Windows Event Log.

### Alert Types

| Type | Severity | Description |
|---|---|---|
| **ServiceTamper** | Critical | Someone attempted to kill or modify the service |
| **HeartbeatLost** | High | Driver detected service heartbeat failure |
| **UnlockBruteForce** | High | Multiple failed unlock attempts |
| **FileTamper** | Medium | Attempt to delete/rename protected files |

### Filtering

| Filter | Description |
|---|---|
| **All** | Show all alert types |
| **Critical** | Critical severity only |
| **Warning** | Warnings and above |
| **Today** | Events from today only |

### Search

Type in the search box to filter alerts by message content, source PID, or timestamp.

## 6. Classroom Designer

Visual layout tool for defining classroom desk arrangements.

### Features

- Drag-and-drop canvas for positioning student tiles
- Grid snapping for clean alignment
- Room naming and management
- Export/import room layouts
- Links to Teacher Controller for live monitoring

### Usage

1. Navigate to **Classrooms** in the sidebar
2. Click **New Room** to create a layout
3. Drag student placeholder tiles onto the canvas
4. Name the room (e.g., "Computer Lab A")
5. Click **Save**

Rooms are stored locally and can be referenced by the Teacher Controller.

## 7. Language Settings

Click the globe icon in the top bar to switch the UI language.

Supported languages: English, German, French, Dutch, Spanish, Italian, Polish.

The language preference persists across sessions.

## 8. Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+1` | Dashboard |
| `Ctrl+2` | Deploy |
| `Ctrl+3` | Policy Editor |
| `Ctrl+4` | Alerts & Logs |
| `Ctrl+5` | Classrooms |
| `F5` | Refresh current page |
| `Ctrl+S` | Save (context-dependent) |

---

*See also: [Architecture.md](Architecture.md) · [Deployment-Guide.md](Deployment-Guide.md) · [Teacher-Guide.md](Teacher-Guide.md)*
