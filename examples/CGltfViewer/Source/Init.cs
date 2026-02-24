using System;
using Sokol;
using System.Numerics;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.Utils;
using static Sokol.SLog;
using static Sokol.SImgui;

public static unsafe partial class GltfViewer
{
    static void InitApplication()
    {

        // Initialize FileSystem
        FileSystem.Instance.Initialize();

        // Register KHR_animation_pointer handler so cgltf extension channels
        // (target_node == null) are decoded into MaterialPropertyAnimation entries.
        // Must come before any model is loaded.
        CGltfExtensionRegistry.RegisterAnimationChannelHandler(
            "KHR_animation_pointer",
            ctx =>
            {
                using var json = System.Text.Json.JsonDocument.Parse(ctx.ExtensionJson);
                if (!json.RootElement.TryGetProperty("pointer", out var pointerElem)) return;
                string? pointer = pointerElem.GetString();
                if (pointer == null) return;
                if (!CGltfModel.TryParseMaterialPointerPath(pointer, out int matIdx, out var animTarget)) return;

                var matAnim = new Sokol.MaterialPropertyAnimation
                {
                    MaterialIndex = matIdx,
                    Target        = animTarget,
                    PropertyPath  = pointer,
                };

                float[] times = ctx.ReadTimes();
                if (matAnim.IsFloatType)
                {
                    float[] vals = ctx.ReadOutputFloats(1);
                    for (int i = 0; i < times.Length && i < vals.Length; i++)
                        matAnim.FloatKeyframes.Add((times[i], vals[i]));
                }
                else
                {
                    float[] vals = ctx.ReadOutputFloats(2);
                    for (int i = 0; i < times.Length; i++)
                        matAnim.Vector2Keyframes.Add((times[i], new Vector2(vals[i * 2], vals[i * 2 + 1])));
                }
                ctx.Animation.MaterialAnimations.Add(matAnim);
            });

        sg_setup(new sg_desc()
        {
            environment = sglue_environment(),
            disable_validation = true,
            shader_pool_size = 64,
            buffer_pool_size = 4096 * 2,//increased to handle very large scene graphs
            sampler_pool_size = 512, // Reduced from 2048 - texture cache prevents duplicate samplers
            view_pool_size = 512, // Increased to handle many texture views (each texture needs a view)
            uniform_buffer_size = 64 * 1024 * 1024, // 64 MB - increased to handle very large scene graphs (2500+ nodes)
            logger = {
                func = &slog_func,
            }
        });

        // Setup sokol-imgui
        simgui_setup(new simgui_desc_t
        {
            logger = { func = &slog_func }
        });

        // Setup camera with initial defaults
        // These will be recalculated based on model bounds when a model is loaded
        state.camera.Init(new CameraDesc()
        {
            Aspect = 60.0f,
            NearZ = 0.01f,  // Will be adjusted based on model size to avoid Z-fighting
            FarZ = 5000.0f,
            Center = new Vector3(0.0f, 1.0f, 0.0f),
            Distance = 3.0f,
            Latitude = 10.0f,
            Longitude = 0.0f,
        });

        // Initialize UI state - show model browser by default
        state.ui.model_browser_open = true;

        // Initialize lighting system
        // Note: Maximum lights is limited to RenderingConstants.MAX_LIGHTS
        // Light 1: Main directional light
        state.lights.Add(Light.CreateDirectionalLight(
            new Vector3(-0.5f, 0.3f, -0.3f),
            new Vector3(1.0f, 0.95f, 0.85f),
            3f
        ));

        // Light 2: Fill light
        state.lights.Add(Light.CreateDirectionalLight(
            new Vector3(0.5f, -0.3f, 0.3f),
            new Vector3(1.0f, 1f, 1f),
            1f
        ));

        // Light 3: Point light
        state.lights.Add(Light.CreatePointLight(
            new Vector3(0.0f, 15.0f, 0.0f),
            new Vector3(1.0f, 0.9f, 0.8f),
            4.0f,      // intensity
            100.0f     // range
        ));

        // Light 4: Back light
        state.lights.Add(Light.CreateDirectionalLight(
            new Vector3(0.2f, 0.1f, 0.8f),
            new Vector3(0.8f, 0.85f, 1.0f),
            0.5f
        ));

        if (state.lights.Count > RenderingConstants.MAX_LIGHTS)
        {
            Warning($"[Init] Warning: {state.lights.Count} lights defined but only {RenderingConstants.MAX_LIGHTS} will be used. Consider reducing initial lights or increasing MAX_LIGHTS.");
        }

        state.pass_action = default;
        state.pass_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.colors[0].clear_value = state.clear_color;

        PipeLineManager.GetOrCreatePipeline(PipelineType.Standard);
        PipeLineManager.GetOrCreatePipeline(PipelineType.Skinned);

        // Initialize bloom post-processing
        InitializeBloom();

        // Initialize transmission (glass/refraction) rendering
        InitializeTransmission();

        // Initialize Image-Based Lighting (IBL)
        InitializeIBL();



        // Load model path via FileSystem (handles platform-specific path conversion),
        // then synchronously parse with cgltf in RunSingleFrame once path is resolved.
        FileSystem.Instance.LoadFile(filename, (path, buffer, status) =>
        {
            if (status == FileLoadStatus.Success)
            {
                state.pendingModelPath = path;
                state.pendingModelBuffer = buffer;  // always use memory buffer (works on Web/iOS/Android/Desktop)
                state.isLoadingModel = true;
                state.loadingStage = "Loading model...";
                Info($"[CGltf] Queued model for loading: {path} ({buffer?.Length ?? 0} bytes)");
            }
            else
            {
                Error($"[CGltf] Failed to resolve file '{path}': {status}");
            }
        });
    }

