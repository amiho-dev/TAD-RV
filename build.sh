#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
echo ""

# ── Clean old artifacts ────────────────────────────────────────────────
echo "[0/5] Cleaning old build artifacts..."
# Remove previous publish dirs
rm -rf tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish src/Service/bin/Release/net8.0-windows/win-x64/publish src/Console/bin/Release/net8.0-windows/win-x64/publish src/Teacher/bin/Release/net8.0-windows/win-x64/publish
for proj in tools/Bootstrap src/Service src/Console src/Teacher; do
  rm -rf "$proj/bin" "$proj/obj" || true
done

# Clean results & release folders
rm -rf build/results/*.exe build/results/*.pdb build/results/*.dll build/results/*.xml build/results/*.json \
  build/results/cs build/results/de build/results/es build/results/fr build/results/it build/results/ja \
  build/results/ko build/results/pl build/results/pt-BR build/results/ru build/results/tr \
  build/results/zh-Hans build/results/zh-Hant build/results/runtimes \
  build/release-client/* build/release-teacher/* build/release-addc/*
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
echo "[2/5] Publishing TadBootstrap (Single File Exe)..."
dotnet publish tools/Bootstrap/TadBootstrap.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Service
echo "[3/5] Publishing TadBridgeService (Single File Exe)..."
dotnet publish src/Service/TadBridgeService.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Console (WPF)
echo "[4/5] Publishing TadConsole (Single File Exe)..."
dotnet publish src/Console/TadConsole.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Teacher (WPF + WebView2)
echo "[5/5] Publishing TadTeacher (Single File Exe)..."
dotnet publish src/Teacher/TadTeacher.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Clean results & release folders
rm -rf build/results/*.exe build/results/*.pdb build/results/*.dll build/results/*.xml build/results/*.json \
  build/results/cs build/results/de build/results/es build/results/fr build/results/it build/results/ja \
  build/results/ko build/results/pl build/results/pt-BR build/results/ru build/results/tr \
  build/results/zh-Hans build/results/zh-Hant build/results/runtimes \
  build/release-client/* build/release-teacher/* build/release-addc/*
echo "   Done."
echo ""

echo "[+] Publishing Release builds (Single File) to results/..."
# Copy the single file executables
cp tools/Bootstrap/bin/Release/net8.0-windows/win-x64/publish/TadBootstrap.exe build/results/
cp src/Service/bin/Release/net8.0-windows/win-x64/publish/TadBridgeService.exe build/results/
cp src/Console/bin/Release/net8.0-windows/win-x64/publish/TadConsole.exe build/results/
cp src/Teacher/bin/Release/net8.0-windows/win-x64/publish/TadTeacher.exe build/results/
# WebView2Loader.dll might still be needed if not fully embedded (it usually isn't for WebView2)
# Check if it was extracted alongside
if [ -f src/Teacher/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll ]; then
  cp src/Teacher/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll build/results/
fi

echo "[+] Publishing Console → release-client/..."
cp build/results/TadConsole.exe build/release-client/

echo "[+] Publishing Teacher → release-teacher/..."
cp build/results/TadTeacher.exe build/release-teacher/
if [ -f build/results/WebView2Loader.dll ]; then
  cp build/results/WebView2Loader.dll build/release-teacher/
fi

echo "[+] Publishing Service + Bootstrap → release-addc/..."
cp build/results/TadBridgeService.exe build/release-addc/
cp build/results/TadBootstrap.exe build/release-addc/

echo "[+] Creating installers (SFX)..."
# We just ZIP the single file exe for now as requested "installer" often means "distributable file"
# To make it truly ONE file, we should have self-contained exe. If WebView2Loader.dll is separate, we zipping it with exe is best.

# Read Release label from InformationalVersion (e.g. 26030.02NR-teacher)
VERSION=$(grep -oP '(?<=<InformationalVersion>)[^<]+' version-teacher.props | head -1 | sed 's/-teacher$//')

# Console
cd build/release-client && zip -r ../TadConsole-$VERSION-win-x64.zip * && cd ../..
# Teacher
cd build/release-teacher && zip -r ../TadTeacher-$VERSION-win-x64.zip * && cd ../..
# Service
cd build/release-addc && zip -r ../TadBridgeService-$VERSION-win-x64.zip * && cd ../..

echo "=== Build Complete ==="
echo "Artifacts ready for release:"
echo "  TadConsole-$VERSION-win-x64.zip"
echo "  TadTeacher-$VERSION-win-x64.zip"
echo "  TadBridgeService-$VERSION-win-x64.zip"
