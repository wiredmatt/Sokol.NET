#!/bin/bash
# Build camerac for macOS – produces a universal (arm64 + x86_64) dylib.
# Individual arch slices are kept in libs/macos/{arm64,X64}/release/.
# The lipo-merged universal binary lands in libs/macos/universal/release/.
#
# Usage: ./build-camerac-macos.sh [build_type]
# Example: ./build-camerac-macos.sh Release

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CAMERAC_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

BUILD_TYPE="${1:-Release}"
BUILD_TYPE_LOWER=$(echo "$BUILD_TYPE" | tr '[:upper:]' '[:lower:]')

echo "=========================================="
echo "Building camerac for macOS (universal)"
echo "Build Type: $BUILD_TYPE"
echo "=========================================="

build_arch() {
    local ARCH="$1"
    local BUILD_DIR="$CAMERAC_DIR/build-xcode-macos-$ARCH"

    # All progress output goes to stderr so callers can capture the dylib path via stdout
    echo "" >&2
    echo "--- Building $ARCH ---" >&2
    rm -rf "$BUILD_DIR"
    mkdir -p "$BUILD_DIR"
    cd "$BUILD_DIR"

    cmake "$CAMERAC_DIR" \
        -G Xcode \
        -DCMAKE_OSX_ARCHITECTURES="$ARCH" \
        -DCMAKE_BUILD_TYPE="$BUILD_TYPE" \
        -DCMAKE_OSX_DEPLOYMENT_TARGET="11.0" \
        -DBUILD_SHARED_LIBS=ON \
        -DCAMERAC_BUILD_SAMPLE=OFF >&2

    cmake --build . --config "$BUILD_TYPE" --target camerac >&2

    # Only the dylib path is printed to stdout (what the caller captures)
    find "$BUILD_DIR" -path "*/$BUILD_TYPE/libcamerac*.dylib" -not -type l | head -n 1
}

ARM64_DYLIB=$(build_arch arm64)
X86_DYLIB=$(build_arch x86_64)

if [ -z "$ARM64_DYLIB" ] || [ -z "$X86_DYLIB" ]; then
    echo "✗ Could not locate built dylibs"
    exit 1
fi

echo ""
echo "--- Assembling universal binary ---"

ARM64_OUT="$CAMERAC_DIR/libs/macos/arm64/$BUILD_TYPE_LOWER"
X64_OUT="$CAMERAC_DIR/libs/macos/X64/$BUILD_TYPE_LOWER"
UNIVERSAL_OUT="$CAMERAC_DIR/libs/macos/universal/$BUILD_TYPE_LOWER"
mkdir -p "$ARM64_OUT" "$X64_OUT" "$UNIVERSAL_OUT"

cp "$ARM64_DYLIB" "$ARM64_OUT/libcamerac.dylib"
cp "$X86_DYLIB"   "$X64_OUT/libcamerac.dylib"

lipo -create "$ARM64_DYLIB" "$X86_DYLIB" -output "$UNIVERSAL_OUT/libcamerac.dylib"

echo "--- Fixing install names and re-signing ---"
for DYLIB in "$ARM64_OUT/libcamerac.dylib" "$X64_OUT/libcamerac.dylib" "$UNIVERSAL_OUT/libcamerac.dylib"; do
    install_name_tool -id "@loader_path/libcamerac.dylib" "$DYLIB"
    codesign --remove-signature "$DYLIB" 2>/dev/null || true
    TIMESTAMP=$(date +%s)
    codesign --force --sign - --identifier "libcamerac.${TIMESTAMP}" "$DYLIB"
done

echo ""
echo "=========================================="
echo "Build complete!"
echo "=========================================="
echo "✓ arm64:     $ARM64_OUT/libcamerac.dylib"
echo "✓ x86_64:    $X64_OUT/libcamerac.dylib"
echo "✓ universal: $UNIVERSAL_OUT/libcamerac.dylib"
echo ""
lipo -info "$UNIVERSAL_OUT/libcamerac.dylib"
