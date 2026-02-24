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
using static Sokol.STM;

public static unsafe class SokolGpBenchApp
{
    const int COUNT = 48;
    const int RECT_COUNT = 4;

    struct _state
    {
        public sg_image image1;
        public sg_image image2;
        public sg_view view1;
        public sg_view view2;

        // FPS counter
        public int fps;
        public ulong last_time;
    }

    static _state state = new _state();

    static sg_image CreateImage(int width, int height)
    {
        byte[] data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                data[idx + 0] = (byte)((x * 255) / width);
                data[idx + 1] = (byte)((y * 255) / height);
                data[idx + 2] = (byte)(255 - (x * y * 255) / (width * height));
                data[idx + 3] = 255;
            }

        sg_image_data img_data = default;
        img_data.mip_levels[0] = SG_RANGE(data);

        return sg_make_image(new sg_image_desc
        {
            width = width,
            height = height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8,
            data = img_data,
        });
    }

    // --- Bench functions ---

    static void BenchRepeatedTextured()
    {
        sgp_reset_color();
        sgp_set_view(0, state.view1);
        for (int y = 0; y < COUNT; y++)
            for (int x = 0; x < COUNT; x++)
                sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
        sgp_reset_view(0);
    }

    static void BenchMultipleTextured()
    {
        sgp_reset_color();
        for (int y = 0; y < COUNT; y++)
            for (int x = 0; x < COUNT; x++)
            {
                sgp_set_view(0, (x % 2 == 0) ? state.view1 : state.view2);
                sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
            }
        sgp_reset_view(0);
    }

    static void BenchColoredTextured()
    {
        sgp_reset_color();
        sgp_set_view(0, state.view1);
        for (int y = 0; y < COUNT; y++)
            for (int x = 0; x < COUNT; x++)
            {
                if (x % 3 == 0)       sgp_set_color(1.0f, 0.0f, 0.0f, 1.0f);
                else if (x % 3 == 1)  sgp_set_color(0.0f, 1.0f, 0.0f, 1.0f);
                else                  sgp_set_color(0.0f, 0.0f, 1.0f, 1.0f);
                sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
            }
        sgp_reset_view(0);
    }

    static void BenchRepeatedFilled()
    {
        sgp_reset_color();
        for (int y = 0; y < COUNT; y++)
            for (int x = 0; x < COUNT; x++)
                sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
    }

    static void BenchColoredFilled()
    {
        sgp_reset_color();
        for (int y = 0; y < COUNT; y++)
            for (int x = 0; x < COUNT; x++)
            {
                if (x % 3 == 0)       sgp_set_color(1.0f, 0.0f, 0.0f, 1.0f);
                else if (x % 3 == 1)  sgp_set_color(0.0f, 1.0f, 0.0f, 1.0f);
                else                  sgp_set_color(0.0f, 0.0f, 1.0f, 1.0f);
                sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
            }
    }

    static void BenchMixed()
    {
        for (int diagonal = 0; diagonal < 2 * COUNT - 1; diagonal++)
        {
            int advance = Math.Max(diagonal - COUNT + 1, 0);
            for (int y = diagonal - advance, x = advance; y >= 0 && x < COUNT; y--, x++)
            {
                if (x % 3 == 0)       sgp_set_color(1.0f, 0.0f, 0.0f, 1.0f);
                else if (x % 3 == 1)  sgp_set_color(0.0f, 1.0f, 0.0f, 1.0f);
                else                  sgp_set_color(0.0f, 0.0f, 1.0f, 1.0f);

                if ((x + y) % 2 == 0)
                {
                    sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
                }
                else
                {
                    sgp_set_view(0, state.view1);
                    sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
                    sgp_reset_view(0);
                }
            }
        }
    }

    static void BenchSyncMixed()
    {
        sgp_set_view(0, state.view1);
        sgp_reset_color();
        for (int y = 0; y < COUNT; y++)
            for (int x = 0; x < COUNT; x++)
            {
                if ((x + y) % 2 == 0)
                {
                    sgp_set_color(1.0f, 0.0f, 0.0f, 1.0f);
                    sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
                }
                else
                {
                    sgp_set_color(0.0f, 1.0f, 0.0f, 1.0f);
                    sgp_draw_filled_rect(x * RECT_COUNT * 2, y * RECT_COUNT * 2, RECT_COUNT, RECT_COUNT);
                }
            }
        sgp_reset_view(0);
    }

    static void DrawCat()
    {
        sgp_reset_color();
        sgp_set_view(0, state.view1);
        sgp_draw_filled_rect(0, 0, RECT_COUNT * COUNT * 2, RECT_COUNT * COUNT * 2);
        sgp_reset_view(0);
    }

    static void DrawRect()
    {
        sgp_reset_color();
        sgp_draw_filled_rect(0, 0, RECT_COUNT * COUNT * 2, RECT_COUNT * COUNT * 2);
    }

    // --- Callbacks ---

    [UnmanagedCallersOnly]
    private static unsafe void Init()
    {
        stm_setup();

        sg_setup(new sg_desc()
        {
            environment = sglue_environment(),
            logger = { func = &slog_func }
        });

        sgp_setup(new sgp_desc
        {
            max_vertices = 262144,
            max_commands = 32768,
        });

        state.image1 = CreateImage(128, 128);
        state.image2 = CreateImage(128, 128);
        state.view1 = sgp_make_texture_view_from_image(state.image1, "view1");
        state.view2 = sgp_make_texture_view_from_image(state.image2, "view2");
        state.fps = 0;
        state.last_time = stm_now();
    }

    [UnmanagedCallersOnly]
    private static unsafe void Frame()
    {
        int width = sapp_width();
        int height = sapp_height();

        sgp_begin(width, height);

        sgp_set_color(0.05f, 0.05f, 0.05f, 1.0f);
        sgp_clear();
        sgp_reset_color();

        int off = COUNT * RECT_COUNT * 2;

        BenchRepeatedTextured();

        sgp_translate(off, 0);
        BenchMultipleTextured();

        sgp_translate(off, 0);
        BenchColoredTextured();

        sgp_translate(-2 * off, off);
        BenchRepeatedFilled();

        sgp_translate(off, 0);
        BenchMixed();

        sgp_translate(off, 0);
        BenchColoredFilled();

        sgp_translate(-2 * off, off);
        DrawCat();

        sgp_translate(off, 0);
        DrawRect();

        sgp_translate(off, 0);
        BenchSyncMixed();

        sg_begin_pass(new sg_pass { swapchain = sglue_swapchain() });
        sgp_flush();
        sgp_end();
        sg_end_pass();
        sg_commit();

        // FPS counter
        state.fps++;
        ulong now = stm_now();
        if (stm_sec(now - state.last_time) >= 1.0)
        {
            Console.WriteLine($"FPS: {state.fps}");
            state.last_time = now;
            state.fps = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe void Event(sapp_event* e)
    {
    }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        sg_destroy_view(state.view1);
        sg_destroy_view(state.view2);
        sg_destroy_image(state.image1);
        sg_destroy_image(state.image2);
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
            width = 1280,
            height = 1280,
            window_title = "Bench (Sokol GP)",
            icon = { sokol_default = true },
            logger = { func = &slog_func },
        };
    }
}
