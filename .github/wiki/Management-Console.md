# Management Console Guide

**TADDomainController** is the IT administrator's console for deployment, policy management, and alert monitoring. It is separate from the teacher-facing TADAdmin dashboard.

---

## Table of Contents

1. [Launching the Console](#1-launching-the-console)
2. [Dashboard](#2-dashboard)
3. [Deployment](#3-deployment)
4. [Policy Editor](#4-policy-editor)
5. [Alerts & Event Log](#5-alerts--event-log)
6. [Classroom Designer](#6-classroom-designer)
7. [Language / Internationalization](#7-language--internationalization)

---

## 1. Launching the Console

```
TADDomainController.exe
```

The console requests **UAC elevation** on launch because it needs:
- Windows Service management
- HKLM Registry access
- Driver installation (`pnputil`)
- Windows Event Log access

### System Requirements

| Requirement | Detail |
|---|---|
| OS | Windows 10/11 or Windows Server 2019/2022 |
| Privileges | Local Administrator |
| Display | 1280×800 minimum |

---

## 2. Dashboard

The dashboard provides a real-time health overview of the TAD.RV installation on the current machine.

### Status Cards

| Card | What it shows |
|---|---|
| **Driver Status** | Whether `TAD.RV.sys` is loaded (optional component) |
| **Service Status** | Whether `TADBridgeService` is running and responding |
| **Policy Flags** | Active policy bitmask |
| **Service Uptime** | Time since last service start |

### Health Checks

Automated checks run every 5 seconds:
- Driver loaded and responding (if installed)
- Service running and heartbeat active
- Registry configuration valid (`HKLM\SOFTWARE\TAD_RV`)
- Active Directory connectivity (if domain-joined)

Green = OK · Yellow = Warning · Red = Error

### System Information

Displays: hostname, OS version, .NET 8 runtime version, CPU model, RAM, domain name.

### Registry Live View

Shows all values under `HKLM\SOFTWARE\TAD_RV` in real time. Values are editable from the Policy page.

### Service Controls

| Button | Action |
|---|---|
| **Start** | Start TADBridgeService |
| **Stop** | Stop the service (admin confirmation required) |
| **Restart** | Stop + 3-second wait + Start |
| **Refresh** | Re-read all status values |

---

## 3. Deployment

The Deployment page replaces the legacy PowerShell scripts with a visual guided wizard. Use this to install TAD.RV on the **current machine** or to prepare deployment packages.

### Fields

| Field | Description |
|---|---|
| **Driver path** | Browse to `TAD.RV.sys` (optional — leave blank for user-mode only) |
| **Service binary** | Browse to `TADBridgeService.exe` |
| **Install directory** | Default: `C:\Program Files\TAD_RV` |

### Deployment Steps (automatic)

When you click **Deploy Now**, the wizard runs these steps in order:

1. Create install directory
2. Copy service binary
3. Copy driver (if selected)
4. Install driver with `pnputil /add-driver TAD_RV.inf /install` (if selected)
5. Register Windows Service (`sc create`) with auto-start
6. Set service recovery policy (restart on failure: 5s / 10s / 30s)
7. Start the service
8. Verify service is running

Progress and detailed command output are shown in real time in the log panel.

### Re-deployment / Update

Run the wizard again with the new binary path. The wizard:
- Stops the running service
- Replaces the binary
- Restarts the service

---

## 4. Policy Editor

Visual editor for all TAD.RV enforcement flags. Changes apply immediately to the registry and take effect on the next service policy reload (within ~10 seconds).

### Available Policy Flags

| Flag | Effect |
|---|---|
| **Block USB Storage** | Prevents USB mass storage devices from mounting |
| **Block Printing** | Disables print spooler for student sessions |
| **Log Screenshots** | Enables periodic screen capture logging to Event Log |
| **Block Task Manager** | Prevents `taskmgr.exe` from launching |
| **Block Command Prompt** | Blocks `cmd.exe` and `powershell.exe` |
| **Enforce Web Filter** | Activates URL-based content filtering (requires filter rules) |
| **Block Registry Editor** | Prevents `regedit.exe` and `reg.exe` |
| **Lock on Screensaver** | Sends TAD lock command when Windows screensaver activates |

### Saving Policy

- **Save to Registry** — writes immediately to `HKLM\SOFTWARE\TAD_RV\PolicyFlags`
- **Export to JSON** — saves a `Policy.json` for distribution via NETLOGON share in AD environments

### Importing Policy

Upload a previously exported `Policy.json` to apply a saved configuration.

---

## 5. Alerts & Event Log

The Alerts page shows events written by `TADBridgeService` to the Windows Application Event Log.

### Alert Severity Levels

| Level | Icon | Typical Source |
|---|---|---|
| **Info** | ℹ️ | Service start/stop, policy reload, provisioning |
| **Warning** | ⚠️ | AD connectivity lost, offline cache in use |
| **Error** | ❌ | Driver communication failure, service crash |
| **Critical** | 🔴 | Tamper attempt detected (kernel driver alert) |

### Filtering

- Filter by severity (All / Info / Warning / Error / Critical)
- Full-text search across event messages
- Filter by time range

### Detail Pane

Click any alert to expand the full event detail, including source, event ID, and full message text.

### Export

Click **Export** to save the visible alerts to a CSV file.

---

## 6. Classroom Designer

Visual drag-and-drop room layout editor. Arrange student PC icons on a canvas to match the physical classroom layout.

### Creating a room layout

1. Navigate to **Classrooms**
2. Click **New Room** — enter a name (e.g. "Room 101")
3. Drag PC icons onto the canvas to match the physical desk arrangement
4. Click **Save**

### Connecting TADAdmin to a room

In TADAdmin, use the **Room** dropdown in the toolbar to select a saved room layout. The grid will highlight which students belong to which seats.

---

## 7. Language / Internationalization

The console UI is fully localized. To change the language:

1. Click the **language selector** in the top-right toolbar
2. Select a language

**Available languages:** English (EN), German (DE), French (FR), Dutch (NL), Spanish (ES), Italian (IT), Polish (PL)

Language preference is saved per-user in the user profile.

### Adding a language

See [Internationalization.md](https://github.com/amiho-dev/TAD-RV/blob/main/docs/Internationalization.md) in the repository for instructions on adding new language packs.
