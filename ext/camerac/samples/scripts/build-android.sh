#!/bin/bash
# Build the camerac Android sample APK.
#
# Usage:
#   ./build-android.sh              # build debug APK
#   ./build-android.sh install      # build + adb-install
#   ./build-android.sh install logcat  # build + install + tail Logcat

set -euo pipefail

ANDROID_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../android" && pwd)"
cd "$ANDROID_DIR"

# ── Locate Android SDK ────────────────────────────────────────────────────────
if [ -z "${ANDROID_HOME:-}" ]; then
    if [ -d "$HOME/Library/Android/sdk" ]; then
        export ANDROID_HOME="$HOME/Library/Android/sdk"
    else
        echo "Error: ANDROID_HOME is not set and ~/Library/Android/sdk not found."
        exit 1
    fi
fi

echo "ANDROID_HOME = $ANDROID_HOME"

# Detect highest installed NDK version
NDK_VERSION=$(ls -d "$ANDROID_HOME/ndk/"* 2>/dev/null | sort -V | tail -n 1 | xargs basename)
if [ -z "$NDK_VERSION" ]; then
    echo "Error: No NDK found under $ANDROID_HOME/ndk/."
    exit 1
fi
echo "NDK version  = $NDK_VERSION"

# ── local.properties ─────────────────────────────────────────────────────────
echo "sdk.dir=${ANDROID_HOME}" > local.properties

# ── Gradle wrapper (bootstrap once) ──────────────────────────────────────────
if [ ! -f "gradlew" ]; then
    echo "Generating Gradle wrapper …"
    gradle wrapper --gradle-version 8.9 --distribution-type bin
fi
chmod +x gradlew

# ── Build ─────────────────────────────────────────────────────────────────────
echo ""
echo "Building debug APK …"
./gradlew assembleDebug -PndkVersion="$NDK_VERSION"

APK="app/build/outputs/apk/debug/app-debug.apk"
echo ""
echo "✓  APK: $ANDROID_DIR/$APK"

# ── Install ───────────────────────────────────────────────────────────────────
if [[ "${1:-}" == "install" ]] || [[ "${2:-}" == "install" ]]; then
    echo ""
    echo "Installing on device …"
    adb install -r "$APK"
    adb shell am start -n "com.camerac.sample/.MainActivity"
    echo "✓  Launched."

    if [[ "${1:-}" == "logcat" ]] || [[ "${2:-}" == "logcat" ]]; then
        echo ""
        echo "Tailing Logcat – Ctrl-C to stop …"
        adb logcat -c
        adb logcat -s CameracSample:V DEBUG:E AndroidRuntime:E
    fi
fi
