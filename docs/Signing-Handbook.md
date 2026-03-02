# TAD.RV — Driver Signing & GPO Distribution Handbook

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: This document covers how to sign the TAD.RV kernel driver with a
> self-signed certificate and distribute the trust via Active Directory Group
> Policy so every domain-joined school workstation accepts the driver without
> user interaction.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Prerequisites](#2-prerequisites)
3. [Step 1 — Create a Self-Signed Code-Signing Certificate](#3-step-1--create-a-self-signed-code-signing-certificate)
4. [Step 2 — Sign the Driver with signtool](#4-step-2--sign-the-driver-with-signtool)
5. [Step 3 — Enable Test-Signing Mode (Development Only)](#5-step-3--enable-test-signing-mode-development-only)
6. [Step 4 — Distribute the Certificate via GPO](#6-step-4--distribute-the-certificate-via-gpo)
7. [Step 5 — Verify on a Client Machine](#7-step-5--verify-on-a-client-machine)
8. [Automating with PowerShell](#8-automating-with-powershell)
9. [Production Signing (EV Certificate)](#9-production-signing-ev-certificate)
10. [Troubleshooting](#10-troubleshooting)
11. [Security Considerations](#11-security-considerations)

---

## 1. Overview

Windows requires kernel drivers to have a valid digital signature.
There are three trust levels:

| Level | Requirement | Scope |
|---|---|---|
| **WHQL / EV-signed** | Microsoft-approved certificate ($300–$500/yr) | All Windows machines globally |
| **Self-signed + GPO** | Self-signed certificate distributed via AD Group Policy | All domain-joined machines |
| **Test-Signed** | `bcdedit /set testsigning on` per machine | Single machine only |

For a school environment with Active Directory, **self-signed + GPO** is the
ideal approach — zero recurring cost, full control, works across all
domain-joined workstations.

---

## 2. Prerequisites

| Component | Where | Why |
|---|---|---|
| **Windows SDK** | Build machine | Provides `makecert.exe`, `signtool.exe`, `certutil.exe` |
| **Windows Driver Kit (WDK)** | Build machine | Provides `Inf2Cat.exe`, driver build tools |
| **Active Directory** | Domain controller (e.g. `dc01.school.local`) | GPO distribution |
| **Administrative access** | Build machine + DC | Certificate creation & GPO editing |

Install the Windows SDK:
```
winget install Microsoft.WindowsSDK.10.0.22621
```

Install the WDK:
```
winget install Microsoft.WindowsWDK.10.0.22621
```

The tools are located at:
```
C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\
```

> **Tip**: Add the above path to your `%PATH%` environment variable.

---

## 3. Step 1 — Create a Self-Signed Code-Signing Certificate

### Option A: `New-SelfSignedCertificate` (PowerShell 5+, recommended)

```powershell
# Create a code-signing certificate valid for 10 years
$cert = New-SelfSignedCertificate `
    -Subject "CN=TAD Europe Driver Signing, O=TAD Europe, L=Vienna, C=AT" `
    -Type CodeSigningCert `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(10) `
    -KeyLength 4096 `
    -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature `
    -FriendlyName "TAD Europe Code Signing"

Write-Host "Thumbprint: $($cert.Thumbprint)"
```

Export the certificate for GPO distribution:

```powershell
# Export public key (.cer) — this is what goes into GPO
Export-Certificate -Cert $cert -FilePath "C:\Certs\TAD-Europe-CodeSign.cer"

# Export with private key (.pfx) — PROTECT THIS FILE
$password = ConvertTo-SecureString -String "YourStrongPassword123!" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "C:\Certs\TAD-Europe-CodeSign.pfx" -Password $password
```

### Option B: `makecert` + `pvk2pfx` (Legacy)

```cmd
:: Create root CA
makecert -r -pe -n "CN=TAD Europe Root CA" -ss CA -sr CurrentUser ^
         -a sha256 -len 4096 -cy authority -sky signature ^
         -sv TAD-RootCA.pvk TAD-RootCA.cer

:: Create code-signing certificate signed by root
makecert -pe -n "CN=TAD Europe Driver Signing" -ss My -sr CurrentUser ^
         -a sha256 -len 4096 -cy end -sky signature ^
         -eku 1.3.6.1.5.5.7.3.3 ^
         -ic TAD-RootCA.cer -iv TAD-RootCA.pvk ^
         -sv TAD-CodeSign.pvk TAD-CodeSign.cer

:: Convert to PFX
pvk2pfx -pvk TAD-CodeSign.pvk -spc TAD-CodeSign.cer ^
        -pfx TAD-CodeSign.pfx -po YourStrongPassword123!
```

> **Important**: Store the `.pfx` and `.pvk` files somewhere secure (not in the
> repository). They contain the private key.

---

## 4. Step 2 — Sign the Driver with signtool

### 4.1 Create a Catalog File

Windows drivers must be accompanied by a `.cat` (catalog) file that contains
the hashes of all driver files:

```cmd
:: Generate the catalog from the INF
Inf2Cat /driver:"C:\Build\TAD_RV" /os:10_X64 /verbose
```

The `Inf2Cat` tool reads `TAD_RV.inf` and creates `TAD_RV.cat` in the same
directory.

### 4.2 Sign the Catalog

```cmd
:: Sign using certificate from the local store (recommended)
signtool sign /v /s My /n "TAD Europe Driver Signing" ^
    /t http://timestamp.digicert.com ^
    /fd SHA256 ^
    "C:\Build\TAD_RV\TAD_RV.cat"
```

Or sign using a PFX file:

```cmd
signtool sign /v /f "C:\Certs\TAD-Europe-CodeSign.pfx" ^
    /p "YourStrongPassword123!" ^
    /t http://timestamp.digicert.com ^
    /fd SHA256 ^
    "C:\Build\TAD_RV\TAD_RV.cat"
```

### 4.3 Sign the Driver Binary (Belt & Suspenders)

Although the catalog signature is sufficient, signing the `.sys` directly
provides an extra layer of trust:

```cmd
signtool sign /v /s My /n "TAD Europe Driver Signing" ^
    /t http://timestamp.digicert.com ^
    /fd SHA256 ^
    "C:\Build\TAD_RV\TAD_RV.sys"
```

### 4.4 Verify the Signatures

```cmd
signtool verify /v /pa "C:\Build\TAD_RV\TAD_RV.cat"
signtool verify /v /pa "C:\Build\TAD_RV\TAD_RV.sys"
```

---

## 5. Step 3 — Enable Test-Signing Mode (Development Only)

> **Warning**: This step is for development/test machines only. In
> production, use GPO distribution (Step 4) instead.

On a single development machine:

```cmd
bcdedit /set testsigning on
```

Reboot required. A "Test Mode" watermark will appear in the bottom-right
corner of the desktop (harmless but visible).

To disable later:

```cmd
bcdedit /set testsigning off
```

---

## 6. Step 4 — Distribute the Certificate via GPO

This is the core step that makes self-signing work across all school machines.
By pushing the certificate into the **Trusted Publishers** and
**Trusted Root Certification Authorities** stores via Group Policy, every
domain-joined machine will trust your driver signature.

### 6.1 Copy the Certificate to an Accessible Location

```powershell
# Copy to the SYSVOL share (replicated across all DCs)
Copy-Item "C:\Certs\TAD-Europe-CodeSign.cer" `
    "\\dc01.school.local\SYSVOL\school.local\Certs\TAD-Europe-CodeSign.cer"
```

### 6.2 Open Group Policy Management

```
gpmc.msc
```

1. Navigate to the OU containing your school workstations  
   (e.g., `school.local → Workstations`)
2. Right-click → **Create a GPO in this domain, and Link it here...**
3. Name: **TAD.RV Driver Trust Certificate**
4. Right-click the new GPO → **Edit**

### 6.3 Add the Certificate to Trusted Publishers

In the Group Policy Editor:

```
Computer Configuration
  → Policies
    → Windows Settings
      → Security Settings
        → Public Key Policies
          → Trusted Publishers
```

1. Right-click **Trusted Publishers** → **Import...**
2. Browse to `TAD-Europe-CodeSign.cer`
3. Place it in **Trusted Publishers** store
4. Complete the wizard

### 6.4 Add the Root CA to Trusted Root Certification Authorities

> **Only needed if you used Option B (makecert with a Root CA).**
> If you used `New-SelfSignedCertificate` (Option A), the code-signing cert
> is self-signed and should be added to both stores.

```
Computer Configuration
  → Policies
    → Windows Settings
      → Security Settings
        → Public Key Policies
          → Trusted Root Certification Authorities
```

1. Right-click → **Import...**
2. Browse to `TAD-RootCA.cer` (or `TAD-Europe-CodeSign.cer` for Option A)
3. Complete the wizard

### 6.5 Force Group Policy Update

On the domain controller:

```powershell
# Optional: force immediate replication
Invoke-GPUpdate -Computer "workstation01" -Force -RandomDelayInMinutes 0
```

On a client machine:

```cmd
gpupdate /force
```

### 6.6 GPO Summary Diagram

```
┌─────────────────────────────────┐
│   Domain Controller (dc01)      │
│                                 │
│   GPO: TAD.RV Driver Trust      │
│   ├── Trusted Publishers        │
│   │   └── TAD-Europe-CodeSign   │
│   └── Trusted Root CAs          │
│       └── TAD-Europe-CodeSign   │
└──────────────┬──────────────────┘
               │ Group Policy (automatic)
               ▼
┌──────────────────────────────────┐
│   Domain-Joined Workstations     │
│                                  │
│   Certificate Store (Machine)    │
│   ├── Trusted Publishers ✓       │
│   └── Trusted Root CAs   ✓      │
│                                  │
│   TAD_RV.sys → Signature Valid ✓ │
└──────────────────────────────────┘
```

---

## 7. Step 5 — Verify on a Client Machine

After GPO has been applied, verify on a workstation:

### 7.1 Check the Certificate Store

```powershell
# Should list the TAD Europe certificate
Get-ChildItem Cert:\LocalMachine\TrustedPublisher | Where-Object {
    $_.Subject -like "*TAD Europe*"
}

Get-ChildItem Cert:\LocalMachine\Root | Where-Object {
    $_.Subject -like "*TAD Europe*"
}
```

### 7.2 Verify the Driver Signature

```powershell
# Check the driver file signature
Get-AuthenticodeSignature "C:\Windows\System32\drivers\TAD_RV.sys"

# Expected output:
# SignerCertificate: [Thumbprint] CN=TAD Europe Driver Signing, ...
# Status           : Valid
```

### 7.3 Check Driver Load Status

```cmd
sc query TAD_RV
driverquery /v | findstr TAD
```

### 7.4 Check Event Log for Signing Issues

```powershell
Get-WinEvent -LogName "Microsoft-Windows-CodeIntegrity/Operational" |
    Where-Object { $_.Message -like "*TAD*" } |
    Select-Object -First 10 TimeCreated, Message
```

---

## 8. Automating with PowerShell

Below is a complete script that automates the entire signing + GPO process.
Run it on the build machine with domain admin rights.

```powershell
#Requires -RunAsAdministrator
#Requires -Modules GroupPolicy, ActiveDirectory

param(
    [string]$DriverPath    = "C:\Build\TAD_RV",
    [string]$CertPath      = "C:\Certs",
    [string]$Domain        = "school.local",
    [string]$DC            = "dc01.school.local",
    [string]$TargetOU      = "OU=Workstations,DC=school,DC=local",
    [string]$GPOName       = "TAD.RV Driver Trust Certificate",
    [string]$PfxPassword   = "YourStrongPassword123!",
    [int]$CertValidYears   = 10
)

$ErrorActionPreference = "Stop"

# ── 1. Create certificate (if not already present) ──────────────────────
$existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -like "*TAD Europe*" }

if ($existing) {
    Write-Host "[OK] Certificate already exists: $($existing.Thumbprint)" -ForegroundColor Green
    $cert = $existing
} else {
    Write-Host "[...] Creating code-signing certificate..."
    $cert = New-SelfSignedCertificate `
        -Subject "CN=TAD Europe Driver Signing, O=TAD Europe, L=Vienna, C=AT" `
        -Type CodeSigningCert `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears($CertValidYears) `
        -KeyLength 4096 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -FriendlyName "TAD Europe Code Signing"
    Write-Host "[OK] Created: $($cert.Thumbprint)" -ForegroundColor Green
}

# ── 2. Export certificates ──────────────────────────────────────────────
New-Item -ItemType Directory -Path $CertPath -Force | Out-Null

$cerFile = Join-Path $CertPath "TAD-Europe-CodeSign.cer"
$pfxFile = Join-Path $CertPath "TAD-Europe-CodeSign.pfx"

Export-Certificate -Cert $cert -FilePath $cerFile | Out-Null
$secPass = ConvertTo-SecureString $PfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxFile -Password $secPass | Out-Null
Write-Host "[OK] Exported .cer and .pfx to $CertPath" -ForegroundColor Green

# ── 3. Sign the driver ─────────────────────────────────────────────────
Write-Host "[...] Running Inf2Cat..."
& Inf2Cat /driver:"$DriverPath" /os:10_X64 /verbose
if ($LASTEXITCODE -ne 0) { throw "Inf2Cat failed" }

$catFile = Join-Path $DriverPath "TAD_RV.cat"
$sysFile = Join-Path $DriverPath "TAD_RV.sys"

Write-Host "[...] Signing catalog..."
& signtool sign /v /f $pfxFile /p $PfxPassword `
    /t http://timestamp.digicert.com /fd SHA256 $catFile
if ($LASTEXITCODE -ne 0) { throw "signtool (catalog) failed" }

Write-Host "[...] Signing driver binary..."
& signtool sign /v /f $pfxFile /p $PfxPassword `
    /t http://timestamp.digicert.com /fd SHA256 $sysFile
if ($LASTEXITCODE -ne 0) { throw "signtool (sys) failed" }

Write-Host "[OK] Driver signed successfully" -ForegroundColor Green

# ── 4. Verify signatures ───────────────────────────────────────────────
& signtool verify /v /pa $catFile
& signtool verify /v /pa $sysFile

# ── 5. Deploy certificate via GPO ──────────────────────────────────────
Write-Host "[...] Copying certificate to SYSVOL..."
$sysvolCerts = "\\$DC\SYSVOL\$Domain\Certs"
New-Item -ItemType Directory -Path $sysvolCerts -Force | Out-Null
Copy-Item $cerFile "$sysvolCerts\TAD-Europe-CodeSign.cer" -Force

$gpo = Get-GPO -Name $GPOName -ErrorAction SilentlyContinue
if (-not $gpo) {
    Write-Host "[...] Creating GPO: $GPOName"
    $gpo = New-GPO -Name $GPOName -Comment "Distributes TAD.RV driver-signing certificate"
    $gpo | New-GPLink -Target $TargetOU
    Write-Host "[OK] GPO created and linked to $TargetOU" -ForegroundColor Green
} else {
    Write-Host "[OK] GPO already exists: $GPOName" -ForegroundColor Green
}

# Import certificate into Trusted Publishers (machine-level via GPO)
# Note: PowerShell GPO cmdlets don't directly support certificate import.
# Use certutil on the GPO's file system representation instead.
$gpoId   = $gpo.Id.ToString("B")
$machPath = "\\$DC\SYSVOL\$Domain\Policies\$gpoId\Machine"

# Trusted Publishers store
$tpPath = Join-Path $machPath "Registry\SOFTWARE\Policies\Microsoft\SystemCertificates\TrustedPublisher\Certificates"
New-Item -ItemType Directory -Path $tpPath -Force | Out-Null

# Trusted Root store
$trPath = Join-Path $machPath "Registry\SOFTWARE\Policies\Microsoft\SystemCertificates\Root\Certificates"
New-Item -ItemType Directory -Path $trPath -Force | Out-Null

# Use certutil to add to GPO stores
& certutil -f -addstore -gpo "$gpoId" TrustedPublisher $cerFile
& certutil -f -addstore -gpo "$gpoId" Root $cerFile

Write-Host ""
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " TAD.RV Signing & GPO Deployment Complete!"          -ForegroundColor Cyan
Write-Host " Thumbprint : $($cert.Thumbprint)"                  -ForegroundColor Cyan
Write-Host " GPO Name   : $GPOName"                              -ForegroundColor Cyan
Write-Host " Target OU  : $TargetOU"                             -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Run 'gpupdate /force' on clients or wait for next policy refresh." -ForegroundColor Yellow
```

Save this as `Scripts\Sign-TadRV.ps1`.

---

## 9. Production Signing (EV Certificate)

When you're ready for public distribution outside your AD domain:

| Provider | Type | Price | Notes |
|---|---|---|---|
| **Sectigo** (Comodo) | EV Code Signing | ~€300/yr | Has educational discounts |
| **DigiCert** | EV Code Signing | ~€500/yr | Industry standard |
| **GlobalSign** | EV Code Signing | ~€350/yr | Good EU support |
| **SSL.com** | EV Code Signing | ~€240/yr | Budget-friendly |

With an EV certificate:
- No test-signing mode needed
- No GPO distribution needed
- WHQL submission becomes possible (for Microsoft co-signature)
- Driver works on any Windows machine out of the box

> **Note**: EV certificates require identity verification and are shipped on
> a hardware USB token (e.g., SafeNet eToken). You cannot export the private
> key.

---

## 10. Troubleshooting

### Driver won't load — "Windows cannot verify the digital signature"

```powershell
# Check Code Integrity logs
Get-WinEvent -LogName "Microsoft-Windows-CodeIntegrity/Operational" -MaxEvents 20 |
    Format-Table TimeCreated, Id, Message -Wrap
```

**Common causes:**
- Certificate not in **Trusted Publishers** → Check GPO propagation
- Certificate not in **Root CAs** → Add it to both stores
- GPO not applied yet → Run `gpupdate /force` and reboot
- Catalog file missing or unsigned → Re-run `Inf2Cat` + `signtool`

### GPO not applying

```cmd
:: Check which GPOs are applied
gpresult /r /scope computer

:: Detailed GPO report (HTML)
gpresult /h C:\gpresult.html
```

### Certificate mismatch

```powershell
# Compare thumbprints
$driverSig = (Get-AuthenticodeSignature "C:\Windows\System32\drivers\TAD_RV.sys").SignerCertificate
$storeCert = Get-ChildItem Cert:\LocalMachine\TrustedPublisher | Where-Object { $_.Subject -like "*TAD*" }

Write-Host "Driver signed with : $($driverSig.Thumbprint)"
Write-Host "Store contains     : $($storeCert.Thumbprint)"
# These must match!
```

### Secure Boot blocking the driver

Secure Boot + HVCI (Hypervisor-protected Code Integrity) may block
self-signed drivers even with GPO trust. Options:

1. **Disable HVCI** via GPO:
   ```
   Computer Configuration → Admin Templates → System → Device Guard
     → Turn On Virtualization Based Security → Disabled
   ```

2. **Create a HVCI policy** that includes your certificate:
   ```powershell
   New-CIPolicy -FilePath "C:\Policies\TAD-Allow.xml" `
       -Level Publisher -ScanPath "C:\Build\TAD_RV"
   ConvertFrom-CIPolicy "C:\Policies\TAD-Allow.xml" "C:\Policies\TAD-Allow.p7b"
   ```

---

## 11. Security Considerations

| Risk | Mitigation |
|---|---|
| Private key theft | Store `.pfx` in a secure vault (e.g., Azure Key Vault, USB token). Never commit to git. |
| Overly broad trust | Only add cert to **Trusted Publishers**, not to Root CAs if possible |
| Certificate expiry | Set a long validity (10 years) or monitor with scheduled task |
| Rogue driver signed | Restrict access to the private key; use separate build machine |
| GPO scope too wide | Link GPO only to workstation OUs, not the entire domain |

### Recommended `.gitignore` additions

```gitignore
# Never commit private keys or certificates
*.pfx
*.pvk
*.p12
*.cer
*.p7b
```

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────────────────┐
│  TAD.RV Signing — Quick Reference                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. CREATE CERT                                                  │
│     New-SelfSignedCertificate -Type CodeSigningCert ...          │
│                                                                  │
│  2. EXPORT                                                       │
│     Export-Certificate → .cer  (public, for GPO)                 │
│     Export-PfxCertificate → .pfx  (private, keep safe!)          │
│                                                                  │
│  3. CATALOG                                                      │
│     Inf2Cat /driver:"path" /os:10_X64                            │
│                                                                  │
│  4. SIGN                                                         │
│     signtool sign /f cert.pfx /fd SHA256 /t timestamp TAD_RV.cat│
│     signtool sign /f cert.pfx /fd SHA256 /t timestamp TAD_RV.sys│
│                                                                  │
│  5. GPO                                                          │
│     gpmc.msc → Trusted Publishers → Import .cer                  │
│     gpmc.msc → Trusted Root CAs  → Import .cer                  │
│                                                                  │
│  6. VERIFY                                                       │
│     signtool verify /v /pa TAD_RV.cat                            │
│     Get-AuthenticodeSignature TAD_RV.sys                         │
│                                                                  │
│  (C) 2026 TAD Europe — https://tad-it.eu                        │
└─────────────────────────────────────────────────────────────────┘
```

---

*Document version 1.0 — Last updated 2025*  
*(C) 2026 TAD Europe — https://tad-it.eu*
