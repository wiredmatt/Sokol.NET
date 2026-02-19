# CGltfViewer

A physically-based glTF 2.0 model viewer built on top of [Sokol.NET](../../README.md).  
It replaces the managed [SharpGLTF](https://github.com/vpenades/SharpGLTF) parser with the native **cgltf** C library (already bundled in Sokol.NET), giving zero-allocation, cross-platform glTF/GLB loading that works identically on **Desktop · Web (WASM) · iOS · Android**.

![screenshot](screenshot.png)

---

## Features

| Category | Details |
|---|---|
| **Formats** | `.gltf` (JSON + external `.bin` + external textures), `.glb` (self-contained binary) |
| **Rendering** | Full PBR pipeline — metallic-roughness, normal/occlusion/emissive maps |
| **Skinning** | Uniform-based (≤85 bones, fast) and Texture-based (unlimited bones) — auto-selected |
| **Morphing** | Morph targets via RGBA32F texture array, GPU-driven weights |
| **Animation** | Skeletal (TRS + quaternion channels), morph-weight animations, multi-character |
| **IBL** | Image-Based Lighting with cubemap environment + skybox background |
| **Transmission** | `KHR_materials_transmission` — screen-space refraction (glass/water) |
| **Post-processing** | Bloom (bright-pass + dual blur), tone mapping (ACES, Khronos PBR Neutral) |
| **Frustum culling** | Per-mesh AABB frustum culling |
| **Debug view** | 35 PBR material debug visualizations (normals, roughness, transmission, etc.) |

### Supported glTF Extensions

| Extension | Support |
|---|---|
| `KHR_materials_transmission` | ✅ |
| `KHR_materials_volume` | ✅ |
| `KHR_materials_clearcoat` | ✅ |
| `KHR_materials_ior` | ✅ |
| `KHR_materials_specular` | ✅ |
| `KHR_materials_iridescence` | ✅ |
| `KHR_materials_emissive_strength` | ✅ |
| `KHR_materials_diffuse_transmission` | ✅ |
| `KHR_materials_pbrSpecularGlossiness` | ✅ |
| `KHR_lights_punctual` | ✅ |
| `KHR_texture_transform` | ✅ |
| `KHR_animation_pointer` | ⚠️ model loads; material-pointer animations ignored |

---

## Architecture

### Why cgltf instead of SharpGLTF?

SharpGLTF uses managed I/O (`File.ReadAllBytes`, `Stream`) which is **unavailable on Web/WASM, iOS and Android**. cgltf parses from a raw memory buffer (`cgltf_parse`), letting Sokol-fetch supply the bytes asynchronously on every platform.

### Async Loading Pipeline

Loading is split into three phases executed across multiple frames:

```
Frame N   [Phase 1] FileSystem.LoadFile(.gltf/.glb)
               ↓ callback
           cgltf_parse(buffer)  →  ParsedGltf
           Discover external .bin URIs
           Discover external texture URIs
           FileSystem.LoadFile() × N  (bin + textures in parallel)

Frame N+k [Progress update] pendingExternalBinCount + pendingTextureCount → 0

Frame N+k [Phase 3] CGltfModel.FinishLoad(ParsedGltf, preloadedTextures)
               Build meshes, bones, animations, upload GPU resources
```

**GLB** files have their binary chunk injected into `buffers[0].data` manually (since `cgltf_load_buffers` calls `fopen()` which is unavailable on web/mobile).

**External `.bin`** buffers are pinned via `GCHandle.Alloc(Pinned)` and injected into the live `cgltf_data*`; `data_free_method = none` prevents cgltf from freeing managed memory.

**External textures** are prefetched in parallel via `FileSystem.LoadFile` before `FinishLoad` runs, so `LoadTexture` never needs `File.ReadAllBytes`.

### Key Source Files

| File | Purpose |
|---|---|
| `CGltfModel.cs` | Core loader — `BeginParse` / `InjectExternalBuffer` / `FinishLoad`; mesh, material, skin extraction |
| `CGltfAnimator.cs` | Per-character animator — TRS bone matrices, morph weight sampling |
| `CGltfAnimation.cs` | Animation data — bone channels, morph channels, sampler interpolation |
| `CGltfAnimatedCharacter.cs` | Character wrapper — per-character skinning texture, animation playback |
| `CGltfNode.cs` | Scene node — local transform, world matrix, parent/child links |
| `Frame.cs` | Per-frame dispatch — async loading state machine, render passes |
| `FileSystem.cs` | Sokol-fetch wrapper — 64-slot pool, automatic buffer-resize retry, re-queue on pool exhaustion |
| `PipelineManager.cs` | Lazily created `sg_pipeline` cache keyed by shader type + blend mode |
| `Frame_Skinning.cs` | Skinned mesh rendering (uniform path) |
| `Frame_Morphing.cs` | Morph-target rendering |
| `Frame_SkinnedMorphing.cs` | Combined skinning + morphing |
| `Frame_Static.cs` | Static (no skinning/morphing) mesh rendering |
| `TextureCache.cs` | `sg_image` cache — prevents duplicate GPU uploads across meshes sharing an image |

---

## Controls

| Input | Action |
|---|---|
| Left mouse drag / 1-finger | Orbit camera |
| Mouse wheel / pinch | Zoom |
| Middle mouse drag / 2-finger | Rotate model |
| WASD / Arrow keys | Move camera |
| Q / E | Move camera up/down |
| Shift | Faster movement |

---

## UI Windows (Menu → Windows)

- **Model Browser** — cycle through built-in models with live loading progress bar
- **Model Info** — mesh/bone count, model rotation reset
- **Animation** — select animation, adjust playback speed, per-character skinning mode
- **Lighting** — toggle IBL, adjust ambient, toggle individual punctual lights
- **Bloom** — enable/disable HDR bloom, intensity and threshold sliders
- **Tone Mapping** — exposure, algorithm selector
- **Glass Materials** — per-model transmission stats, override IOR/transmission/attenuation with presets
- **Frustum Culling** — enable/disable, visible/culled mesh counts
- **Statistics** — FPS, frame time, vertices/indices/faces, texture cache stats
- **Debug View** — 35 PBR channel visualizations

---

## Running

### Desktop
```bash
# From VS Code task
prepare-CGltfViewer
```

### Web (WASM)
```bash
prepare-CGltfViewer-web
```

### iOS / Android
Use the corresponding build tasks in VS Code (`iOS: Build`, `Android: Build APK`, etc.).

---

## Built-in Models

Models are located in `Assets/` and referenced in `Main.cs → availableModels[]`.

| Model | Source |
|---|---|
| MorphStressTest | Khronos glTF-Sample-Models |
| DancingGangster | Mixamo |
| littlest_tokyo | sketchfab |
| ChronographWatch | Khronos glTF-Sample-Models |
| DamagedHelmet | Khronos glTF-Sample-Models |
| DragonAttenuation | Khronos glTF-Sample-Models |
| BoomBox / IridescenceLamp / WaterBottle | Khronos glTF-Sample-Models |

> Please verify each model's individual license before using in commercial projects.
