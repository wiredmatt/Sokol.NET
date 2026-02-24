# sokol_gp_blend

A **blend mode showcase** for the [sokol_gp](https://github.com/edubart/sokol_gp) 2D graphics painter API, ported to C# with Sokol.NET.

## Description

This sample demonstrates every blend mode available in `sokol_gp` by rendering three overlapping colored rectangles on a checkerboard background. The window is divided into panels — one per blend mode — so you can visually compare how each mode composites source and destination pixels.

### Blend modes demonstrated

| Mode | Description |
|------|-------------|
| `SGP_BLENDMODE_NONE` | No blending, opaque overwrite |
| `SGP_BLENDMODE_BLEND` | Standard alpha blending (`src_alpha + (1-src_alpha)`) |
| `SGP_BLENDMODE_BLEND_PREMULTIPLIED` | Alpha blending with pre-multiplied alpha colors |
| `SGP_BLENDMODE_ADD` | Additive blending — colors are summed |
| `SGP_BLENDMODE_ADD_PREMULTIPLIED` | Additive blending with pre-multiplied alpha |
| `SGP_BLENDMODE_MOD` | Modulate — multiply destination by source |
| `SGP_BLENDMODE_MUL` | Multiply source and destination RGBA channels |

### Key APIs demonstrated

- `sgp_set_blend_mode` / `sgp_reset_blend_mode` — switch blend mode per draw call
- `sgp_set_color` / `sgp_reset_color` — per-draw RGBA color tint
- `sgp_draw_filled_rect` — solid rectangle primitive
- `sgp_translate` / `sgp_push_transform` / `sgp_pop_transform` — 2D transform stack

## Reference

Original C sample: [`sample-blend.c`](https://github.com/edubart/sokol_gp/blob/master/samples/sample-blend.c)

## Related

- [sokol_gp repository](https://github.com/edubart/sokol_gp) — upstream library by Eduardo Bart
- [sokol_gp_bench](../sokol_gp_bench) — performance benchmark
- [sokol_gp_primitives](../sokol_gp_primitives) — all primitive types and transforms
- [sokol_gp_effect](../sokol_gp_effect) — custom shader effects
- [sokol_gp_framebuffer](../sokol_gp_framebuffer) — render-to-framebuffer
- [sokol_gp_sdf](../sokol_gp_sdf) — signed distance field shader
