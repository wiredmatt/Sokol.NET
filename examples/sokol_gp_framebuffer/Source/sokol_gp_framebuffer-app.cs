using System;
using System.Runtime.InteropServices;
using Sokol;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SGP;
using static Sokol.SLog;
using System.Diagnostics;

public static unsafe class SokolGpFramebufferApp
{
    static sg_image fb_color_image;
    static sg_image fb_resolve_image;
    static sg_image fb_depth_image;
    static sg_view fb_color_view;
    static sg_view fb_resolve_view;
    static sg_view fb_depth_view;
    static sg_view fb_tex_view;
    static sg_attachments fb_attachments;
    static sg_sampler linear_sampler;

    static void DrawTriangles()
    {
        const float PI = 3.14159265f;
        sgp_point* points_buffer = stackalloc sgp_point[4096];

        sgp_state* st = sgp_query_state();
        int width = st->viewport.w, height = st->viewport.h;
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        float w = height * 0.3f;
        uint count = 0;
        float step = (2.0f * PI) / 6.0f;
        for (float theta = 0.0f; theta <= 2.0f * PI + step * 0.5f; theta += step)
        {
            points_buffer[count++] = new sgp_point { x = hw * 1.33f + w * (float)Math.Cos(theta), y = hh * 1.33f - w * (float)Math.Sin(theta) };
            if (count % 3 == 1)
                points_buffer[count++] = new sgp_point { x = hw, y = hh };
        }
        sgp_set_color(1.0f, 0.0f, 1.0f, 1.0f);
        sgp_draw_filled_triangles_strip(in points_buffer[0], count);
    }

    static void DrawFBO()
    {
        sgp_begin(128, 128);
        sgp_project(0, 128, 128, 0);
        DrawTriangles();

        sg_pass_action pass_action = default;
        pass_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        pass_action.colors[0].store_action = sg_store_action.SG_STOREACTION_DONTCARE;
        pass_action.colors[0].clear_value = new sg_color { r = 1.0f, g = 1.0f, b = 1.0f, a = 0.2f };

        sg_begin_pass(new sg_pass { action = pass_action, attachments = fb_attachments });
        sgp_flush();
        sgp_end();
        sg_end_pass();
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

        // Color attachment (multi-sampled)
        fb_color_image = sg_make_image(new sg_image_desc
        {
            usage = new sg_image_usage { color_attachment = true },
            width = 128,
            height = 128,
            pixel_format = (sg_pixel_format)sapp_color_format(),
            sample_count = sapp_sample_count(),
        });

        // Resolve attachment (single-sample, can be sampled as texture)
        fb_resolve_image = sg_make_image(new sg_image_desc
        {
            usage = new sg_image_usage { resolve_attachment = true },
            width = 128,
            height = 128,
            pixel_format = (sg_pixel_format)sapp_color_format(),
            sample_count = 1,
        });

        // Depth/stencil attachment
        fb_depth_image = sg_make_image(new sg_image_desc
        {
            usage = new sg_image_usage { depth_stencil_attachment = true },
            width = 128,
            height = 128,
            pixel_format = (sg_pixel_format)sapp_depth_format(),
            sample_count = sapp_sample_count(),
        });

        // Views
        fb_color_view = sg_make_view(new sg_view_desc
        {
            color_attachment = new sg_image_view_desc { image = fb_color_image },
            label = "fb_color_view",
        });
        fb_resolve_view = sg_make_view(new sg_view_desc
        {
            resolve_attachment = new sg_image_view_desc { image = fb_resolve_image },
            label = "fb_resolve_view",
        });
        fb_depth_view = sg_make_view(new sg_view_desc
        {
            depth_stencil_attachment = new sg_image_view_desc { image = fb_depth_image },
            label = "fb_depth_view",
        });
        fb_tex_view = sgp_make_texture_view_from_image(fb_resolve_image, "fb_tex_view");

        // Attachments struct (used inline in sg_pass)
        fb_attachments = default;
        fb_attachments.colors[0] = fb_color_view;
        fb_attachments.resolves[0] = fb_resolve_view;
        fb_attachments.depth_stencil = fb_depth_view;

        // Linear clamp sampler
        linear_sampler = sg_make_sampler(new sg_sampler_desc
        {
            min_filter = sg_filter.SG_FILTER_LINEAR,
            mag_filter = sg_filter.SG_FILTER_LINEAR,
            wrap_u = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
            wrap_v = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
        });
    }

    [UnmanagedCallersOnly]
    static void Frame()
    {
        int width = sapp_width();
        int height = sapp_height();

        sgp_begin(width, height);
        sgp_set_color(0.05f, 0.05f, 0.05f, 1.0f);
        sgp_clear();
        sgp_reset_color();

        float time = sapp_frame_count() / 60.0f;
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);
        DrawFBO();

        int i = 0;
        for (int y = 0; y < height; y += 192)
        {
            for (int x = 0; x < width; x += 192)
            {
                sgp_push_transform();
                sgp_rotate_at(time, x + 64, y + 64);
                sgp_set_view(0, fb_tex_view);
                sgp_set_sampler(0, linear_sampler);
                if (i % 2 == 0)
                {
                    sgp_draw_filled_rect(x, y, 128, 128);
                }
                else
                {
                    sgp_draw_textured_rect(0,
                        new sgp_rect { x = x, y = y, w = 128, h = 128 },
                        new sgp_rect { x = 0, y = 0, w = 128, h = 128 });
                }
                sgp_reset_view(0);
                sgp_reset_sampler(0);
                sgp_pop_transform();
                i++;
            }
        }

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
        sg_destroy_view(fb_color_view);
        sg_destroy_view(fb_resolve_view);
        sg_destroy_view(fb_depth_view);
        sg_destroy_view(fb_tex_view);
        sg_destroy_image(fb_color_image);
        sg_destroy_image(fb_resolve_image);
        sg_destroy_image(fb_depth_image);
        sg_destroy_sampler(linear_sampler);
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
            window_title = "Frame buffer (Sokol GP)",
            icon = { sokol_default = true },
            logger = { func = &slog_func },
            sample_count = 4,
        };
    }
}
