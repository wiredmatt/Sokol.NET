# camerac – Implementation Status

This document describes every component of the library, its current implementation state,
known gaps, and what still needs to be done before it can be considered production-ready.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Fully implemented |
| ⚠️ | Implemented but with known limitations / caveats |
| ❌ | Not implemented / stub only |

---

## 1. Public API (`include/camerac.h`)

| Function | Status | Notes |
|----------|--------|-------|
| `cam_init()` | ✅ | Selects backend, calls `DetectDevices` |
| `cam_shutdown()` | ✅ | Signals threads, joins, frees all devices |
| `cam_get_backend()` | ✅ | Returns active backend name string |
| `cam_get_device_count()` | ✅ | |
| `cam_get_device_info()` | ✅ | Copies specs into `camDeviceInfo`; caller must call `cam_free_device_info` |
| `cam_free_device_info()` | ✅ | Frees the internal `specs` copy |
| `cam_open()` | ✅ | Opens device, spawns capture thread (if backend does not own one) |
| `cam_close()` | ✅ | Signals shutdown, joins thread, calls `CloseDevice` / `FreeDeviceHandle` |
| `cam_get_permission()` | ✅ | Returns cached permission state from `camDevice_t` |
| `cam_set_permission_callback()` | ✅ | Stored; fired on main thread via `cam_update()` |
| `cam_get_actual_spec()` | ✅ | Returns `device->actual_spec` |
| `cam_get_device_name()` | ✅ | |
| `cam_get_device_position()` | ✅ | |
| `cam_update()` | ✅ | Drains `pending_perms` queue, fires `perm_cb` |
| `cam_bytes_per_pixel()` | ✅ | Returns -1 for planar/compressed formats |
| `cam_pixel_format_name()` | ✅ | String table |
| `cam_position_name()` | ✅ | String table |
| `cam_get_error()` | ✅ | Per-thread error string (single string on Emscripten) |

---

## 2. Core (`src/camerac.c` + `src/camerac_internal.h`)

| Component | Status | Notes |
|-----------|--------|-------|
| Per-thread error string | ✅ | `__thread` / `__declspec(thread)` / single on Emscripten |
| Mutex abstraction | ✅ | `CRITICAL_SECTION` on Win32, `pthread_mutex_t` elsewhere |
| Backend bootstrap table | ✅ | `s_bootstrap[]` with compile-time platform selection |
| `cam_add_device()` | ✅ | Allocates `camDevice_t`, copies specs, prepends to linked list |
| `cam_deliver_frame()` | ✅ | Calls `frame_cb` under the assumption it is already locked on the backend side |
| `cam_permission_outcome()` | ✅ | Enqueues `camPendingPermission` node; thread-safe |
| `cam_capture_thread_run()` | ✅ | `WaitDevice → AcquireFrame → deliver → ReleaseFrame` loop |
| Capture thread creation | ✅ | `CreateThread` (Win32) / `pthread_create` (POSIX) in `cam_open()` |
| Device indexing | ⚠️ | `cam_get_device_info()` allocates a temporary pointer array each call to reverse the linked list. Fine for small counts; should use a flat array for large device counts |
| Multi-backend support | ❌ | Only one backend is selected at compile time. Cannot enumerate cameras from multiple subsystems simultaneously (e.g., USB + IP camera) |
| Hot-plug / device change | ❌ | No mechanism to add/remove devices after `cam_init()`. `cam_update()` does not re-detect devices |
| Re-initialization | ⚠️ | `cam_init()` after `cam_shutdown()` works but device handles from the first init are dangling |

---

## 3. macOS / iOS Backend (`src/coremedia/camerac_coremedia.m`)

