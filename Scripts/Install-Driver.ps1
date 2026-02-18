<#
.SYNOPSIS
    One-click TAD.RV driver installation.

.DESCRIPTION
    Installs the TAD.RV kernel driver using the simplest method available.
    Prefers pnputil (INF-based) with automatic fallback to sc.exe.

    Run this once on each student PC. The TadBridgeService will
    auto-detect and connect to the driver at startup.

.EXAMPLE
    # Right-click → "Run with PowerShell" or:
    powershell -ExecutionPolicy Bypass -File Install-Driver.ps1
#>

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

Write-Host ""
Write-Host "  TAD.RV — Kernel Driver Installer" -ForegroundColor Cyan
Write-Host "  (C) 2026 TAD Europe — https://tad-it.eu" -ForegroundColor DarkGray
Write-Host ""

# ── Locate files ────────────────────────────────────────────────────
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$kernelDir = Join-Path $scriptDir "..\Kernel"

$infPath = $null
$sysPath = $null

foreach ($dir in @($scriptDir, $kernelDir, "$scriptDir\Kernel")) {
    $inf = Join-Path $dir "TAD_RV.inf"
    $sys = Join-Path $dir "TAD.RV.sys"
    if (Test-Path $inf) { $infPath = $inf }
    if (Test-Path $sys) { $sysPath = $sys }
}

# ── Check if already installed ──────────────────────────────────────
$existing = sc.exe query "TAD.RV" 2>$null
if ($LASTEXITCODE -eq 0 -and $existing -match "RUNNING") {
    Write-Host "[OK] TAD.RV driver is already installed and running." -ForegroundColor Green
    Write-Host ""
    pause
    exit 0
}

# ── Strategy 1: pnputil + INF ──────────────────────────────────────
if ($infPath) {
    Write-Host "[*] Installing via pnputil ..." -ForegroundColor Yellow
    Write-Host "    INF: $infPath"
    pnputil /add-driver "$infPath" /install
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[OK] Driver installed via pnputil." -ForegroundColor Green
        sc.exe start "TAD.RV" 2>$null | Out-Null
        Write-Host "[OK] Driver started." -ForegroundColor Green
        Write-Host ""
        pause
        exit 0
    }
    Write-Host "[!] pnputil failed — trying fallback ..." -ForegroundColor Yellow
}

# ── Strategy 2: sc.exe create ──────────────────────────────────────
if ($sysPath) {
    Write-Host "[*] Installing via sc.exe ..." -ForegroundColor Yellow
    $dest = "$env:SystemRoot\system32\drivers\TAD.RV.sys"
    Copy-Item -Path $sysPath -Destination $dest -Force
    Write-Host "    Copied to $dest"

    sc.exe create "TAD.RV" type=kernel binPath="$dest" start=auto | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[OK] Driver registered." -ForegroundColor Green
        sc.exe start "TAD.RV" 2>$null | Out-Null
        Write-Host "[OK] Driver started." -ForegroundColor Green
        Write-Host ""
        pause
        exit 0
    }
}

# ── Neither method worked ──────────────────────────────────────────
Write-Host ""
Write-Host "[ERROR] Could not install driver." -ForegroundColor Red
Write-Host "  Place TAD_RV.inf and TAD.RV.sys in the same folder as this script," -ForegroundColor Red
Write-Host "  or in a Kernel\ subfolder, then run again as Administrator." -ForegroundColor Red
Write-Host ""
pause
exit 1
