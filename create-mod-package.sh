#!/bin/bash

# file path: create-mod-package.sh
# Author: 0Shard
# Date Created: 29-10-2025
# Date Last Modified: 13-10-2026
# Description: Creates a distributable RimWorld mod package from the Eternal project.
#              Output: Mods/Eternal/ - ready to copy directly to RimWorld's Mods folder.
#              Includes Textures folder for gizmo icons (RimWorld ContentFinder requirement).
#              Shares one compiled DLL between explicit 1.6 and 1.7 load folders.
#              Includes Patches folder for conditional XML PatchOperations (mod compatibility).
#              Automatically cleans up legacy 'Mod' directory if present.

# Exit immediately if a command exits with a non-zero status
set -euo pipefail

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
SUPPORTED_VERSIONS=(1.6 1.7)
ASSEMBLY_SOURCE_DIR="$SOURCE_DIR/1.6/Assemblies/net472"

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

# Validate inputs before replacing a previously built package.
for file in \
    "$SOURCE_DIR/About/About.xml" \
    "$SOURCE_DIR/About/PublishedFileId.txt" \
    "$SOURCE_DIR/LoadFolders.xml" \
    "$ASSEMBLY_SOURCE_DIR/Eternal.dll" \
    "$SOURCE_DIR/Textures/UI/Gizmos/Eternal_Resurrection.png"; do
    verify_file "$file"
done
for dir in "$SOURCE_DIR/Defs" "$SOURCE_DIR/Languages"; do
    verify_dir "$dir"
done

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
for version in "${SUPPORTED_VERSIONS[@]}"; do
    mkdir -p "$DEST_DIR/$version/Assemblies"
done

# Copy About.xml
print_status "Copying About.xml..."
if verify_file "$SOURCE_DIR/About/About.xml"; then
    cp "$SOURCE_DIR/About/About.xml" "$DEST_DIR/About/"
    print_success "Copied About.xml"
fi

# Explicit version mappings keep RimWorld from loading both copies of the DLL.
cp "$SOURCE_DIR/LoadFolders.xml" "$DEST_DIR/LoadFolders.xml"

# Copy Preview.png (Steam Workshop preview image)
if [ -f "$SOURCE_DIR/About/Preview.png" ]; then
    cp "$SOURCE_DIR/About/Preview.png" "$DEST_DIR/About/"
    print_success "Copied Preview.png"
else
    print_warning "No Preview.png found in About/ — Steam Workshop preview will be missing"
fi

# Copy PublishedFileId.txt (Steam Workshop item ID)
if [ -f "$SOURCE_DIR/About/PublishedFileId.txt" ]; then
    cp "$SOURCE_DIR/About/PublishedFileId.txt" "$DEST_DIR/About/"
    print_success "Copied PublishedFileId.txt"
else
    print_warning "No PublishedFileId.txt found in About/ — Steam Workshop item linking will not work"
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

# Copy XML PatchOperations (conditional mod-compatibility patches)
print_status "Copying XML patches from Patches directory..."
if [ -d "$SOURCE_DIR/Patches" ]; then
    find "$SOURCE_DIR/Patches" -name "*.xml" -type f | while read -r file; do
        rel_path=${file#$SOURCE_DIR/Patches/}
        dest_dir="$DEST_DIR/Patches/$(dirname "$rel_path")"
        mkdir -p "$dest_dir"
        cp "$file" "$dest_dir/"
        print_success "Copied: Patches/$rel_path"
    done
else
    print_warning "No Patches directory found — skipping XML patches"
fi

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

# Distribute only this mod's DLL; debugging symbols remain in the local build folder.
# Build folders can also contain game, Harmony or test DLLs.
print_status "Copying shared Eternal assembly for RimWorld 1.6 and 1.7..."
for version in "${SUPPORTED_VERSIONS[@]}"; do
    cp "$ASSEMBLY_SOURCE_DIR/Eternal.dll" "$DEST_DIR/$version/Assemblies/"
    print_success "Copied Eternal.dll for $version"
done

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
    "$DEST_DIR/1.7/Assemblies"
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
    "$DEST_DIR/About/PublishedFileId.txt"
    "$DEST_DIR/LoadFolders.xml"
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

# Check if required DLL files exist.
required_assemblies=(
    "$DEST_DIR/1.6/Assemblies/Eternal.dll"
    "$DEST_DIR/1.7/Assemblies/Eternal.dll"
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

# Both version folders must carry the same build, without dependencies.
cmp "$DEST_DIR/1.6/Assemblies/Eternal.dll" "$DEST_DIR/1.7/Assemblies/Eternal.dll"
for version in "${SUPPORTED_VERSIONS[@]}"; do
    while IFS= read -r -d '' file; do
        case "$(basename "$file")" in
            Eternal.dll) ;;
            *)
                print_error "Unexpected assembly output: $file"
                missing_files=$((missing_files + 1))
                ;;
        esac
    done < <(find "$DEST_DIR/$version/Assemblies" -type f -print0)
done

if [ "$missing_files" -gt 0 ]; then
    print_error "Mod package verification failed with $missing_files missing or unexpected files"
    exit 1
fi

# Count the total files copied
xml_count=$(find "$DEST_DIR" -name "*.xml" | wc -l)
dll_count=$(find "$DEST_DIR" -name "*.dll" | wc -l)
png_count=$(find "$DEST_DIR" -name "*.png" | wc -l)

# Final summary
print_success "Mod package created successfully!"
echo ""
echo "Package Summary:"
echo "  - XML files: $xml_count"
echo "  - DLL files (excluding Harmony): $dll_count"
echo "  - PNG image files: $png_count"
echo ""
echo "The mod package is ready for distribution at: Mods/Eternal/"
echo "Users can copy the 'Eternal' folder (inside Mods/) to their RimWorld Mods directory."
echo ""
echo "Note: This package excludes the Harmony DLL (0Harmony.dll)."
echo "Users must install Harmony separately as a prerequisite for this mod."

print_status "Script completed successfully."
