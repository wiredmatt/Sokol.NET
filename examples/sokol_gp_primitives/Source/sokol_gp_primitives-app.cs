using System;
using System.Runtime.InteropServices;
using Sokol;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SGP;
using static Sokol.SLog;
using System.Diagnostics;

public static unsafe class SokolGpPrimitivesApp
{
    static void DrawRects()
    {
        sgp_state* st = sgp_query_state();
        int width = st->viewport.w, height = st->viewport.h;
        int size = 64, hsize = size / 2;
        float time = sapp_frame_count() / 60.0f;
        float t = (1.0f + MathF.Sin(time)) / 2.0f;

        // left – translate
        sgp_push_transform();
        sgp_translate(width * 0.25f - hsize, height * 0.5f - hsize);
        sgp_translate(0.0f, 2 * size * t - size);
        sgp_set_color(t, 0.3f, 1.0f - t, 1.0f);
        sgp_draw_filled_rect(0, 0, size, size);
        sgp_pop_transform();

        // middle – rotate
        sgp_push_transform();
        sgp_translate(width * 0.5f - hsize, height * 0.5f - hsize);
        sgp_rotate_at(time, hsize, hsize);
        sgp_set_color(t, 1.0f - t, 0.3f, 1.0f);
        sgp_draw_filled_rect(0, 0, size, size);
        sgp_pop_transform();

        // right – scale
        sgp_push_transform();
        sgp_translate(width * 0.75f - hsize, height * 0.5f - hsize);
        sgp_scale_at(t + 0.25f, t + 0.5f, hsize, hsize);
        sgp_set_color(0.3f, t, 1.0f - t, 1.0f);
        sgp_draw_filled_rect(0, 0, size, size);
        sgp_pop_transform();
    }

    static void DrawPoints()
    {
        sgp_set_color(1.0f, 1.0f, 1.0f, 1.0f);
        sgp_state* st = sgp_query_state();
        int width = st->viewport.w, height = st->viewport.h;
        sgp_point* pts = stackalloc sgp_point[4096];
        uint count = 0;
        for (int y = 64; y < height - 64 && count < 4096; y += 8)
            for (int x = 64; x < width - 64 && count < 4096; x += 8)
                pts[count++] = new sgp_point { x = x, y = y };
        sgp_draw_points(in pts[0], count);
    }

    static void DrawLines()
    {
        sgp_set_color(1.0f, 1.0f, 1.0f, 1.0f);
        sgp_state* st = sgp_query_state();
        float cx = st->viewport.w / 2.0f, cy = st->viewport.h / 2.0f;
        sgp_point* pts = stackalloc sgp_point[4096];
        uint count = 0;
        pts[count++] = new sgp_point { x = cx, y = cy };
        for (float theta = 0.0f; theta <= 2.0f * MathF.PI * 8.0f; theta += MathF.PI / 16.0f)
        {
            float r = 10.0f * theta;
            pts[count++] = new sgp_point { x = cx + r * MathF.Cos(theta), y = cy + r * MathF.Sin(theta) };
        }
        sgp_draw_lines_strip(in pts[0], count);

        // X cross
        sgp_push_transform();
        sgp_translate(st->viewport.w / 2, st->viewport.h / 2);
        int xs = 32;
        sgp_line* lines = stackalloc sgp_line[2];
        lines[0] = new sgp_line { a = new sgp_point { x = -xs, y = -xs }, b = new sgp_point { x = xs, y = xs } };
        lines[1] = new sgp_line { a = new sgp_point { x = xs, y = -xs }, b = new sgp_point { x = -xs, y = xs } };
        sgp_draw_lines(in lines[0], 2);
        sgp_pop_transform();
    }

