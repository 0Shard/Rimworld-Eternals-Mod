#!/bin/bash

# Relative Path: deploy-to-rimworld.sh
# Creation Date: 24-02-2026
# Last Edit: 24-02-2026
# Author: 0Shard
# Description: One-command build + package + deploy pipeline for the Eternal RimWorld mod.
#              Builds the solution in Release mode, packages it via create-mod-package.sh,
#              then copies the packaged Mods/Eternal/ folder into RimWorld's Mods directory.

# Exit immediately if a command exits with a non-zero status
set -e

# Define colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if script is being run from the project root
if [ ! -d "Eternal" ]; then
    print_error "This script must be run from the project root directory where the 'Eternal' folder exists."
    exit 1
fi

# Define RimWorld Mods path
RIMWORLD_MODS="/c/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods"

# Validate that the RimWorld Mods directory exists
if [ ! -d "$RIMWORLD_MODS" ]; then
    print_error "RimWorld Mods directory not found at: $RIMWORLD_MODS"
    print_error "Ensure RimWorld is installed via Steam at the default path."
    exit 1
fi

print_status "Starting full build + package + deploy pipeline..."
echo ""

# ---------------------------------------------------------------------------
# Step 1 — Build
# ---------------------------------------------------------------------------
print_status "Step 1/3: Building mod (Release)..."
dotnet build Eternal/Source/EternalSolution.sln -c Release
print_success "Step 1/3: Build complete."
echo ""

# ---------------------------------------------------------------------------
# Step 2 — Package
# ---------------------------------------------------------------------------
print_status "Step 2/3: Packaging mod..."
bash create-mod-package.sh
print_success "Step 2/3: Package complete."
echo ""

# ---------------------------------------------------------------------------
# Step 3 — Deploy
# ---------------------------------------------------------------------------
print_status "Step 3/3: Deploying to RimWorld..."

# Remove any previously deployed version to ensure a clean install
if [ -d "$RIMWORLD_MODS/Eternal" ]; then
    print_warning "Removing existing deployment at: $RIMWORLD_MODS/Eternal"
    rm -rf "$RIMWORLD_MODS/Eternal"
fi

# Copy the freshly packaged mod into the RimWorld Mods folder
cp -r Mods/Eternal "$RIMWORLD_MODS/"

# Verify the core DLL is present at the destination
if [ ! -f "$RIMWORLD_MODS/Eternal/1.6/Assemblies/Eternal.dll" ]; then
    print_error "Deployment verification failed: Eternal.dll not found at destination."
    print_error "Expected: $RIMWORLD_MODS/Eternal/1.6/Assemblies/Eternal.dll"
    exit 1
fi

print_success "Step 3/3: Deployment verified."
echo ""

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
print_success "Eternal mod deployed successfully!"
echo ""
echo "Deployed to: $RIMWORLD_MODS/Eternal/"
echo ""
print_warning "If RimWorld is currently running, restart it to pick up the new version."
print_status "Script completed successfully."
