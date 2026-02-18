# TAD.RV — Build Instructions

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: How to build all TAD.RV components from source — including
> cross-compilation notes for CI/CD environments.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Quick Build (All Components)](#2-quick-build-all-components)
3. [Individual Projects](#3-individual-projects)
4. [Release Builds](#4-release-builds)
5. [Kernel Driver](#5-kernel-driver)
6. [CI/CD Notes](#6-cicd-notes)

---

## 1. Prerequisites

### .NET 8 SDK

All managed projects target `net8.0-windows` with `win-x64` RID.

```bash
# Verify SDK
dotnet --version     # 8.0.x required

# Install on Ubuntu/Debian (for cross-compile)
sudo apt-get install -y dotnet-sdk-8.0
```

> **Note**: Cross-compilation from Linux works for all managed projects.
> The binaries will only *run* on Windows, but can be built anywhere.

### Windows Driver Kit (WDK)

Required only for the kernel driver (`TAD.RV.sys`). Install from:
https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk

## 2. Quick Build (All Components)

### Debug Build

```bash
dotnet build TAD-RV.sln -c Debug
```

### Release Build (with publish to release folders)

```bash
./build.sh
```

This runs `dotnet publish` for each project and copies outputs to:

| Folder | Contents |
|---|---|
| `release-client/` | `TadConsole.exe` + PDB |
| `release-teacher/` | `TadTeacher.exe` + PDB |
| `release-addc/` | `TadBridgeService.exe`, `TadBootstrap.exe` + PDBs |

## 3. Individual Projects

### Management Console

```bash
cd Console
dotnet build -c Debug
dotnet publish -c Release -r win-x64 --self-contained
```

Output: `Console/bin/Release/net8.0-windows/win-x64/publish/TadConsole.exe`

### Teacher Controller

```bash
cd Teacher
dotnet build -c Debug
dotnet publish -c Release -r win-x64 --self-contained
```

Output: `Teacher/bin/Release/net8.0-windows/win-x64/publish/TadTeacher.exe`

### Bridge Service

```bash
cd Service
dotnet build -c Debug
dotnet publish -c Release -r win-x64 --self-contained
```

Output: `Service/bin/Release/net8.0-windows/win-x64/publish/TadBridgeService.exe`

### Bootstrap Loader

```bash
cd Bootstrap
dotnet build -c Debug
dotnet publish -c Release -r win-x64 --self-contained
```

Output: `Bootstrap/bin/Release/net8.0-windows/win-x64/publish/TadBootstrap.exe`

## 4. Release Builds

The `build.sh` script handles the full release pipeline:

```bash
#!/usr/bin/env bash
# Cleans release folders → publishes all 4 projects → copies to release dirs
./build.sh
```

After completion, the release folders contain single-file, self-contained executables ready for distribution. No .NET runtime is needed on target machines.

### Version Control

Version numbers are managed via `.props` files:

| File | Scope |
|---|---|
| `version-client.props` | Bootstrap (shared with service deployment) |
| `version-console.props` | Management Console |
| `version-teacher.props` | Teacher Controller |
| `Directory.Build.props` | Solution-wide defaults |

## 5. Kernel Driver

The kernel driver must be built on Windows with the WDK installed.

### Using WDK Build Tools

```batch
cd Kernel
build -ceZ
```

### Using Visual Studio

1. Open `Kernel/` folder in Visual Studio with WDK extension
2. The `SOURCES` file will be auto-detected
3. Build → Build Solution

### Driver Files

| File | Purpose |
|---|---|
| `TAD_RV.c` | Full driver implementation |
| `TAD_RV.h` | Driver header (includes `../Shared/TadShared.h`) |
| `TAD_RV.inf` | Installation INF (minifilter) |
| `TAD_RV.rc` | Version resource |
| `SOURCES` | WDK build metadata |
| `makefile` | WDK makefile |

> **Important**: The driver must be signed before deployment.
> See [Signing-Handbook.md](Signing-Handbook.md) for details.

## 6. CI/CD Notes

### Cross-Compile on Linux

All .NET projects use `<EnableWindowsTargeting>true</EnableWindowsTargeting>` which allows building on Linux:

```bash
# Works on Ubuntu, Debian, CI containers
dotnet build TAD-RV.sln -c Release
dotnet publish -c Release -r win-x64 --self-contained
```

### GitHub Actions Example

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore TAD-RV.sln
      - run: dotnet build TAD-RV.sln -c Release --no-restore
      - run: ./build.sh
      - uses: actions/upload-artifact@v4
        with:
          name: release-client
          path: release-client/
      - uses: actions/upload-artifact@v4
        with:
          name: release-teacher
          path: release-teacher/
      - uses: actions/upload-artifact@v4
        with:
          name: release-addc
          path: release-addc/
```

### Docker Build (Dev Container)

The repository includes a dev container configuration. Build inside the container:

```bash
# Inside dev container
dotnet build TAD-RV.sln -c Debug   # Verify
./build.sh                           # Full release
```

---

*See also: [Architecture.md](Architecture.md) · [Deployment-Guide.md](Deployment-Guide.md)*
