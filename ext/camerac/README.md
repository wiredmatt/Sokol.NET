# camerac

A cross-platform camera capture library with a clean C API, supporting:

| Platform   | Backend          | Notes                        |
|------------|------------------|------------------------------|
| macOS      | CoreMedia/AVFoundation | macOS 11+            |
| iOS        | CoreMedia/AVFoundation | iOS 13+              |
| Windows    | Media Foundation | Windows 8+                   |
| Linux      | V4L2             | Video4Linux2                 |
| Android    | Camera2 NDK      | API level 24+ (Android 7.0+) |
| Emscripten | getUserMedia     | Modern browsers              |

**camerac** is a standalone C library with no external dependencies, designed to integrate directly into any C/C++ project.

---

## API Overview

```c
#include "camerac.h"

// 1. Initialize the subsystem
cam_init();

// 2. Enumerate devices
int n = cam_get_device_count();
for (int i = 0; i < n; i++) {
    camDeviceInfo info;
    cam_get_device_info(i, &info);
    printf("Camera %d: %s (%s)\n", i, info.name,
           cam_position_name(info.position));
    for (int s = 0; s < info.num_specs; s++) {
        const camSpec *sp = &info.specs[s];
        printf("  %dx%d @ %d/%d fps  format=%s\n",
               sp->width, sp->height,
               sp->fps_numerator, sp->fps_denominator,
               cam_pixel_format_name(sp->format));
    }
    cam_free_device_info(&info);
}

// 3. Open a camera (NULL spec → first supported format)
camSpec req = { 1280, 720, 30, 1, CAM_PIXEL_FORMAT_NV12 };
camDevice dev = cam_open(0, &req, my_frame_callback, NULL);

// 4. In your frame callback (called on capture thread):
void my_frame_callback(camDevice device, const camFrame *frame, void *ud) {
    // frame->data, frame->pitch, frame->width, frame->height, frame->format
    // Copy frame->data if needed — it's only valid during this call
}

// 5. Call once per frame from the main thread
cam_update();

// 6. Close and shutdown
cam_close(dev);
cam_shutdown();
```

---

## Permissions

### macOS
Add to your `.entitlements`:
```xml
<key>com.apple.security.device.camera</key><true/>
```

### iOS / macOS (Info.plist)
```xml
<key>NSCameraUsageDescription</key>
<string>This app uses the camera.</string>
```

### Android (AndroidManifest.xml)
```xml
<uses-permission android:name="android.permission.CAMERA"/>
<uses-feature android:name="android.hardware.camera"/>
```
Runtime permission must be granted before calling `cam_open()`.

### Emscripten
The browser's permission prompt fires automatically when `cam_open()` is called.
Set a permission callback via `cam_set_permission_callback()` to react to the outcome.

> **Note:** Emscripten runs single-threaded — there is no separate capture thread.
> `cam_update()` (called each frame from the main thread) pumps `AcquireFrame`
> internally, so frames arrive as if from a background thread on other platforms.
> Frames are delivered as `CAM_PIXEL_FORMAT_RGBA32` (not NV12) on this backend.

---

## Building

### macOS

```bash
cd scripts
./build-camerac-macos.sh Release
# Builds arm64, X64, and a lipo-merged universal binary in one step.
```

### iOS

```bash
./build-camerac-ios.sh iphoneos     Release   # Device
./build-camerac-ios.sh iphonesimulator Release # Simulator
```

### Linux

```bash
./build-camerac-linux.sh Release
```

### Android

```bash
export ANDROID_NDK=/path/to/ndk
./build-camerac-android-all.sh Release   # arm64-v8a, armeabi-v7a, x86_64
# or a single ABI:
./build-camerac-android.sh arm64-v8a Release
```

### Windows

```powershell
.\build-camerac-windows.ps1 -Architecture x64 -BuildType Release
```

### Emscripten

```bash
./build-camerac-web.sh Release
```

### All (current platform)

```bash
./build-all.sh Release
```

---

## Prebuilt library locations

```
libs/
  macos/
    arm64/release/libcamerac.dylib
    X64/release/libcamerac.dylib
    universal/release/libcamerac.dylib   (lipo-merged)
  ios/
    release/camerac.framework   (or libcamerac.dylib)
  linux/
    X64/release/libcamerac.so
  android/
    arm64-v8a/release/libcamerac.so
    armeabi-v7a/release/libcamerac.so
    x86_64/release/libcamerac.so
  windows/
    x64/release/camerac.dll
    x64/release/camerac.lib
  emscripten/
    x86/release/camerac.a
```

---

## Pixel formats

| Constant                    | Description                       |
|-----------------------------|-----------------------------------|
| `CAM_PIXEL_FORMAT_RGB24`    | 8-8-8 packed RGB                  |
| `CAM_PIXEL_FORMAT_BGR24`    | 8-8-8 packed BGR                  |
| `CAM_PIXEL_FORMAT_RGBA32`   | 8-8-8-8 RGBA                      |
| `CAM_PIXEL_FORMAT_BGRA32`   | 8-8-8-8 BGRA                      |
| `CAM_PIXEL_FORMAT_NV12`     | YUV 4:2:0 bi-planar (most common) |
| `CAM_PIXEL_FORMAT_YUY2`     | YUV 4:2:2 packed                  |
| `CAM_PIXEL_FORMAT_UYVY`     | YUV 4:2:2 packed (UYVY order)     |
| `CAM_PIXEL_FORMAT_MJPEG`    | Motion JPEG                       |

---

## License

MIT — see [LICENSE](LICENSE).

This project is based upon the design of the
[SDL3 camera subsystem](https://wiki.libsdl.org/SDL3/CategoryCamera)
(Copyright © 1997-2024 Sam Lantinga and the SDL contributors, licensed under the
[zlib license](https://www.libsdl.org/license.php)).
camerac is an independent reimplementation and carries no SDL source code.
