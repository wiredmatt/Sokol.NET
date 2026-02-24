# sokol_gp_framebuffer

A **render-to-framebuffer** sample for the [sokol_gp](https://github.com/edubart/sokol_gp) 2D graphics painter API, ported to C# with Sokol.NET.

## Description

This sample shows how to use `sokol_gp` with off-screen render targets (framebuffers). An animated triangle fan is rendered into a multi-sampled off-screen framebuffer, which is then resolved to a single-sample texture and tiled across the screen with alternating orientations using rotation transforms.

### Render pipeline

1. **Off-screen pass** — Draw animated triangles into an MSAA framebuffer (`sg_attachments` with color, resolve, and depth images).
2. **On-screen pass** — Tile the resolved texture across the full window using `sgp_draw_textured_rect` and `sgp_rotate_at` to alternate tile orientations.

### Key APIs demonstrated

- `sg_make_image` with `SG_USAGE_RENDER_ATTACHMENT` — create render-target images
- `sg_make_view` / `sg_view_desc` — create attachment views (color, resolve, depth)
- `sg_make_attachments` — assemble a framebuffer object
- `sgp_begin` used twice — once per pass (off-screen + on-screen)
- `sgp_draw_filled_triangles_strip` — draw a triangle fan from a point array
- `sgp_draw_textured_rect` — draw a textured (resolved) rectangle tile
- `sgp_rotate_at` — rotate around a specific pivot point
- `sgp_query_state` — query current viewport size for dynamic layout

## Reference

Original C sample: [`sample-framebuffer.c`](https://github.com/edubart/sokol_gp/blob/master/samples/sample-framebuffer.c)

## Related

- [sokol_gp repository](https://github.com/edubart/sokol_gp) — upstream library by Eduardo Bart
- [sokol_gp_bench](../sokol_gp_bench) — performance benchmark
- [sokol_gp_blend](../sokol_gp_blend) — blend mode showcase
- [sokol_gp_primitives](../sokol_gp_primitives) — all primitive types and transforms
- [sokol_gp_effect](../sokol_gp_effect) — custom shader effects
- [sokol_gp_sdf](../sokol_gp_sdf) — signed distance field shader
