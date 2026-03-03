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
       build/release-client/* build/release-admin/* build/release-addc/*

# Clean embedded resource from previous build
rm -f tools/Setup/Resources/TADBridgeService.exe

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

# Build Service BEFORE Setup — Setup embeds the service binary
echo "[3/6] Publishing TADBridgeService (Single File Exe)..."
dotnet publish src/Service/TADBridgeService.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Copy service binary into Setup's Resources folder so it gets embedded
echo "[+] Embedding TADBridgeService.exe into setup installer..."
mkdir -p tools/Setup/Resources
cp src/Service/bin/Release/net8.0-windows/win-x64/publish/TADBridgeService.exe \
   tools/Setup/Resources/TADBridgeService.exe
echo "    Embedded: $(du -h tools/Setup/Resources/TADBridgeService.exe | cut -f1)"
echo ""

# Build Setup installer (now bundles TADBridgeService.exe as embedded resource)
echo "[4/6] Publishing TADClientSetup (Bundled Installer EXE)..."
dotnet publish tools/Setup/TADSetup.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Domain Controller (WPF)
echo "[5/6] Publishing TADDomainController (Single File Exe)..."
dotnet publish src/DomainController/TADDomainController.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Admin (WPF + WebView2)
echo "[6/6] Publishing TADAdmin (Single File Exe)..."
dotnet publish src/Admin/TADAdmin.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

echo "[+] Copying artifacts to results/ and release folders..."
cp tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish/TADBootstrap.exe \
   build/results/
cp tools/Setup/bin/Release/net8.0-windows/win-x64/publish/TADClientSetup.exe \
   build/results/
cp src/Service/bin/Release/net8.0-windows/win-x64/publish/TADBridgeService.exe \
   build/results/
cp src/DomainController/bin/Release/net8.0-windows/win-x64/publish/TADDomainController.exe \
   build/results/
cp src/Admin/bin/Release/net8.0-windows/win-x64/publish/TADAdmin.exe \
   build/results/

if [ -f src/Admin/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll ]; then
  cp src/Admin/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll \
     build/results/
fi

# Read version label
VERSION=$(grep -oP '(?<=<InformationalVersion>)[^<]+' version-admin.props \
          | head -1 | sed 's/-admin$//')

echo "[+] Publishing Console → release-client/..."
cp build/results/TADDomainController.exe build/release-client/

echo "[+] Publishing Admin → release-admin/..."
cp build/results/TADAdmin.exe build/release-admin/
if [ -f build/results/WebView2Loader.dll ]; then
  cp build/results/WebView2Loader.dll build/release-admin/
fi

# release-addc: Bootstrap (for GPO deployment) + the bundled client setup EXE
echo "[+] Publishing Bootstrap + TADClientSetup → release-addc/..."
cp build/results/TADBootstrap.exe    build/release-addc/
cp build/results/TADClientSetup.exe  build/release-addc/
# Also keep the raw service binary for GPO/Bootstrap-only deployments
cp build/results/TADBridgeService.exe build/release-addc/

echo "[+] Creating release archives..."
# Console
cd build/release-client && zip -r ../TADDomainController-$VERSION-win-x64.zip * && cd ../..
# Admin
cd build/release-admin && zip -r ../TADAdmin-$VERSION-win-x64.zip * && cd ../..

# Client: single bundled installer EXE — no zip needed
cp build/results/TADClientSetup.exe \
   "build/TADClientSetup-$VERSION-win-x64.exe"

echo ""
echo "=== Build Complete ==="
echo "Artifacts ready for release:"
echo "  TADDomainController-$VERSION-win-x64.zip"
echo "  TADAdmin-$VERSION-win-x64.zip"
echo "  TADClientSetup-$VERSION-win-x64.exe   (bundled installer — replaces client ZIP)"
