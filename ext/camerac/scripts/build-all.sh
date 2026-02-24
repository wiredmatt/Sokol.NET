#!/bin/bash
# Master build script for camerac – builds for the current platform
# Usage: ./build-all.sh [build_type]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_TYPE="${1:-Release}"

echo "=========================================="
echo "Building camerac for all supported targets"
echo "Build Type: $BUILD_TYPE"
echo "=========================================="

PLATFORM="$(uname -s)"

case "$PLATFORM" in
    Darwin*)
        echo "Running on macOS"

        echo ""
        echo "── macOS (arm64) ──"
        "$SCRIPT_DIR/build-camerac-macos.sh" arm64 "$BUILD_TYPE"

        echo ""
        echo "── macOS (x86_64) ──"
        "$SCRIPT_DIR/build-camerac-macos.sh" x86_64 "$BUILD_TYPE"

        echo ""
        echo "── iOS (device) ──"
        "$SCRIPT_DIR/build-camerac-ios.sh" iphoneos "$BUILD_TYPE"

        echo ""
        echo "── iOS (simulator) ──"
        "$SCRIPT_DIR/build-camerac-ios.sh" iphonesimulator "$BUILD_TYPE"
        ;;

    Linux*)
        echo "Running on Linux"

        echo ""
        echo "── Linux (X64) ──"
        "$SCRIPT_DIR/build-camerac-linux.sh" "$BUILD_TYPE"
        ;;

    MINGW*|MSYS*|CYGWIN*)
        echo "Running on Windows (PowerShell)"

        echo ""
        echo "── Windows (x64) ──"
        pwsh "$SCRIPT_DIR/build-camerac-windows.ps1" \
            -Architecture x64 -BuildType "$BUILD_TYPE"
        ;;

    *)
        echo "Unknown platform: $PLATFORM"
        exit 1
        ;;
esac

echo ""
echo "=========================================="
echo "Build-all complete!"
echo "=========================================="
