# TAD.RV — School Endpoint Monitoring Platform
# THIS PROJECT IS CONFIDENTIAL AND SHOULD NOT BE PUBLISHED!!!

Complete kernel + service solution for protecting and managing the TAD.RV monitoring agent on school-managed Windows workstations.

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  Active Directory          \\DC01\NETLOGON\TAD\Policy.json       │
│  ─────────────────         (Group → Role mappings, flags)        │
└──────────┬───────────────────────────────┬───────────────────────┘
           │ LDAP / GroupPolicy             │ SMB
           ▼                                ▼
┌────────────────────────────────────────────────────────────────┐
│  TadBridgeService.exe        (.NET 8 Worker Service / SYSTEM)  │
│  ┌──────────────┐ ┌───────────────┐ ┌──────────────────────┐   │
│  │ Provisioning  │ │ AdGroupWatch  │ │ OfflineCache (DPAPI) │  │
│  └──────┬───────┘ └───────┬───────┘ └──────────┬───────────┘   │
│         │                 │                     │              │
│  ┌──────▼─────────────────▼─────────────────────▼───────────┐  │
│  │              DriverBridge (P/Invoke)                      │ │
│  │  PROTECT_PID · UNLOCK · HEARTBEAT · SET_ROLE · SET_POLICY │ │
│  │  READ_ALERT                                               │ │
│  └──────────────────────────┬────────────────────────────────┘ │
└─────────────────────────────┼──────────────────────────────────┘
                              │ DeviceIoControl  (\\.\TadRvLink)
┌─────────────────────────────┼──────────────────────────────────┐
│  TAD.RV.sys                 │      (Ring 0  —  WDM + Minifilter)│
│  ┌──────────────────────────▼────────────────────────────────┐  │
│  │ ObRegisterCallbacks   (process + thread protection)        │  │
│  │ FltRegisterFilter     (anti-delete, anti-rename)           │  │
│  │ Heartbeat DPC timer   (service liveness watchdog)          │  │
│  │ DACL-hardened device  (SYSTEM + Admins only)               │  │
│  │ 256-bit XOR auth key  (constant-time + rate-limiting)      │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

## Repository Layout

```
TAD-RV/
├── Kernel/                     Ring 0 driver
│   ├── TAD_RV.h                Driver header (includes ../Shared/TadShared.h)
│   ├── TAD_RV.c                Full driver implementation
│   ├── TAD_RV.inf              Installation INF (minifilter)
│   ├── TAD_RV.rc               Version resource
│   ├── SOURCES                 WDK build file
│   └── makefile                WDK makefile
│
├── Shared/                     Single-source-of-truth for IOCTLs
│   ├── TadShared.h             C header  — IOCTL codes, structs, enums
│   └── TadSharedInterop.cs     C# mirror — linked into Service.csproj
│
├── Service/                    Ring 3 management service (.NET 8)
│   ├── TadBridgeService.csproj
│   ├── Program.cs              DI host / service registration
│   ├── Driver/
│   │   └── DriverBridge.cs     P/Invoke wrapper for all 6 IOCTLs
│   ├── Core/
│   │   ├── TadBridgeWorker.cs  Primary startup orchestrator
│   │   ├── HeartbeatWorker.cs  2-second heartbeat loop
│   │   └── AlertReaderWorker.cs Kernel → service alert reader
│   ├── Provisioning/
│   │   └── ProvisioningManager.cs  First-boot AD/OU provisioning
│   ├── ActiveDirectory/
│   │   └── AdGroupWatcher.cs   Group → USER_ROLE resolver
│   └── Cache/
│       └── OfflineCacheManager.cs  DPAPI-encrypted offline cache
│
├── Console/                    GUI management console (.NET 8 / WPF)
│   ├── TadConsole.csproj
│   ├── App.xaml                Application entry + elevation check
│   ├── Resources/
│   │   └── Styles.xaml         Dark theme, custom controls
│   ├── Helpers/
│   │   └── RelayCommand.cs     ICommand / async ICommand for MVVM
│   ├── Services/
│   │   ├── DeploymentService.cs    Full deployment pipeline (replaces PS script)
│   │   ├── TadServiceController.cs Service start/stop/restart via sc.exe
│   │   ├── RegistryService.cs      HKLM\SOFTWARE\TAD_RV read/write
│   │   └── EventLogService.cs      Windows Event Log reader
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs        INPC base class
│   │   ├── MainViewModel.cs        Sidebar navigation
│   │   ├── DashboardViewModel.cs   Live driver/service status
│   │   ├── DeployViewModel.cs      Deployment wizard
│   │   ├── PolicyViewModel.cs      Policy editor + JSON preview
│   │   └── AlertsViewModel.cs      Event log viewer
│   └── Views/
│       ├── MainWindow.xaml         Sidebar + content shell
│       ├── DashboardView.xaml      Status cards, service controls
│       ├── DeployView.xaml         File pickers, progress bar, log
│       ├── PolicyView.xaml         Checkbox flags, role mappings, JSON
│       └── AlertsView.xaml         DataGrid + detail pane
│
├── Scripts/
│   └── Deploy-TadRV.ps1        Legacy deployment script (CLI)
│
└── Readme.md
```

