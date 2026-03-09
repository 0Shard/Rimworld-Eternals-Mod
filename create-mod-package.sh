#!/bin/bash

# file path: create-mod-package.sh
# Author Name: 0Shard
# Date Created: 29-10-2025
# Date Last Modified: 08-01-2026
# Description: Creates a distributable RimWorld mod package from the Eternal project.
#              Output: Mods/Eternal/ - ready to copy directly to RimWorld's Mods folder.
#              Includes Textures folder for gizmo icons (RimWorld ContentFinder requirement).
#              Automatically cleans up legacy 'Mod' directory if present.

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

# Define paths
SOURCE_DIR="Eternal"
DEST_DIR="Mods/Eternal"
TEMP_DIR=$(mktemp -d)

# Function to verify file exists
verify_file() {
    if [ ! -f "$1" ]; then
        print_error "Required file not found: $1"
        return 1
    fi
    return 0
}

# Function to verify directory exists
verify_dir() {
    if [ ! -d "$1" ]; then
        print_error "Required directory not found: $1"
        return 1
    fi
    return 0
}

# Start of script
print_status "Starting creation of distributable mod package..."

# Clean up legacy Mod directory if it exists
if [ -d "Mod" ]; then
    print_warning "Found legacy 'Mod' directory, removing..."
    rm -rf "Mod"
fi

# Clean up existing Mods/Eternal directory (update/overwrite)
if [ -d "$DEST_DIR" ]; then
    print_warning "Removing existing Mods/Eternal directory for clean rebuild..."
    rm -rf "$DEST_DIR"
fi

# Create the Mods directory structure
print_status "Creating Mods/Eternal directory structure..."
mkdir -p "$DEST_DIR/About"
mkdir -p "$DEST_DIR/Defs"
mkdir -p "$DEST_DIR/Languages"
mkdir -p "$DEST_DIR/1.6/Assemblies"

# Copy About.xml
print_status "Copying About.xml..."
if verify_file "$SOURCE_DIR/About/About.xml"; then
    cp "$SOURCE_DIR/About/About.xml" "$DEST_DIR/About/"
    print_success "Copied About.xml"
fi

# Copy Preview.png (Steam Workshop preview image)
if [ -f "$SOURCE_DIR/About/Preview.png" ]; then
    cp "$SOURCE_DIR/About/Preview.png" "$DEST_DIR/About/"
    print_success "Copied Preview.png"
else
    print_warning "No Preview.png found in About/ — Steam Workshop preview will be missing"
fi

