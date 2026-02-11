#Requires -RunAsAdministrator
#Requires -Modules GroupPolicy, ActiveDirectory
<#
.SYNOPSIS
    Signs the TAD.RV kernel driver and distributes the certificate via GPO.

.DESCRIPTION
    This script automates the complete signing and GPO distribution workflow:
      1. Creates a self-signed code-signing certificate (if not already present)
      2. Exports it (.cer for GPO, .pfx for signing)
      3. Generates a catalog file via Inf2Cat
      4. Signs the catalog and .sys with signtool
      5. Creates/updates a GPO to push the certificate into Trusted Publishers
         and Trusted Root CAs on all domain-joined machines.

.NOTES
    (C) 2026 TAD Europe — https://tad-it.eu
    See Docs/Signing-Handbook.md for the full guide.

.PARAMETER DriverPath
    Path to the directory containing TAD_RV.sys and TAD_RV.inf.

.PARAMETER CertPath
    Directory where exported .cer and .pfx files will be stored.

.PARAMETER Domain
    Active Directory domain name (e.g. school.local).

.PARAMETER DC
    Hostname of the primary domain controller.

.PARAMETER TargetOU
    Distinguished Name of the OU to which the GPO will be linked.

.PARAMETER GPOName
    Name for the Group Policy Object.

.PARAMETER PfxPassword
    Password for the exported PFX file.

.PARAMETER CertValidYears
    Validity period of the certificate in years.

.EXAMPLE
    .\Sign-TadRV.ps1 -DriverPath "C:\Build\TAD_RV" -Domain "school.local"
#>

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
Set-StrictMode -Version Latest

function Write-Step([string]$msg) { Write-Host "[...] $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "[OK]  $msg" -ForegroundColor Green }
function Write-Err([string]$msg)  { Write-Host "[ERR] $msg" -ForegroundColor Red }

# ═══════════════════════════════════════════════════════════════════════
# 1. Create or locate the code-signing certificate
# ═══════════════════════════════════════════════════════════════════════
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -like "*TAD Europe*" } |
    Select-Object -First 1

if ($cert) {
    Write-Ok "Certificate already exists: $($cert.Thumbprint)"
} else {
    Write-Step "Creating code-signing certificate..."
    $cert = New-SelfSignedCertificate `
        -Subject "CN=TAD Europe Driver Signing, O=TAD Europe, L=Vienna, C=AT" `
        -Type CodeSigningCert `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears($CertValidYears) `
        -KeyLength 4096 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -FriendlyName "TAD Europe Code Signing"
    Write-Ok "Created: $($cert.Thumbprint)"
}

# ═══════════════════════════════════════════════════════════════════════
# 2. Export certificates
# ═══════════════════════════════════════════════════════════════════════
Write-Step "Exporting certificates to $CertPath..."
New-Item -ItemType Directory -Path $CertPath -Force | Out-Null

$cerFile = Join-Path $CertPath "TAD-Europe-CodeSign.cer"
$pfxFile = Join-Path $CertPath "TAD-Europe-CodeSign.pfx"

Export-Certificate -Cert $cert -FilePath $cerFile | Out-Null

$secPass = ConvertTo-SecureString $PfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxFile -Password $secPass | Out-Null

Write-Ok "Exported .cer and .pfx"

# ═══════════════════════════════════════════════════════════════════════
# 3. Generate catalog file
# ═══════════════════════════════════════════════════════════════════════
Write-Step "Running Inf2Cat..."
& Inf2Cat /driver:"$DriverPath" /os:10_X64 /verbose
if ($LASTEXITCODE -ne 0) { throw "Inf2Cat failed with exit code $LASTEXITCODE" }
Write-Ok "Catalog generated"

# ═══════════════════════════════════════════════════════════════════════
# 4. Sign catalog + binary
# ═══════════════════════════════════════════════════════════════════════
$catFile = Join-Path $DriverPath "TAD_RV.cat"
$sysFile = Join-Path $DriverPath "TAD_RV.sys"

Write-Step "Signing catalog file..."
& signtool sign /v /f $pfxFile /p $PfxPassword `
    /t http://timestamp.digicert.com /fd SHA256 $catFile
if ($LASTEXITCODE -ne 0) { throw "Failed to sign catalog" }

Write-Step "Signing driver binary..."
& signtool sign /v /f $pfxFile /p $PfxPassword `
    /t http://timestamp.digicert.com /fd SHA256 $sysFile
if ($LASTEXITCODE -ne 0) { throw "Failed to sign driver binary" }

Write-Ok "Both files signed"

# Verify
Write-Step "Verifying signatures..."
& signtool verify /v /pa $catFile
& signtool verify /v /pa $sysFile
Write-Ok "Signatures valid"

# ═══════════════════════════════════════════════════════════════════════
# 5. Deploy certificate via GPO
# ═══════════════════════════════════════════════════════════════════════
Write-Step "Copying certificate to SYSVOL..."
$sysvolCerts = "\\$DC\SYSVOL\$Domain\Certs"
New-Item -ItemType Directory -Path $sysvolCerts -Force | Out-Null
Copy-Item $cerFile "$sysvolCerts\TAD-Europe-CodeSign.cer" -Force
Write-Ok "Certificate on SYSVOL"

$gpo = Get-GPO -Name $GPOName -ErrorAction SilentlyContinue
if (-not $gpo) {
    Write-Step "Creating GPO: $GPOName..."
    $gpo = New-GPO -Name $GPOName -Comment "Distributes TAD.RV driver-signing certificate to Trusted Publishers and Root CAs."
    $gpo | New-GPLink -Target $TargetOU
    Write-Ok "GPO created and linked to $TargetOU"
} else {
    Write-Ok "GPO already exists: $GPOName"
}

# Import certificate into GPO certificate stores
$gpoId = $gpo.Id.ToString("B")

Write-Step "Adding certificate to Trusted Publishers via GPO..."
& certutil -f -addstore -gpo "$gpoId" TrustedPublisher $cerFile
if ($LASTEXITCODE -ne 0) { Write-Err "certutil TrustedPublisher failed" }

Write-Step "Adding certificate to Trusted Root CAs via GPO..."
& certutil -f -addstore -gpo "$gpoId" Root $cerFile
if ($LASTEXITCODE -ne 0) { Write-Err "certutil Root failed" }

# ═══════════════════════════════════════════════════════════════════════
# Summary
# ═══════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  TAD.RV Signing & GPO Deployment Complete!                " -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Certificate : $($cert.Subject)"                           -ForegroundColor White
Write-Host "  Thumbprint  : $($cert.Thumbprint)"                       -ForegroundColor White
Write-Host "  GPO Name    : $GPOName"                                   -ForegroundColor White
Write-Host "  Target OU   : $TargetOU"                                  -ForegroundColor White
Write-Host "  Driver Path : $DriverPath"                                -ForegroundColor White
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Next: Run 'gpupdate /force' on clients or wait for the"  -ForegroundColor Yellow
Write-Host "        next Group Policy refresh cycle (~90 minutes)."     -ForegroundColor Yellow
Write-Host ""
