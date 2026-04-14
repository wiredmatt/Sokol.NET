using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sokol;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SLog;
using static Sokol.NanoVG;

public static unsafe class NanovgdemoApp
{
    struct _state
    {
        public sg_pass_action pass_action;
    }

    static _state      state    = new _state();
    static IntPtr      vg       = IntPtr.Zero;
    static Demo.DemoData? demoData = null;
    static float       mouseX        = 0f;
    static float       mouseY        = 0f;
    static bool        blowup        = false;
    static double      time          = 0.0;
    static double      lastTapTime   = -1.0;
    const  double      DoubleTapMs   = 0.3;

    [UnmanagedCallersOnly]
    private static void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger = { func = &slog_func },
        });

        FileSystem.Instance.Initialize();

        // NanoVG context — ANTIALIAS+STENCIL_STROKES (no NVG_DEBUG for release)
        vg = nvgCreateSokol(NVG_ANTIALIAS | NVG_STENCIL_STROKES);

        // Pass action: clear color, depth and stencil (NanoVG needs stencil)
        state.pass_action = default;
        state.pass_action.colors[0].load_action  = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.colors[0].clear_value  = new sg_color { r = 0.3f, g = 0.3f, b = 0.32f, a = 1.0f };
        state.pass_action.depth.load_action      = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.depth.clear_value      = 1.0f;
        state.pass_action.stencil.load_action    = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.stencil.clear_value    = 0;

        demoData = Demo.LoadDemoData(vg);
    }

    [UnmanagedCallersOnly]
    private static void Frame()
    {
        FileSystem.Instance.Update();

        time += sapp_frame_duration();

        float dpiScale = sapp_dpi_scale();
        float winW     = sapp_widthf()  / dpiScale;
        float winH     = sapp_heightf() / dpiScale;

        sg_begin_pass(new sg_pass { action = state.pass_action, swapchain = sglue_swapchain() });

        if (vg != IntPtr.Zero && demoData != null)
        {
            nvgBeginFrame(vg, winW, winH, dpiScale);
            Demo.RenderDemo(vg, mouseX, mouseY, winW, winH, (float)time, blowup, demoData);
            nvgEndFrame(vg);
        }

        sg_end_pass();
        sg_commit();
    }

    [UnmanagedCallersOnly]
    private static void Event(sapp_event* e)
    {
        switch (e->type)
        {
            case sapp_event_type.SAPP_EVENTTYPE_MOUSE_MOVE:
                mouseX = e->mouse_x / sapp_dpi_scale();
                mouseY = e->mouse_y / sapp_dpi_scale();
                break;
            case sapp_event_type.SAPP_EVENTTYPE_TOUCHES_BEGAN:
            case sapp_event_type.SAPP_EVENTTYPE_TOUCHES_MOVED:
                if (e->num_touches > 0)
                {
                    mouseX = e->touches[0].pos_x / sapp_dpi_scale();
                    mouseY = e->touches[0].pos_y / sapp_dpi_scale();
                }
                break;
            case sapp_event_type.SAPP_EVENTTYPE_TOUCHES_ENDED:
                if (time - lastTapTime <= DoubleTapMs)
                {
                    blowup = !blowup;
                    lastTapTime = -1.0;
                }
                else
                {
                    lastTapTime = time;
                }
                break;
            case sapp_event_type.SAPP_EVENTTYPE_KEY_DOWN:
                if (e->key_code == sapp_keycode.SAPP_KEYCODE_SPACE)
                    blowup = !blowup;
                break;
        }
    }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        if (demoData != null && vg != IntPtr.Zero)
            Demo.FreeDemoData(vg, demoData);
        if (vg != IntPtr.Zero)
            nvgDeleteSokol(vg);
        FileSystem.Instance.Shutdown();
        sg_shutdown();

        if (Debugger.IsAttached)
            Environment.Exit(0);
    }

    public static sapp_desc sokol_main()
    {
        return new sapp_desc
        {
            init_cb      = &Init,
            frame_cb     = &Frame,
            event_cb     = &Event,
            cleanup_cb   = &Cleanup,
            width        = 1000,
            height       = 600,
            sample_count = 4,
            window_title = "NanoVG (Sokol.NET)",
            icon         = { sokol_default = true },
            logger       = { func = &slog_func },
        };
    }
}