# Copy all XML files from Defs directory
print_status "Copying XML definitions from Defs directory..."
find "$SOURCE_DIR/Defs" -name "*.xml" -type f | while read -r file; do
    # Get the relative path from the Defs directory
    rel_path=${file#$SOURCE_DIR/Defs/}
    # Create the destination directory if it doesn't exist
    dest_dir="$DEST_DIR/Defs/$(dirname "$rel_path")"
    mkdir -p "$dest_dir"
    # Copy the file
    cp "$file" "$dest_dir/"
    print_success "Copied: $rel_path"
done

# Copy all language files
print_status "Copying language files..."
find "$SOURCE_DIR/Languages" -name "*.xml" -type f | while read -r file; do
    # Get the relative path from the Languages directory
    rel_path=${file#$SOURCE_DIR/Languages/}
    # Create the destination directory if it doesn't exist
    dest_dir="$DEST_DIR/Languages/$(dirname "$rel_path")"
    mkdir -p "$dest_dir"
    # Copy the file
    cp "$file" "$dest_dir/"
    print_success "Copied: Languages/$rel_path"
done

# Copy compiled DLLs and PDBs (excluding Harmony DLL)
print_status "Copying compiled assemblies (excluding Harmony DLL)..."
if verify_dir "$SOURCE_DIR/1.6/Assemblies/net472"; then
    find "$SOURCE_DIR/1.6/Assemblies/net472" -name "*.dll" -o -name "*.pdb" | while read -r file; do
        # Get just the filename
        filename=$(basename "$file")
        # Skip Harmony DLL as users should have their own Harmony installation
        if [ "$filename" = "0Harmony.dll" ]; then
            print_warning "Excluding Harmony DLL: $filename (users should install Harmony separately)"
            continue
        fi
        # Copy the file
        cp "$file" "$DEST_DIR/1.6/Assemblies/"
        print_success "Copied: $filename"
    done
else
    print_error "Assemblies directory not found at $SOURCE_DIR/1.6/Assemblies/net472"
    print_error "Please ensure the project has been built before running this script."
    exit 1
fi

# Copy Textures folder (contains gizmo icons)
print_status "Copying Textures folder..."
if verify_dir "$SOURCE_DIR/Textures"; then
    cp -r "$SOURCE_DIR/Textures" "$DEST_DIR/"
    texture_count=$(find "$SOURCE_DIR/Textures" -name "*.png" | wc -l)
    print_success "Copied Textures folder ($texture_count texture files)"
else
    print_error "Textures directory not found at $SOURCE_DIR/Textures"
    print_error "Gizmo icons will not be available in the mod package."
    exit 1
fi

# Verification step
print_status "Verifying the mod package structure..."

# Check if all required directories exist
required_dirs=(
    "$DEST_DIR/About"
    "$DEST_DIR/Defs"
    "$DEST_DIR/Languages"
    "$DEST_DIR/1.6/Assemblies"
    "$DEST_DIR/Textures"
)

for dir in "${required_dirs[@]}"; do
    if verify_dir "$dir"; then
        print_success "Verified directory: $dir"
    else
        print_error "Missing directory: $dir"
        exit 1
    fi
done

# Check if all required files exist
required_files=(
    "$DEST_DIR/About/About.xml"
    "$DEST_DIR/Textures/UI/Gizmos/Eternal_Resurrection.png"
)

for file in "${required_files[@]}"; do
    if verify_file "$file"; then
        print_success "Verified file: $file"
    else
        print_error "Missing file: $file"
        exit 1
    fi
done

# Check if required DLL files exist (PDB is optional — Release builds omit it)
required_assemblies=(
    "$DEST_DIR/1.6/Assemblies/Eternal.dll"
)

missing_files=0
for file in "${required_assemblies[@]}"; do
    if verify_file "$file"; then
        print_success "Verified file: $(basename "$file")"
    else
        print_error "Missing required file: $(basename "$file")"
        missing_files=$((missing_files + 1))
    fi
done

# Check if Harmony DLL is correctly excluded
if [ -f "$DEST_DIR/1.6/Assemblies/0Harmony.dll" ]; then
    print_warning "Harmony DLL was not excluded as expected"
    missing_files=$((missing_files + 1))
else
    print_success "Harmony DLL correctly excluded from distribution"
fi

if [ "$missing_files" -gt 0 ]; then
    print_error "Mod package verification failed with $missing_files missing or unexpected files"
    exit 1
fi

# Count the total files copied
xml_count=$(find "$DEST_DIR" -name "*.xml" | wc -l)
dll_count=$(find "$DEST_DIR" -name "*.dll" | wc -l)
pdb_count=$(find "$DEST_DIR" -name "*.pdb" | wc -l)
png_count=$(find "$DEST_DIR" -name "*.png" | wc -l)

# Final summary
print_success "Mod package created successfully!"
echo ""
echo "Package Summary:"
echo "  - XML files: $xml_count"
echo "  - DLL files (excluding Harmony): $dll_count"
echo "  - PDB files: $pdb_count"
echo "  - PNG image files: $png_count"
echo ""
echo "The mod package is ready for distribution at: Mods/Eternal/"
echo "Users can copy the 'Eternal' folder (inside Mods/) to their RimWorld Mods directory."
echo ""
echo "Note: This package excludes the Harmony DLL (0Harmony.dll)."
echo "Users must install Harmony separately as a prerequisite for this mod."

# Clean up temporary directory
rm -rf "$TEMP_DIR"

print_status "Script completed successfully."