## Kernel Driver — Capabilities

| Feature | Implementation |
|---|---|
| **Process protection** | `ObRegisterCallbacks` strips `PROCESS_TERMINATE`, `PROCESS_VM_WRITE`, `PROCESS_VM_OPERATION`, `PROCESS_CREATE_THREAD`, `PROCESS_SUSPEND_RESUME` |
| **Thread protection** | Same mechanism for `THREAD_TERMINATE`, `THREAD_SUSPEND_RESUME`, `THREAD_SET_CONTEXT` |
| **Anti-deletion** | Minifilter blocks `FileDispositionInformation(Ex)` on protected filenames |
| **Anti-rename** | Minifilter blocks `FileRenameInformation(Ex)` to close rename-then-delete bypass |
| **Authenticated unload** | 256-bit XOR-obfuscated pre-shared key via `IOCTL_TAD_UNLOCK` |
| **Rate limiting** | 5 consecutive bad unlock attempts → 30-second lockout |
| **DACL hardening** | Device object restricted to SYSTEM and Administrators |
| **Spectre V1 mitigation** | `_mm_lfence()` barriers on all IOCTL input paths |
| **Caller validation** | Only registered agent PID may send privileged IOCTLs |
| **Heartbeat watchdog** | Kernel DPC timer detects service death (6-second timeout) |
| **Policy enforcement** | Receives policy flags from service (USB block, print block, screenshot logging) |

## IOCTL Interface

All IOCTLs use `METHOD_BUFFERED` on device type `0x8000`, defined in `Shared/TadShared.h`.

| Code | Name | Direction | Payload | Purpose |
|---|---|---|---|---|
| 0x800 | `IOCTL_TAD_PROTECT_PID` | Service → Driver | `TAD_PROTECT_PID_INPUT` | Register service PID for protection |
| 0x801 | `IOCTL_TAD_UNLOCK` | Service → Driver | `TAD_UNLOCK_INPUT` (32 bytes) | Present auth key to permit unload |
| 0x802 | `IOCTL_TAD_HEARTBEAT` | Service → Driver | — / `TAD_HEARTBEAT_OUTPUT` | Heartbeat + status polling |
| 0x803 | `IOCTL_TAD_SET_USER_ROLE` | Service → Driver | `TAD_SET_USER_ROLE_INPUT` | Push current user role |
| 0x804 | `IOCTL_TAD_SET_POLICY` | Service → Driver | `TAD_POLICY_BUFFER` | Push policy flags |
| 0x805 | `IOCTL_TAD_READ_ALERT` | Driver → Service | `TAD_ALERT_OUTPUT` | Read driver alerts |

## Service — TadBridgeService

.NET 8 Worker Service running as `LocalSystem`. Manages the full lifecycle:

1. **Provisioning** — First-boot: Reads machine OU from AD, fetches `Policy.json` from NETLOGON share, stores config in `HKLM\SOFTWARE\TAD_RV`.
2. **AD Group Watching** — Polls the logged-on user's AD groups every 10 seconds, resolves `TAD_USER_ROLE` from policy mappings.
3. **Heartbeat** — Sends `IOCTL_TAD_HEARTBEAT` every 2 seconds. Kernel DPC fires if no heartbeat within 6 seconds.
4. **Alert Reading** — Long-polls `IOCTL_TAD_READ_ALERT`, writes critical alerts to Windows Event Log (source `TadBridgeService`, event ID 9001).
5. **Offline Cache** — DPAPI-encrypted fallback for AD role information when domain controller is unreachable. 7-day TTL.

## Building

### Kernel Driver

Requires the **Windows Driver Kit (WDK)** matching your target OS version.

```batch
cd Kernel
build -ceZ
```

Or open in Visual Studio with the WDK extension — `SOURCES` will be detected automatically.

### Service

Requires **.NET 8 SDK** (Windows).

```bash
cd Service
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained
```

### Management Console (GUI)

Requires **.NET 8 SDK** (Windows) with WPF workload.

```bash
cd Console
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained
```

The console is a standalone WPF application with a dark-themed UI.  It provides:

