#!/bin/bash
# Build camerac for all Android ABIs
# Usage: ./build-camerac-android-all.sh [build_type]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_TYPE="${1:-Release}"

echo "=========================================="
echo "Building camerac for all Android ABIs"
echo "Build Type: $BUILD_TYPE"
echo "=========================================="

ABIS=("arm64-v8a" "armeabi-v7a" "x86_64")

for ABI in "${ABIS[@]}"; do
    echo ""
    echo "Building for $ABI..."
    "$SCRIPT_DIR/build-camerac-android.sh" "$ABI" "$BUILD_TYPE"
done

echo ""
echo "=========================================="
echo "All Android builds complete!"
echo "=========================================="