| Component | Status | Notes |
|-----------|--------|-------|
| `DetectDevices` | ✅ | Enumerates `AVCaptureDevice`, maps formats and frame-rate ranges to `camSpec` |
| `OpenDevice` | ✅ | Creates session, input, output, GCD dispatch queue, requests `AVCaptureDevice` permission |
| `CloseDevice` | ✅ | Stops session, releases Obj-C objects via `CFBridgingRelease` / `__bridge_transfer` |
| Frame delivery | ✅ | `captureOutput:didOutputSampleBuffer:` delegate fires on GCD queue, locks CVPixelBuffer, calls `cam_deliver_frame` |
| Permission handling | ✅ | `requestAccessForMediaType:completionHandler:` result queued via `cam_permission_outcome()` |
| iOS orientation / rotation | ✅ | `UIDevice` orientation mapped to `frame.rotation` degrees (`USE_UIKIT_ROTATION`) |
| macOS position reporting | ⚠️ | `CAM_POSITION_UNKNOWN` always on macOS (only iOS has `AVCaptureDevicePositionFront/Back`). macOS code uses same `devicesWithMediaType:` path but skips position; should use `AVCaptureDevice.deviceType` on macOS 13+ |
| Format selection | ⚠️ | Uses `AVCaptureSessionPreset` (gross-grained). Does not lock the device to the exact `AVCaptureDeviceFormat` that matches the requested `camSpec`. Actual delivered format may differ slightly |
| Multi-session | ⚠️ | Each opened camera gets its own `AVCaptureSession`. macOS may limit concurrent sessions |
| `AVCaptureDeviceDiscoverySession` | ❌ | Still uses deprecated `devicesWithMediaType:`. Should migrate to `AVCaptureDeviceDiscoverySession` (available macOS 10.15 / iOS 10) for better filtering and future-proofing |
| NV12 planar handling | ⚠️ | For bi-planar formats (NV12) `CVPixelBufferGetBaseAddress` returns only the luma plane. The chroma plane is at `CVPixelBufferGetBaseAddressOfPlane(buf, 1)`. The current code passes only luma as `frame.data` |

---

## 4. Windows Backend (`src/mediafoundation/camerac_mediafoundation.c`)

