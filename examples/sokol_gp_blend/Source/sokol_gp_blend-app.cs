using System;
using Sokol;
using System.Runtime.InteropServices;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.Utils;
using System.Diagnostics;
using static Sokol.SLog;
using static Sokol.SGP;

public static unsafe class SokolGpBlendApp
{
    static void Draw3Rects(float brightness, float alpha)
    {
        sgp_translate(2.5f, 2.5f);
        sgp_set_color(brightness, 0.0f, 0.0f, alpha);
        sgp_draw_filled_rect(0, 0, 10, 10);
        sgp_set_color(0.0f, brightness, 0.0f, alpha);
        sgp_draw_filled_rect(0, 5, 10, 10);
        sgp_set_color(0.0f, 0.0f, brightness, alpha);
        sgp_draw_filled_rect(5, 2.5f, 10, 10);
    }

    static void DrawRects(float ratio)
    {
        sgp_project(0, 60 * ratio, 0, 60);
        sgp_set_color(1.0f, 1.0f, 1.0f, 1.0f);

        // none
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_NONE);
        sgp_push_transform();
        sgp_translate(0, 0);
        Draw3Rects(1.0f, 0.5f);
        sgp_pop_transform();

        // blend
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);
        sgp_push_transform();
        sgp_translate(20, 0);
        Draw3Rects(1.0f, 0.5f);
        sgp_pop_transform();

        // blend premultiplied
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND_PREMULTIPLIED);
        sgp_push_transform();
        sgp_translate(40, 0);
        Draw3Rects(1.0f, 0.5f);
        sgp_pop_transform();

        // add
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_ADD);
        sgp_push_transform();
        sgp_translate(20, 20);
        Draw3Rects(1.0f, 0.5f);
        sgp_pop_transform();

        // add premultiplied
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_ADD_PREMULTIPLIED);
        sgp_push_transform();
        sgp_translate(40, 20);
        Draw3Rects(1.0f, 0.5f);
        sgp_pop_transform();

        // mod
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_MOD);
        sgp_push_transform();
        sgp_translate(20, 40);
        Draw3Rects(1.0f, 0.5f);
        sgp_pop_transform();

        // mul
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_MUL);
        sgp_push_transform();
        sgp_translate(40, 40);
        Draw3Rects(1.0f, 0.5f);
        sgp_pop_transform();
    }

    static void DrawCheckerboard(int width, int height)
    {
        sgp_set_color(0.2f, 0.2f, 0.2f, 1.0f);
        sgp_clear();
        sgp_set_color(0.4f, 0.4f, 0.4f, 1.0f);
        for (int y = 0; y < height / 32 + 1; y++)
            for (int x = 0; x < width / 32 + 1; x++)
                if ((x + y) % 2 == 0)
                    sgp_draw_filled_rect(x * 32, y * 32, 32, 32);
        sgp_reset_color();
    }

    [UnmanagedCallersOnly]
    private static void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger = { func = &slog_func }
        });

        sgp_setup(new sgp_desc());
    }

    [UnmanagedCallersOnly]
    private static void Frame()
    {
        int width = sapp_width();
        int height = sapp_height();

        sgp_begin(width, height);

        DrawCheckerboard(width, height);
        DrawRects(width / (float)height);

        sg_begin_pass(new sg_pass { swapchain = sglue_swapchain() });
        sgp_flush();
        sgp_end();
        sg_end_pass();
        sg_commit();
    }

    [UnmanagedCallersOnly]
    private static void Event(sapp_event* e)
    {
    }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        sgp_shutdown();
        sg_shutdown();

        if (Debugger.IsAttached)
            Environment.Exit(0);
    }

    public static SApp.sapp_desc sokol_main()
    {
        return new SApp.sapp_desc()
        {
            init_cb = &Init,
            frame_cb = &Frame,
            event_cb = &Event,
            cleanup_cb = &Cleanup,
            window_title = "Blend (Sokol GP)",
            icon = { sokol_default = true },
            logger = { func = &slog_func },
        };
    }
}
