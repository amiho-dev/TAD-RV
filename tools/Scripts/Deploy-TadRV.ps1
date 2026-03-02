<#
.SYNOPSIS
    Deploys the TAD.RV user-mode TADBridgeService to a target machine.

.DESCRIPTION
    Copies published service binaries,
    registers the service, and performs first-boot
    provisioning against Active Directory.

.PARAMETER ServicePath
    Path to the published TADBridgeService directory (self-contained).

.PARAMETER TargetDir
    Installation directory on the target machine.
    Default: C:\Program Files\TAD_RV

.PARAMETER DomainController
    FQDN of the domain controller for NETLOGON policy fetch.
    Default: dc01.school.local

.PARAMETER SkipService
    Skip service installation.

.EXAMPLE
    .\Deploy-TadRV.ps1 -ServicePath .\Service\bin\Release\net8.0-windows\win-x64\publish
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ServicePath,

    [Parameter(Mandatory = $false)]
    [string]$TargetDir = "C:\Program Files\TAD_RV",

    [Parameter(Mandatory = $false)]
    [string]$DomainController = "dc01.school.local",

    [switch]$SkipService
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Elevation check ──────────────────────────────────────────────────
function Assert-Admin {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run as Administrator."
    }
}

# ── Service helpers ──────────────────────────────────────────────────
function Stop-ServiceSafe([string]$Name) {
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Write-Host "  Stopping $Name ..." -ForegroundColor Yellow
        Stop-Service -Name $Name -Force
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
}

function Remove-ServiceSafe([string]$Name) {
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($svc) {
        Stop-ServiceSafe $Name
        Write-Host "  Removing $Name ..." -ForegroundColor Yellow
        sc.exe delete $Name | Out-Null
    }
}

# ══════════════════════════════════════════════════════════════════════
#   MAIN
# ══════════════════════════════════════════════════════════════════════

Assert-Admin
Write-Host "`n╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║        TAD.RV Deployment Script                  ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# ── 1. Validate parameters ───────────────────────────────────────────
if (-not $SkipService) {
    if (-not $ServicePath) { throw "-ServicePath is required unless -SkipService is set." }
    if (-not (Test-Path $ServicePath)) { throw "Service publish directory not found: $ServicePath" }
}

# ── 2. Create target directory ───────────────────────────────────────
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Write-Host "[+] Created $TargetDir" -ForegroundColor Green
}

# ── 3. Stop existing services ────────────────────────────────────────
Write-Host "`n[*] Stopping existing services ..." -ForegroundColor Cyan
Stop-ServiceSafe "TADBridgeService"

# ── 4. Deploy user-mode service ──────────────────────────────────────
if (-not $SkipService) {
    Write-Host "`n[*] Deploying TADBridgeService ..." -ForegroundColor Cyan

    $svcDir = Join-Path $TargetDir "Service"
    if (-not (Test-Path $svcDir)) {
        New-Item -ItemType Directory -Path $svcDir -Force | Out-Null
    }

    # Copy published binaries
    Copy-Item -Path "$ServicePath\*" -Destination $svcDir -Recurse -Force
    Write-Host "  Copied service binaries to $svcDir"

    $svcExe = Join-Path $svcDir "TADBridgeService.exe"
    if (-not (Test-Path $svcExe)) {
        throw "TADBridgeService.exe not found in publish output!"
    }

    # Register Windows service
    Remove-ServiceSafe "TADBridgeService"
    sc.exe create "TADBridgeService" `
        binPath="$svcExe" `
        start=auto `
        obj=LocalSystem `
        DisplayName="TAD.RV Bridge Service" | Out-Null

    sc.exe description "TADBridgeService" "Runs the TAD.RV user-mode protection service with Active Directory integration for school endpoint monitoring." | Out-Null
    sc.exe sidtype "TADBridgeService" unrestricted | Out-Null
    sc.exe failureflag "TADBridgeService" 1 | Out-Null
    sc.exe failure "TADBridgeService" reset=86400 actions=restart/5000/restart/10000/restart/30000 | Out-Null

    Write-Host "[+] TADBridgeService registered with auto-restart" -ForegroundColor Green

    sc.exe start "TADBridgeService" | Out-Null
    Write-Host "[+] TADBridgeService started" -ForegroundColor Green
}

# ── 5. Create registry provisioning key ──────────────────────────────
Write-Host "`n[*] Setting up registry ..." -ForegroundColor Cyan
$regPath = "HKLM:\SOFTWARE\TAD_RV"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name "InstallDir"     -Value $TargetDir
Set-ItemProperty -Path $regPath -Name "DomainController" -Value $DomainController
Set-ItemProperty -Path $regPath -Name "DeployedAt"     -Value (Get-Date -Format "o")
Write-Host "[+] Registry configured at $regPath" -ForegroundColor Green

# ── 6. Verify NETLOGON policy share ─────────────────────────────────
Write-Host "`n[*] Checking NETLOGON policy share ..." -ForegroundColor Cyan
$policyPath = "\\$DomainController\NETLOGON\TAD\Policy.json"
if (Test-Path $policyPath -ErrorAction SilentlyContinue) {
    Write-Host "[+] Policy.json found at $policyPath" -ForegroundColor Green
} else {
    Write-Host "[!] Policy.json NOT found at $policyPath" -ForegroundColor Yellow
    Write-Host "    The service will use default policy until the file is created." -ForegroundColor Yellow
    Write-Host "    Expected location: \\$DomainController\NETLOGON\TAD\Policy.json" -ForegroundColor Yellow
}

# ── 7. Summary ───────────────────────────────────────────────────────
Write-Host "`n╔══════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║        Deployment Complete                       ║" -ForegroundColor Green
Write-Host "╠══════════════════════════════════════════════════╣" -ForegroundColor Green
Write-Host "║  Service: $(if($SkipService){'SKIPPED'}else{'INSTALLED'})" -ForegroundColor Green
Write-Host "║  Target:  $TargetDir" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════╝`n" -ForegroundColor Green
