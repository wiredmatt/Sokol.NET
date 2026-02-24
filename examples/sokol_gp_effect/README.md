# sokol_gp_effect

A **custom shader effect** sample for the [sokol_gp](https://github.com/edubart/sokol_gp) 2D graphics painter API, ported to C# with Sokol.NET.

## Description

This sample shows how to plug a fully custom GLSL shader into the `sokol_gp` pipeline. The effect shader blends two textures — a tileset preview image and a Perlin noise texture — using time-based animation to produce a dynamic distortion effect. It demonstrates the complete workflow of loading images asynchronously, creating a custom `sgp_pipeline`, binding texture views and samplers, and uploading per-frame uniform data.

### What it showcases

- Writing a custom fragment shader that samples two texture channels
- Asynchronous asset loading via `sokol_fetch` with pre-allocated `sg_alloc_image` / `sg_init_image`
- Building a `sgp_pipeline` with a custom `sg_shader`
- Binding textures with `sgp_set_view` / `sgp_set_sampler` / `sgp_reset_sampler` / `sgp_reset_view`
- Uploading vertex and fragment uniforms each frame with `sgp_set_uniform`
- Drawing a fullscreen quad with `sgp_draw_filled_rect`

### Assets

| File | Purpose |
|------|---------|
| `Assets/images/lpc_winter_preview.png` | Base tileset texture (channel 0) |
| `Assets/images/perlin.png` | Perlin noise texture used for distortion (channel 1) |

### Shader uniforms

| Block | Field | Description |
|-------|-------|-------------|
| `effect_fs_uniforms_t` | `iTime` | Elapsed time driving the animation |
| `effect_fs_uniforms_t` | `iTimeDelta` | Delta time for frame-rate independent effects |

## Reference

Original C sample: [`sample-effect.c`](https://github.com/edubart/sokol_gp/blob/master/samples/sample-effect.c)

## Related

- [sokol_gp repository](https://github.com/edubart/sokol_gp) — upstream library by Eduardo Bart
- [sokol_gp_sdf](../sokol_gp_sdf) — another custom shader example (SDF animation)
- [sokol_gp_bench](../sokol_gp_bench) — performance benchmark
- [sokol_gp_blend](../sokol_gp_blend) — blend mode showcase
- [sokol_gp_framebuffer](../sokol_gp_framebuffer) — render-to-framebuffer
- [sokol_gp_primitives](../sokol_gp_primitives) — all primitive types and transforms
