# Sokol.NET Sample Browser

A collection of graphics, physics, and animation samples demonstrating Sokol.NET capabilities across desktop, mobile, and web platforms.

## Samples Included

- **Cube** - Basic 3D cube rendering
- **Dyntex** - Dynamic texture updates
- **Drawcallperf** - Draw call performance testing
- **Offscreen** - Offscreen rendering to texture
- **Instancing** - GPU instancing demonstration
- **Loadpng** - PNG image loading
- **CubemapJpeg** - Cubemap with JPEG textures
- **Box2dPhysics** - 2D physics simulation using Box2D
- **ShaderToyApp** - ShaderToy-style effects
- **Cgltf** - glTF model loading
- **JoltPhysics** - 3D physics using JoltPhysics
- **Sdf** - Signed Distance Field rendering
- **Spine Inspector** - Interactive Spine skeletal animation inspector

## Important Licensing Notice

### Spine Runtime License

The **Spine Inspector** sample uses the Spine Runtime under a valid commercial license.

**License Information:**
- **Licensee**: Eli Aloni
- **License Type**: Spine Essential License
- **Purchase Date**: December 12, 2022
- **Invoice**: #ch_3MEAkiLDEe7LyNND1fEkZmpB

This project is authorized to distribute the Spine Runtime as part of this application under the terms of the [Spine Runtimes License Agreement](http://esotericsoftware.com/spine-runtimes-license).

**For Developers Using This Code:**

If you fork or modify this project for your own distribution:
- You will need your own [Spine license](https://esotericsoftware.com/spine-purchase) to distribute software containing the Spine Runtime
- The license above covers only the original SampleBrowser application
- For evaluation purposes, you may use the Spine Runtime free of charge

For the official legal terms governing the Spine Runtimes, please read:
- [Spine Runtimes License Agreement](http://esotericsoftware.com/spine-runtimes-license)
- [Spine Editor License Agreement - Section 2](http://esotericsoftware.com/spine-editor-license#s2)

## Building

### Desktop (macOS/Windows/Linux)

```bash
cd examples/SampleBrowser
dotnet build
```

### Web (WebAssembly)

```bash
# Prepare assets and build
dotnet run --project ../../tools/SokolApplicationBuilder -- --task prepare --architecture web --path .
dotnet build -c Release
```

### Android

```bash
# Build APK
dotnet build -c Release -p:RuntimeIdentifier=android-arm64
```

### iOS

```bash
# Build for iOS device
dotnet build -c Release -p:RuntimeIdentifier=ios-arm64
```

## Running

After building, run the executable for your platform. The Sample Browser will display a menu where you can select and run individual samples.

## Asset Attribution

- Spine animation assets are © Esoteric Software and are included for demonstration purposes under the Spine Runtime License.
- Other assets are part of the Sokol.NET project.

## More Information

For more details about Sokol.NET, visit the [main repository](https://github.com/elix22/Sokol.NET).
