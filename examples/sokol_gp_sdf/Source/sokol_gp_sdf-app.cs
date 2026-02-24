using System;
using Sokol;
using System.Runtime.InteropServices;
using System.Diagnostics;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SGP;
using static Sokol.SLog;
using static sample_sdf_shader_cs_sdf.Shaders;

public static unsafe class SokolGpSdfApp
{
    static sg_pipeline pip;
    static sg_shader shd;

    [UnmanagedCallersOnly]
    private static void Init()
    {
        sg_setup(new sg_desc()
        {
            environment = sglue_environment(),
            logger = { func = &slog_func }
        });

        sgp_setup(new sgp_desc());

        shd = sg_make_shader(sdf_program_shader_desc(sg_query_backend()));
        pip = sgp_make_pipeline(new sgp_pipeline_desc
        {
            shader = shd,
            has_vs_color = true,
        });
    }

    [UnmanagedCallersOnly]
    private static void Frame()
    {
        int width = sapp_width(), height = sapp_height();
        sgp_begin(width, height);

        sgp_set_pipeline(pip);

        sdf_vs_uniforms_t vs_uniform = new sdf_vs_uniforms_t();
        vs_uniform.iResolution.x = (float)width;
        vs_uniform.iResolution.y = (float)height;

        sdf_fs_uniforms_t fs_uniform = new sdf_fs_uniforms_t();
        fs_uniform.iTime = sapp_frame_count() / 60.0f;

        sgp_set_uniform(&vs_uniform, (uint)sizeof(sdf_vs_uniforms_t),
                        &fs_uniform, (uint)sizeof(sdf_fs_uniforms_t));
        sgp_unset_view(0);
        sgp_draw_filled_rect(0, 0, width, height);
        sgp_reset_view(0);
        sgp_reset_pipeline();

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
        sg_destroy_pipeline(pip);
        sg_destroy_shader(shd);
        sgp_shutdown();
        sg_shutdown();

        if (Debugger.IsAttached)
        {
            Environment.Exit(0);
        }
    }

    public static sapp_desc sokol_main()
    {
        return new sapp_desc()
        {
            init_cb = &Init,
            frame_cb = &Frame,
            event_cb = &Event,
            cleanup_cb = &Cleanup,
            window_title = "SDF (Sokol GP)",
            icon = { sokol_default = true },
            logger = { func = &slog_func }
        };
    }
}
