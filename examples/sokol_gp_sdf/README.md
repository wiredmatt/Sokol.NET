# sokol_gp_sdf

A **Signed Distance Field (SDF) shader animation** sample for the [sokol_gp](https://github.com/edubart/sokol_gp) 2D graphics painter API, ported to C# with Sokol.NET.

## Description

This sample shows how to inject a fully custom GLSL shader into `sokol_gp` to produce a real-time SDF animation. A custom pipeline is created with a vertex shader that exposes the screen resolution and a fragment shader driven by elapsed time, producing smooth, analytically anti-aliased 2D shapes that animate over time.

### What it showcases

- Creating a custom `sg_shader` from a precompiled shader descriptor (`sdf_program_shader_desc`)
- Wrapping it in a `sgp_pipeline` with `sgp_make_pipeline`
- Setting the active pipeline with `sgp_set_pipeline` / `sgp_reset_pipeline`
- Uploading both vertex-stage and fragment-stage uniforms per frame with `sgp_set_uniform`
- Resetting the default texture view with `sgp_unset_view` / `sgp_reset_view` so the custom shader can run without a bound texture
- Drawing a fullscreen rectangle to execute the fragment shader across every pixel

### Shader uniforms

| Block | Field | Description |
|-------|-------|-------------|
| `sdf_vs_uniforms_t` | `iResolution` | Viewport size in pixels (`sgp_vec2`) |
| `sdf_fs_uniforms_t` | `iTime` | Elapsed time in seconds (`frame_count / 60`) |

### Shader pipeline

The shader is precompiled with `sokol-shdc` and lives in `shaders/compiled/`. Sokol.NET's build system selects the correct platform variant (Metal on macOS, WGSL on Web, GLSL on Android/Linux, HLSL on Windows) automatically.

## Reference

Original C sample: [`sample-sdf.c`](https://github.com/edubart/sokol_gp/blob/master/samples/sample-sdf.c)

## Related

- [sokol_gp repository](https://github.com/edubart/sokol_gp) — upstream library by Eduardo Bart
- [sokol_gp_effect](../sokol_gp_effect) — another custom shader example (multi-texture distortion)
- [sokol_gp_bench](../sokol_gp_bench) — performance benchmark
- [sokol_gp_blend](../sokol_gp_blend) — blend mode showcase
- [sokol_gp_framebuffer](../sokol_gp_framebuffer) — render-to-framebuffer
- [sokol_gp_primitives](../sokol_gp_primitives) — all primitive types and transforms
