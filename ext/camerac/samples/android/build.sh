#!/bin/bash
# Build and optionally install the camerac Android sample APK.
#
# Usage:
#   ./build.sh                  # build debug APK
#   ./build.sh install          # build and adb-install
#   ./build.sh install logcat   # build, install, launch, and tail Logcat
#
# Requires:
#   ANDROID_HOME  –  Android SDK root (or ~/Library/Android/sdk on macOS)
#   ANDROID_NDK   –  NDK root (auto-detected from ANDROID_HOME/ndk/ if unset)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# ── Locate Android SDK ────────────────────────────────────────────────────────
if [ -z "${ANDROID_HOME:-}" ]; then
    if [ -d "$HOME/Library/Android/sdk" ]; then
        export ANDROID_HOME="$HOME/Library/Android/sdk"
    else
        echo "Error: ANDROID_HOME is not set and ~/.Library/Android/sdk not found."
        exit 1
    fi
fi

# ── Locate NDK ────────────────────────────────────────────────────────────────
if [ -z "${ANDROID_NDK:-}" ]; then
    NDK_DIR=$(ls -d "$ANDROID_HOME/ndk/"* 2>/dev/null | sort -V | tail -n 1)
    if [ -n "$NDK_DIR" ] && [ -d "$NDK_DIR" ]; then
        export ANDROID_NDK="$NDK_DIR"
    else
        echo "Error: Android NDK not found under $ANDROID_HOME/ndk/."
        exit 1
    fi
fi

echo "ANDROID_HOME = $ANDROID_HOME"
echo "ANDROID_NDK  = $ANDROID_NDK"

# Detect installed NDK version (highest under ANDROID_HOME/ndk/)
ANDROID_NDK_VERSION=$(ls -d "$ANDROID_HOME/ndk/"* 2>/dev/null | sort -V | tail -n 1 | xargs basename)
echo "NDK version = $ANDROID_NDK_VERSION"

# ── Write local.properties ────────────────────────────────────────────────────
# Only sdk.dir is written – ndkVersion in app/build.gradle picks up ANDROID_NDK.
echo "sdk.dir=${ANDROID_HOME}" > local.properties

# ── Bootstrap Gradle wrapper (only needed once) ───────────────────────────────
if [ ! -f "gradlew" ]; then
    echo "Generating Gradle wrapper …"
    gradle wrapper --gradle-version 8.9 --distribution-type bin
fi

chmod +x gradlew

# ── Build ─────────────────────────────────────────────────────────────────────
echo ""
echo "Building debug APK …"
./gradlew assembleDebug -PndkVersion="$ANDROID_NDK_VERSION"

APK="app/build/outputs/apk/debug/app-debug.apk"
if [ ! -f "$APK" ]; then
    echo "Error: APK not found at $APK"
    exit 1
fi
echo ""
echo "✓  APK: $SCRIPT_DIR/$APK"

# ── Install ───────────────────────────────────────────────────────────────────
if [[ "${1:-}" == "install" ]] || [[ "${2:-}" == "install" ]]; then
    echo ""
    echo "Installing on device …"
    adb install -r "$APK"
    echo "✓  Installed."

    # Launch the Activity
    adb shell am start -n "com.camerac.sample/.MainActivity"

    if [[ "${1:-}" == "logcat" ]] || [[ "${2:-}" == "logcat" ]]; then
        echo ""
        echo "Tailing Logcat (tag CameracSample) – Ctrl-C to stop …"
        adb logcat -c
        adb logcat -s "CameracSample:V"
    fi
fi
