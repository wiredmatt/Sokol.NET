#!/bin/bash
# Build script for camerac library for Emscripten (WebAssembly)
# Usage: ./build-camerac-web.sh [build_type]
# Example: ./build-camerac-web.sh Release

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CAMERAC_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SOKOL_CSHARP_ROOT="$(cd "$CAMERAC_DIR/../.." && pwd)"

BUILD_TYPE="${1:-Release}"
EMSCRIPTEN_VERSION="3.1.34"

echo "=========================================="
echo "Building camerac for Web/Emscripten"
echo "Build Type: $BUILD_TYPE"
echo "Emscripten Version: $EMSCRIPTEN_VERSION"
echo "=========================================="

# Locate emsdk
EMSDK_PATH="$SOKOL_CSHARP_ROOT/tools/emsdk/emsdk"

if [ -f "$EMSDK_PATH" ]; then
    echo "Using local emsdk..."
    chmod +x "$EMSDK_PATH"
    "$EMSDK_PATH" install  "$EMSCRIPTEN_VERSION"
    "$EMSDK_PATH" activate "$EMSCRIPTEN_VERSION"
    source "$SOKOL_CSHARP_ROOT/tools/emsdk/emsdk_env.sh"
else
    echo "Local emsdk not found, using system Emscripten..."
    if ! command -v emcc &> /dev/null; then
        echo "Error: emcc not found. Install Emscripten."
        exit 1
    fi
fi

echo "Emscripten: $(emcc --version | head -n 1)"

BUILD_DIR="$CAMERAC_DIR/build-emscripten"
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

emcmake cmake .. \
    -DCMAKE_BUILD_TYPE="$BUILD_TYPE" \
    -DCAMERAC_BUILD_SAMPLE=OFF

emmake make camerac -j$(nproc)

echo "=========================================="
echo "Build complete!"
echo "=========================================="

BUILD_TYPE_LOWER=$(echo "$BUILD_TYPE" | tr '[:upper:]' '[:lower:]')
OUTPUT_DIR="$CAMERAC_DIR/libs/emscripten/x86/$BUILD_TYPE_LOWER"
mkdir -p "$OUTPUT_DIR"

LIB_PATH=$(find "$BUILD_DIR" -name "libcamerac.a" | head -n 1)
if [ -n "$LIB_PATH" ]; then
    cp "$LIB_PATH" "$OUTPUT_DIR/camerac.a"
    echo "✓ Copied to $OUTPUT_DIR/camerac.a"
else
    echo "✗ libcamerac.a not found"
    exit 1
fi