    /// <summary>
    /// Destroy only the resources that MUST be recreated on window resize (images, views, samplers).
    /// Pipelines are NOT destroyed because they contain shaders and don't depend on framebuffer size.
    /// This prevents shader pool exhaustion while still handling resize correctly.
    /// </summary>
    private static void CleanupAllResources()
    {
        Info("[Resize] Cleaning up size-dependent resources...");

        // Uninitialize Bloom images and views (size-dependent) - following MRT example pattern
        if (state.bloom.scene_color_img.id != 0)
        {
            Info("[Resize] Uninitializing bloom resources...");
            sg_uninit_image(state.bloom.scene_color_img);
            sg_uninit_image(state.bloom.scene_depth_img);
            sg_uninit_image(state.bloom.bright_img);
            sg_uninit_image(state.bloom.blur_h_img);
            sg_uninit_image(state.bloom.blur_v_img);
            sg_uninit_image(state.bloom.dummy_depth_img);

            // Uninitialize bloom attachment views
            sg_uninit_view(state.bloom.scene_pass.attachments.colors[0]);
            sg_uninit_view(state.bloom.scene_pass.attachments.depth_stencil);
            sg_uninit_view(state.bloom.bright_pass.attachments.colors[0]);
            sg_uninit_view(state.bloom.bright_pass.attachments.depth_stencil);
            sg_uninit_view(state.bloom.blur_h_pass.attachments.colors[0]);
            sg_uninit_view(state.bloom.blur_h_pass.attachments.depth_stencil);
            sg_uninit_view(state.bloom.blur_v_pass.attachments.colors[0]);
            sg_uninit_view(state.bloom.blur_v_pass.attachments.depth_stencil);

            // Uninitialize binding texture views (these were leaking on every resize)
            if (state.bloom.bright_input_view.id != 0)      sg_uninit_view(state.bloom.bright_input_view);
            if (state.bloom.blur_h_input_view.id != 0)      sg_uninit_view(state.bloom.blur_h_input_view);
            if (state.bloom.blur_v_input_view.id != 0)      sg_uninit_view(state.bloom.blur_v_input_view);
            if (state.bloom.composite_scene_view.id != 0)   sg_uninit_view(state.bloom.composite_scene_view);
            if (state.bloom.composite_bloom_view.id != 0)   sg_uninit_view(state.bloom.composite_bloom_view);

            // Uninitialize sampler
            if (state.bloom.sampler.id != 0)
            {
                sg_uninit_sampler(state.bloom.sampler);
            }

            Info("[Resize] Bloom resources uninitialized");
        }

        // Uninitialize Transmission images and views (size-dependent)
        if (state.transmission.screen_color_img.id != 0)
        {
            Info("[Resize] Uninitializing transmission resources...");
            sg_uninit_image(state.transmission.screen_color_img);
            sg_uninit_image(state.transmission.screen_depth_img);
            sg_uninit_view(state.transmission.screen_color_view);
            sg_uninit_view(state.transmission.opaque_pass.attachments.colors[0]);
            sg_uninit_view(state.transmission.opaque_pass.attachments.depth_stencil);

            // Uninitialize sampler
            if (state.transmission.sampler.id != 0)
            {
                sg_uninit_sampler(state.transmission.sampler);
            }

            Info("[Resize] Transmission resources uninitialized");
        }

        Info("[Resize] Cleanup complete");
    }

   

