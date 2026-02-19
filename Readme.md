<p align="center">
  <img src="Shared/logo32.png" alt="TAD.RV Logo">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-26700.192-blue?style=flat-square" alt="Version">
  <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet" alt=".NET 8">
  <img src="https://img.shields.io/badge/WDK-Windows%20Kernel-orange?style=flat-square&logo=windows" alt="WDK">
  <img src="https://img.shields.io/badge/platform-win--x64-lightgrey?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/license-proprietary-red?style=flat-square" alt="License">
</p>

# TAD.RV

**Classroom endpoint monitoring and protection platform for Windows school labs.**

TAD.RV combines a Windows kernel driver with user-mode services and teacher/admin tooling to deliver tamper-proof student oversight across 50+ seat school labs — with zero-config networking, GPU-accelerated screen capture, and full Active Directory integration.

---

## Overview

| Component | Technology | Purpose |
|:--|:--|:--|
| **Kernel Driver** | C / WDK / Minifilter | Process protection, file tamper prevention, heartbeat watchdog |
| **Bridge Service** | .NET 8 Worker Service | AD integration, driver communication, screen capture, TCP listener |
| **Teacher App** | .NET 8 / WPF + WebView2 | Real-time student grid, remote view, lock/freeze/blank controls |
| **Admin Console** | .NET 8 / WPF | Deployment wizard, policy editor, service management, alert viewer |
| **Bootstrap** | .NET 8 Console | Zero-install GPO loader for domain-joined labs |

---

## Architecture

```
                        ┌──────────────────────────┐
                        │   Active Directory / GPO  │
                        │   Policy.json on NETLOGON │
                        └────────────┬─────────────┘
                                     │ LDAP / SMB
┌─────────────────┐     ┌───────────▼───────────────────────────────┐
│  TadTeacher.exe │────▶│  TadBridgeService.exe   (SYSTEM service)  │
│  (Teacher PC)   │ TCP │  ┌─────────────┬──────────┬────────────┐  │
│  WebView2 UI    │17420│  │ Provisioning│ AD Watch │ Offline $  │  │
└─────────────────┘     │  └──────┬──────┴────┬─────┴─────┬──────┘  │
                        │  ┌──────▼───────────▼───────────▼──────┐  │
       UDP 239.1.1.1    │  │          DriverBridge (IOCTL)       │  │
     ◄──────────────────│  └──────────────────┬──────────────────┘  │
       :17421 Discovery │                     │ DeviceIoControl     │
                        └─────────────────────┼─────────────────────┘
                                              │ \\.\TadRvLink
                        ┌─────────────────────▼─────────────────────┐
                        │  TAD_RV.sys              (Ring 0 Driver)   │
                        │  ● ObRegisterCallbacks — process/thread   │
                        │  ● FltRegisterFilter   — anti-delete      │
                        │  ● Heartbeat DPC       — liveness guard   │
                        │  ● DACL + 256-bit auth — anti-tamper      │
                        └───────────────────────────────────────────┘
```

---

## Key Features

### Kernel Driver (`TAD_RV.sys`)

- **Process & thread protection** via `ObRegisterCallbacks` — blocks terminate, suspend, VM-write, context-set
- **File system minifilter** — prevents deletion and renaming of protected binaries
- **Heartbeat DPC watchdog** — if the service dies, the driver auto-engages a network killswitch within 6 seconds
- **Authenticated unload** — 256-bit XOR-obfuscated pre-shared key with constant-time comparison and brute-force lockout
- **DACL-hardened device object** — only SYSTEM and Administrators can open the control channel
- **Spectre V1 mitigation** — `_mm_lfence()` barriers on all IOCTL input validation paths

### Bridge Service (`TadBridgeService.exe`)

- **Active Directory provisioning** — auto-reads machine OU, fetches `Policy.json` from NETLOGON share
- **AD group → role mapping** — resolves logged-on user to Student / Teacher / Admin every 10 s
- **Dual-stream screen capture** — DXGI Desktop Duplication + Intel QuickSync H.264 (sub: 1 fps 480p / main: 30 fps 720p)
- **Privacy redaction** — UIAutomation detects password fields and blacks them out in GPU memory pre-encode
- **UDP multicast discovery** — zero-config `239.1.1.1:17421` for DC-less environments
- **TCP command listener** — port 17420, binary-framed protocol for teacher commands
- **DPAPI-encrypted offline cache** — 7-day TTL for AD role data when DC is unreachable
- **Auto-update** — periodic GitHub Release checks with download + apply + restart

### Teacher Dashboard (`TadTeacher.exe`)

