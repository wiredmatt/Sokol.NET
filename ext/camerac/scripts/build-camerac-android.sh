#!/bin/bash
# Build script for camerac library for Android (Camera2 NDK)
# Usage: ./build-camerac-android.sh [abi] [build_type]
# Example: ./build-camerac-android.sh arm64-v8a Release
# Supported ABIs: arm64-v8a, armeabi-v7a, x86_64

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CAMERAC_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

ANDROID_ABI="${1:-arm64-v8a}"
BUILD_TYPE="${2:-Release}"
BUILD_DIR="$CAMERAC_DIR/build-android-$ANDROID_ABI"

echo "=========================================="
echo "Building camerac for Android"
echo "ABI: $ANDROID_ABI"
echo "Build Type: $BUILD_TYPE"
echo "=========================================="

# Locate Android NDK
if [ -z "$ANDROID_NDK" ]; then
    if [ -n "$ANDROID_NDK_HOME" ]; then
        ANDROID_NDK="$ANDROID_NDK_HOME"
    elif [ -n "$ANDROID_HOME" ]; then
        # Try to find latest NDK under ANDROID_HOME/ndk
        NDK_DIR=$(ls -d "$ANDROID_HOME/ndk/"* 2>/dev/null | sort -V | tail -n 1)
        ANDROID_NDK="$NDK_DIR"
    fi
fi

if [ -z "$ANDROID_NDK" ] || [ ! -d "$ANDROID_NDK" ]; then
    echo "Error: Android NDK not found."
    echo "Set ANDROID_NDK or ANDROID_NDK_HOME environment variable."
    exit 1
fi

echo "Using Android NDK: $ANDROID_NDK"

ANDROID_NATIVE_API_LEVEL="${ANDROID_NATIVE_API_LEVEL:-24}"
echo "API Level: $ANDROID_NATIVE_API_LEVEL"

mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

cmake .. \
    -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI="$ANDROID_ABI" \
    -DANDROID_NATIVE_API_LEVEL="$ANDROID_NATIVE_API_LEVEL" \
    -DANDROID_STL=c++_shared \
    -DCMAKE_BUILD_TYPE="$BUILD_TYPE" \
    -DBUILD_SHARED_LIBS=ON \
    -DCAMERAC_BUILD_SAMPLE=OFF

cmake --build . --config "$BUILD_TYPE" --target camerac -- -j$(nproc)

echo "=========================================="
echo "Build complete!"
echo "=========================================="

BUILD_TYPE_LOWER=$(echo "$BUILD_TYPE" | tr '[:upper:]' '[:lower:]')
OUTPUT_DIR="$CAMERAC_DIR/libs/android/$ANDROID_ABI/$BUILD_TYPE_LOWER"
mkdir -p "$OUTPUT_DIR"

SO_PATH=$(find "$BUILD_DIR" -name "libcamerac.so" | head -n 1)
if [ -n "$SO_PATH" ]; then
    cp "$SO_PATH" "$OUTPUT_DIR/libcamerac.so"
    echo "✓ Copied to $OUTPUT_DIR/libcamerac.so"
else
    echo "✗ libcamerac.so not found"
    exit 1
fi