    static void InitializeBloom()
    {
        // Get screen dimensions (we'll create bloom textures at 1/2 resolution for performance)
        int fb_width = sapp_width();
        int fb_height = sapp_height();
        int bloom_width = Math.Max(fb_width / 2, 256);
        int bloom_height = Math.Max(fb_height / 2, 256);

        Info($"[Bloom] Initializing bloom: fb={fb_width}x{fb_height}, bloom={bloom_width}x{bloom_height}, backend={sg_query_backend()}");

        // Get swapchain info to match formats
        var swapchain = sglue_swapchain();

        // Allocate/initialize color texture for main scene rendering (full resolution)
        // Following MRT example pattern: alloc once, then init/uninit/reinit on resize
        if (state.bloom.scene_color_img.id == 0)
        {
            state.bloom.scene_color_img = sg_alloc_image();
        }
        sg_init_image(state.bloom.scene_color_img, new sg_image_desc()
        {
            usage = { color_attachment = true },
            width = fb_width,
            height = fb_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8,  // Explicit format for offscreen rendering
            sample_count = 1,  // Offscreen passes don't use MSAA
            label = "bloom-scene-color"
        });

        // Allocate/initialize depth texture for main scene rendering  
        if (state.bloom.scene_depth_img.id == 0)
        {
            state.bloom.scene_depth_img = sg_alloc_image();
        }
        sg_init_image(state.bloom.scene_depth_img, new sg_image_desc()
        {
            usage = { depth_stencil_attachment = true },
            width = fb_width,
            height = fb_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_DEPTH,  // Same as offscreen example - works on all platforms
            sample_count = 1,  // Offscreen passes don't use MSAA
            label = "bloom-scene-depth"
        });

        // Allocate/initialize bloom processing textures (reduced resolution)
        if (state.bloom.bright_img.id == 0)
        {
            state.bloom.bright_img = sg_alloc_image();
        }
        sg_init_image(state.bloom.bright_img, new sg_image_desc()
        {
            usage = { color_attachment = true },
            width = bloom_width,
            height = bloom_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8,
            sample_count = 1,
            label = "bloom-bright"
        });

        if (state.bloom.blur_h_img.id == 0)
        {
            state.bloom.blur_h_img = sg_alloc_image();
        }
        sg_init_image(state.bloom.blur_h_img, new sg_image_desc()
        {
            usage = { color_attachment = true },
            width = bloom_width,
            height = bloom_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8,
            sample_count = 1,
            label = "bloom-blur-h"
        });

        if (state.bloom.blur_v_img.id == 0)
        {
            state.bloom.blur_v_img = sg_alloc_image();
        }
        sg_init_image(state.bloom.blur_v_img, new sg_image_desc()
        {
            usage = { color_attachment = true },
            width = bloom_width,
            height = bloom_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8,
            sample_count = 1,
            label = "bloom-blur-v"
        });

        // Allocate/initialize dummy depth buffer matching bloom resolution
        // WebGL requires consistent framebuffer attachments - we can't switch between
        // passes with depth and without depth on the same FBO without explicitly unbinding.
        // The workaround is to always have a depth attachment, even if unused.
        // IMPORTANT: All attachments must have the same dimensions!
        if (state.bloom.dummy_depth_img.id == 0)
        {
            state.bloom.dummy_depth_img = sg_alloc_image();
        }
        sg_init_image(state.bloom.dummy_depth_img, new sg_image_desc()
        {
            usage = { depth_stencil_attachment = true },
            width = bloom_width,
            height = bloom_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_DEPTH,
            sample_count = 1,
            label = "bloom-dummy-depth"
        });

        // Allocate/initialize sampler for all bloom passes
        if (state.bloom.sampler.id == 0)
        {
            state.bloom.sampler = sg_alloc_sampler();
        }
        sg_init_sampler(state.bloom.sampler, new sg_sampler_desc()
        {
            min_filter = sg_filter.SG_FILTER_LINEAR,
            mag_filter = sg_filter.SG_FILTER_LINEAR,
            wrap_u = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
            wrap_v = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
            label = "bloom-sampler"
        });

        // Allocate/initialize render passes
        // Scene pass (renders main scene to offscreen buffer)
        // Following MRT example pattern: alloc once, then init/uninit/reinit on resize
        if (state.bloom.scene_pass.attachments.colors[0].id == 0)
        {
            state.bloom.scene_pass.attachments.colors[0] = sg_alloc_view();
        }
        sg_init_view(state.bloom.scene_pass.attachments.colors[0], new sg_view_desc()
        {
            color_attachment = { image = state.bloom.scene_color_img },
            label = "scene-color-view"
        });

        if (state.bloom.scene_pass.attachments.depth_stencil.id == 0)
        {
            state.bloom.scene_pass.attachments.depth_stencil = sg_alloc_view();
        }
        sg_init_view(state.bloom.scene_pass.attachments.depth_stencil, new sg_view_desc()
        {
            depth_stencil_attachment = { image = state.bloom.scene_depth_img },
            label = "scene-depth-view"
        });

        Info($"[Bloom] Scene views initialized: color={state.bloom.scene_pass.attachments.colors[0].id}, depth={state.bloom.scene_pass.attachments.depth_stencil.id}");

        // Create action
        sg_pass_action scene_action = default;
        scene_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        scene_action.colors[0].clear_value = state.clear_color;
        scene_action.depth.load_action = sg_load_action.SG_LOADACTION_CLEAR;
        scene_action.depth.clear_value = 1.0f;

        // Assign action to pass (attachments already set above)
        state.bloom.scene_pass.action = scene_action;
        state.bloom.scene_pass.label = "bloom-scene-pass";

        // Allocate/initialize bright pass - WebGL requires depth attachment for FBO consistency
        if (state.bloom.bright_pass.attachments.colors[0].id == 0)
        {
            state.bloom.bright_pass.attachments.colors[0] = sg_alloc_view();
        }
        sg_init_view(state.bloom.bright_pass.attachments.colors[0], new sg_view_desc()
        {
            color_attachment = { image = state.bloom.bright_img },
            label = "bright-view"
        });

        if (state.bloom.bright_pass.attachments.depth_stencil.id == 0)
        {
            state.bloom.bright_pass.attachments.depth_stencil = sg_alloc_view();
        }
        sg_init_view(state.bloom.bright_pass.attachments.depth_stencil, new sg_view_desc()
        {
            depth_stencil_attachment = { image = state.bloom.dummy_depth_img },
            label = "bright-depth-view"
        });

        sg_pass_action bright_action = default;
        bright_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        bright_action.colors[0].clear_value = state.clear_color;

        state.bloom.bright_pass.action = bright_action;
        state.bloom.bright_pass.label = "bloom-bright-pass";

        // Allocate/initialize horizontal blur pass - WebGL requires depth attachment for FBO consistency
        if (state.bloom.blur_h_pass.attachments.colors[0].id == 0)
        {
            state.bloom.blur_h_pass.attachments.colors[0] = sg_alloc_view();
        }
        sg_init_view(state.bloom.blur_h_pass.attachments.colors[0], new sg_view_desc()
        {
            color_attachment = { image = state.bloom.blur_h_img },
            label = "blur-h-view"
        });

        if (state.bloom.blur_h_pass.attachments.depth_stencil.id == 0)
        {
            state.bloom.blur_h_pass.attachments.depth_stencil = sg_alloc_view();
        }
        sg_init_view(state.bloom.blur_h_pass.attachments.depth_stencil, new sg_view_desc()
        {
            depth_stencil_attachment = { image = state.bloom.dummy_depth_img },
            label = "blur-h-depth-view"
        });

        sg_pass_action blur_h_action = default;
        blur_h_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        blur_h_action.colors[0].clear_value = state.clear_color;

        state.bloom.blur_h_pass.action = blur_h_action;
        state.bloom.blur_h_pass.label = "bloom-blur-h-pass";

        // Allocate/initialize vertical blur pass - WebGL requires depth attachment for FBO consistency
        if (state.bloom.blur_v_pass.attachments.colors[0].id == 0)
        {
            state.bloom.blur_v_pass.attachments.colors[0] = sg_alloc_view();
        }
        sg_init_view(state.bloom.blur_v_pass.attachments.colors[0], new sg_view_desc()
        {
            color_attachment = { image = state.bloom.blur_v_img },
            label = "blur-v-view"
        });

        if (state.bloom.blur_v_pass.attachments.depth_stencil.id == 0)
        {
            state.bloom.blur_v_pass.attachments.depth_stencil = sg_alloc_view();
        }
        sg_init_view(state.bloom.blur_v_pass.attachments.depth_stencil, new sg_view_desc()
        {
            depth_stencil_attachment = { image = state.bloom.dummy_depth_img },
            label = "blur-v-depth-view"
        });

        sg_pass_action blur_v_action = default;
        blur_v_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        blur_v_action.colors[0].clear_value = state.clear_color;

        state.bloom.blur_v_pass.action = blur_v_action;
        state.bloom.blur_v_pass.label = "bloom-blur-v-pass";

        // Note: Composite pass renders to swapchain and must be created each frame
        // with the current swapchain, so we don't create it here.

        // Create offscreen pipelines for rendering the model to bloom scene pass (only if not already created)
        // Use SG_PIXELFORMAT_DEPTH exactly like the offscreen example
        // These pipelines don't depend on framebuffer size, so we only create them once
        if (state.bloom.scene_standard_pipeline.id == 0)
        {
            state.bloom.scene_standard_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.Standard, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_skinned_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.Skinned, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_morphing_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.Morphing, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_skinned_morphing_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.SkinnedMorphing, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_standard_blend_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.StandardBlend, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_skinned_blend_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.SkinnedBlend, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_morphing_blend_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.MorphingBlend, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_skinned_morphing_blend_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.SkinnedMorphingBlend, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_standard_mask_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.StandardMask, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_skinned_mask_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.SkinnedMask, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_morphing_mask_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.MorphingMask, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
            state.bloom.scene_skinned_morphing_mask_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.SkinnedMorphingMask, colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8, depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH, sampleCount: 1);
        }

