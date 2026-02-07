using System;
using Sokol;
using static Sokol.SG;
using System.Diagnostics;
using static Sokol.SLog;
using static Sokol.SImgui;

public static unsafe partial class GltfViewer
{
    static void ApplicationCleanup()
    {
        // Print texture cache statistics before cleanup
        Info("[SharpGLTF] Cleanup - Texture Cache Statistics:");
        TextureCache.Instance.PrintStats();
        
        // Print view tracker statistics
        Info("[SharpGLTF] Cleanup - View Tracker Statistics:");
        ViewTracker.PrintStats();

        state.model?.Dispose();

        // Dispose environment map resources
        if (state.environmentMap != null)
        {
            state.environmentMap.Dispose();
            state.environmentMap = null;
        }

        // Dispose skybox renderer
        if (state.skybox != null && state.skybox.IsInitialized)
        {
            state.skybox.Dispose();
        }

        // Dispose joint matrix texture resources (texture-based skinning)
        if (state.jointMatrixView.id != 0)
        {
            sg_destroy_view(state.jointMatrixView);
        }
        if (state.jointMatrixTexture.id != 0)
        {
            sg_destroy_image(state.jointMatrixTexture);
        }
        if (state.jointMatrixSampler.id != 0)
        {
            sg_destroy_sampler(state.jointMatrixSampler);
        }

        // Dispose morph target texture resources
        if (state.morphTargetView.id != 0)
        {
            sg_destroy_view(state.morphTargetView);
        }
        if (state.morphTargetTexture.id != 0)
        {
            sg_destroy_image(state.morphTargetTexture);
        }
        if (state.morphTargetSampler.id != 0)
        {
            sg_destroy_sampler(state.morphTargetSampler);
        }

        // Dispose bloom pass resources
        CleanupBloomPass();

        // Dispose transmission pass resources
        // CleanupTransmissionPass();

        // Shutdown texture cache (will dispose all cached textures and cleanup Basis Universal)
        TextureCache.Instance.Shutdown();

        FileSystem.Instance.Shutdown();
        simgui_shutdown();
        // sg_shutdown();

        state = new _state();
    }

    static void CleanupBloomPass()
    {
        // Note: sg_pass objects don't need explicit destruction in newer Sokol
        // They are automatically cleaned up when their attachments are destroyed

        // Destroy bloom pipelines
        if (state.bloom.scene_standard_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_standard_pipeline);
        if (state.bloom.scene_skinned_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_skinned_pipeline);
        if (state.bloom.scene_morphing_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_morphing_pipeline);
        if (state.bloom.scene_skinned_morphing_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_skinned_morphing_pipeline);
        if (state.bloom.scene_standard_blend_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_standard_blend_pipeline);
        if (state.bloom.scene_skinned_blend_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_skinned_blend_pipeline);
        if (state.bloom.scene_morphing_blend_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_morphing_blend_pipeline);
        if (state.bloom.scene_skinned_morphing_blend_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_skinned_morphing_blend_pipeline);
        if (state.bloom.scene_standard_mask_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_standard_mask_pipeline);
        if (state.bloom.scene_skinned_mask_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_skinned_mask_pipeline);
        if (state.bloom.scene_morphing_mask_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_morphing_mask_pipeline);
        if (state.bloom.scene_skinned_morphing_mask_pipeline.id != 0) sg_destroy_pipeline(state.bloom.scene_skinned_morphing_mask_pipeline);
        if (state.bloom.bright_pipeline.id != 0) sg_destroy_pipeline(state.bloom.bright_pipeline);
        if (state.bloom.blur_h_pipeline.id != 0) sg_destroy_pipeline(state.bloom.blur_h_pipeline);
        if (state.bloom.blur_v_pipeline.id != 0) sg_destroy_pipeline(state.bloom.blur_v_pipeline);
        if (state.bloom.composite_pipeline.id != 0) sg_destroy_pipeline(state.bloom.composite_pipeline);

        // Destroy bloom images
        if (state.bloom.scene_color_img.id != 0) sg_destroy_image(state.bloom.scene_color_img);
        if (state.bloom.scene_depth_img.id != 0) sg_destroy_image(state.bloom.scene_depth_img);
        if (state.bloom.bright_img.id != 0) sg_destroy_image(state.bloom.bright_img);
        if (state.bloom.blur_h_img.id != 0) sg_destroy_image(state.bloom.blur_h_img);
        if (state.bloom.blur_v_img.id != 0) sg_destroy_image(state.bloom.blur_v_img);
        if (state.bloom.dummy_depth_img.id != 0) sg_destroy_image(state.bloom.dummy_depth_img);

        // Destroy bloom sampler
        if (state.bloom.sampler.id != 0) sg_destroy_sampler(state.bloom.sampler);
    }

    static void CleanupTransmissionPass()
    {
        // Note: sg_pass objects don't need explicit destruction in newer Sokol
        // They are automatically cleaned up when their attachments are destroyed

        // Destroy transmission pipelines
        if (state.transmission.opaque_standard_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_standard_pipeline);
        if (state.transmission.opaque_skinned_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_skinned_pipeline);
        if (state.transmission.opaque_morphing_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_morphing_pipeline);
        if (state.transmission.opaque_skinned_morphing_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_skinned_morphing_pipeline);
        if (state.transmission.opaque_standard_blend_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_standard_blend_pipeline);
        if (state.transmission.opaque_skinned_blend_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_skinned_blend_pipeline);
        if (state.transmission.opaque_standard_mask_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_standard_mask_pipeline);
        if (state.transmission.opaque_skinned_mask_pipeline.id != 0) sg_destroy_pipeline(state.transmission.opaque_skinned_mask_pipeline);

        // Destroy transmission view
        if (state.transmission.screen_color_view.id != 0) sg_destroy_view(state.transmission.screen_color_view);

        // Destroy transmission images
        if (state.transmission.screen_color_img.id != 0) sg_destroy_image(state.transmission.screen_color_img);
        if (state.transmission.screen_depth_img.id != 0) sg_destroy_image(state.transmission.screen_depth_img);

        // Destroy transmission sampler
        if (state.transmission.sampler.id != 0) sg_destroy_sampler(state.transmission.sampler);
    }
}