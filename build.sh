#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
echo ""

# ── Clean old artifacts ────────────────────────────────────────────────
echo "[0/5] Cleaning old build artifacts..."
# Remove previous publish dirs
rm -rf Bootstrap/bin/Release/net8.0-windows/win-x64/publish Service/bin/Release/net8.0-windows/win-x64/publish Console/bin/Release/net8.0-windows/win-x64/publish Teacher/bin/Release/net8.0-windows/win-x64/publish
for proj in Bootstrap Service Console Teacher; do
  rm -rf "$proj/bin" "$proj/obj" || true
done

# Clean results & release folders
rm -rf results/*.exe results/*.pdb results/*.dll results/*.xml results/*.json \
       results/cs results/de results/es results/fr results/it results/ja \
       results/ko results/pl results/pt-BR results/ru results/tr \
       results/zh-Hans results/zh-Hant results/runtimes \
       release-client/* release-teacher/* release-addc/*
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
dotnet publish Bootstrap/TadBootstrap.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Service
echo "[3/5] Publishing TadBridgeService (Single File Exe)..."
dotnet publish Service/TadBridgeService.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Console (WPF)
echo "[4/5] Publishing TadConsole (Single File Exe)..."
dotnet publish Console/TadConsole.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Build Teacher (WPF + WebView2)
echo "[5/5] Publishing TadTeacher (Single File Exe)..."
dotnet publish Teacher/TadTeacher.csproj -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false --no-restore
echo ""

# Clean results & release folders
rm -rf results/*.exe results/*.pdb results/*.dll results/*.xml results/*.json \
       results/cs results/de results/es results/fr results/it results/ja \
       results/ko results/pl results/pt-BR results/ru results/tr \
       results/zh-Hans results/zh-Hant results/runtimes \
       release-client/* release-teacher/* release-addc/*
echo "   Done."
echo ""

echo "[+] Publishing Release builds (Single File) to results/..."
# Copy the single file executables
cp Bootstrap/bin/Release/net8.0-windows/win-x64/publish/TadBootstrap.exe results/
cp Service/bin/Release/net8.0-windows/win-x64/publish/TadBridgeService.exe results/
cp Console/bin/Release/net8.0-windows/win-x64/publish/TadConsole.exe results/
cp Teacher/bin/Release/net8.0-windows/win-x64/publish/TadTeacher.exe results/
# WebView2Loader.dll might still be needed if not fully embedded (it usually isn't for WebView2)
# Check if it was extracted alongside
if [ -f Teacher/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll ]; then
    cp Teacher/bin/Release/net8.0-windows/win-x64/publish/WebView2Loader.dll results/
fi

echo "[+] Publishing Console → release-client/..."
cp results/TadConsole.exe release-client/

echo "[+] Publishing Teacher → release-teacher/..."
cp results/TadTeacher.exe release-teacher/
if [ -f results/WebView2Loader.dll ]; then
    cp results/WebView2Loader.dll release-teacher/
fi

echo "[+] Publishing Service + Bootstrap → release-addc/..."
cp results/TadBridgeService.exe release-addc/
cp results/TadBootstrap.exe release-addc/

echo "[+] Creating installers (SFX)..."
# We just ZIP the single file exe for now as requested "installer" often means "distributable file"
# To make it truly ONE file, we should have self-contained exe. If WebView2Loader.dll is separate, we zipping it with exe is best.

# Read Version
VERSION=$(grep -oP '(?<=<Version>)[^<]+' version-teacher.props | head -1)

# Console
cd release-client && zip -r ../TadConsole-$VERSION-win-x64.zip * && cd ..
# Teacher
cd release-teacher && zip -r ../TadTeacher-$VERSION-win-x64.zip * && cd ..
# Service
cd release-addc && zip -r ../TadBridgeService-$VERSION-win-x64.zip * && cd ..

echo "=== Build Complete ==="
echo "Artifacts ready for release:"
echo "  TadConsole-$VERSION-win-x64.zip"
echo "  TadTeacher-$VERSION-win-x64.zip"
echo "  TadBridgeService-$VERSION-win-x64.zip"