        // Create fullscreen quad vertices for post-processing passes (size-independent, create once)
        if (state.bloom.fullscreen_vbuf.id == 0)
        {
            float[] fullscreen_quad_vertices = {
                // Full-screen triangle (covers entire NDC)
                -1.0f, -1.0f,   // Bottom-left
                 3.0f, -1.0f,   // Bottom-right (extends past screen)
                -1.0f,  3.0f    // Top-left (extends past screen)
            };
            state.bloom.fullscreen_vbuf = sg_make_buffer(new sg_buffer_desc()
            {
                data = SG_RANGE(fullscreen_quad_vertices),
                label = "bloom-fullscreen-vbuf"
            });
        }
        var fullscreen_vbuf = state.bloom.fullscreen_vbuf;

        // Create pipelines for bloom post-processing passes (only if not already created)
        // These don't depend on framebuffer size, so we only create them once
        if (state.bloom.bright_pipeline.id == 0)
        {
            state.bloom.bright_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.BloomBright,
                cullMode: sg_cull_mode.SG_CULLMODE_NONE,
                colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8,
                depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH,
                sampleCount: 1
            );
        }

        if (state.bloom.blur_h_pipeline.id == 0)
        {
            state.bloom.blur_h_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.BloomBlurHorizontal,
                cullMode: sg_cull_mode.SG_CULLMODE_NONE,
                colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8,
                depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH,
                sampleCount: 1
            );
        }

        if (state.bloom.blur_v_pipeline.id == 0)
        {
            state.bloom.blur_v_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.BloomBlurVertical,
                cullMode: sg_cull_mode.SG_CULLMODE_NONE,
                colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8,
                depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH,
                sampleCount: 1
            );
        }

        if (state.bloom.composite_pipeline.id == 0)
        {
            state.bloom.composite_pipeline = PipeLineManager.GetOrCreatePipeline(PipelineType.BloomComposite,cullMode: sg_cull_mode.SG_CULLMODE_NONE);
        }

        // Create/reinit binding texture views using alloc-once pattern to avoid pool leaks on resize
        if (state.bloom.bright_input_view.id == 0)
            state.bloom.bright_input_view = sg_alloc_view();
        sg_init_view(state.bloom.bright_input_view, new sg_view_desc
        {
            texture = { image = state.bloom.scene_color_img },
            label = "bright-scene-texture-view"
        });

        if (state.bloom.blur_h_input_view.id == 0)
            state.bloom.blur_h_input_view = sg_alloc_view();
        sg_init_view(state.bloom.blur_h_input_view, new sg_view_desc
        {
            texture = { image = state.bloom.bright_img },
            label = "blur-h-input-view"
        });

        if (state.bloom.blur_v_input_view.id == 0)
            state.bloom.blur_v_input_view = sg_alloc_view();
        sg_init_view(state.bloom.blur_v_input_view, new sg_view_desc
        {
            texture = { image = state.bloom.blur_h_img },
            label = "blur-v-input-view"
        });

        if (state.bloom.composite_scene_view.id == 0)
            state.bloom.composite_scene_view = sg_alloc_view();
        sg_init_view(state.bloom.composite_scene_view, new sg_view_desc
        {
            texture = { image = state.bloom.scene_color_img },
            label = "composite-scene-view"
        });

        if (state.bloom.composite_bloom_view.id == 0)
            state.bloom.composite_bloom_view = sg_alloc_view();
        sg_init_view(state.bloom.composite_bloom_view, new sg_view_desc
        {
            texture = { image = state.bloom.blur_v_img },
            label = "composite-bloom-view"
        });

        // Create resource bindings
        // Bright pass bindings (scene texture -> bright pass)
        state.bloom.bright_bindings = new sg_bindings()
        {
            vertex_buffers = { [0] = fullscreen_vbuf },
            views = { [0] = state.bloom.bright_input_view },
            samplers = { [0] = state.bloom.sampler }
        };

        // Horizontal blur bindings (bright pass -> blur horizontal)
        state.bloom.blur_h_bindings = new sg_bindings()
        {
            vertex_buffers = { [0] = fullscreen_vbuf },
            views = { [0] = state.bloom.blur_h_input_view },
            samplers = { [0] = state.bloom.sampler }
        };

        // Vertical blur bindings (horizontal blur -> blur vertical)
        state.bloom.blur_v_bindings = new sg_bindings()
        {
            vertex_buffers = { [0] = fullscreen_vbuf },
            views = { [0] = state.bloom.blur_v_input_view },
            samplers = { [0] = state.bloom.sampler }
        };

        // Composite bindings (scene + final bloom -> swapchain)
        state.bloom.composite_bindings = new sg_bindings()
        {
            vertex_buffers = { [0] = fullscreen_vbuf },
            views = { [0] = state.bloom.composite_scene_view, [1] = state.bloom.composite_bloom_view },
            samplers = { [0] = state.bloom.sampler, [1] = state.bloom.sampler }
        };

        Info("[Bloom] Bloom system initialized successfully");
    }

    static void InitializeTransmission()
    {
        int fb_width = sapp_width();
        int fb_height = sapp_height();

        Info($"[Transmission] Initializing screen-space refraction: {fb_width}x{fb_height}, backend={sg_query_backend()}");

        // Allocate/initialize screen texture for capturing scene behind transparent objects
        // Following MRT example pattern: alloc once, then init/uninit/reinit on resize
        if (state.transmission.screen_color_img.id == 0)
        {
            state.transmission.screen_color_img = sg_alloc_image();
        }
        sg_init_image(state.transmission.screen_color_img, new sg_image_desc()
        {
            usage = { color_attachment = true },
            width = fb_width,
            height = fb_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_RGBA8,
            sample_count = 1,  // No MSAA for offscreen rendering
            label = "transmission-screen-color"
        });

        // Allocate/initialize depth texture for opaque pass
        if (state.transmission.screen_depth_img.id == 0)
        {
            state.transmission.screen_depth_img = sg_alloc_image();
        }
        sg_init_image(state.transmission.screen_depth_img, new sg_image_desc()
        {
            usage = { depth_stencil_attachment = true },
            width = fb_width,
            height = fb_height,
            pixel_format = sg_pixel_format.SG_PIXELFORMAT_DEPTH,
            sample_count = 1,
            label = "transmission-screen-depth"
        });

        // Allocate/initialize sampler for screen texture sampling
        if (state.transmission.sampler.id == 0)
        {
            state.transmission.sampler = sg_alloc_sampler();
        }
        sg_init_sampler(state.transmission.sampler, new sg_sampler_desc()
        {
            min_filter = sg_filter.SG_FILTER_LINEAR,
            mag_filter = sg_filter.SG_FILTER_LINEAR,
            wrap_u = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
            wrap_v = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
            label = "transmission-sampler"
        });

        // Allocate/initialize view for screen texture (create once, reuse every frame)
        if (state.transmission.screen_color_view.id == 0)
        {
            state.transmission.screen_color_view = sg_alloc_view();
        }
        sg_init_view(state.transmission.screen_color_view, new sg_view_desc
        {
            texture = { image = state.transmission.screen_color_img },
            label = "transmission-screen-color-view"
        });
        Info("[Transmission] Initialized screen color view for refraction sampling");

        // Allocate/initialize opaque pass (renders opaque objects to screen texture)
        // Following MRT example pattern: alloc once, then init/uninit/reinit on resize
        if (state.transmission.opaque_pass.attachments.colors[0].id == 0)
        {
            state.transmission.opaque_pass.attachments.colors[0] = sg_alloc_view();
        }
        sg_init_view(state.transmission.opaque_pass.attachments.colors[0], new sg_view_desc()
        {
            color_attachment = { image = state.transmission.screen_color_img },
            label = "opaque-color-view"
        });

        if (state.transmission.opaque_pass.attachments.depth_stencil.id == 0)
        {
            state.transmission.opaque_pass.attachments.depth_stencil = sg_alloc_view();
        }
        sg_init_view(state.transmission.opaque_pass.attachments.depth_stencil, new sg_view_desc()
        {
            depth_stencil_attachment = { image = state.transmission.screen_depth_img },
            label = "opaque-depth-view"
        });

        sg_pass_action opaque_action = default;
        opaque_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        // TBD ELI
        opaque_action.colors[0].clear_value = state.clear_color;
        opaque_action.depth.load_action = sg_load_action.SG_LOADACTION_CLEAR;
        opaque_action.depth.clear_value = 1.0f;

        state.transmission.opaque_pass.action = opaque_action;
        state.transmission.opaque_pass.label = "transmission-opaque-pass";

        // Create pipelines for rendering opaque objects to screen texture (only if not already created)
        // These pipelines don't depend on framebuffer size, so we only create them once
        if (state.transmission.opaque_standard_pipeline.id == 0)
        {
            Info("[Transmission] Creating opaque rendering pipelines...");
            state.transmission.opaque_standard_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.Transmission,
                colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8,
                depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH,
                sampleCount: 1
            );
            state.transmission.opaque_skinned_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.TransmissionSkinned,
                colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8,
                depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH,
                sampleCount: 1
            );
            state.transmission.opaque_morphing_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.TransmissionMorphing,
                colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8,
                depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH,
                sampleCount: 1
            );
            state.transmission.opaque_skinned_morphing_pipeline = PipeLineManager.GetOrCreatePipeline(
                PipelineType.TransmissionSkinnedMorphing,
                colorFormat: sg_pixel_format.SG_PIXELFORMAT_RGBA8,
                depthFormat: sg_pixel_format.SG_PIXELFORMAT_DEPTH,
                sampleCount: 1
            );
            Info("[Transmission] Opaque pipelines created (standard, skinned, morphing, skinned+morphing)");
        }

        Info("[Transmission] Transmission system initialized successfully");
        Info("[Transmission] Two-pass rendering ready: Pass 1 (opaque objects → screen texture), Pass 2 (transparent with refraction)");
        Info("[Transmission] Screen texture available for refraction shader sampling");
    }

    static void InitializeIBL()
    {
        Info("[IBL] Initializing Image-Based Lighting...");

        try
        {
            // Choose IBL loading method:
            // Option 1: Load from 6 cubemap faces (fast, works on all platforms including Web)
            // Option 2: Load from EXR panorama (slow ~300ms, Desktop/Mobile only)
            
            bool useCubemapFaces = true; // Set to false to use EXR panorama
            bool useHDRPanorama = false; // Set to true to use HDR panorama (not implemented here)
            
            if (useCubemapFaces)
            {

                string[] faceFiles = new string[]
                {
                    "skyboxes/skybox/skybox_px.jpg",  // +X
                    "skyboxes/skybox/skybox_nx.jpg",  // -X
                    "skyboxes/skybox/skybox_py.jpg",  // +Y
                    "skyboxes/skybox/skybox_ny.jpg",  // -Y
                    "skyboxes/skybox/skybox_pz.jpg",  // +Z
                    "skyboxes/skybox/skybox_nz.jpg"   // -Z
                };

                
                EnvironmentMapLoader.LoadCubemapFacesAsync(faceFiles, (envMap) =>
                {
                    if (envMap != null && envMap.IsLoaded)
                    {
                        state.environmentMap = envMap;
                        Info($"[IBL] Cubemap environment loaded successfully:");
                        Info($"[IBL]   - Mip count: {state.environmentMap.MipCount}");
                        Info($"[IBL]   - Intensity: {state.iblIntensity}");
                        Info($"[IBL]   - Enabled: {state.useIBL}");
                    }
                    else
                    {
                        Warning("[IBL] Failed to load cubemap faces, using procedural fallback");
                        state.environmentMap = EnvironmentMapLoader.CreateTestEnvironment("fallback-environment");
                        state.useIBL = state.environmentMap != null && state.environmentMap.IsLoaded;
                    }
                }, "nb2-environment");
            }
            else
            {

                if (useHDRPanorama)
                {
                    // Load HDR environment map asynchronously (Desktop/Mobile only, ~800ms)
                    EnvironmentMapLoader.LoadHDREnvironmentAsync("Environment/umhlanga_sunrise_1k.hdr", (envMap) =>
                    {
                        if (envMap != null && envMap.IsLoaded)
                        {
                            state.environmentMap = envMap;
                            Info($"[IBL] HDR environment map loaded successfully:");
                            Info($"[IBL]   - Mip count: {state.environmentMap.MipCount}");
                            Info($"[IBL]   - Intensity: {state.iblIntensity}");
                            Info($"[IBL]   - Enabled: {state.useIBL}");
                        }
                        else
                        {
                            Warning("[IBL] Failed to load HDR, using procedural fallback");
                            state.environmentMap = EnvironmentMapLoader.CreateTestEnvironment("fallback-environment");
                            state.useIBL = state.environmentMap != null && state.environmentMap.IsLoaded;
                        }
                    });
                }
                else
                {
                    // Load EXR environment map asynchronously (Desktop/Mobile only, ~300ms)
                    // EXR files load much faster than HDR since they can be pre-filtered offline
                    EnvironmentMapLoader.LoadEXREnvironmentAsync("Environment/citrus_orchard_road_puresky_1k.exr", (envMap) =>
                    {
                        if (envMap != null && envMap.IsLoaded)
                        {
                            state.environmentMap = envMap;
                            Info($"[IBL] EXR environment map loaded successfully:");
                            Info($"[IBL]   - Mip count: {state.environmentMap.MipCount}");
                            Info($"[IBL]   - Intensity: {state.iblIntensity}");
                            Info($"[IBL]   - Enabled: {state.useIBL}");
                        }
                        else
                        {
                            Warning("[IBL] Failed to load HDR, using procedural fallback");
                            state.environmentMap = EnvironmentMapLoader.CreateTestEnvironment("fallback-environment");
                            state.useIBL = state.environmentMap != null && state.environmentMap.IsLoaded;
                        }
                    });
                }
            }

            // Create temporary procedural environment while loading
            state.environmentMap = EnvironmentMapLoader.CreateTestEnvironment("temp-environment");
            
            if (state.environmentMap != null && state.environmentMap.IsLoaded)
            {
                Info($"[IBL] Temporary environment ready (will be replaced when loading completes)");
            }
            else
            {
                Warning("[IBL] Environment map creation succeeded but not fully loaded");
                state.useIBL = false;
            }
        }
        catch (Exception ex)
        {
            Error($"[IBL] Failed to initialize IBL: {ex.Message}");
            state.environmentMap = null;
            state.useIBL = false;
        }
    }

    public static void LoadNewModel()
    {
        if (state.isLoadingModel)
            return;

        state.isLoadingModel = true;
        state.loadingStage = "Preparing...";
        state.loadingProgress = 0;
        Info($"[ModelBrowser] Loading new model: {filename}");

        // Store old model for disposal after new one loads
        var oldModel = state.model;

        // Clear state BEFORE disposing (so rendering stops using it)
        state.model = null;
        state.animator = null;
        state.modelLoaded = false;
        state.cameraInitialized = false;
        
        // Cleanup joint matrix texture if it exists
        if (state.jointMatrixTexture.id != 0)
        {
            sg_uninit_image(state.jointMatrixTexture);
            state.jointMatrixTexture = default;
            Info("[JointTexture] Cleaned up joint matrix texture");
        }
        if (state.jointMatrixView.id != 0)
        {
            sg_uninit_view(state.jointMatrixView);
            state.jointMatrixView = default;
        }
        if (state.jointMatrixSampler.id != 0)
        {
            sg_uninit_sampler(state.jointMatrixSampler);
            state.jointMatrixSampler = default;
        }
        state.jointTextureWidth = 0;

        // Cleanup morph target texture if it exists
        if (state.morphTargetTexture.id != 0)
        {
            sg_uninit_image(state.morphTargetTexture);
            state.morphTargetTexture = default;
            Info("[MorphTexture] Cleaned up morph target texture");
        }
        if (state.morphTargetView.id != 0)
        {
            sg_uninit_view(state.morphTargetView);
            state.morphTargetView = default;
        }
        if (state.morphTargetSampler.id != 0)
        {
            sg_uninit_sampler(state.morphTargetSampler);
            state.morphTargetSampler = default;
        }
        state.morphTextureWidth = 0;
        state.morphTextureLayerCount = 0;

        // Dispose the old model (after rendering has stopped using it)
        oldModel?.Dispose();

        // Clear the texture cache since old textures are now invalid
        // This ensures the new model creates fresh textures instead of reusing disposed ones
        TextureCache.Instance.Clear();

        state.loadingStage = "Downloading...";
        state.loadingProgress = 10;

        FileSystem.Instance.LoadFile(filename, (path, buffer, status) =>
        {
            if (status == FileLoadStatus.Success)
            {
                state.pendingModelPath = path;
                state.pendingModelBuffer = buffer;  // always use memory buffer (works on Web/iOS/Android/Desktop)
                state.loadingStage = "Parsing...";
                state.loadingProgress = 30;
                Info($"[CGltf] Queued model for loading: {path} ({buffer?.Length ?? 0} bytes)");
            }
            else
            {
                Error($"[ModelBrowser] Failed to resolve file '{path}': {status}");
                state.loadingStage = "Failed!";
                state.loadingProgress = 0;
                state.isLoadingModel = false;
            }
        });
    }
}