- **50-student real-time grid** — live thumbnails via H.264 sub-stream decoding
- **Focused remote view** — click a tile for 30 fps 720p full-resolution view
- **Classroom controls** — lock, freeze, blank screen, push message, collect files
- **Auto-discovery** — finds students via multicast without configuration
- **System tray** — minimize to tray, always accessible
- **Demo mode** — `--demo` flag generates synthetic students (no network needed)
- **Update notifications** — banner in dashboard when new versions are available

### Admin Console (`TadConsole.exe`)

- **Dashboard** — real-time driver/service status, registry overview, update checker
- **Deployment wizard** — point-and-click deployment replacing PowerShell scripts
- **Policy editor** — visual flag toggles, group → role mappings, blocked app list, live JSON preview
- **Alert viewer** — Windows Event Log integration with filtering and detail pane

---

## Quick Start

### Prerequisites

| Requirement | Version |
|:--|:--|
| .NET SDK | 8.0+ |
| Windows | 10 1903+ / 11 |
| Visual Studio (optional) | 2022 17.4+ |
| WDK (kernel only) | 10.0.22621+ |

### Build

```bash
# Full solution build
dotnet build TAD-RV.sln -c Release

# Or use the build script (includes publish + demo packaging)
chmod +x build.sh && ./build.sh
```

### Run in Demo Mode (no driver, no AD)

```bash
# Teacher — standalone with synthetic students
TadTeacher.exe --demo

# Service — emulated driver + AD
TadBridgeService.exe --emulate
```

### Deploy to Production

See [Docs/Deployment-Guide.md](Docs/Deployment-Guide.md) for full instructions, or use the Console's **Deploy** tab.

```powershell
# Quick: PowerShell one-liner
.\Scripts\Deploy-TadRV.ps1 -DriverPath .\Kernel\TAD_RV.sys `
  -ServicePath .\Service\bin\Release\net8.0-windows\win-x64\publish
