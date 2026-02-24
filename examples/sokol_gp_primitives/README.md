# sokol_gp_primitives

A **primitives and transforms** showcase for the [sokol_gp](https://github.com/edubart/sokol_gp) 2D graphics painter API, ported to C# with Sokol.NET.

## Description

This sample demonstrates every drawing primitive and transform available in `sokol_gp`. The screen is split into four viewports, each highlighting a different category of API:

| Viewport | Content |
|----------|---------|
| Top-left | Filled & outlined rectangles with translate / rotate / scale |
| Top-right | Points drawn in a regular grid pattern |
| Bottom-left | Lines — Archimedes spiral and crossing diagonals |
| Bottom-right | Triangles — single triangle, hexagon strip, and a per-vertex color wheel |

### Key APIs demonstrated

- `sgp_viewport` / `sgp_scissor` / `sgp_reset_scissor` — viewport and scissor rectangle
- `sgp_translate` / `sgp_rotate_at` / `sgp_scale_at` — 2D transform stack operations
- `sgp_push_transform` / `sgp_pop_transform` — save/restore transform state
- `sgp_draw_filled_rect` / `sgp_draw_outlined_rect` — rectangle primitives
- `sgp_draw_points` — batch point drawing via `sgp_point[]`
- `sgp_draw_lines` — batch line drawing via `sgp_line[]`
- `sgp_draw_lines_strip` — connected polyline from a point array
- `sgp_draw` with `SG_PRIMITIVETYPE_TRIANGLE_STRIP` — custom geometry with per-vertex `sgp_vertex` (position + texcoord + `sgp_color_ub4` RGBA)
- `sgp_query_state` — query the current viewport for dynamic layout

## Reference

Original C sample: [`sample-primitives.c`](https://github.com/edubart/sokol_gp/blob/master/samples/sample-primitives.c)

## Related

- [sokol_gp repository](https://github.com/edubart/sokol_gp) — upstream library by Eduardo Bart
- [sokol_gp_bench](../sokol_gp_bench) — performance benchmark
- [sokol_gp_blend](../sokol_gp_blend) — blend mode showcase
- [sokol_gp_effect](../sokol_gp_effect) — custom shader effects
- [sokol_gp_framebuffer](../sokol_gp_framebuffer) — render-to-framebuffer
- [sokol_gp_sdf](../sokol_gp_sdf) — signed distance field shader