| View | Description |
|---|---|
| **Dashboard** | Real-time status of both the kernel driver and bridge service, registry configuration overview, start/stop/restart controls with auto-refresh every 5 seconds |
| **Deployment** | Full wizard replacing `Deploy-TadRV.ps1` — file pickers, progress bar, step-by-step results, and a live deployment log |
| **Policy Editor** | Visual policy flag toggles, AD group→role mappings, blocked app list, heartbeat settings, with a live JSON preview pane. Import/export JSON, save to registry |
| **Alerts & Logs** | DataGrid view of TAD.RV events from the Windows Event Log with a detail pane |

The console auto-requests elevation (UAC) on startup since it needs admin access to manage services and registry.

## Deployment

### GUI (Recommended)

Run `TadConsole.exe` and use the **Deployment** tab.  Fill in the paths and click **Deploy Now**.

### PowerShell (Legacy)

The original script is still available:

```powershell
.\Scripts\Deploy-TadRV.ps1 -DriverPath .\Kernel\TAD.RV.sys -ServicePath .\Service\bin\Release\net8.0-windows\win-x64\publish
```

### Manual Installation

```batch
:: 1. Install the kernel driver
pnputil /add-driver Kernel\TAD_RV.inf /install
:: -or-
sc create TAD.RV type=kernel binPath=%SystemRoot%\system32\drivers\TAD.RV.sys start=auto
sc start TAD.RV

:: 2. Install the service
sc create TadBridgeService binPath="C:\Program Files\TAD_RV\TadBridgeService.exe" start=auto obj=LocalSystem
sc start TadBridgeService
```

> **Note:** `ObRegisterCallbacks` requires the driver to be **signed** (EV Authenticode or WHQL). For development, enable test-signing: `bcdedit /set testsigning on`.

## AD Policy Configuration

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

**Role values:** 0 = Unknown, 1 = Student, 2 = Teacher, 3 = Admin

**PolicyFlags bitmask:**
| Bit | Value | Flag |
|-----|-------|------|
| 0 | 0x01 | `BLOCK_USB` |
| 1 | 0x02 | `BLOCK_PRINTING` |
| 2 | 0x04 | `LOG_SCREENSHOTS` |
| 3 | 0x08 | `BLOCK_TASK_MANAGER` |
| 4 | 0x10 | `ENFORCE_WEB_FILTER` |

## Self-Signed Development Setup

For local development and testing (single machine or internal lab):

```batch
REM 1. Generate self-signed certificate
makecert -r -pe -a sha256 -ss my -n "CN=TAD-RV-Dev" -sy 24 -len 2048 -m 120 TAD-RV-Dev.cer

REM 2. Sign the driver
signtool sign /f TAD-RV-Dev.pfx /fd sha256 /v Kernel\TAD.RV.sys

REM 3. Enable test-signing mode (requires reboot)
bcdedit /set testsigning on
shutdown /r /t 0

REM 4. After reboot, install certificate to trusted root
certutil -addstore Root TAD-RV-Dev.cer

REM 5. Deploy as normal (driver will be accepted now)
.\Scripts\Deploy-TadRV.ps1 -DriverPath .\Kernel\TAD.RV.sys -ServicePath .\Service\bin\Release\net8.0-windows\win-x64\publish

REM To disable test-signing later:
REM bcdedit /set testsigning off
```

**This is free and legal** for development/lab environments. The "Test Mode" watermark will appear on boot (indicating non-production state).

## Signing for Production

1. Obtain an **EV code-signing certificate** from a CA (Sectigo, DigiCert, GlobalSign, etc.)
   - Budget: ~$40-150/year (Sectigo offers educational discounts)
   - Check with your school IT for Microsoft Education Partner benefits (often free)
2. Submit the driver to the **Windows Hardware Dev Center** for WHQL attestation signing
3. The resulting `.cat` file is referenced in `TAD_RV.inf` → `CatalogFile = TAD.RV.cat`

## Key Rotation

The 256-bit authentication key is embedded XOR-obfuscated in both `Kernel/TAD_RV.h` and `Shared/TadSharedInterop.cs`. To rotate:

1. Generate 32 random bytes (the new raw key)
2. XOR each byte with `TAD_KEY_XOR_MASK` (currently `0xA7`)
3. Replace `TadObfuscatedKey[]` in `Kernel/TAD_RV.h`
4. Replace `RawAuthKey` in `Shared/TadSharedInterop.cs`
5. Rebuild and re-sign both the driver and service

## License

(C) 2026 TAD Europe — [https://tad-it.eu](https://tad-it.eu)

Proprietary — all rights reserved.
