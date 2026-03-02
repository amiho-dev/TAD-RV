# ─────────────────────────────────────────────────────────────────────
# Start-Emulated.ps1 — Launch TAD.RV stack in emulation / demo mode
#
# Modes:
#   Default      — Stops real service, starts emulated bridge + Console + Teacher
#   -Demo        — No bridge service at all; Console & Teacher use built-in demo data
#
# Self-elevates to Administrator automatically (skipped in -Demo mode).
# ─────────────────────────────────────────────────────────────────────

param(
    [switch]$Demo
)

# ── Self-elevate if not running as admin (skip for demo-only) ─────
if (-not $Demo) {
    if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $args = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $MyInvocation.MyCommand.Definition
        if ($Demo) { $args += ' -Demo' }
        Start-Process powershell.exe -Verb RunAs -ArgumentList $args
        exit
    }
}

$ErrorActionPreference = 'SilentlyContinue'

# Resolve paths relative to script location (or same folder as exes)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$baseDir   = Split-Path -Parent $scriptDir   # repo root / results folder

# Try results/ first, then script's own folder, then parent
$exeDirs = @(
    (Join-Path $baseDir 'results'),
    $scriptDir,
    $baseDir
)

$exeDir = $null
foreach ($d in $exeDirs) {
    if (Test-Path (Join-Path $d 'TadBridgeService.exe')) {
        $exeDir = $d
        break
    }
}

if (-not $exeDir) {
    Write-Host '[ERROR] Cannot find TadBridgeService.exe. Place this script next to the exe files or in Scripts/.' -ForegroundColor Red
    Read-Host 'Press Enter to exit'
    exit 1
}

$modeLabel = if ($Demo) { 'Demo' } else { 'Emulated' }

Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host "  TAD.RV — $modeLabel Mode Launcher" -ForegroundColor Cyan
Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host ''
Write-Host "  Exe directory: $exeDir"
Write-Host ''

# ── Step 1: Stop existing service if running ──────────────────────
if ($Demo) {
    Write-Host '[1/3] Demo mode — skipping service management.' -ForegroundColor Gray
} else {
    Write-Host '[1/4] Stopping existing TadBridgeService...' -ForegroundColor Yellow
    $svc = Get-Service -Name TadBridgeService -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        Stop-Service TadBridgeService -Force
        Start-Sleep -Seconds 2
        Write-Host '       Service stopped.' -ForegroundColor Green
    } else {
        Write-Host '       Service not running (OK).' -ForegroundColor Gray
    }

    # Also kill any leftover processes
    Get-Process -Name TadBridgeService -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# ── Step 2: Launch Bridge Service in emulation mode ───────────────
$bridge = $null
if ($Demo) {
    Write-Host '[2/3] Demo mode — skipping bridge service.' -ForegroundColor Gray
} else {
    Write-Host '[2/4] Starting TadBridgeService --emulate...' -ForegroundColor Yellow
    $bridge = Start-Process -FilePath (Join-Path $exeDir 'TadBridgeService.exe') `
        -ArgumentList '--emulate' `
        -PassThru -WindowStyle Normal
    Write-Host "       PID: $($bridge.Id)" -ForegroundColor Green

    # Give the service a moment to bind the port
    Start-Sleep -Seconds 3
}

# ── Step 3: Launch Console ────────────────────────────────────────
$consoleStep = if ($Demo) { '2/3' } else { '3/4' }
$consolePath = Join-Path $exeDir 'TadConsole.exe'
if (Test-Path $consolePath) {
    $consoleArgs = if ($Demo) { '--demo' } else { $null }
    Write-Host "[$consoleStep] Launching TadConsole$(if ($Demo) {' --demo'})..." -ForegroundColor Yellow
    $console = Start-Process -FilePath $consolePath -ArgumentList $consoleArgs -PassThru
    Write-Host "       PID: $($console.Id)" -ForegroundColor Green
} else {
    Write-Host "[$consoleStep] TadConsole.exe not found — skipping." -ForegroundColor Gray
}

# ── Step 4: Launch Teacher ────────────────────────────────────────
$teacherStep = if ($Demo) { '3/3' } else { '4/4' }
$teacherPath = Join-Path $exeDir 'TadTeacher.exe'
if (Test-Path $teacherPath) {
    $teacherArgs = if ($Demo) { '--demo' } else { $null }
    Write-Host "[$teacherStep] Launching TadTeacher$(if ($Demo) {' --demo'})..." -ForegroundColor Yellow
    $teacher = Start-Process -FilePath $teacherPath -ArgumentList $teacherArgs -PassThru
    Write-Host "       PID: $($teacher.Id)" -ForegroundColor Green
} else {
    Write-Host "[$teacherStep] TadTeacher.exe not found — skipping." -ForegroundColor Gray
}

Write-Host ''
Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host "  All components launched in $modeLabel mode." -ForegroundColor Green
if ($Demo) {
    Write-Host '  No bridge service running — apps use built-in demo data.' -ForegroundColor Gray
} else {
    Write-Host '  Close this window or press Ctrl+C to stop the bridge.' -ForegroundColor Gray
}
Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host ''

if ($bridge) {
    # Wait for the bridge to exit (keeps the window open)
    try {
        $bridge.WaitForExit()
    } catch {}
    Write-Host 'Bridge service exited.' -ForegroundColor Yellow
} else {
    Write-Host 'Press Enter to exit...' -ForegroundColor Gray
    Read-Host
}