| Component | Status | Notes |
|-----------|--------|-------|
| `DetectDevices` | ✅ | `MFEnumDeviceSources` → brief `IMFSourceReader` to enumerate native types |
| `OpenDevice` | ✅ | `MFCreateDeviceSource` → `MFCreateSourceReaderFromMediaSource`, sets output media type |
| `CloseDevice` | ✅ | Releases `IMFSourceReader`, frees frame buffer |
| `WaitDevice` | ✅ | Blocking `IMFSourceReader_ReadSample` with `MF_SOURCE_READER_FIRST_VIDEO_STREAM` |
| `AcquireFrame` | ✅ | `ReadSample` → `ConvertToContiguousBuffer` → `Lock` → memcpy into frame buffer → `Unlock` |
| `ReleaseFrame` | ✅ | No-op (data already memcpy'd) |
| COM initialization | ⚠️ | Calls `CoInitializeEx` / `MFStartup` only once at init. Not safe if the host application also initialises COM in MTA mode |
| Permission | ✅ | Always approved (Windows does not require permission requests in the same way) |
| Camera position | ❌ | Always `CAM_POSITION_UNKNOWN`. Windows MF does not expose front/back metadata for most webcams, but Surface tablets do via device attributes. Not queried |
| MF Async / DXGI | ❌ | Uses synchronous `ReadSample`. Async `ReadSampleAsync` with `IMFSourceReaderCallback` would be more efficient and avoid blocking the capture thread |
| Error recovery | ⚠️ | `MF_E_END_OF_STREAM` / `MF_E_MEDIA_LOST` errors cause `CAM_FRAME_ERROR` which stops the thread permanently; no reconnection |
| Wide-char device ID | ⚠️ | `device_id` is UTF-8 conversion of the symbolic link. Length truncation is possible for very long symbolic link paths |

---

## 5. Linux Backend (`src/v4l2/camerac_v4l2.c`)

| Component | Status | Notes |
|-----------|--------|-------|
| `DetectDevices` | ✅ | Scans `/dev/video*`, `VIDIOC_QUERYCAP`, `VIDIOC_ENUM_FMT` / `VIDIOC_ENUM_FRAMESIZES` / `VIDIOC_ENUM_FRAMEINTERVALS` |
| `OpenDevice` | ✅ | `VIDIOC_S_FMT`, `VIDIOC_REQBUFS` (4 mmap buffers), `VIDIOC_QBUF` all, `VIDIOC_STREAMON` |
| `CloseDevice` | ✅ | `VIDIOC_STREAMOFF`, `munmap` all buffers, `close(fd)` |
| `WaitDevice` | ✅ | `select()` with 2-second timeout |
| `AcquireFrame` | ✅ | `VIDIOC_DQBUF`, memcpy to `frame_copy`, immediate `VIDIOC_QBUF` re-enqueue |
| `ReleaseFrame` | ✅ | No-op (data memcpy'd) |
| Format negotiation | ⚠️ | Sets `VIDIOC_S_FMT` but does not try fallback formats if the requested one is rejected. Actual format may be driver-adjusted without notice |
| Frame-rate setting | ❌ | `VIDIOC_S_PARM` (stream parameters / fps) is not called. Frame rate is whatever the driver defaults to regardless of requested `camSpec.fps_numerator` |
| Camera position | ❌ | Always `CAM_POSITION_UNKNOWN`. V4L2 has no standard position introspection |
| Device path ordering | ⚠️ | `readdir` order is filesystem-dependent; `/dev/video0` is not guaranteed to be the primary camera. Should sort by path or use `udev` attributes |
| Permissions | ✅ | Always approved; V4L2 uses file permissions via the filesystem |
| DMABUF / zero-copy | ❌ | Uses `mmap` copy (safe), not `VIDIOC_EXPBUF` zero-copy sharing |

---

## 6. Android Backend (`src/android/camerac_android.c`)

| Component | Status | Notes |
|-----------|--------|-------|
| `dlopen` / `dlsym` loading | ✅ | `libcamera2ndk.so` + `libmediandk.so` loaded at runtime; all 30+ function pointers resolved |
| `DetectDevices` | ✅ | `ACameraManager_getCameraIdList`, reads `ACAMERA_LENS_FACING`, adds device per camera ID |
| `OpenDevice` | ✅ | `AImageReader_new` (YUV_420_888), `setImageListener`, `openCamera`, `createCaptureRequest(TEMPLATE_PREVIEW)`, `createCaptureSession`, `setRepeatingRequest` |
| `CloseDevice` | ✅ | Closes session, request, device, reader; frees `AndroidPrivateData` |
| Frame delivery | ✅ | `android_image_callback` reads planes, assembles `camFrame`, calls `cam_deliver_frame` |
| Permission | ⚠️ | Calls `cam_permission_outcome(APPROVED)` immediately on first frame, not when the OS permission dialog is resolved. The actual Android `CAMERA` permission must be requested by the Java/Kotlin layer before `cam_open()` is called; the library does not initiate an OS permission request via JNI |
| Frame format | ⚠️ | Hardcoded to `AIMAGE_FORMAT_YUV_420_888`. Some devices only support `JPEG` or `RGBA_8888`. No fallback tried |
| Spec enumeration | ⚠️ | `DetectDevices` only reports one synthetic spec per device (`1280×720 @ 30fps NV12`). Actual supported resolutions from `ACAMERA_SCALER_AVAILABLE_STREAM_CONFIGURATIONS` are not read |
| Camera2 state callbacks | ⚠️ | `ACameraDevice_StateCallbacks.onError` and `onDisconnected` are set but only log; no reconnection or `cam_permission_outcome(DENIED)` on disconnect |
| JNI runtime permission | ❌ | The library has no JNI helper to call `ActivityCompat.requestPermissions`. The host app must request `android.permission.CAMERA` before calling `cam_init()` |
| API < 24 guard | ✅ | `#undef __ANDROID_API__` trick ensures Camera2 headers compile; `dlopen` provides runtime safety |

---

## 7. Emscripten Backend (`src/emscripten/camerac_emscripten.c`)

| Component | Status | Notes |
|-----------|--------|-------|
| `DetectDevices` | ⚠️ | Registers one placeholder "Default Camera" device with a single synthetic spec. `enumerateDevices()` is not called (requires async + prior permission) |
| `OpenDevice` | ✅ | Injects `getUserMedia()` JS via `MAIN_THREAD_EM_ASM`; sets up `<video>` + 2D `<canvas>` elements |
| Permission callback | ✅ | `camerac_emscripten_permission_outcome()` (`EMSCRIPTEN_KEEPALIVE`) called from JS promise chain |
| `AcquireFrame` | ✅ | `drawImage(video)` + `getImageData` → copies RGBA pixels to C buffer via heap |
| Frame format | ⚠️ | Always `CAM_PIXEL_FORMAT_RGBA32`. No YUV delivery possible through the canvas 2D API |
| `WaitDevice` | ✅ | Returns `false` (no background thread used) |
| Frame driving | ⚠️ | The backend has `ProvidesOwnCallbackThread = true` but does **not** actually call `cam_deliver_frame` on a timer. `EMSCRIPTENCAMERA_AcquireFrame` must be called by the user through the core's capture loop — but the core's capture loop is only started when `ProvidesOwnCallbackThread = false`. **Currently no frames will be delivered automatically.** The host must call `cam_acquire_frame_and_deliver()` or a similar mechanism manually, or the `ProvidesOwnCallbackThread` flag must be set to `false` so the core loop runs |
| Multi-device | ❌ | Only one `Module['_camerac']` slot, so only one camera at a time is usable |
| `requestAnimationFrame` ticker | ❌ | No JS animation-frame ticker is installed; frame polling must be driven externally |

---

## 8. CMakeLists.txt

| Feature | Status | Notes |
|---------|--------|-------|
| macOS / iOS | ✅ | Links `AVFoundation`, `CoreMedia`, `CoreVideo`, optionally `AppKit` |
| Windows | ✅ | Links `mf mfplat mfreadwrite mfuuid ole32 oleaut32` |
| Linux | ✅ | Links `pthread`; source is `camerac_v4l2.c` |
| Android | ✅ | Links `log`; `-Wl,-z,max-page-size=16384` for Android 15+ 16 KB pages |
| Emscripten | ✅ | `STATIC` library; `EXPORTED_FUNCTIONS` linker flags for `camerac_emscripten_permission_outcome` |
| `CAMERAC_BUILD_SAMPLE` option | ⚠️ | Option defined; `samples/camerac_sample.c` exists but the `CMakeLists.txt` `add_executable` block references it correctly only if paths match. **Needs verification** |
| Install rules | ⚠️ | No `install()` targets defined. Headers and library are not installed to a prefix |
| `find_package` / `pkg-config` | ❌ | No `cameracConfig.cmake` or `.pc` file generated; consumers must add as a subdirectory |

---

## 9. Build Scripts (`scripts/`)

| Script | Status | Notes |
|--------|--------|-------|
| `build-camerac-macos.sh` | ✅ | arm64 / x86_64, copies dylib, `install_name_tool`, codesigns |
| `build-camerac-ios.sh` | ✅ | `iphoneos` / `iphonesimulator`, Xcode generator |
| `build-camerac-linux.sh` | ✅ | Standard cmake + make |
| `build-camerac-android.sh` | ✅ | NDK toolchain, single ABI |
| `build-camerac-android-all.sh` | ✅ | Loops `arm64-v8a armeabi-v7a x86_64` |
| `build-camerac-web.sh` | ✅ | `emcmake cmake`, copies `.a` |
| `build-camerac-windows.ps1` | ✅ | VS 2022 generator, copies DLL + `.lib` |
| `build-all.sh` | ✅ | `uname -s` platform detection, delegates to per-platform scripts |
| Execute permissions | ✅ | All `.sh` scripts are `+x` |
| NDK path detection | ⚠️ | Looks for `ANDROID_NDK` then `ANDROID_NDK_ROOT` env vars. Falls back to `~/Android/Sdk/ndk/`. If none found, the script exits with an error but doesn't suggest how to fix it |
| Xcode code-signing | ⚠️ | Uses `codesign --remove-signature` followed by ad-hoc signing. Will fail on CI without Apple Developer entitlements for distribution builds |

---

## 10. Sample (`samples/camerac_sample.c`)

| Feature | Status | Notes |
|---------|--------|-------|
| Init / shutdown lifecycle | ✅ | |
| Device enumeration | ✅ | Prints all cameras and their specs |
| `cam_open()` / `cam_close()` | ✅ | |
| Frame callback | ✅ | Prints stats every 30 frames |
| Permission callback | ✅ | |
| Main loop / 300-frame limit | ✅ | Uses `nanosleep` on POSIX, `Sleep` on Win32 |
| Emscripten compiles | ⚠️ | `nanosleep` block is `#ifdef`-guarded but the sample loop is a `while` spin that Emscripten cannot run |

---

## 11. What Still Needs To Be Done

The items below are the most impactful gaps, roughly ordered by priority.

### Critical / Correctness

1. **Emscripten frame delivery loop** — The Emscripten backend sets `ProvidesOwnCallbackThread = true` but never actually calls `cam_deliver_frame`. Either:
   - Change to `ProvidesOwnCallbackThread = false` and let the core run a POSIX thread (which Emscripten supports via pthreads with `-pthread`), or
   - Install a `requestAnimationFrame` ticker in JS that calls `EMSCRIPTENCAMERA_AcquireFrame` + `cam_deliver_frame` on each browser frame.

2. **NV12 bi-planar delivery on CoreMedia** — `CVPixelBufferGetBaseAddress` only yields the luma (Y) plane for bi-planar formats. The chroma (UV) plane at index 1 must be passed separately. Either:
   - Deliver both planes as a contiguous copy (copy Y + UV into a single allocation), or
   - Change `camFrame` to carry a second `data2` pointer + `pitch2` for the chroma plane.

3. **Android real spec enumeration** — `DetectDevices` registers a fake `1280×720` spec instead of reading `ACAMERA_SCALER_AVAILABLE_STREAM_CONFIGURATIONS`. This means `cam_get_device_info()` returns inaccurate information and `cam_open()` may request a resolution the hardware does not support.

4. **Android permission flow** — The library does not trigger the OS permission dialog. The caller must request `android.permission.CAMERA` at the Java/Kotlin level before `cam_init()`. This should be documented more prominently, and ideally a JNI helper `cam_android_request_permission(JNIEnv*, jobject activity)` should be provided.

### Important / Quality

5. **V4L2 frame rate setting** — `VIDIOC_S_PARM` is never called. The camera will run at whatever fps the driver defaults to.

6. **CoreMedia exact format locking** — `OpenDevice` should iterate `avDev.formats`, find the closest matching `AVCaptureDeviceFormat` for the requested `camSpec`, lock it with `lockForConfiguration`/`setActiveFormat`, and set `activeVideoMinFrameDuration`/`activeVideoMaxFrameDuration` for the requested fps.

7. **macOS / iOS back position** — On macOS 13+ use `AVCaptureDevice.deviceType == AVCaptureDeviceTypeBuiltInWideAngleCamera` to infer position. On iOS the position is already read but only set for iOS targets.

8. **Hot-plug support** — None of the backends notify the core when a USB camera is connected or disconnected. `cam_update()` should support a device-change callback, and at minimum `cam_shutdown()` + `cam_init()` should be safe to call.

9. **`find_package` integration** — Add a `cameracConfig.cmake.in` + `cameracConfigVersion.cmake.in` so CMake consumers can use `find_package(camerac REQUIRED)`.

10. **Windows async reader** — Replace synchronous `ReadSample` with `ReadSampleAsync` + `IMFSourceReaderCallback` to avoid blocking the capture thread indefinitely.

### Documentation / Polish

11. **Error messages from backends** — Most backends call `cam_set_error("short message")` without providing OS error codes / `NSError` descriptions / `HRESULT` values.

12. **Thread safety of `cam_deliver_frame`** — The frame callback fires on the capture thread. Document clearly that `frame->data` is only valid for the duration of the call and that the callback must not call any `cam_*` API other than reading device properties.

13. **Version header** — No `CAMERAC_VERSION_MAJOR/MINOR/PATCH` defines in `camerac.h`.

14. **iOS Simulator** — `AVCaptureDevice` is not available on the iOS Simulator. The backend should either detect `TARGET_IPHONE_SIMULATOR` and return an empty device list gracefully, or add a software test-pattern source.
