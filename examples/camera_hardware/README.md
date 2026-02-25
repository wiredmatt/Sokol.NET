# camera_hardware

> **⚠️ Work in Progress** — This example is still under active development and may be buggy.

A cross-platform camera capture demo built with **Sokol.NET**, the **camerac** native library, and **Dear ImGui**. It renders a live camera feed as a fullscreen quad and provides an interactive camera picker panel.

## Platform Status

| Platform | Status |
|----------|--------|
| macOS    | ✅ Verified working |
| iOS      | ✅ Verified working |
| Android  | ✅ Verified working |
| Web (Emscripten) | ✅ Verified working |
| Windows  | 🔬 Built via CI, not yet verified at runtime |
| Linux    | 🔬 Built via CI, not yet verified at runtime |

## Features

- **Fullscreen camera preview** — live feed rendered via a Sokol graphics pipeline
- **ImGui camera picker** — an in-app ImGui window lists all available camera devices; click any entry to switch to that camera instantly at runtime
- **Device info panel** — displays active camera name, position (front/back/unspecified), resolution, FPS, pixel format, and permission status
- **Hot device switching** — selecting a camera in the ImGui window closes the current device and reopens the chosen one without restarting the app
- **Frame counter** — shows the total number of captured frames

## Architecture

### Pixel Formats

| Platform | Format | Texture layout |
|----------|--------|----------------|
| macOS / iOS / Android | NV12 (YUV 4:2:0) | Dual-plane: Y (R8) + UV (RG8) via `NV12Texture` |
| Web (Emscripten) | RGBA32 | Single-plane via `FaceFlowTexture` |

NV12 is converted to RGB in the fragment shader (`shaders/camera-texture.glsl`) using BT.601 coefficients.

### Key Source Files

```
Source/
  camera_hardware-app.cs   — main app: init, frame loop, camera state, ImGui GUI
  NV12Texture.cs           — dual-plane Y+UV texture helper (macOS/iOS/Android)
  FaceFlowTexture.cs       — single-plane RGBA texture helper (Web)
  SamplerSettings.cs       — shared sampler configuration
  Program.cs               — entry point (calls sokol_main)
shaders/
  camera-texture.glsl      — GLSL source (NV12 → RGB conversion)
  compiled/                — pre-compiled per-platform shader outputs
```

## Prerequisites

1. **Sokol.NET** — the workspace must be configured with the Sokol native libraries.
2. **camerac native library** — pre-built libs are expected in `ext/camerac/libs/<platform>/<arch>/release/`.  
   To rebuild them run the scripts in `ext/camerac/scripts/` or trigger the `build-camerac` GitHub Actions workflow.

## Building

Follow the standard Sokol.NET example build flow. Use the VS Code tasks provided in the workspace (`prepare-camera_hardware`, `prepare-camera_hardware-web`), or run manually:

### macOS (desktop)
```bash
cd examples/camera_hardware
dotnet build camera_hardware.csproj
```

### Web (Emscripten)
```bash
cd examples/camera_hardware
dotnet build camera_hardwareWeb.csproj
```

### iOS
Use the **iOS: Build** VS Code task, or follow the instructions in [`docs/QUICK_BUILD.md`](../../docs/QUICK_BUILD.md).

### Android
Use the **Android: Build APK** VS Code task, or follow [`docs/QUICK_BUILD.md`](../../docs/QUICK_BUILD.md).

## Permissions

### iOS
`NSCameraUsageDescription` is set in `Directory.Build.props`. The system permission dialog will appear on first launch.

### Android
The following permissions are declared automatically by the build:
- `CAMERA` (required)
- `RECORD_AUDIO`
- `WAKE_LOCK`
- `INTERNET`

### Web
The browser will request `getUserMedia` camera access on startup.

## Known Limitations / TODOs

- Windows and Linux runtime behaviour is not yet verified.
- Camera switching on some Android devices may have a brief black frame.
- Web: only RGBA32 is supported; NV12 path is not used on Emscripten.
- Error handling for denied camera permissions is minimal — a status string is displayed in the ImGui panel but the app does not gracefully degrade.
