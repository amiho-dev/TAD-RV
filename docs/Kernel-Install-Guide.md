# Kernel Driver Installation Guide

Complete guide for building, signing, and installing the **TAD_RV.sys** kernel driver on Windows 10/11 endpoints.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Building the Driver](#2-building-the-driver)
3. [Signing](#3-signing)
4. [Installation Methods](#4-installation-methods)
5. [Verification](#5-verification)
6. [Troubleshooting](#6-troubleshooting)
7. [Uninstallation](#7-uninstallation)

---

## 1. Prerequisites

| Component | Version | Purpose |
|---|---|---|
| **Windows Driver Kit (WDK)** | 10.0.22621+ | Build toolchain |
| **Visual Studio 2022** | 17.4+ | IDE (optional, CLI also works) |
| **Windows SDK** | 10.0.22621+ | Headers & libs |
| **.NET 8 SDK** | 8.0.x | Service build (optional for driver-only) |

### Required Libraries

The driver links against:
- `fltMgr.lib` — Minifilter Manager
- `ntstrsafe.lib` — Safe string functions

These are included automatically by WDK.

### Hardware Requirements

- **CPU**: x64 only (ARM64 not supported)
- **OS**: Windows 10 1903+ or Windows 11
- **RAM**: Minimal driver footprint (~64 KB resident)

---

## 2. Building the Driver

### Option A: WDK Build Environment (Recommended)

```batch
cd Kernel
build -ceZ
```

This reads the `SOURCES` file which defines:
- Target name: `TAD_RV`
- Target type: `DRIVER` (via `DRIVERTYPE=FS` for minifilter)
- Links: `fltMgr.lib`, `ntstrsafe.lib`

### Option B: Visual Studio

1. Open `TAD-RV.sln` in Visual Studio 2022
2. Right-click `Kernel/` folder → **Build**
3. Output: `Kernel/objfre_amd64/amd64/TAD_RV.sys`

### Option C: MSBuild CLI

```batch
msbuild Kernel\TAD_RV.vcxproj /p:Configuration=Release /p:Platform=x64
```

### Build Output

| File | Description |
|---|---|
| `TAD_RV.sys` | The kernel driver binary |
| `TAD_RV.inf` | Installation information file |
| `TAD_RV.cat` | Catalog file (created during signing) |
| `TAD_RV.pdb` | Debug symbols (keep for crash analysis) |

---

## 3. Signing

### Development / Lab (Test-Signing)

For internal testing without a purchased certificate:

```batch
REM 1. Enable test-signing mode (one-time, requires reboot)
bcdedit /set testsigning on
shutdown /r /t 0

REM 2. Create a self-signed certificate
makecert -r -pe -a sha256 -ss PrivateCertStore ^
  -n "CN=TAD.RV Dev" -len 2048 TAD-Dev.cer

REM 3. Sign the driver
signtool sign /a /s PrivateCertStore ^
  /n "TAD.RV Dev" /fd sha256 /v Kernel\TAD_RV.sys

REM 4. Create the catalog file
inf2cat /driver:Kernel /os:10_x64
signtool sign /a /s PrivateCertStore ^
  /n "TAD.RV Dev" /fd sha256 /v Kernel\TAD_RV.cat

REM 5. Trust the certificate (on each target machine)
certutil -addstore Root TAD-Dev.cer
certutil -addstore TrustedPublisher TAD-Dev.cer
```

> **Note**: Test-signing shows a watermark on the desktop. This is normal for development environments.

### Production (EV Certificate + WHQL)

For school-wide deployment without test-signing:

1. **Purchase an EV code-signing certificate** from DigiCert, Sectigo, or GlobalSign (~€40–150/year)
2. **Sign the driver**:
   ```batch
   signtool sign /f certificate.pfx /p PASSWORD ^
     /fd sha256 /tr http://timestamp.digicert.com ^
     /td sha256 /v Kernel\TAD_RV.sys
   ```
3. **Submit for WHQL attestation** via the [Windows Hardware Dev Center](https://partner.microsoft.com/dashboard/hardware)
4. Download the signed `.cat` file and place it alongside `TAD_RV.inf`

See [Docs/Signing-Handbook.md](Signing-Handbook.md) for detailed instructions.

---

## 4. Installation Methods

### Method 1: PowerShell Script (Recommended)

```powershell
# Run as Administrator
.\Scripts\Install-Driver.ps1
```

This script:
- Copies `TAD_RV.sys` to `%SystemRoot%\System32\drivers\`
- Registers via `pnputil /add-driver`
- Starts the driver
- Verifies it's running

### Method 2: PnPUtil (Windows Built-in)

```batch
REM Run as Administrator
pnputil /add-driver Kernel\TAD_RV.inf /install

REM Start the driver
sc start TAD_RV
```

### Method 3: Service Control Manager

```batch
REM Register the driver as a kernel service
sc create TAD_RV type=kernel start=demand ^
  binPath=%SystemRoot%\System32\drivers\TAD_RV.sys ^
  DisplayName="TAD.RV Kernel Driver"

REM Start it
sc start TAD_RV
```

### Method 4: Programmatic (via TadBridgeService)

```batch
REM The service can auto-install the driver on first run
TadBridgeService.exe --auto-install
```

This calls `DriverInstaller.EnsureInstalled()` which handles:
- Copying `.sys` and `.inf` to `%SystemRoot%\System32\drivers\`
- Running `pnputil /add-driver /install`
- Starting the driver
- Registering the service PID for protection

### Method 5: Bootstrap Loader (GPO Deployment)

For zero-touch deployment in school labs:

```batch
REM Place on NETLOGON share
\\dc01.school.local\NETLOGON\TAD\TadBootstrap.exe
```

Configure as a **GPO Computer Startup Script**. The bootstrap loader:
1. Copies binaries to a hidden local cache (`%ProgramData%\.tad_cache\`)
2. Installs the kernel driver via `sc create`
3. Registers and starts TadBridgeService as auto-start SYSTEM service
4. Configures crash recovery (restart after 5s/10s/30s)

See [Docs/Deployment-Guide.md](Deployment-Guide.md) for full GPO walkthrough.

---

## 5. Verification

### Check Driver Status

```batch
sc query TAD_RV
```

Expected output:
```
SERVICE_NAME: TAD_RV
  TYPE               : 2  FILE_SYSTEM_DRIVER
  STATE              : 4  RUNNING
  WIN32_EXIT_CODE    : 0  (0x0)
```

### Check Device Object

```batch
REM The driver creates \\.\TadRvLink
dir \\.\TadRvLink
```

### Check via Registry

```batch
reg query HKLM\SYSTEM\CurrentControlSet\Services\TAD_RV
```

### Check Minifilter Registration

```batch
fltmc filters
```

Look for `TAD_RV` with altitude `371100`.

### Check ObCallback Registration

Use **WinObj** (Sysinternals) to browse `\Callback\` for the TAD.RV registration, or check the Event Log for:

```
Source: TadBridgeService
Event ID: 9001
```

### Run Emulated Mode (No Real Driver)

```batch
cd Service
dotnet run -- --emulate
```

This uses `EmulatedDriverBridge` which simulates all IOCTL responses.

---

## 6. Troubleshooting

### Driver Won't Load: "Windows cannot verify the digital signature"

**Cause**: Driver is unsigned or test-signing is disabled.

**Fix**:
```batch
bcdedit /set testsigning on
shutdown /r /t 0
```

Or sign the driver with a valid certificate (see Section 3).

### Driver Won't Load: "A device attached to the system is not functioning"

**Cause**: Missing `fltMgr.lib` dependency or driver binary corruption.

**Fix**: Rebuild from source and verify the `.sys` file hash.

### ObRegisterCallbacks Returns STATUS_ACCESS_DENIED

**Cause**: The driver must be signed to use `ObRegisterCallbacks`. Even test-signed drivers need the certificate trusted.

**Fix**: Ensure the certificate is in both `Root` and `TrustedPublisher` stores:
```batch
certutil -addstore Root TAD-Dev.cer
certutil -addstore TrustedPublisher TAD-Dev.cer
```

### Heartbeat Timeout — Service Can't Connect

**Cause**: Device object `\\.\TadRvLink` not created or DACL prevents access.

**Fix**: Verify the service runs as `LocalSystem` (which has access). Check:
```batch
sc qc TadBridgeService
```
Ensure `SERVICE_START_NAME` is `LocalSystem`.

### BSOD During Development

**Cause**: Bug in driver code (null pointer, pool corruption, IRQL mismatch).

**Fix**:
1. Enable kernel debugging: `bcdedit /debug on`
2. Attach WinDbg
3. Analyze the crash dump: `!analyze -v`
4. Check `TAD_RV.pdb` is in the symbol path

### Minifilter Not Blocking File Deletions

**Cause**: Filter registration failed or altitude not recognized.

**Fix**: Verify filter is registered:
```batch
fltmc filters | findstr TAD
```

If missing, check `TAD_RV.inf` includes the correct `Instance1.Altitude = 371100` entry.

---

## 7. Uninstallation

### Stop and Remove

```batch
REM Stop the user-mode service first
sc stop TadBridgeService
sc delete TadBridgeService

REM Stop and remove the kernel driver
sc stop TAD_RV
sc delete TAD_RV

REM Or via pnputil
pnputil /delete-driver TAD_RV.inf /uninstall /force
```

### Programmatic Uninstall

```powershell
# Via the Install-Driver.ps1 script
.\Scripts\Install-Driver.ps1 -Uninstall
```

### Clean Up Files

```batch
del %SystemRoot%\System32\drivers\TAD_RV.sys
del %SystemRoot%\System32\drivers\TAD_RV.inf
rd /s /q %ProgramData%\TAD_RV
rd /s /q %ProgramData%\.tad_cache
reg delete HKLM\SOFTWARE\TAD_RV /f
reg delete HKLM\SYSTEM\CurrentControlSet\Services\TAD_RV /f
reg delete HKLM\SYSTEM\CurrentControlSet\Services\TadBridgeService /f
```

---

## Driver Architecture Quick Reference

```
DriverEntry()
  ├── Create device: \Device\TadRv → \DosDevices\TadRvLink
  ├── Harden DACL (SYSTEM + Admins only)
  ├── ObRegisterCallbacks (process + thread protection)
  ├── FltRegisterFilter (anti-delete, anti-rename)
  ├── FltStartFiltering
  ├── Initialize heartbeat DPC timer (6s timeout)
  └── Register IRP handlers (CREATE, CLOSE, IOCTL)

DriverUnload()
  ├── Requires IOCTL_TAD_UNLOCK (256-bit key) first
  ├── FltUnregisterFilter
  ├── ObUnRegisterCallbacks
  ├── Cancel heartbeat DPC
  └── Delete device object
```

**Version**: 26200.172.0.0
**Copyright**: (C) 2026 TAD Europe — https://tad-it.eu
