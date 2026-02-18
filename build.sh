#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
echo ""

# ── Clean old artifacts ────────────────────────────────────────────────
echo "[0/5] Cleaning old build artifacts..."
for proj in Bootstrap Service Console Teacher; do
  rm -rf "$proj/bin" "$proj/obj"
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
dotnet restore TAD-RV.sln
echo ""

# Build Bootstrap
echo "[2/5] Building TadBootstrap..."
dotnet build Bootstrap/TadBootstrap.csproj -c Release --no-restore
echo ""

# Build Service
echo "[3/5] Building TadBridgeService..."
dotnet build Service/TadBridgeService.csproj -c Release --no-restore
echo ""

# Build Console (WPF)
echo "[4/5] Building TadConsole (WPF)..."
dotnet build Console/TadConsole.csproj -c Release --no-restore
echo ""

# Build Teacher (WPF + WebView2)
echo "[5/5] Building TadTeacher (WPF + WebView2)..."
dotnet build Teacher/TadTeacher.csproj -c Release --no-restore
echo ""

# ── Publish all projects to results/ ──────────────────────────────────
echo "[+] Publishing Release builds to results/..."
mkdir -p results

dotnet publish Bootstrap/TadBootstrap.csproj   -c Release --no-restore --no-build -o results/
dotnet publish Service/TadBridgeService.csproj  -c Release --no-restore --no-build -o results/
dotnet publish Console/TadConsole.csproj        -c Release --no-restore --no-build -o results/
dotnet publish Teacher/TadTeacher.csproj        -c Release --no-restore --no-build -o results/
echo ""

# ── Publish to release folders ────────────────────────────────────────
echo "[+] Publishing Console → release-client/..."
mkdir -p release-client
dotnet publish Console/TadConsole.csproj -c Release --no-restore --no-build -o release-client/
echo ""

echo "[+] Publishing Teacher → release-teacher/..."
mkdir -p release-teacher
dotnet publish Teacher/TadTeacher.csproj -c Release --no-restore --no-build -o release-teacher/
echo ""

echo "[+] Publishing Service + Bootstrap → release-addc/..."
mkdir -p release-addc
dotnet publish Service/TadBridgeService.csproj -c Release --no-restore --no-build -o release-addc/
dotnet publish Bootstrap/TadBootstrap.csproj   -c Release --no-restore --no-build -o release-addc/
echo ""

# ── Publish self-contained demo builds ────────────────────────────────
echo "[+] Publishing self-contained demo builds..."
mkdir -p demo_compiled/client demo_compiled/teacher
dotnet publish Service/TadBridgeService.csproj -c Release -r win-x64 --self-contained true -o demo_compiled/client/
dotnet publish Teacher/TadTeacher.csproj       -c Release -r win-x64 --self-contained true -o demo_compiled/teacher/
echo ""

echo "=== Build Complete ==="
echo "Combined output:         results/"
echo "Console (Admin):         release-client/"
echo "Teacher (Classroom):     release-teacher/"
echo "AD DC (Service+Boot):    release-addc/"
echo "Demo (self-contained):   demo_compiled/client/  demo_compiled/teacher/"
echo ""
echo "── Demo Usage ──────────────────────────────────────────────"
echo "  Network demo:  client/Start-Client-Demo.bat  +  teacher/Start-Teacher-Demo.bat"
echo "  Offline demo:  teacher/Start-Offline-Demo.bat"
echo "  (No .NET runtime, kernel driver, or domain controller required)"
echo ""
ls -lh results/*.exe 2>/dev/null || echo "(no .exe — cross-compiled DLLs in results/)"
