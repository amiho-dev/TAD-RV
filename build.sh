#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
echo ""

# ── Clean old artifacts ────────────────────────────────────────────────
echo "[0/5] Cleaning old build artifacts..."
for proj in Bootstrap Service Console Teacher; do
  rm -rf "$proj/bin" "$proj/obj"
done

# Clean results & publish
rm -rf results/*.exe results/*.pdb results/*.dll results/*.xml results/*.json \
       results/cs results/de results/es results/fr results/it results/ja \
       results/ko results/pl results/pt-BR results/ru results/tr \
       results/zh-Hans results/zh-Hant results/runtimes \
       publish/Console/* publish/Teacher/*
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

echo "=== Build Complete ==="
echo "Output: results/"
ls -lh results/*.exe 2>/dev/null || echo "(no .exe — cross-compiled DLLs in results/)"
