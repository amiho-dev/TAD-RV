#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
echo ""

# ── Clean old artifacts ────────────────────────────────────────────────
echo "[0/5] Cleaning old build artifacts..."
# Remove previous publish dirs
rm -rf tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish src/Service/bin/Release/net8.0-windows/win-x64/publish src/DomainController/bin/Release/net8.0-windows/win-x64/publish src/Admin/bin/Release/net8.0-windows/win-x64/publish tools/Setup/bin/Release/net8.0-windows/win-x64/publish
for proj in tools/Bootstrap tools/Setup src/Service src/DomainController src/Admin; do
  rm -rf "$proj/bin" "$proj/obj" || true
done

# Clean results & release folders
rm -rf build/results/*.exe build/results/*.pdb build/results/*.dll build/results/*.xml build/results/*.json \
  build/results/cs build/results/de build/results/es build/results/fr build/results/it build/results/ja \
  build/results/ko build/results/pl build/results/pt-BR build/results/ru build/results/tr \
  build/results/zh-Hans build/results/zh-Hant build/results/runtimes \
  build/release-client/* build/release-admin/* build/release-addc/*
echo "   Done."
echo ""

# Check SDK
echo "[INFO] .NET SDK version: $(dotnet --version)"
echo ""

# Restore all packages
echo "[1/5] Restoring NuGet packages..."
dotnet restore TAD-RV.sln -r win-x64
echo ""

# Build Bootstrap
echo "[2/6] Publishing TADBootstrap (Single File Exe)..."
dotnet publish tools/Bootstrap/TADBootstrap.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Setup installer
echo "[3/6] Publishing TADSetup (Client Installer EXE)..."
dotnet publish tools/Setup/TADSetup.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Service
echo "[4/6] Publishing TADBridgeService (Single File Exe)..."
dotnet publish src/Service/TADBridgeService.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Console (WPF)
echo "[5/6] Publishing TADDomainController (Single File Exe)..."
dotnet publish src/DomainController/TADDomainController.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Admin (WPF + WebView2)
echo "[6/6] Publishing TADAdmin (Single File Exe)..."
dotnet publish src/Admin/TADAdmin.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

echo "[+] Copying artifacts to results/ and release folders..."
# Copy the single file executables
cp tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish/TADBootstrap.exe build/results/
cp tools/Setup/bin/Release/net8.0-windows/win-x64/publish/TADSetup.exe build/results/
cp src/Service/bin/Release/net8.0-windows/win-x64/publish/TADBridgeService.exe build/results/
cp src/DomainController/bin/Release/net8.0-windows/win-x64/publish/TADDomainController.exe build/results/
cp src/Admin/bin/Release/net8.0-windows/win-x64/publish/TADAdmin.exe build/results/
# WebView2Loader.dll might still be needed if not fully embedded (it usually isn't for WebView2)
# Check if it was extracted alongside
if [ -f src/Admin/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll ]; then
  cp src/Admin/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll build/results/
fi

echo "[+] Publishing Console → release-client/..."
cp build/results/TADDomainController.exe build/release-client/

echo "[+] Publishing Admin → release-admin/..."
cp build/results/TADAdmin.exe build/release-admin/
if [ -f build/results/WebView2Loader.dll ]; then
  cp build/results/WebView2Loader.dll build/release-admin/
fi

echo "[+] Publishing Service + Bootstrap + Setup → release-addc/..."
cp build/results/TADBridgeService.exe build/release-addc/
cp build/results/TADBootstrap.exe build/release-addc/
cp build/results/TADSetup.exe build/release-addc/

echo "[+] Creating installers (SFX)..."
# We just ZIP the single file exe for now as requested "installer" often means "distributable file"
# To make it truly ONE file, we should have self-contained exe. If WebView2Loader.dll is separate, we zipping it with exe is best.

# Read Release label from InformationalVersion
VERSION=$(grep -oP '(?<=<InformationalVersion>)[^<]+' version-admin.props | head -1 | sed 's/-admin$//')

# Console
cd build/release-client && zip -r ../TADDomainController-$VERSION-win-x64.zip * && cd ../..
# Admin
cd build/release-admin && zip -r ../TADAdmin-$VERSION-win-x64.zip * && cd ../..
# Service
cd build/release-addc && zip -r ../TADBridgeService-$VERSION-win-x64.zip * && cd ../..

echo "=== Build Complete ==="
echo "Artifacts ready for release:"
echo "  TADDomainController-$VERSION-win-x64.zip"
echo "  TADAdmin-$VERSION-win-x64.zip"
echo "  TADBridgeService-$VERSION-win-x64.zip"
