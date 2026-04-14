# NanoVGDemo

A full C# port of the official **[NanoVG](https://github.com/memononen/nanovg)** demo, running on Sokol.NET with cross-platform support for Desktop, Web, iOS, and Android.

[← Back to Sokol.NET](../../README.md)

## Overview

This example demonstrates the full NanoVG vector graphics API integrated with the Sokol rendering backend (`sokol_nanovg`). It is a faithful C# translation of the original `demo.c` from the NanoVG repository, covering all 22 demo draw routines.

## Features

- **Vector Graphics** — Gradients, rounded rectangles, bezier paths, arcs
- **Text Rendering** — TrueType fonts via fontstash (Roboto Regular + Bold, NotoEmoji)
- **Image Textures** — PNG image thumbnails rendered via NanoVG image API
- **Stencil-based Fills** — Complex clipping and winding fill rules
- **Blending & Compositing** — Alpha gradients, drop shadows, inner glow
- **Animated Demo** — Time-driven animation: spinner, eyes, graph, blowup effect
- **Touch & Mouse** — First-touch position drives hover/highlight effects; double-tap toggles blowup
- **All Platforms** — macOS (Metal), iOS (Metal), Android (GLES3), Web (WebGL2)

## Demo Scenes

| Scene | Description |
|-------|-------------|
| Window & shadows | Rounded window panel with drop shadow |
| Search box | Rounded input field with icon |
| Dropdown | Combobox with gradient background |
| Login | Label + input field layout |
| Checkboxes | Custom-drawn checkboxes |
| Button group | Segmented control buttons |
| Slider | Track + thumb with value label |
| Eyes | Animated eyes following the cursor/touch |
| Paragraph | Multi-line wrapped text with selection highlight |
| Graph | Animated line graph with gradient fill |
| Color wheel | Hue ring + saturation/value triangle |
| Spinner | Animated arc spinner |
| Thumbnails | Image grid with clipping and rounded images |
| Lines & caps | Stroke cap/join styles |
| Widths | Varying stroke widths |
| Scissor | Rotated scissor clipping demo |

## Controls

| Platform | Move cursor / hover | Toggle blowup |
|----------|---------------------|---------------|
| Desktop / Web | Mouse move | Space bar |
| Mobile (iOS / Android) | Touch drag | Double-tap |

## Build and Run

```bash
# Desktop (macOS / Windows / Linux)
dotnet run -p NanoVGDemo.csproj

# WebAssembly
dotnet run -p NanoVGDemoWeb.csproj
```

For Android / iOS, use the VS Code tasks in the workspace. See [../../docs](../../docs) for platform-specific guides.

## Implementation Notes

- **Android stencil fix**: The sokol_app.h Android EGL config must request `EGL_STENCIL_SIZE, 8`. Without a stencil buffer, NanoVG's stencil-based winding fill passes silently fail, causing all gradients to render as flat solids.
- **NanoVG flags**: `NVG_ANTIALIAS | NVG_STENCIL_STROKES` — both required for correct rendering on all platforms.
- **Pass action**: Color clear `(0.3, 0.3, 0.32)` + depth clear `1.0` + stencil clear `0` must all be set.
- **DPI scaling**: `nvgBeginFrame` receives logical size (`sapp_widthf() / dpiScale`) while the pixel ratio is passed as the third argument.

## Screenshots

![NanoVGDemo on macOS](screenshots/Screenshot%202026-04-14%20at%2015.55.58.png)

## Project Files

- App entry & event loop: [Source/NanoVGDemo-app.cs](Source/NanoVGDemo-app.cs)
- Demo draw routines: [Source/Demo.cs](Source/Demo.cs)
- Asset loading: [Assets/fonts/](Assets/fonts/), [Assets/images/](Assets/images/)