```

---

## Repository Structure

```
TAD-RV/
├── Kernel/                 Ring 0 WDM + minifilter driver
│   ├── TAD_RV.c/.h            Core implementation (~900 lines)
│   ├── TAD_RV.inf             Installation INF (altitude 371100)
│   ├── TAD_RV.rc              Version resource
│   └── SOURCES / makefile      WDK build system
│
├── Service/                .NET 8 Worker Service (SYSTEM)
│   ├── Core/                   TadBridgeWorker, HeartbeatWorker, AlertReader, UpdateWorker
│   ├── Driver/                 DriverBridge, DriverInstaller, EmulatedDriverBridge
│   ├── Networking/             TadTcpListener, MulticastDiscovery
│   ├── Capture/                ScreenCaptureEngine (DXGI+QSV), PrivacyRedactor
│   ├── ActiveDirectory/        AdGroupWatcher, EmulatedAdGroupWatcher
│   ├── Provisioning/           ProvisioningManager, EmulatedProvisioningManager
│   ├── Cache/                  OfflineCacheManager (DPAPI)
│   └── Tray/                   TrayIconManager
│
├── Teacher/                .NET 8 WPF + WebView2 dashboard
│   ├── Networking/             TcpClientManager, DiscoveryListener, DemoTcpClientManager
│   └── Web/                    dashboard.html/.css/.js (embedded SPA)
│
├── Console/                .NET 8 WPF admin console
│   ├── Services/               Deployment, ServiceControl, Registry, EventLog
│   ├── ViewModels/             MVVM: Dashboard, Deploy, Policy, Alerts
│   └── Views/                  WPF views + dark theme
│
├── Bootstrap/              Zero-install GPO bootstrap loader
├── Shared/                 Cross-project: IOCTL defs, protocol codec, i18n, UpdateManager
├── Scripts/                PowerShell: Deploy, Install-Driver, Sign, Start-Emulated
└── Docs/                   Architecture, Build, Deploy, Kernel, Console, Teacher, Signing, i18n
```

---

## IOCTL Interface

All IOCTLs use `METHOD_BUFFERED` on device type `0x8000`. Defined in [Shared/TadShared.h](Shared/TadShared.h) (C) and [Shared/TadSharedInterop.cs](Shared/TadSharedInterop.cs) (C#).

| Code | Name | Direction | Purpose |
|:--:|:--|:--:|:--|
| `0x800` | `IOCTL_TAD_PROTECT_PID` | Svc → Drv | Register/unregister PID for kernel protection |
| `0x801` | `IOCTL_TAD_UNLOCK` | Svc → Drv | Present 256-bit auth key to permit unload |
| `0x802` | `IOCTL_TAD_HEARTBEAT` | Svc ↔ Drv | Heartbeat ping + driver status response |
| `0x803` | `IOCTL_TAD_SET_USER_ROLE` | Svc → Drv | Push current user role (Student / Teacher / Admin) |
| `0x804` | `IOCTL_TAD_SET_POLICY` | Svc → Drv | Push policy flags + configuration blob |
| `0x805` | `IOCTL_TAD_READ_ALERT` | Drv → Svc | Long-poll for security alerts |

---

## TCP Protocol

Binary framing: `[4-byte LE length][1-byte command][payload]`

Port **17420** — full command list in [Shared/TadProtocol.cs](Shared/TadProtocol.cs).

| Range | Direction | Commands |
|:--|:--|:--|
| `0x01` | Teacher → Student | Ping |
| `0x10–0x15` | Teacher → Student | Lock, Unlock, Freeze, Unfreeze, Blank, Unblank |
| `0x20–0x23` | Teacher → Student | RvStart, RvStop, RvFocusStart, RvFocusStop |
| `0x30` | Teacher → Student | CollectFiles |
| `0x40–0x43` | Teacher → Student | PushMessage, Chat, LaunchApp, LaunchUrl |
| `0x81–0x85` | Student → Teacher | Pong, Status, HandRaise, HandLower, ChatReply |
| `0xA0–0xA3` | Student → Teacher | VideoFrame, VideoKeyFrame, MainFrame, MainKeyFrame |
| `0xB0–0xB1` | Student → Teacher | FileChunk, FileComplete |

---

## Policy Configuration

Place `Policy.json` on the NETLOGON share (`\\dc01\NETLOGON\TAD\Policy.json`):

```json
{
  "Version": 1,
  "HeartbeatIntervalMs": 2000,
  "HeartbeatTimeoutMs": 6000,
  "AllowedUnloadRoles": 4,
  "GroupRoleMappings": {
    "Domain Students": 0,
    "Domain Teachers": 1,
    "Domain Admins":   2,
    "TAD-Administrators": 2
  },
  "BlockedApplications": [
    "taskmgr.exe", "cmd.exe", "powershell.exe", "regedit.exe"
  ]
}
```

**Policy flags** (bitmask):

| Bit | Value | Flag |
|:--:|:--:|:--|
| 0 | `0x01` | `BLOCK_USB` |
| 1 | `0x02` | `BLOCK_PRINTING` |
| 2 | `0x04` | `LOG_SCREENSHOTS` |
| 3 | `0x08` | `BLOCK_TASK_MANAGER` |
| 4 | `0x10` | `ENFORCE_WEB_FILTER` |

---

## Signing & Security

| Environment | Method | Notes |
|:--|:--|:--|
| **Development** | Self-signed + `bcdedit /set testsigning on` | Free, shows desktop watermark |
| **Production** | EV certificate + WHQL attestation | ~€40–150/yr, no watermark, Windows-trusted |

The 256-bit driver auth key is XOR-obfuscated (mask `0xA7`) in [Kernel/TAD_RV.h](Kernel/TAD_RV.h) and [Shared/TadSharedInterop.cs](Shared/TadSharedInterop.cs). See [Docs/Signing-Handbook.md](Docs/Signing-Handbook.md) for rotation procedure.

---

## Software Updates

All components include a built-in update system based on **GitHub Releases**:

| Component | Behavior |
|:--|:--|
| **Service** | `UpdateWorker` checks every 6 hours, auto-downloads and applies, restarts via service recovery |
| **Teacher** | Checks on startup, shows banner in dashboard with release link |
| **Console** | Checks on startup, shows status in Dashboard view |
| **Bootstrap** | Compares cached version on each GPO run, overwrites if newer |

Configure the repo via registry (`HKLM\SOFTWARE\TAD_RV\UpdateRepo`) or environment variable (`TAD_UPDATE_REPO`).
Default: `tad-europe/TAD-RV`.

---

## Documentation

| Guide | Description |
|:--|:--|
| [Architecture](Docs/Architecture.md) | System design, data flow, threat model |
| [Build Guide](Docs/Build-Guide.md) | Step-by-step build instructions for all components |
| [Deployment Guide](Docs/Deployment-Guide.md) | Production deployment (GPO, manual, Console wizard) |
| [Kernel Install Guide](Docs/Kernel-Install-Guide.md) | Driver build, signing, installation, troubleshooting |
| [Console Guide](Docs/Console-Guide.md) | Admin console features and usage |
| [Teacher Guide](Docs/Teacher-Guide.md) | Teacher dashboard walkthrough |
| [Emulation Guide](Docs/Emulation-Guide.md) | Demo / emulation mode setup |
| [Signing Handbook](Docs/Signing-Handbook.md) | Certificate workflow, key rotation |
| [Internationalization](Docs/Internationalization.md) | i18n system (8 languages) |

---

## Release Log

### v26700.192 — 2026-02-19

**Bug Fixes**
- Fixed crash on startup: "Initializing WebView2" — `InitializeWebView()` was called from the `MainWindow` constructor before the window's HWND existed, causing `EnsureCoreWebView2Async` to throw. Deferred to the `Loaded` event.

**Kernel**
- Added `PsSetCreateProcessNotifyRoutineEx` subsystem for per-app process-launch blocking.
- Added `IOCTL_TAD_SET_BANNED_APPS` (0x809) — service pushes a list of banned image names; driver denies matching process creations when `TAD_POLICY_FLAG_BLOCK_APPS` is set.
- Added `/INTEGRITYCHECK` linker flag to `Kernel/SOURCES` (required by `PsSetCreateProcessNotifyRoutineEx`).

**Service**
- `DriverBridge.SetBannedApps(IEnumerable<string>)` — typed IOCTL wrapper.
- `EmulatedDriverBridge` no-op override for demo/emulated mode.

**Update Notifications**
- Teacher: added "What's New?" link in update banner that opens a modal with full release notes.
- Console: added scrollable Release Notes card that appears when an update is available.

### v26500.181 — 2026-02-18

**General**
- Updated version numbering to `26500.181` across all components.
- Fixed `UpdateManager.cs` to default to `amiho-dev/TAD-RV` repository.
- Moved update packaging logic to create `.zip` artifacts correctly.

**Admin Console**
- Added software update status card to the Dashboard view.
- Improved visibility of service and driver status.

**Documentation**
- Added Auto-Update Configuration section to [Docs/Deployment-Guide.md](Docs/Deployment-Guide.md).
- Updated repository visibility and branding.

### v26200.172 — 2026-02-18

**Kernel Driver**
- Fixed lockout timer calculation (double-negative caused lockout to expire immediately)
- Added thread protection for UI overlay PID in `TadObThreadPreCallback`
- Migrated from deprecated `ExAllocatePoolWithTag` → `ExAllocatePool2`
- Fixed INF catalog name mismatch (`TAD.RV.cat` → `TAD_RV.cat`)
- Synchronized version to `26200.172.0.0` across INF, RC, and `TadShared.h`

**Bridge Service**
- Fixed `RamUsedMb` reporting available memory instead of working set
- Fixed `StopMainStream` race condition (encoder disposed before task completed)
- Added `UnprotectPid` to clean up stale PID protection after lock overlay exit
- Fixed `ProcessFrames` unnecessary allocation (`ToArray()` → `GetBuffer()`)
- Fixed emulated driver alert timestamps (Unix → FILETIME format)
- Added `UpdateWorker` — automatic update checks every 6 hours via GitHub Releases

**Teacher App**
- Added WebView2 error handling with retry and fallback
- Added system tray icon with minimize / restore
- Added backpressure throttle for demo video frames
- Added changelog modal accessible via About dialog
- Added update notification banner in dashboard
- Fixed WebMessage case sensitivity (`Action` → `action`)
- Fixed double-encoding of JSON status payloads

**Admin Console**
- Added update checker in Dashboard view

**Bootstrap**
- Synchronized version to `26200.172`

**Shared**
- New `UpdateManager.cs` — GitHub Release-based update system for all components
- New `TadProtocol.cs` — binary framing codec with full command set

**Documentation**
- New: Kernel Install Guide (build, signing, 5 installation methods, troubleshooting)
- Updated README with modern layout, full architecture diagram, release log

**Infrastructure**
- Cleaned stale `Demo/` folder (~328 MB), `release-addc/` artifacts
- Updated `.gitignore` for all publish output directories

---

### v26200.171 — 2026-02-17

**Initial Release**
- Kernel driver: ObCallbacks, minifilter, heartbeat DPC, authenticated unload
- Bridge service: AD provisioning, heartbeat, alert reader, DXGI+QSV capture, privacy redaction
- Teacher dashboard: WebView2 SPA, 50-student grid, remote view, lock / freeze / blank controls
- Admin console: deployment wizard, policy editor, alert viewer
- Bootstrap: zero-install GPO loader
- UDP multicast auto-discovery (239.1.1.1:17421)
- Full emulation mode (`--emulate` / `--demo`) for all components
- 8-language internationalization (EN, DE, FR, ES, IT, NL, PL)

---

## License

**(C) 2026 TAD Europe** — [tad-it.eu](https://tad-it.eu)

Proprietary. All rights reserved. Unauthorized distribution prohibited.
