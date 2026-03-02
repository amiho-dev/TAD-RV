<p align="center">
  <img src="logo.png" alt="TAD.RV Logo" width="96">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-26030.02NR-blue?style=flat-square" alt="Version">
  <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet" alt=".NET 8">
  <img src="https://img.shields.io/badge/runtime-user--mode-success?style=flat-square" alt="User Mode">
  <img src="https://img.shields.io/badge/platform-win--x64-lightgrey?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/license-proprietary-red?style=flat-square" alt="License">
</p>

# TAD.RV

User-mode classroom endpoint monitoring and control platform for Windows school labs.

## What changed in 26030.02NR

- Complete runtime pivot to user-mode protection by default
- `TADBridgeService` now runs as hardened `LocalSystem` service (auto-recovery + unrestricted service SID)
- Kernel driver installation path removed from deployment scripts and bootstrap flow
- Service runtime defaults to user-mode; legacy kernel mode is opt-in via `--kernel`
- Release and version metadata synchronized to `26030.02NR`

## Architecture (Current)

| Component | Technology | Purpose |
|:--|:--|:--|
| Bridge Service | .NET 8 Worker Service | User-mode policy enforcement, AD integration, capture, networking |
| Teacher App | .NET 8 WPF + WebView2 | Classroom control dashboard |
| Admin Console | .NET 8 WPF | Deployment, service operations, policy management, alerts |
| Bootstrap | .NET 8 Console | Zero-install distribution and service registration |
| Shared | C# shared contracts | Protocol, interop structs, update manager |

## Security model (user-mode)

- Runs as `LocalSystem` service (higher effective role than local admin user sessions)
- Service recovery configured to restart on failures (5s/10s/30s)
- Service SID set to `unrestricted` for tighter service identity handling
- Registry and deployment paths remain under machine scope (`HKLM`, Program Files)

## Build

```bash
dotnet restore TAD-RV.sln -r win-x64
dotnet build TAD-RV.sln -c Release

# full publish + release ZIP artifacts
chmod +x build.sh && ./build.sh
```

## Runtime modes

```bash
# default (recommended): user-mode protection
TADBridgeService.exe

# user-mode with synthetic alerts/demo telemetry
TADBridgeService.exe --demo

# legacy compatibility mode (kernel bridge)
TADBridgeService.exe --kernel
```

## Release artifacts

`build.sh` produces:

- `TADDomainController-26030.02NR-win-x64.zip`
- `TADAdmin-26030.02NR-win-x64.zip`
- `TADBridgeService-26030.02NR-win-x64.zip`

## Repository layout

```text
TAD-RV/
├── src/
│   ├── Service/
│   ├── Teacher/
│   ├── Console/
│   ├── Shared/
│   └── Driver/              # legacy kernel assets (not required for default runtime)
├── tools/
│   ├── Bootstrap/
│   └── Scripts/
├── docs/
├── build.sh
└── TAD-RV.sln
```

## Documentation

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Build-Guide.md](docs/Build-Guide.md)
- [docs/Deployment-Guide.md](docs/Deployment-Guide.md)
- [docs/Console-Guide.md](docs/Console-Guide.md)
- [docs/Teacher-Guide.md](docs/Teacher-Guide.md)

## License

Proprietary, all rights reserved.
