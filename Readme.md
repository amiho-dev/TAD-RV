<p align="center">
  <img src="logo.png" alt="TAD.RV Logo" width="96">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-v26.3.02.003-blue?style=flat-square" alt="Version">
  <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet" alt=".NET 8">
  <img src="https://img.shields.io/badge/runtime-user--mode-success?style=flat-square" alt="User Mode">
  <img src="https://img.shields.io/badge/platform-win--x64-lightgrey?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/license-proprietary-red?style=flat-square" alt="License">
</p>

# TAD.RV

Real-time endpoint monitoring and control platform for Windows — built for IT administrators managing labs, classrooms, open-plan offices, and any multi-seat Windows environment.

TAD.RV gives administrators a live view of every managed workstation: screen thumbnails, lock/unlock, screen freeze, message broadcast, software deployment, and Active Directory-aware provisioning. No kernel drivers required. Everything runs in user-mode.

---

## Components

| Component | Executable | Technology | Role |
|:--|:--|:--|:--|
| **TADAdmin** | `TADAdmin.exe` | .NET 8 WPF + WebView2 | Admin dashboard — live endpoint grid, remote control, screen capture |
| **TADDomainController** | `TADDomainController.exe` | .NET 8 WPF | DC management console — deployment, policy editor, event logs, alerts |
| **TADBridgeService** | `TADBridgeService.exe` | .NET 8 Worker Service | `LocalSystem` Windows service on each managed endpoint |
| **TADBootstrap** | `TADBootstrap.exe` | .NET 8 Console | Zero-install bootstrap — installs and registers the service via GPO or scripted push |
| **TADSetup** | `TADSetup.exe` | .NET 8 Console | Interactive setup installer for manual endpoint enrollment |

---

## Architecture

```
┌──────────────────────────────────┐
│  Admin machine                   │
│                                  │
│  TADAdmin.exe          ──────────┼──── TCP (port 9000) ────┐
│  TADDomainController.exe         │                          │
└──────────────────────────────────┘                          │
                                                               ▼
                                          ┌─────────────────────────────┐
                                          │  Managed endpoint (each)    │
                                          │                             │
                                          │  TADBridgeService.exe       │
                                          │  (LocalSystem, auto-start)  │
                                          │                             │
                                          │  • screen capture           │
                                          │  • lock / unlock            │
                                          │  • policy enforcement       │
                                          │  • AD-aware provisioning    │
                                          └─────────────────────────────┘
```

- **Discovery**: zero-config UDP multicast — `TADAdmin` finds all online endpoints automatically
- **Transport**: single persistent TCP connection per endpoint; lightweight binary framing over the LAN
- **Capture**: iGPU-accelerated H.264 via Media Foundation / Intel QuickSync; hardware JPEG fallback
- **Privacy**: password-field pixel redaction — the admin never receives raw credential pixels
- **AD integration**: room/group targeting via Active Directory OUs and security groups

---

## Security model

- `TADBridgeService` runs as **LocalSystem** — the highest local service identity on Windows
- Service recovery: restart on failure (5 s / 10 s / 30 s backoff)
- Service SID type: `unrestricted` for tighter service identity scoping
- All registry and deployment paths are machine-scoped (`HKLM`, `%ProgramFiles%`)
- Screen capture architecture deliberately excludes raw password pixels (privacy redaction layer)
- No kernel driver or ring-0 code required — fully user-mode

---

## Release artifacts

`build.sh` produces three self-contained single-file executables:

| ZIP | Deploy to |
|:--|:--|
| `TADAdmin-v26.3.02.003-win-x64.zip` | Admin machine |
| `TADDomainController-v26.3.02.003-win-x64.zip` | Admin / DC machine |
| `TADBridgeService-v26.3.02.003-win-x64.zip` | Every managed endpoint (via GPO / TADBootstrap) |

---

## Build

Requires .NET 8 SDK on a Linux or Windows host.

```bash
# restore packages
dotnet restore TAD-RV.sln -r win-x64

# compile check
dotnet build src/Admin/TADAdmin.csproj -c Release -r win-x64
dotnet build src/DomainController/TADDomainController.csproj -c Release -r win-x64
dotnet build src/Service/TADBridgeService.csproj -c Release -r win-x64

# full publish + ZIP artifacts
chmod +x build.sh && ./build.sh
```

Artifacts land in `build/results/` and `build/*.zip`.

---

## Running TADAdmin

```bash
# production mode — connects to live managed endpoints
TADAdmin.exe

# demo mode — full UI without any managed endpoints or service installed
TADAdmin.exe --demo
```

Demo mode generates synthetic endpoints with simulated screen thumbnails, lock state, and alerts. Useful for evaluation, screenshots, and training.

---

## Repository layout

```text
TAD-RV/
├── src/
│   ├── Admin/                   # TADAdmin — admin dashboard (WPF + WebView2)
│   ├── DomainController/        # TADDomainController — DC management console (WPF)
│   ├── Service/                 # TADBridgeService — endpoint agent (Worker Service)
│   ├── Shared/                  # TADProtocol, TADSharedInterop, UpdateManager
│   └── Driver/                  # legacy kernel assets (not used in default runtime)
├── tools/
│   ├── Bootstrap/               # TADBootstrap — GPO-ready silent installer
│   ├── Setup/                   # TADSetup — interactive installer
│   └── Scripts/                 # PowerShell deployment helpers
├── docs/                        # guides and reference documentation
├── version-admin.props          # version for TADAdmin
├── version-dc.props             # version for TADDomainController
├── version-client.props         # version for TADBridgeService, TADBootstrap, TADSetup
├── build.sh
└── TAD-RV.sln
```

---

## Documentation

| Guide | Description |
|:--|:--|
| [Architecture.md](docs/Architecture.md) | Component design, protocol, capture pipeline |
| [Build-Guide.md](docs/Build-Guide.md) | Build environment, cross-compile, signing |
| [Deployment-Guide.md](docs/Deployment-Guide.md) | GPO push, OU targeting, service registration |
| [Console-Guide.md](docs/Console-Guide.md) | TADDomainController user guide |
| [Teacher-Guide.md](docs/Teacher-Guide.md) | TADAdmin user guide |
| [Emulation-Guide.md](docs/Emulation-Guide.md) | Running a local emulated multi-endpoint lab |
| [Signing-Handbook.md](docs/Signing-Handbook.md) | Code signing for production deployment |
| [Kernel-Install-Guide.md](docs/Kernel-Install-Guide.md) | Legacy kernel driver (optional, advanced) |
| [Internationalization.md](docs/Internationalization.md) | Adding UI language packs |

---

## License

Proprietary — all rights reserved. © 2026 TAD Europe — https://tad-it.eu
