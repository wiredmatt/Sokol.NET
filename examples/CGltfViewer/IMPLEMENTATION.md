# CGltfViewer Implementation Plan

CGltfViewer is a port of GltfViewer that replaces SharpGLTF with the native cgltf C library (via existing C# bindings). All shaders and rendering logic are shared identically. Only the GLTF loading layer changes.

---

## Architecture Overview

| Concern | GltfViewer (SharpGLTF) | CGltfViewer (cgltf) |
|---|---|---|
| GLTF parsing | `SharpGLTF.Schema2.ModelRoot` | `cgltf_data*` (native) |
| Model wrapper | `SharpGltfModel` | `CGltfModel` |
| Node hierarchy | `SharpGltfNode` | `CGltfNode` |
| Animation data | `SharpGltfAnimation` + `SharpGltfBone` | `CGltfAnimation` + `CGltfBone` |
| Animator | `SharpGltfAnimator` | `CGltfAnimator` |
| Character | `AnimatedCharacter` (in SharpGltfAnimatedCharacter.cs) | `AnimatedCharacter` (adapted) |
| Shaders | pbr.glsl, ibl.glsl, brdf.glsl, etc. | **Identical — shared** |
| Mesh/Vertex/Texture | Same types | **Identical — copied** |
| Camera/Light/GUI | Same types | **Identical — copied** |

---

## Extension Support

| Extension | cgltf | Status |
|---|---|---|
| KHR_materials_transmission | ✅ `has_transmission` | Supported |
| KHR_materials_volume | ✅ `has_volume` | Supported |
| KHR_materials_clearcoat | ✅ `has_clearcoat` | Supported |
| KHR_materials_ior | ✅ `has_ior` | Supported |
| KHR_materials_specular | ✅ `has_specular` | Supported |
| KHR_materials_iridescence | ✅ `has_iridescence` | Supported |
| KHR_materials_emissive_strength | ✅ `has_emissive_strength` | Supported |
| KHR_materials_diffuse_transmission | ✅ `has_diffuse_transmission` | Supported |
| KHR_materials_pbrSpecularGlossiness | ✅ `has_pbr_specular_glossiness` | Supported |
| KHR_materials_variants | ✅ `cgltf_material_variant` | Supported |
| KHR_texture_transform | ✅ `cgltf_texture_transform` | Supported |
| KHR_lights_punctual | ✅ `cgltf_light` | Supported |
| **KHR_animation_pointer** | ❌ Not in cgltf | **Not supported** — PotOfCoalsAnimationPointer loads but material animations don't play |
| OMI_physics_body/shape | ❌ Vendor ext | **Ignored** — no rendering impact |
| GODOT_single_root | ❌ Vendor ext | **Ignored** — no rendering impact |

---

## Implementation Steps

- [x] **Step 1** — Create this plan document
- [x] **Step 2** — Copy shaders from GltfViewer (`shaders/`) to CGltfViewer
- [x] **Step 3** — Copy source files that require no changes (Camera, Light, Texture, etc.)
- [x] **Step 4** — Write `CGltfNode.cs` (replaces SharpGltfNode.cs)
- [x] **Step 5** — Write `CGltfAnimation.cs` + `CGltfBone.cs` (replaces SharpGltfAnimation/Bone)
- [x] **Step 6** — Write `CGltfModel.cs` — core GLTF loading, mesh/material extraction via cgltf
- [x] **Step 7** — Write `CGltfAnimator.cs` — drives bone matrices and node transforms
- [x] **Step 8** — Adapt `Main.cs` — replace SharpGltfModel/Animator fields, remove ModelRoot async state
- [x] **Step 9** — Adapt `Init.cs` — replace SharpGLTF model loading with synchronous cgltf path
- [x] **Step 10** — Adapt `Frame.cs` — replace async state machine + `LoadLightsFromModel`/`LoadIBLFromModel`
- [x] **Step 11** — Adapt `GUI.cs` + `Event.cs` (copy unchanged, fixed `.Animation` → `.Animations[...]`)
- [x] **Step 12** — Adapt remaining files: `Cleanup.cs`, `Mesh.cs`, `PipelineManager.cs`, `EnvironmentMapLoader.cs`, `Frame_Static.cs`, `Frame_Skinning.cs`, `Frame_Morphing.cs`, `Frame_SkinnedMorphing.cs`
- [x] **Step 13** — Build and fix compilation errors — **BUILD SUCCEEDED: 0 errors, 0 warnings**

---

## Files: Copy Unchanged
These files have zero SharpGLTF references:

| File | Status |
|---|---|
| AnimationConstants.cs | ⬜ |
| BoneInfo.cs | ⬜ |
| BoundingBox.cs | ⬜ |
| Camera.cs | ⬜ |
| EnvironmentMap.cs | ⬜ |
| Event.cs | ⬜ |
| FileSystem.cs | ⬜ |
| GUI.cs | ⬜ |
| Light.cs | ⬜ |
| RenderingConstants.cs | ⬜ |
| SamplerSettings.cs | ⬜ |
| SkyboxRenderer.cs | ⬜ |
| Texture.cs | ⬜ |
| TextureCache.cs | ⬜ |
| Vertex.cs | ⬜ |
| ViewTracker.cs | ⬜ |

## Files: Adapt (Remove SharpGLTF)

| File | SharpGLTF usage | Status |
|---|---|---|
| Main.cs | `SharpGltfModel`, `SharpGltfAnimator`, `ModelRoot`, `AsyncSatelliteLoadState`, `SharpGltfNode` | ⬜ |
| Init.cs | Load model via SharpGLTF | ⬜ |
| Frame.cs | `LoadLightsFromModel(ModelRoot)`, `LoadIBLFromModel(ModelRoot)` | ⬜ |
| Frame_Static.cs | `SharpGltfNode`, `SharpGltfModel` | ⬜ |
| Frame_Skinning.cs | `SharpGltfNode`, `SharpGltfModel` | ⬜ |
| Frame_Morphing.cs | `SharpGltfModel`, `MeshPrimitive` | ⬜ |
| Frame_SkinnedMorphing.cs | `SharpGltfModel`, `MeshPrimitive` | ⬜ |
| Cleanup.cs | `SharpGltfModel` | ⬜ |
| Mesh.cs | `MeshPrimitive?` field | ⬜ |
| PipelineManager.cs | Minimal | ⬜ |
| EnvironmentMapLoader.cs | `LoadFromGltfOrCreateTest(ModelRoot)` | ⬜ |

## Files: Create New

| File | Description | Status |
|---|---|---|
| CGltfNode.cs | Node with TRS, parent ref, mesh index, animation flags | ⬜ |
| CGltfBone.cs | Bone with offset matrix, ID | ⬜ |
| CGltfAnimation.cs | Animation clip with bone tree, keyframe data, sampling | ⬜ |
| CGltfModel.cs | Full GLTF loading: mesh, material, skin, node hierarchy | ⬜ |
| CGltfAnimator.cs | Per-frame bone matrix calculation, node transform animation | ⬜ |
