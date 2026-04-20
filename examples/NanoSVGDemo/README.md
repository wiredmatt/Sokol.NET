# NanoSVGDemo

An interactive SVG viewer demonstrating [NanoSVG](https://github.com/memononen/nanovg) integration in [Sokol.NET](../../README.md). Browse 31 SVG images in two rendering modes — rasterized via the NanoSVG software rasterizer, or rendered as live vector graphics via NanoVG.

## Screenshot

![NanoSVG Demo](screenshots/Screenshot%202026-04-20%20at%2016.32.16.png)

## Features

- **Raster mode** — SVGs are parsed and rasterized by NanoSVG into an RGBA texture, then drawn with a simple textured quad pipeline.
- **Vector mode** — SVGs are parsed and drawn each frame as resolution-independent vector paths using NanoVG, with full support for fills, strokes, linear and radial gradients, and compound paths with holes.
- **CSS class inlining** — NanoSVG only handles inline `style=` attributes; a pre-processing step automatically inlines `<style>` block class rules so colors render correctly on all SVG files.
- **Keyboard navigation** — Left/Right arrows and Space to cycle through SVGs.
- **ImGui panel** — Back/Next buttons and a mode toggle.

## Controls

| Input | Action |
|-------|--------|
| `→` / `Space` | Next SVG |
| `←` | Previous SVG |
| Back / Next buttons | Navigate via ImGui panel |
| Switch Vector / Switch Raster button | Toggle rendering mode |

## SVG Assets

The 31 SVG images bundled in `Assets/svg/` were sourced from:

- [https://freesvg.org/](https://freesvg.org/)
- [https://www.rawpng.com/vector](https://www.rawpng.com/vector)

## Running

```bash
# Compile shaders
dotnet build NanoSVGDemo.csproj -t:CompileShaders

# Run on desktop
dotnet run --project NanoSVGDemo.csproj

# Build for WebAssembly
dotnet run --project ../../tools/SokolApplicationBuilder -- --task build --architecture web --path examples/NanoSVGDemo
```

## Project Structure

```
NanoSVGDemo/
├── Source/
│   └── NanoSVGDemo-app.cs   # Main application logic
├── Assets/
│   └── svg/                 # 31 bundled SVG images
├── shaders/                 # GLSL shader sources and compiled output
├── screenshots/
└── NanoSVGDemo.csproj
```

## See Also

- [Sokol.NET](../../README.md) — root project readme with platform support and full example list
- [NanoVGDemo](../NanoVGDemo) — full NanoVG demo (vector graphics, gradients, text, animated widgets)
