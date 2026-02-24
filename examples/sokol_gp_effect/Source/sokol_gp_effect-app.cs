using System;
using System.Runtime.InteropServices;
using Sokol;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.Utils;
using static Sokol.SFetch;
using static Sokol.StbImage;
using static Sokol.SGP;
using static Sokol.SLog;
using static sample_effect_shader_cs_effect.Shaders;
using System.Diagnostics;

public static unsafe class SokolGpEffectApp
{
    struct _state
    {
        public sg_image image;
        public sg_image perlin_image;
        public sg_view view;
        public sg_view perlin_view;
        public sg_sampler linear_sampler;
        public sg_pipeline pip;
        public sg_shader shd;
    }

    static _state state = new _state();
    static SharedBuffer fetch_buffer = SharedBuffer.Create(4 * 1024 * 1024);
    static SharedBuffer perlin_fetch_buffer = SharedBuffer.Create(2 * 1024 * 1024);

    [UnmanagedCallersOnly]
    static void FetchImageCallback(sfetch_response_t* response)
    {
        if (!response->fetched) return;
        int w = 0, h = 0, ch = 0;
        byte* pixels = stbi_load_csharp(in fetch_buffer.Buffer[0], (int)response->data.size, ref w, ref h, ref ch, 4);
        if (pixels == null) return;
        var span = new ReadOnlySpan<byte>(pixels, w * h * 4);
        sg_image_desc desc = default;
        desc.width = w;
        desc.height = h;
        desc.pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8;
        desc.label = "effect-image";
        desc.data.mip_levels[0] = SG_RANGE(span);
        sg_init_image(state.image, desc);
        stbi_image_free_csharp(pixels);
        sg_init_view(state.view, new sg_view_desc { texture = { image = state.image }, label = "effect-view" });
    }

    [UnmanagedCallersOnly]
    static void FetchPerlinCallback(sfetch_response_t* response)
    {
        if (!response->fetched) return;
        int w = 0, h = 0, ch = 0;
        byte* pixels = stbi_load_csharp(in perlin_fetch_buffer.Buffer[0], (int)response->data.size, ref w, ref h, ref ch, 4);
        if (pixels == null) return;
        var span = new ReadOnlySpan<byte>(pixels, w * h * 4);
        sg_image_desc desc = default;
        desc.width = w;
        desc.height = h;
        desc.pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8;
        desc.label = "perlin-image";
        desc.data.mip_levels[0] = SG_RANGE(span);
        sg_init_image(state.perlin_image, desc);
        stbi_image_free_csharp(pixels);
        sg_init_view(state.perlin_view, new sg_view_desc { texture = { image = state.perlin_image }, label = "perlin-view" });
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

        sfetch_setup(new sfetch_desc_t
        {
            max_requests = 2,
            num_channels = 1,
            num_lanes = 2,
        });

        // Pre-allocate image and view handles (filled when fetch completes)
        state.image = sg_alloc_image();
        state.perlin_image = sg_alloc_image();
        state.view = sg_alloc_view();
        state.perlin_view = sg_alloc_view();

        // Linear repeat sampler
        state.linear_sampler = sg_make_sampler(new sg_sampler_desc
        {
            min_filter = sg_filter.SG_FILTER_LINEAR,
            mag_filter = sg_filter.SG_FILTER_LINEAR,
            wrap_u = sg_wrap.SG_WRAP_REPEAT,
            wrap_v = sg_wrap.SG_WRAP_REPEAT,
        });

        // Custom shader pipeline
        state.shd = sg_make_shader(effect_program_shader_desc(sg_query_backend()));
        state.pip = sgp_make_pipeline(new sgp_pipeline_desc
        {
            shader = state.shd,
            has_vs_color = true,
        });

        // Start async image loads
        sfetch_request_t req = default;
        req.path = util_get_file_path("images/lpc_winter_preview.png");
        req.callback = &FetchImageCallback;
        req.buffer = SFETCH_RANGE(fetch_buffer);
        sfetch_send(req);

        sfetch_request_t req2 = default;
        req2.path = util_get_file_path("images/perlin.png");
        req2.callback = &FetchPerlinCallback;
        req2.buffer = SFETCH_RANGE(perlin_fetch_buffer);
        sfetch_send(req2);
    }

    [UnmanagedCallersOnly]
    static void Frame()
    {
        sfetch_dowork();

        int width = sapp_width();
        int height = sapp_height();

        sgp_begin(width, height);

        if (sg_query_image_state(state.image) == sg_resource_state.SG_RESOURCESTATE_VALID &&
            sg_query_image_state(state.perlin_image) == sg_resource_state.SG_RESOURCESTATE_VALID)
        {
            float secs = (float)(sapp_frame_count() * sapp_frame_duration());
            sg_image_desc image_desc = sg_query_image_desc(state.image);
            float window_ratio = width / (float)height;
            float image_ratio = image_desc.width / (float)image_desc.height;

            effect_fs_uniforms_t uniforms = new effect_fs_uniforms_t();
            uniforms.iVelocity = new sgp_vec2 { x = 0.02f, y = 0.01f };
            uniforms.iPressure = 0.3f;
            uniforms.iTime = secs;
            uniforms.iWarpiness = 0.2f;
            uniforms.iRatio = image_ratio;
            uniforms.iZoom = 0.4f;
            uniforms.iLevel = 1.0f;

            sgp_set_pipeline(state.pip);
            sgp_set_uniform(null, 0, &uniforms, (uint)sizeof(effect_fs_uniforms_t));
            sgp_set_view(VIEW_effect_iTexChannel0, state.view);
            sgp_set_view(VIEW_effect_iTexChannel1, state.perlin_view);
            sgp_set_sampler(SMP_effect_iSmpChannel0, state.linear_sampler);
            sgp_set_sampler(SMP_effect_iSmpChannel1, state.linear_sampler);

            float draw_w = (window_ratio >= image_ratio) ? width : image_ratio * height;
            float draw_h = (window_ratio >= image_ratio) ? width / image_ratio : height;
            sgp_draw_filled_rect(0, 0, draw_w, draw_h);

            sgp_reset_view(VIEW_effect_iTexChannel0);
            sgp_reset_view(VIEW_effect_iTexChannel1);
            sgp_reset_sampler(SMP_effect_iSmpChannel0);
            sgp_reset_sampler(SMP_effect_iSmpChannel1);
            sgp_reset_pipeline();
        }
        else
        {
            // Loading – show dark background
            sgp_set_color(0.1f, 0.1f, 0.1f, 1.0f);
            sgp_clear();
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
        fetch_buffer.Dispose();
        perlin_fetch_buffer.Dispose();
        sfetch_shutdown();
        sg_destroy_view(state.view);
        sg_destroy_view(state.perlin_view);
        sg_destroy_image(state.image);
        sg_destroy_image(state.perlin_image);
        sg_destroy_sampler(state.linear_sampler);
        sg_destroy_pipeline(state.pip);
        sg_destroy_shader(state.shd);
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
            window_title = "Effect (Sokol GP)",
            icon = { sokol_default = true },
            logger = { func = &slog_func },
        };
    }
}
