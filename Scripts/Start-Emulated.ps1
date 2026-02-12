# ─────────────────────────────────────────────────────────────────────
# Start-Emulated.ps1 — Launch TAD.RV stack in emulation mode
#
# Stops any running TadBridgeService, starts the emulated bridge,
# then launches Console and Teacher. No kernel driver required.
#
# Self-elevates to Administrator automatically.
# ─────────────────────────────────────────────────────────────────────

# ── Self-elevate if not running as admin ──────────────────────────
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList (
        '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $MyInvocation.MyCommand.Definition
    )
    exit
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

Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host '  TAD.RV — Emulated Mode Launcher' -ForegroundColor Cyan
Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host ''
Write-Host "  Exe directory: $exeDir"
Write-Host ''

# ── Step 1: Stop existing service if running ──────────────────────
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

# ── Step 2: Launch Bridge Service in emulation mode ───────────────
Write-Host '[2/4] Starting TadBridgeService --emulate...' -ForegroundColor Yellow
$bridge = Start-Process -FilePath (Join-Path $exeDir 'TadBridgeService.exe') `
    -ArgumentList '--emulate' `
    -PassThru -WindowStyle Normal
Write-Host "       PID: $($bridge.Id)" -ForegroundColor Green

# Give the service a moment to bind the port
Start-Sleep -Seconds 3

# ── Step 3: Launch Console ────────────────────────────────────────
$consolePath = Join-Path $exeDir 'TadConsole.exe'
if (Test-Path $consolePath) {
    Write-Host '[3/4] Launching TadConsole...' -ForegroundColor Yellow
    $console = Start-Process -FilePath $consolePath -PassThru
    Write-Host "       PID: $($console.Id)" -ForegroundColor Green
} else {
    Write-Host '[3/4] TadConsole.exe not found — skipping.' -ForegroundColor Gray
}

# ── Step 4: Launch Teacher ────────────────────────────────────────
$teacherPath = Join-Path $exeDir 'TadTeacher.exe'
if (Test-Path $teacherPath) {
    Write-Host '[4/4] Launching TadTeacher...' -ForegroundColor Yellow
    $teacher = Start-Process -FilePath $teacherPath -PassThru
    Write-Host "       PID: $($teacher.Id)" -ForegroundColor Green
} else {
    Write-Host '[4/4] TadTeacher.exe not found — skipping.' -ForegroundColor Gray
}

Write-Host ''
Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host '  All components launched in emulation mode.' -ForegroundColor Green
Write-Host '  Close this window or press Ctrl+C to stop the bridge.' -ForegroundColor Gray
Write-Host '═══════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host ''

# Wait for the bridge to exit (keeps the window open)
try {
    $bridge.WaitForExit()
} catch {}

Write-Host 'Bridge service exited.' -ForegroundColor Yellow
