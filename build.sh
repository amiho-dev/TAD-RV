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
echo "[2/5] Building TADBootstrap..."
dotnet build tools/Bootstrap/TADBootstrap.csproj -c Debug --no-restore
echo ""

# Build Service
echo "[3/5] Building TADBridgeService..."
dotnet build src/Service/TADBridgeService.csproj -c Debug --no-restore
echo ""

# Build Console (WPF)
echo "[4/5] Building TADAdmin (WPF)..."
dotnet build src/Admin/TADAdmin.csproj -c Debug --no-restore
echo ""

# Build Teacher (WPF + WebView2)
echo "[5/5] Building TADDomainController (WPF + WebView2)..."
dotnet build src/DomainController/TADDomainController.csproj -c Debug --no-restore
echo ""

echo "=== Build Complete ==="
