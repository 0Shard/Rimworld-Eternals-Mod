#!/bin/bash

echo "========================================"
echo "Eternal Mod Build Script"
echo "========================================"
echo

echo "[1/4] Cleaning project..."
dotnet clean Eternal.csproj
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to clean project"
    exit 1
fi
echo "Project cleaned successfully."
echo

echo "[2/4] Clearing NuGet cache..."
dotnet nuget locals all --clear
if [ $? -ne 0 ]; then
    echo "WARNING: Failed to clear NuGet cache, continuing..."
fi
echo "NuGet cache cleared."
echo

echo "[3/4] Restoring packages..."
dotnet restore Eternal.csproj
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to restore packages"
    exit 1
fi
echo "Packages restored successfully."
echo

echo "[4/4] Building project..."
dotnet build Eternal.csproj --configuration Release
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to build project"
    exit 1
fi
echo "Project built successfully."
echo

echo "========================================"
echo "Build completed successfully!"
echo "Output directory: ../1.6/Assemblies/"
echo "========================================"