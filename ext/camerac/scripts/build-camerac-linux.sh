#!/bin/bash
# Build script for camerac library on Linux (V4L2 backend)
# Usage: ./build-camerac-linux.sh [build_type]
# Example: ./build-camerac-linux.sh Release

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CAMERAC_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BUILD_DIR="$CAMERAC_DIR/build-linux"

BUILD_TYPE="${1:-Release}"

echo "=========================================="
echo "Building camerac for Linux (V4L2)"
echo "Build Type: $BUILD_TYPE"
echo "=========================================="

mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

cmake .. \
    -DCMAKE_BUILD_TYPE="$BUILD_TYPE" \
    -DBUILD_SHARED_LIBS=ON \
    -DCAMERAC_BUILD_SAMPLE=OFF

cmake --build . --config "$BUILD_TYPE" --target camerac -- -j$(nproc)

echo "=========================================="
echo "Build complete!"
echo "=========================================="

BUILD_TYPE_LOWER=$(echo "$BUILD_TYPE" | tr '[:upper:]' '[:lower:]')
OUTPUT_DIR="$CAMERAC_DIR/libs/linux/X64/$BUILD_TYPE_LOWER"
mkdir -p "$OUTPUT_DIR"

SO_PATH=$(find "$BUILD_DIR" -name "libcamerac.so" | head -n 1)
if [ -n "$SO_PATH" ]; then
    cp "$SO_PATH" "$OUTPUT_DIR/libcamerac.so"
    echo "✓ Copied to $OUTPUT_DIR/libcamerac.so"
    file "$OUTPUT_DIR/libcamerac.so"
else
    echo "✗ libcamerac.so not found"
    exit 1
fi
