# sokol_gp_bench

A **benchmarking sample** for the [sokol_gp](https://github.com/edubart/sokol_gp) 2D graphics painter API, ported to C# with Sokol.NET.

## Description

This sample stress-tests the `sokol_gp` drawing pipeline by rendering eight different draw patterns simultaneously at high volume. It measures and displays the frames-per-second (FPS) in the window title to give a clear picture of GPU and CPU throughput.

### Draw patterns benchmarked

| Pattern | Description |
|---------|-------------|
| Filled rectangles | Axis-aligned solid rectangles |
| Textured rectangles | Rectangles with a texture applied |
| Outlined rectangles | Hollow stroked rectangles |
| Lines | Individual line segments |
| Points | Individual screen-space points |
| Filled triangles | Solid triangles |
| Triangle strips | Connected triangle fans |
| Mixed | All of the above interleaved |

### Key APIs demonstrated

- `sgp_begin` / `sgp_end` / `sgp_flush` — frame lifecycle
- `sgp_set_view` / `sgp_reset_view` — texture channel binding
- `sgp_make_texture_view_from_image` — wrap an `sg_image` into an SGP view
- `sgp_draw_filled_rect` / `sgp_draw_textured_rect` — primitive drawing
- `stm_now` / `stm_ms` — high-resolution timing for FPS counter

## Reference

Original C sample: [`sample-bench.c`](https://github.com/edubart/sokol_gp/blob/master/samples/sample-bench.c)

## Related

- [sokol_gp repository](https://github.com/edubart/sokol_gp) — upstream library by Eduardo Bart
- [sokol_gp_blend](../sokol_gp_blend) — blend mode showcase
- [sokol_gp_primitives](../sokol_gp_primitives) — all primitive types and transforms
- [sokol_gp_effect](../sokol_gp_effect) — custom shader effects
- [sokol_gp_framebuffer](../sokol_gp_framebuffer) — render-to-framebuffer
- [sokol_gp_sdf](../sokol_gp_sdf) — signed distance field shader
