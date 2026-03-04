#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
echo ""

# ── Clean old artifacts ────────────────────────────────────────────────
echo "[0/10] Cleaning old build artifacts..."
rm -rf tools/Bootstrap/bin tools/Bootstrap/obj \
       tools/Updater/bin tools/Updater/obj \
       tools/Setup/bin tools/Setup/obj tools/Setup/publish \
       src/Service/bin src/Service/obj \
       src/DomainController/bin src/DomainController/obj \
       src/Admin/bin src/Admin/obj

rm -rf build/results/*.exe build/results/*.pdb build/results/*.dll \
       build/results/*.xml build/results/*.json \
       build/results/cs build/results/de build/results/es build/results/fr \
       build/results/it build/results/ja build/results/ko build/results/pl \
       build/results/pt-BR build/results/ru build/results/tr \
       build/results/zh-Hans build/results/zh-Hant build/results/runtimes \
       build/release-addc/*

# Clean all staged installer resources
rm -f tools/Setup/Resources/TADBridgeService.exe \
      tools/Setup/Resources/TADAdmin.exe \
      tools/Setup/Resources/TADDomainController.exe \
      tools/Setup/Resources/TADUpdater.exe

mkdir -p build/results build/release-addc tools/Setup/Resources tools/Setup/publish

echo "   Done."
echo ""

# Check SDK
echo "[INFO] .NET SDK version: $(dotnet --version)"
echo ""

# ── [1/10] Restore ──────────────────────────────────────────────────────
echo "[1/10] Restoring NuGet packages..."
dotnet restore TAD-RV.sln -r win-x64
echo ""

# ── [2/10] Bootstrap ──────────────────────────────────────────────────
echo "[2/10] Publishing TADBootstrap (Single File Exe)..."
dotnet publish tools/Bootstrap/TADBootstrap.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# ── [3/10] Updater ────────────────────────────────────────────────────
echo "[3/10] Publishing TADUpdater (Single File Exe)..."
dotnet publish tools/Updater/TADUpdater.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# ── [4/10] Service ────────────────────────────────────────────────────
echo "[4/10] Publishing TADBridgeService (Single File Exe)..."
dotnet publish src/Service/TADBridgeService.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# ── [5/10] Admin ──────────────────────────────────────────────────────
echo "[5/10] Publishing TADAdmin (Single File Exe)..."
dotnet publish src/Admin/TADAdmin.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# ── [6/10] Domain Controller ──────────────────────────────────────────────
echo "[6/10] Publishing TADDomainController (Single File Exe)..."
dotnet publish src/DomainController/TADDomainController.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# ── Collect component binaries ─────────────────────────────────────────
SVC_BIN=src/Service/bin/Release/net8.0-windows/win-x64/publish/TADBridgeService.exe
ADM_BIN=src/Admin/bin/Release/net8.0-windows/win-x64/publish/TADAdmin.exe
DC_BIN=src/DomainController/bin/Release/net8.0-windows/win-x64/publish/TADDomainController.exe

cp "$SVC_BIN" build/results/TADBridgeService.exe
cp "$ADM_BIN" build/results/TADAdmin.exe
cp "$DC_BIN"  build/results/TADDomainController.exe
cp tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish/TADBootstrap.exe \
   build/results/TADBootstrap.exe

# Stage TADUpdater into Setup resources (same binary for all 3 Setup variants)
UPD_BIN=tools/Updater/bin/Release/net8.0-windows/win-x64/publish/TADUpdater.exe
cp "$UPD_BIN" tools/Setup/Resources/TADUpdater.exe
echo "   TADUpdater.exe staged  $(du -h tools/Setup/Resources/TADUpdater.exe | cut -f1)"
echo ""

# ── [7/10] TADClientSetup — bundles TADBridgeService ────────────────────────
echo "[7/10] Building TADClientSetup (WPF Installer)..."
cp "$SVC_BIN" tools/Setup/Resources/TADBridgeService.exe
dotnet publish tools/Setup/TADSetup.csproj -c Release -r win-x64 \
  -p:SetupTarget=Client \
  -p:AssemblyName=TADClientSetup \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false \
  -o tools/Setup/publish/client \
  --no-restore
echo "   TADClientSetup.exe  $(du -h tools/Setup/publish/client/TADClientSetup.exe | cut -f1)"
echo ""

# ── [8/10] TADAdminSetup — bundles TADAdmin ───────────────────────────────────
echo "[8/10] Building TADAdminSetup (WPF Installer)..."
rm -f tools/Setup/Resources/TADBridgeService.exe
cp "$ADM_BIN" tools/Setup/Resources/TADAdmin.exe
dotnet publish tools/Setup/TADSetup.csproj -c Release -r win-x64 \
  -p:SetupTarget=Admin \
  -p:AssemblyName=TADAdminSetup \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false \
  -o tools/Setup/publish/admin \
  --no-restore
echo "   TADAdminSetup.exe  $(du -h tools/Setup/publish/admin/TADAdminSetup.exe | cut -f1)"
echo ""

# ── [9/10] TADDomainControllerSetup — bundles TADDomainController ───────────────────
echo "[9/10] Building TADDomainControllerSetup (WPF Installer)..."
rm -f tools/Setup/Resources/TADAdmin.exe
cp "$DC_BIN" tools/Setup/Resources/TADDomainController.exe
dotnet publish tools/Setup/TADSetup.csproj -c Release -r win-x64 \
  -p:SetupTarget=DomainController \
  -p:AssemblyName=TADDomainControllerSetup \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false \
  -o tools/Setup/publish/dc \
  --no-restore
echo "   TADDomainControllerSetup.exe  $(du -h tools/Setup/publish/dc/TADDomainControllerSetup.exe | cut -f1)"
echo ""

# Clean staged resources
rm -f tools/Setup/Resources/TADDomainController.exe \
      tools/Setup/Resources/TADUpdater.exe

# ── [10/10] Package release artifacts ─────────────────────────────────────────────────
echo "[10/10] Packaging release artifacts..."

VERSION=$(grep -oP '(?<=<InformationalVersion>)[^<]+' version-client.props \
          | head -1 | sed 's/-client$//')

cp tools/Setup/publish/client/TADClientSetup.exe         build/results/TADClientSetup.exe
cp tools/Setup/publish/admin/TADAdminSetup.exe           build/results/TADAdminSetup.exe
cp tools/Setup/publish/dc/TADDomainControllerSetup.exe   build/results/TADDomainControllerSetup.exe

# Versioned release EXEs
cp build/results/TADClientSetup.exe           "build/TADClientSetup-$VERSION-win-x64.exe"
cp build/results/TADAdminSetup.exe            "build/TADAdminSetup-$VERSION-win-x64.exe"
cp build/results/TADDomainControllerSetup.exe "build/TADDomainControllerSetup-$VERSION-win-x64.exe"

# release-addc: Bootstrap + raw service binary + client setup (for GPO deployment)
cp build/results/TADBootstrap.exe     build/release-addc/
cp build/results/TADBridgeService.exe build/release-addc/
cp build/results/TADClientSetup.exe   build/release-addc/

echo ""
echo "=== Build Complete ==="
echo "Release artifacts:"
echo "  TADClientSetup-$VERSION-win-x64.exe"
echo "    Installs: TADBridgeService (Windows service, endpoint agent)"
echo "  TADAdminSetup-$VERSION-win-x64.exe"
echo "    Installs: TADAdmin (dashboard app + Start Menu shortcut)"
echo "  TADDomainControllerSetup-$VERSION-win-x64.exe"
echo "    Installs: TADDomainController (DC console + Start Menu shortcut)"
echo ""
echo "  All Setup EXEs embed TADUpdater.exe for background self-update."
