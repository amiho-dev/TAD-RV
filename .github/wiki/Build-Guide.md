# Build Guide

TAD.RV can be built from source on Linux or Windows using the .NET 8 SDK. The kernel driver requires a Windows + WDK environment.

---

## Prerequisites

### .NET 8 SDK

```bash
# Verify
dotnet --version   # Must be 8.0.x

# Install on Ubuntu/Debian
sudo apt-get install -y dotnet-sdk-8.0

# Install on Windows (winget)
winget install Microsoft.DotNet.SDK.8
```

### Kernel Driver (optional)

Building `TAD.RV.sys` requires Windows with the WDK installed:
- Download: https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk

---

## Quick Build (All Components)

```bash
git clone https://github.com/amiho-dev/TAD-RV.git
cd TAD-RV
bash build.sh
```

Output:
```
build/TADClientSetup-v26.X.XX.XXX-win-x64.exe
build/TADAdminSetup-v26.X.XX.XXX-win-x64.exe
build/TADDomainControllerSetup-v26.X.XX.XXX-win-x64.exe
```

Each EXE is a self-contained single-file installer that embeds the .NET runtime. No runtime installation required on target machines.

---

## Individual Projects

### TADAdmin (teacher dashboard)
```bash
dotnet publish src/Admin/TADAdmin.csproj -c Release -r win-x64 --self-contained
```

### TADBridgeService (endpoint agent)
```bash
dotnet publish src/Service/TADBridgeService.csproj -c Release -r win-x64 --self-contained
```

### TADDomainController (IT console)
```bash
dotnet publish src/DomainController/TADDomainController.csproj -c Release -r win-x64 --self-contained
```

---

## Version Numbers

Version numbers are controlled via `.props` files:

| File | Applies to |
|---|---|
| `version-admin.props` | TADAdmin (teacher dashboard) |
| `version-client.props` | TADBridgeService, TADBootstrap, TADSetup |
| `Directory.Build.props` | Solution-wide shared settings |

Version format: `v{year}.{month:0}.{day:00}.{patch:000}`
Example: `v26.3.04.127`

---

## Cross-Compilation from Linux

All managed (.NET) projects cross-compile from Linux — the binaries target Windows but can be built on any OS:

```bash
# Works on Ubuntu in GitHub Codespaces, CI, etc.
dotnet build TAD-RV.sln -c Release -r win-x64
```

The `<EnableWindowsTargeting>true</EnableWindowsTargeting>` property in `Directory.Build.props` enables this.

---

## Kernel Driver

The kernel driver must be built on Windows with the WDK:

```batch
cd src/Driver
build -ceZ
```

Or open the folder in Visual Studio with the WDK extension and build normally.

The `.sys` file must be **signed** before deployment. See [Signing-Handbook.md](https://github.com/amiho-dev/TAD-RV/blob/main/docs/Signing-Handbook.md).

---

## GitHub Actions CI

```yaml
name: Build
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: bash build.sh
      - uses: actions/upload-artifact@v4
        with:
          name: setup-exes
          path: build/TAD*Setup*.exe
```

---

## Warnings

The build produces a few known warnings that are acceptable:

| Warning | Source | Notes |
|---|---|---|
| `CS0414 _allFrozen` | `Admin/MainWindow.xaml.cs` | Field retained for future use |
| `CS0414 _allBlanked` | `Admin/MainWindow.xaml.cs` | Field retained for future use |
| `CS0649 _isFrozen` | `Service/Networking/TadTcpListener.cs` | Placeholder for freeze command |
| `CS8602` (nullable) | `Service/Networking/TadTcpListener.cs` | Non-critical null check |

All warnings are non-blocking. The build succeeds and produces functional binaries.
