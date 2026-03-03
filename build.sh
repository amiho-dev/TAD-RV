#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
echo ""

# ── Clean old artifacts ────────────────────────────────────────────────
echo "[0/6] Cleaning old build artifacts..."
rm -rf tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish \
       src/Service/bin/Release/net8.0-windows/win-x64/publish \
       src/DomainController/bin/Release/net8.0-windows/win-x64/publish \
       src/Admin/bin/Release/net8.0-windows/win-x64/publish \
       tools/Setup/bin/Release/net8.0-windows/win-x64/publish

for proj in tools/Bootstrap tools/Setup src/Service src/DomainController src/Admin; do
  rm -rf "$proj/bin" "$proj/obj" || true
done

rm -rf build/results/*.exe build/results/*.pdb build/results/*.dll \
       build/results/*.xml build/results/*.json \
       build/results/cs build/results/de build/results/es build/results/fr \
       build/results/it build/results/ja build/results/ko build/results/pl \
       build/results/pt-BR build/results/ru build/results/tr \
       build/results/zh-Hans build/results/zh-Hant build/results/runtimes \
       build/release-addc/*

# Clean all embedded resources from previous build
rm -f tools/Setup/Resources/TADBridgeService.exe \
      tools/Setup/Resources/TADAdmin.exe \
      tools/Setup/Resources/TADDomainController.exe

echo "   Done."
echo ""

# Check SDK
echo "[INFO] .NET SDK version: $(dotnet --version)"
echo ""

# Restore all packages
echo "[1/6] Restoring NuGet packages..."
dotnet restore TAD-RV.sln -r win-x64
echo ""

# Build Bootstrap
echo "[2/6] Publishing TADBootstrap (Single File Exe)..."
dotnet publish tools/Bootstrap/TADBootstrap.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Service — must be built before Setup so it can be embedded
echo "[3/6] Publishing TADBridgeService (Single File Exe)..."
dotnet publish src/Service/TADBridgeService.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Admin (WPF + WebView2) — must be built before Setup so it can be embedded
echo "[4/6] Publishing TADAdmin (Single File Exe)..."
dotnet publish src/Admin/TADAdmin.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Domain Controller (WPF) — must be built before Setup so it can be embedded
echo "[5/6] Publishing TADDomainController (Single File Exe)..."
dotnet publish src/DomainController/TADDomainController.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Embed all three component binaries into Setup's Resources folder
echo "[+] Staging all component binaries for embedding into unified setup..."
mkdir -p tools/Setup/Resources
cp src/Service/bin/Release/net8.0-windows/win-x64/publish/TADBridgeService.exe \
   tools/Setup/Resources/TADBridgeService.exe
cp src/Admin/bin/Release/net8.0-windows/win-x64/publish/TADAdmin.exe \
   tools/Setup/Resources/TADAdmin.exe
cp src/DomainController/bin/Release/net8.0-windows/win-x64/publish/TADDomainController.exe \
   tools/Setup/Resources/TADDomainController.exe
echo "    TADBridgeService.exe   $(du -h tools/Setup/Resources/TADBridgeService.exe    | cut -f1)"
echo "    TADAdmin.exe           $(du -h tools/Setup/Resources/TADAdmin.exe            | cut -f1)"
echo "    TADDomainController.exe $(du -h tools/Setup/Resources/TADDomainController.exe | cut -f1)"
echo ""

# Build unified Setup installer (bundles all three components as embedded resources)
echo "[6/6] Publishing TADSetup (Unified Bundled Installer EXE)..."
dotnet publish tools/Setup/TADSetup.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

echo "[+] Copying artifacts to results/ and release-addc/..."
mkdir -p build/results build/release-addc

cp tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish/TADBootstrap.exe \
   build/results/
cp tools/Setup/bin/Release/net8.0-windows/win-x64/publish/TADSetup.exe \
   build/results/
cp src/Service/bin/Release/net8.0-windows/win-x64/publish/TADBridgeService.exe \
   build/results/
cp src/DomainController/bin/Release/net8.0-windows/win-x64/publish/TADDomainController.exe \
   build/results/
cp src/Admin/bin/Release/net8.0-windows/win-x64/publish/TADAdmin.exe \
   build/results/

# Read version label
VERSION=$(grep -oP '(?<=<InformationalVersion>)[^<]+' version-admin.props \
          | head -1 | sed 's/-admin$//')

# release-addc: Bootstrap (for GPO/silent deployment) + raw service binary + unified setup
cp build/results/TADBootstrap.exe      build/release-addc/
cp build/results/TADBridgeService.exe  build/release-addc/
cp build/results/TADSetup.exe          build/release-addc/

# Unified installer — single EXE for the release, no separate ZIPs needed
cp build/results/TADSetup.exe \
   "build/TADSetup-$VERSION-win-x64.exe"

echo ""
echo "=== Build Complete ==="
echo "Artifact ready for release:"
echo "  TADSetup-$VERSION-win-x64.exe"
echo "    Contains: TADBridgeService + TADAdmin + TADDomainController"
echo "    Usage: Run as Administrator → installs all components + Start Menu shortcuts"
