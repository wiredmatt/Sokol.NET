
using Sokol;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.Utils;
using static Sokol.SApp;
using static Sokol.SG.sg_vertex_format;
using static Sokol.SG.sg_load_action;
using static sdf_sapp_shader_cs.Shaders;
using System.Diagnostics;
using static Sokol.SLog;
using static Sokol.SDebugUI;
using static Sokol.SImgui;

public static unsafe class SdfApp
{

    static bool PauseUpdate = false;

    struct State
    {
        public sg_pipeline pip;
        public sg_bindings bind;
        public sg_pass_action pass_action;
        public vs_params_t vs_params;
    }

    static State state = default;

    public static SApp.sapp_desc sokol_main()
    {
        return new SApp.sapp_desc()
        {
            init_cb = &Init,
            frame_cb = &Frame,
            event_cb = &Event,
            cleanup_cb = &Cleanup,
            width = 0,  // Let iOS determine the size based on orientation
            height = 0, // Let iOS determine the size based on orientation
            sample_count = 4,
            window_title = "Sdf (sokol-app)",
            icon = { sokol_default = true },
            logger = {
                func = &slog_func,
            }
        };
    }


    [UnmanagedCallersOnly]
    public static unsafe void Init()
    {
        sg_setup(new sg_desc()
        {
            environment = sglue_environment(),
            logger =    {
                func = &SLog.slog_func,
            }
        });

        // Setup ImGui for back button
        simgui_setup(new simgui_desc_t
        {
            logger = {
                func = &slog_func,
            }
        });

        // a vertex buffer to render a 'fullscreen triangle'
        float[] fsq_verts = { -1.0f, -3.0f, 3.0f, 1.0f, -1.0f, 1.0f };
        state.bind.vertex_buffers[0] = sg_make_buffer(new sg_buffer_desc()
        {
            data = SG_RANGE(fsq_verts),
            label = "fsq vertices"
        });

        // shader and pipeline object for rendering a fullscreen quad

        sg_pipeline_desc desc = default;
        desc.layout.attrs[ATTR_sdf_position].format = SG_VERTEXFORMAT_FLOAT2;
        desc.shader = sg_make_shader(sdf_shader_desc(sg_query_backend()));
        state.pip = sg_make_pipeline(desc);

        state.pass_action = default;
        state.pass_action.colors[0].load_action = SG_LOADACTION_DONTCARE;

    }


    [UnmanagedCallersOnly]
    public static unsafe void Frame()
    {
        if (PauseUpdate) return;

        // Setup ImGui frame
        simgui_new_frame(new simgui_frame_desc_t
        {
            width = sapp_width(),
            height = sapp_height(),
            delta_time = sapp_frame_duration(),
            dpi_scale = 1
        });

        // Draw back button
        SamplebrowserApp.DrawBackButton();

        int w = sapp_width();
        int h = sapp_height();
        state.vs_params.time += (float)sapp_frame_duration();
        state.vs_params.aspect = (float)w / (float)h;
        sg_begin_pass(new sg_pass() { action = state.pass_action, swapchain = sglue_swapchain() });
        sg_apply_pipeline(state.pip);
        sg_apply_bindings(state.bind);
        sg_apply_uniforms(UB_vs_params, SG_RANGE<vs_params_t>(ref state.vs_params));
        sg_draw(0, 3, 1);
        simgui_render();
        sg_end_pass();
        sg_commit();
    }

    [UnmanagedCallersOnly]
    public static void Cleanup()
    {
        // Destroy Sokol graphics resources
        sg_destroy_buffer(state.bind.vertex_buffers[0]);
        sg_destroy_pipeline(state.pip);

        // Shutdown ImGui
        simgui_shutdown();

        // Don't call sg_shutdown - SampleBrowser manages graphics context
        // sg_shutdown();

        // Reset state for next run
        state = new State();
    }

    [UnmanagedCallersOnly]
    public static unsafe void Event(SApp.sapp_event* e)
    {
        if (simgui_handle_event(*e))
            return;

        if (e->type == SApp.sapp_event_type.SAPP_EVENTTYPE_KEY_UP)
        {
            PauseUpdate = !PauseUpdate;
        }
    }

}