    static void DrawTriangles()
    {
        float time = sapp_frame_count() / 60.0f;
        sgp_state* st = sgp_query_state();
        int width = st->viewport.w, height = st->viewport.h;
        float hw = width * 0.5f, hh = height * 0.5f;
        float w = height * 0.2f;

        sgp_push_transform();
        sgp_translate(-w * 1.5f, 0.0f);

        // Single triangle
        sgp_set_color(1.0f, 0.0f, 1.0f, 1.0f);
        sgp_draw_filled_triangle(hw - w, hh + w, hw, hh - w, hw + w, hh + w);

        // Hexagon (filled triangle strip)
        sgp_translate(w * 3.0f, -hh * 0.5f);
        sgp_set_color(0.0f, 1.0f, 1.0f, 1.0f);
        {
            float step = (2.0f *  MathF.PI) / 6.0f;
            sgp_point* pts = stackalloc sgp_point[4096];
            uint count = 0;
            for (float theta = 0.0f; theta <= 2.0f * MathF.PI + step * 0.5f; theta += step)
            {
                pts[count++] = new sgp_point { x = hw + w * MathF.Cos(theta), y = hh - w * MathF.Sin(theta) };
                if (count % 3 == 1)
                    pts[count++] = new sgp_point { x = hw, y = hh };
            }
            sgp_draw_filled_triangles_strip(in pts[0], count);
        }

        // Color wheel (sgp_draw with per-vertex color)
        sgp_translate(0.0f, hh);
        sgp_set_color(1.0f, 1.0f, 1.0f, 1.0f);
        {
            float step = (2.0f * MathF.PI) / 64.0f;
            sgp_vertex* verts = stackalloc sgp_vertex[4096];
            uint count = 0;
            for (float theta = 0.0f; theta <= 2.0f * MathF.PI + step * 0.5f; theta += step)
            {
                verts[count].position = new sgp_vec2 { x = hw + w * MathF.Cos(theta), y = hh - w * MathF.Sin(theta) };
                verts[count].color = new sgp_color_ub4
                {
                    r = (byte)((MathF.Sin(theta + time * 1) + 1.0f) * 0.5f * 255.0f),
                    g = (byte)((MathF.Sin(theta + time * 2) + 1.0f) * 0.5f * 255.0f),
                    b = (byte)((MathF.Sin(theta + time * 4) + 1.0f) * 0.5f * 255.0f),
                    a = 255,
                };
                count++;
                if (count % 3 == 1)
                {
                    verts[count].position = new sgp_vec2 { x = hw, y = hh };
                    verts[count].color = new sgp_color_ub4 { r = 255, g = 255, b = 255, a = 255 };
                    count++;
                }
            }
            sgp_draw(sg_primitive_type.SG_PRIMITIVETYPE_TRIANGLE_STRIP, in verts[0], count);
        }

        sgp_pop_transform();
    }

    [UnmanagedCallersOnly]
    static void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger = { func = &slog_func }
        });
        sgp_setup(new sgp_desc());
    }

    [UnmanagedCallersOnly]
    static void Frame()
    {
        int width = sapp_width(), height = sapp_height();
        int hw = width / 2, hh = height / 2;

        sgp_begin(width, height);
        sgp_set_color(0.05f, 0.05f, 0.05f, 1.0f);
        sgp_clear();
        sgp_reset_color();

        // top-left: rects (twice, second with scissor)
        sgp_viewport(0, 0, hw, hh);
        sgp_set_color(0.1f, 0.1f, 0.1f, 1.0f);
        sgp_clear();
        sgp_reset_color();
        sgp_push_transform();
        sgp_translate(0.0f, -hh / 4.0f);
        DrawRects();
        sgp_pop_transform();
        sgp_push_transform();
        sgp_translate(0.0f, hh / 4.0f);
        sgp_scissor(0, 0, hw, 3 * hh / 4);
        DrawRects();
        sgp_reset_scissor();
        sgp_pop_transform();

        // top-right: triangles
        sgp_viewport(hw, 0, hw, hh);
        DrawTriangles();

        // bottom-left: points
        sgp_viewport(0, hh, hw, hh);
        DrawPoints();

        // bottom-right: lines
        sgp_viewport(hw, hh, hw, hh);
        sgp_set_color(0.1f, 0.1f, 0.1f, 1.0f);
        sgp_clear();
        sgp_reset_color();
        DrawLines();

        sg_begin_pass(new sg_pass { swapchain = sglue_swapchain() });
        sgp_flush();
        sgp_end();
        sg_end_pass();
        sg_commit();
    }

    [UnmanagedCallersOnly]
    static void Event(sapp_event* e) { }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        sgp_shutdown();
        sg_shutdown();
        if (Debugger.IsAttached)
            Environment.Exit(0);
    }

    public static sapp_desc sokol_main()
    {
        return new sapp_desc
        {
            init_cb = &Init,
            frame_cb = &Frame,
            event_cb = &Event,
            cleanup_cb = &Cleanup,
            window_title = "Primitives (Sokol GP)",
            icon = { sokol_default = true },
            logger = { func = &slog_func },
            sample_count = 4,
        };
    }
}
