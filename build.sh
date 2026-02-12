#!/bin/bash
set -e

echo "=== TAD-RV Solution Build (Linux cross-compile) ==="
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
dotnet build Bootstrap/TadBootstrap.csproj -c Debug --no-restore
echo ""

# Build Service
echo "[3/5] Building TadBridgeService..."
dotnet build Service/TadBridgeService.csproj -c Debug --no-restore
echo ""

# Build Console (WPF)
echo "[4/5] Building TadConsole (WPF)..."
dotnet build Console/TadConsole.csproj -c Debug --no-restore
echo ""

# Build Teacher (WPF + WebView2)
echo "[5/5] Building TadTeacher (WPF + WebView2)..."
dotnet build Teacher/TadTeacher.csproj -c Debug --no-restore
echo ""

echo "=== Build Complete ==="
