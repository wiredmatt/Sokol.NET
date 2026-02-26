using System; 
using Sokol;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Numerics;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SG.sg_vertex_format;
using static Sokol.SG.sg_index_type;
using static Sokol.SG.sg_cull_mode;
using static Sokol.SG.sg_compare_func;
using static Sokol.Utils;
using System.Diagnostics;
using static Sokol.SLog;
using static Sokol.SDebugUI;
using static System.Hardware.CameraC;
using static camera_texture_shader_cs.Shaders;
using static Sokol.SImgui;
using static Imgui.ImguiNative;
using Imgui;

public static unsafe class CameraHardwareApp
{
    class _state
    {
        public sg_pass_action pass_action;
        public IntPtr camDevice = IntPtr.Zero;
        public NV12Texture? nv12Texture = null;
        public int frameCount;
        // Camera texture rendering
        public sg_pipeline camTexPip;
        public sg_bindings camTexBind;
        // Pending frame from capture thread (guarded by pendingLock)
        public readonly object pendingLock = new object();
        public byte[]? pendingY;
        public byte[]? pendingUV;
        public int pendingWidth;
        public int pendingHeight;
        public int pendingPitch;   // Y bytes-per-row
        public int pendingPitch2;  // UV bytes-per-row
        public bool pendingFrame;
        // RGBA pending frame (Emscripten / single-plane formats)
        public byte[]? pendingRgba;
        public int pendingRgbaPitch;
        public bool pendingIsRgba;
        // RGBA camera texture rendering
        public StreamableTexture? rgbaTexture = null;
        public sg_pipeline camTexRgbaPip;
        public sg_bindings camTexRgbaBind;
        // Mirror toggle
        public bool mirrorX = true; // front cameras are mirrored by default
        public sg_buffer vbufNormal;
        public sg_buffer vbufMirrored;
        public sg_buffer vbufRgbaNormal;
        public sg_buffer vbufRgbaMirrored;
        // Camera picker UI
        public string[] cameraNames = Array.Empty<string>();
        public int cameraCount;
        public int selectedCamera;
        public int pendingSwitchCamera = -1;
        // Active camera info (for display)
        public string activeCameraName   = "";
        public string activeCameraPos    = "";
        public int    activeWidth;
        public int    activeHeight;
        public int    activeFpsNum;
        public int    activeFpsDen;
        public string activeFormat       = "";
        public string permissionStatus   = "pending...";
    }

    static _state state = new _state();
    static byte _camWindowOpen = 1;
    static byte _mirrorX = 1;

    // -------------------------------------------------------------------------
    // Camera callbacks
    //
    // [UnmanagedCallersOnly] wrappers are required on NativeAOT / WebAssembly
    // where the runtime cannot create a native→managed thunk for a regular
    // delegate.  They simply forward to the real implementation below.
    // On all platforms we pass the function pointer via the IntPtr overload of
    // cam_open / cam_set_permission_callback.
    // -------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    static unsafe void OnFrame(IntPtr device, camFrame* frame, void* userdata)
    {
        state.frameCount++;
        if (state.frameCount % 30 == 0)
        {
            Info($"[frame {state.frameCount,4}]  {frame->width}x{frame->height}" +
                 $"  fmt={cam_pixel_format_name(frame->format),-8}" +
                 $"  pitch={frame->pitch}  ts={frame->timestamp_ns} ns  rot={frame->rotation:F0}°");
        }

        if (frame->data == null)
            return;

        // Single-plane path: RGBA32 (Emscripten) or any format where data2 is absent.
        if (frame->format == camPixelFormat.CAM_PIXEL_FORMAT_RGBA32 || frame->data2 == null)
        {
            int fw    = frame->width;
            int fh    = frame->height;
            int pitch = frame->pitch;   // bytes per row (= width * 4 for RGBA32)
            int bytes = pitch * fh;

            lock (state.pendingLock)
            {
                if (state.pendingRgba == null || state.pendingRgba.Length != bytes)
                    state.pendingRgba = new byte[bytes];
                fixed (byte* dst = state.pendingRgba)
                    Buffer.MemoryCopy((byte*)frame->data, dst, bytes, bytes);

                state.pendingWidth     = fw;
                state.pendingHeight    = fh;
                state.pendingRgbaPitch = pitch;
                state.pendingIsRgba    = true;
                state.pendingFrame     = true;
            }
            return;
        }

        // Dual-plane NV12 path (macOS / iOS / Android).
        {
            int fw     = frame->width;
            int fh     = frame->height;
            int pitch  = frame->pitch;
            int pitch2 = frame->pitch2;

            byte* srcY  = (byte*)frame->data;
            byte* srcUV = (byte*)frame->data2;
            int yBytes  = pitch  * fh;
            int uvBytes = pitch2 * (fh / 2);

            lock (state.pendingLock)
            {
                if (state.pendingY  == null || state.pendingY.Length  != yBytes)  state.pendingY  = new byte[yBytes];
                if (state.pendingUV == null || state.pendingUV.Length != uvBytes) state.pendingUV = new byte[uvBytes];

                fixed (byte* dstY  = state.pendingY)
                fixed (byte* dstUV = state.pendingUV)
                {
                    Buffer.MemoryCopy(srcY,  dstY,  yBytes,  yBytes);
                    Buffer.MemoryCopy(srcUV, dstUV, uvBytes, uvBytes);
                }

                state.pendingWidth  = fw;
                state.pendingHeight = fh;
                state.pendingPitch  = pitch;
                state.pendingPitch2 = pitch2;
                state.pendingIsRgba = false;
                state.pendingFrame  = true;
            }
        }
    }

    [UnmanagedCallersOnly]
    static unsafe void OnPermission(IntPtr device, camPermission perm, void* userdata)
    {
        switch (perm)
        {
            case camPermission.CAM_PERMISSION_APPROVED:
                state.permissionStatus = "approved";
                Info("Camera permission approved – frames will start arriving.");
                break;
            case camPermission.CAM_PERMISSION_DENIED:
                state.permissionStatus = "denied";
                Error("Camera permission denied.");
                break;
        }
    }

    // -------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    private static unsafe void Init()
    {
        sg_setup(new sg_desc()
        {
            environment = sglue_environment(),
            logger = { func = &slog_func }
        });

        simgui_setup(new simgui_desc_t
        {
            logger = { func = &slog_func }
        });

        state.pass_action = default;
        state.pass_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.colors[0].clear_value = new sg_color { r = 0.25f, g = 0.5f, b = 0.75f, a = 1.0f };

        // --- camerac init ---
        if (!cam_init())
        {
            Error($"cam_init failed: {cam_get_error()}");
            return;
        }
        Info($"camerac backend: {cam_get_backend()}");

        // --- enumerate devices ---
        int count = cam_get_device_count();
        Info($"Found {count} camera(s):");

        state.cameraCount = count;
        state.cameraNames = new string[count];

        for (int i = 0; i < count; i++)
        {
            camDeviceInfo info;
            if (!cam_get_device_info(i, &info)) continue;

            // name and device_id are fixed inline byte arrays; read them as UTF-8
            fixed (byte* namePtr = &info.name[0], idPtr = &info.device_id[0])
            {
                string name = Marshal.PtrToStringUTF8((IntPtr)namePtr) ?? "";
                string id   = Marshal.PtrToStringUTF8((IntPtr)idPtr)   ?? "";
                string pos  = cam_position_name(info.position);
                state.cameraNames[i] = $"[{i}] {name} ({pos})";
                Info($"  [{i}] {name}  (id: {id})  position: {pos}");
            }
            cam_free_device_info(&info);
        }

        if (count == 0)
        {
            Info("No cameras found.");
            return;
        }

        // --- pick front-facing camera, fall back to index 0 ---
        int cameraIndex = 0;
        for (int i = 0; i < count; i++)
        {
            camDeviceInfo info;
            if (!cam_get_device_info(i, &info)) continue;
            camPosition pos = info.position;
            cam_free_device_info(&info);
            if (pos == camPosition.CAM_POSITION_FRONT_FACING)
            {
                cameraIndex = i;
                break;
            }
        }
        state.selectedCamera = cameraIndex;
        Info($"Opening camera index {cameraIndex}");

        // --- open camera ---
        camSpec req = new camSpec
        {
            width           = 1280,
            height          = 720,
            fps_numerator   = 30,
            fps_denominator = 1,
            format          = camPixelFormat.CAM_PIXEL_FORMAT_NV12,
        };

        state.camDevice = cam_open(cameraIndex, in req, &OnFrame, null);
        if (state.camDevice == IntPtr.Zero)
        {
            Error($"cam_open failed: {cam_get_error()}");
            return;
        }

        cam_set_permission_callback(state.camDevice, &OnPermission, null);

        camSpec actual;
        if (cam_get_actual_spec(state.camDevice, &actual))
        {
            state.activeWidth   = actual.width;
            state.activeHeight  = actual.height;
            state.activeFpsNum  = actual.fps_numerator;
            state.activeFpsDen  = actual.fps_denominator;
            state.activeFormat  = cam_pixel_format_name(actual.format);
            Info($"Actual spec: {actual.width}x{actual.height}" +
                 $" @ {actual.fps_numerator}/{actual.fps_denominator} fps" +
                 $"  {cam_pixel_format_name(actual.format)}");
        }
        state.activeCameraName = state.cameraNames.Length > cameraIndex ? state.cameraNames[cameraIndex] : $"Camera {cameraIndex}";
        state.activeCameraPos  = "";
        {
            camDeviceInfo dinfo;
            if (cam_get_device_info(cameraIndex, &dinfo))
            {
                state.activeCameraPos = cam_position_name(dinfo.position);
                cam_free_device_info(&dinfo);
            }
        }
    }

    // Called once the first camera frame arrives so sg is already initialised.
    static void InitCameraTexturePipeline()
    {
        // Fullscreen quad: 4 vertices (position xy + texcoord uv), 2 triangles.
        // U is scaled by width/pitch so only the valid (non-padded) portion of the
        // pitched texture is sampled – matches the SDL NV12 reference approach.
        float uMax = state.nv12Texture!.uvScaleX;
        float[] verts = {
            -1f, -1f,  0f,    1f,
             1f, -1f,  uMax,  1f,
             1f,  1f,  uMax,  0f,
            -1f,  1f,  0f,    0f,
        };
        float[] vertsMirrored = {
            -1f, -1f,  uMax, 1f,
             1f, -1f,  0f,   1f,
             1f,  1f,  0f,   0f,
            -1f,  1f,  uMax, 0f,
        };
        ushort[] indices = { 0, 1, 2,  0, 2, 3 };

        sg_buffer vbuf;
        fixed (float* p = verts)
            vbuf = sg_make_buffer(new sg_buffer_desc
            {
                data = new sg_range { ptr = p, size = (nuint)(verts.Length * sizeof(float)) },
                label = "cam-quad-vb"
            });

        sg_buffer vbufMirrored;
        fixed (float* p = vertsMirrored)
            vbufMirrored = sg_make_buffer(new sg_buffer_desc
            {
                data = new sg_range { ptr = p, size = (nuint)(vertsMirrored.Length * sizeof(float)) },
                label = "cam-quad-vb-mirror"
            });

        state.vbufNormal   = vbuf;
        state.vbufMirrored = vbufMirrored;

        sg_buffer ibuf;
        fixed (ushort* p = indices)
            ibuf = sg_make_buffer(new sg_buffer_desc
            {
                usage = new sg_buffer_usage { index_buffer = true },
                data  = new sg_range { ptr = p, size = (nuint)(indices.Length * sizeof(ushort)) },
                label = "cam-quad-ib"
            });

        var shd = sg_make_shader(camera_texture_shader_desc(sg_query_backend()));

        var pip_desc = new sg_pipeline_desc
        {
            shader     = shd,
            index_type = SG_INDEXTYPE_UINT16,
            label      = "cam-texture-pip"
        };
        pip_desc.layout.attrs[ATTR_camera_texture_position]  = new sg_vertex_attr_state { format = SG_VERTEXFORMAT_FLOAT2 };
        pip_desc.layout.attrs[ATTR_camera_texture_texcoord0] = new sg_vertex_attr_state { format = SG_VERTEXFORMAT_FLOAT2 };

        state.camTexPip = sg_make_pipeline(pip_desc);

        state.camTexBind = default;
        state.camTexBind.vertex_buffers[0] = state.mirrorX ? vbufMirrored : vbuf;
        state.camTexBind.index_buffer      = ibuf;
        state.camTexBind.views[VIEW_tex_y]  = state.nv12Texture!.YFaceFlowTexture.View;
        state.camTexBind.views[VIEW_tex_uv] = state.nv12Texture!.UvFaceFlowTexture.View;
        state.camTexBind.samplers[SMP_smp_y]  = state.nv12Texture!.YFaceFlowTexture.Sampler;
        state.camTexBind.samplers[SMP_smp_uv] = state.nv12Texture!.UvFaceFlowTexture.Sampler;
    }

    // Called once the first RGBA frame arrives (Emscripten / single-plane formats).
    static unsafe void InitCameraTextureRGBAPipeline(float uMax)
    {
        float[] verts = {
            -1f, -1f,  0f,    1f,
             1f, -1f,  uMax,  1f,
             1f,  1f,  uMax,  0f,
            -1f,  1f,  0f,    0f,
        };
        float[] vertsMirrored = {
            -1f, -1f,  uMax, 1f,
             1f, -1f,  0f,   1f,
             1f,  1f,  0f,   0f,
            -1f,  1f,  uMax, 0f,
        };
        ushort[] indices = { 0, 1, 2,  0, 2, 3 };

        sg_buffer vbuf, ibuf;
        fixed (float* p = verts)
            vbuf = sg_make_buffer(new sg_buffer_desc
            {
                data  = new sg_range { ptr = p, size = (nuint)(verts.Length * sizeof(float)) },
                label = "cam-rgba-vb"
            });

        sg_buffer vbufMirrored;
        fixed (float* p = vertsMirrored)
            vbufMirrored = sg_make_buffer(new sg_buffer_desc
            {
                data  = new sg_range { ptr = p, size = (nuint)(vertsMirrored.Length * sizeof(float)) },
                label = "cam-rgba-vb-mirror"
            });

        state.vbufRgbaNormal   = vbuf;
        state.vbufRgbaMirrored = vbufMirrored;
        fixed (ushort* p = indices)
            ibuf = sg_make_buffer(new sg_buffer_desc
            {
                usage = new sg_buffer_usage { index_buffer = true },
                data  = new sg_range { ptr = p, size = (nuint)(indices.Length * sizeof(ushort)) },
                label = "cam-rgba-ib"
            });

        var shd = sg_make_shader(camera_texture_rgba_shader_desc(sg_query_backend()));

        var pip_desc = new sg_pipeline_desc
        {
            shader     = shd,
            index_type = SG_INDEXTYPE_UINT16,
            label      = "cam-rgba-pip"
        };
        pip_desc.layout.attrs[ATTR_camera_texture_rgba_position]  = new sg_vertex_attr_state { format = SG_VERTEXFORMAT_FLOAT2 };
        pip_desc.layout.attrs[ATTR_camera_texture_rgba_texcoord0] = new sg_vertex_attr_state { format = SG_VERTEXFORMAT_FLOAT2 };

        state.camTexRgbaPip = sg_make_pipeline(pip_desc);

        state.camTexRgbaBind = default;
        state.camTexRgbaBind.vertex_buffers[0]       = state.mirrorX ? vbufMirrored : vbuf;
        state.camTexRgbaBind.index_buffer            = ibuf;
        state.camTexRgbaBind.views[VIEW_tex_rgba]    = state.rgbaTexture!.View;
        state.camTexRgbaBind.samplers[SMP_smp_rgba]  = state.rgbaTexture!.Sampler;
    }

    // Switch to a different camera index (called on the main thread from Frame).
    static void SwitchCamera(int newIndex)
    {
        if (newIndex == state.selectedCamera && state.camDevice != IntPtr.Zero) return;
        if (newIndex < 0 || newIndex >= state.cameraCount) return;

        // Close existing device
        if (state.camDevice != IntPtr.Zero)
        {
            cam_close(state.camDevice);
            state.camDevice = IntPtr.Zero;
        }

        // Clear texture/pipeline state so they get re-created on next frame
        state.nv12Texture?.Dispose();
        state.nv12Texture = null;
        state.rgbaTexture?.Dispose();
        state.rgbaTexture = null;
        state.camTexPip   = default;
        state.camTexRgbaPip = default;

        // Reset pending frame
        lock (state.pendingLock)
        {
            state.pendingFrame = false;
            state.pendingY     = null;
            state.pendingUV    = null;
            state.pendingRgba  = null;
            state.frameCount   = 0;
        }

        state.selectedCamera = newIndex;
        Info($"Switching to camera index {newIndex}");

        camSpec req = new camSpec
        {
            width           = 1280,
            height          = 720,
            fps_numerator   = 30,
            fps_denominator = 1,
            format          = camPixelFormat.CAM_PIXEL_FORMAT_NV12,
        };

        state.camDevice = cam_open(newIndex, in req, &OnFrame, null);
        if (state.camDevice == IntPtr.Zero)
        {
            Error($"cam_open failed for index {newIndex}: {cam_get_error()}");
            return;
        }
        cam_set_permission_callback(state.camDevice, &OnPermission, null);
        state.permissionStatus = "pending...";

        camSpec actual;
        if (cam_get_actual_spec(state.camDevice, &actual))
        {
            state.activeWidth   = actual.width;
            state.activeHeight  = actual.height;
            state.activeFpsNum  = actual.fps_numerator;
            state.activeFpsDen  = actual.fps_denominator;
            state.activeFormat  = cam_pixel_format_name(actual.format);
            Info($"Actual spec: {actual.width}x{actual.height}" +
                 $" @ {actual.fps_numerator}/{actual.fps_denominator} fps" +
                 $"  {cam_pixel_format_name(actual.format)}");
        }
        state.activeCameraName = state.cameraNames.Length > newIndex ? state.cameraNames[newIndex] : $"Camera {newIndex}";
        {
            camDeviceInfo dinfo;
            if (cam_get_device_info(newIndex, &dinfo))
            {
                state.activeCameraPos = cam_position_name(dinfo.position);
                cam_free_device_info(&dinfo);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe void Frame()
    {
        cam_update(); // dispatch permission callbacks on the main thread

        // Handle camera switch requested from the ImGui picker
        if (state.pendingSwitchCamera >= 0)
        {
            SwitchCamera(state.pendingSwitchCamera);
            state.pendingSwitchCamera = -1;
        }

        // Consume any pending camera frame (copy posted by the capture thread)
        byte[]? yData = null, uvData = null, rgbaData = null;
        int fw = 0, fh = 0, pitch = 0, pitch2 = 0, rgbaPitch = 0;
        bool isRgba = false;
        lock (state.pendingLock)
        {
            if (state.pendingFrame)
            {
                isRgba = state.pendingIsRgba;
                fw     = state.pendingWidth;
                fh     = state.pendingHeight;
                if (isRgba)
                {
                    rgbaData  = state.pendingRgba;
                    rgbaPitch = state.pendingRgbaPitch;
                    state.pendingRgba = null;
                }
                else
                {
                    yData  = state.pendingY;
                    uvData = state.pendingUV;
                    pitch  = state.pendingPitch;
                    pitch2 = state.pendingPitch2;
                    state.pendingY  = null;
                    state.pendingUV = null;
                }
                state.pendingFrame = false;
            }
        }

        if (isRgba && rgbaData != null)
        {
            // Texture stride width in pixels (may exceed visibleWidth due to row alignment)
            int texW   = rgbaPitch > 0 ? rgbaPitch / 4 : fw;
            float uMax = texW > 0 ? (float)fw / texW : 1f;
            fixed (byte* pRgba = rgbaData)
            {
                if (state.rgbaTexture == null || state.rgbaTexture.width != texW || state.rgbaTexture.height != fh)
                {
                    state.rgbaTexture?.Dispose();
                    state.rgbaTexture = new StreamableTexture(null, texW, fh, "camera-rgba",
                        sg_pixel_format.SG_PIXELFORMAT_RGBA8, stream_update: true);
                    InitCameraTextureRGBAPipeline(uMax);
                }
                sg_image_data imgd = default;
                imgd.mip_levels[0] = new sg_range { ptr = pRgba, size = (nuint)(rgbaPitch * fh) };
                sg_update_image(state.rgbaTexture.Image, imgd);
            }
        }
        else if (!isRgba && yData != null && uvData != null)
        {
            fixed (byte* pY = yData, pUV = uvData)
            {
                var f = new camFrame { data = pY, pitch = pitch, data2 = pUV, pitch2 = pitch2, width = fw, height = fh };

                if (state.nv12Texture == null || state.nv12Texture.width != fw || state.nv12Texture.height != fh)
                {
                    state.nv12Texture?.Dispose();
                    state.nv12Texture = new NV12Texture(f);
                    InitCameraTexturePipeline();
                }
                else
                {
                    state.nv12Texture.UpdateTexture(f);
                }
            }
        }

        simgui_new_frame(new simgui_frame_desc_t
        {
            width      = sapp_width(),
            height     = sapp_height(),
            delta_time = sapp_frame_duration(),
        });

        // --- Camera Picker window ---
        if (state.cameraCount > 0)
        {
            igSetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver, Vector2.Zero);
            igSetNextWindowSize(new Vector2(320, 0), ImGuiCond.FirstUseEver);
            igBegin("Camera", ref _camWindowOpen, 0);

            igText($"{state.cameraCount} camera(s) found");
            igSeparator();

            for (int i = 0; i < state.cameraCount; i++)
            {
                bool selected = (i == state.selectedCamera);
                if (igSelectable_Bool(state.cameraNames[i], selected, 0, Vector2.Zero))
                {
                    if (i != state.selectedCamera)
                        state.pendingSwitchCamera = i;
                }
            }

            igSeparator();
            if (igCheckbox("Mirror", ref _mirrorX))
            {
                state.mirrorX = _mirrorX != 0;
                // Update vertex buffer binding for both pipelines
                if (state.vbufNormal.id != 0)
                    state.camTexBind.vertex_buffers[0] = state.mirrorX ? state.vbufMirrored : state.vbufNormal;
                if (state.vbufRgbaNormal.id != 0)
                    state.camTexRgbaBind.vertex_buffers[0] = state.mirrorX ? state.vbufRgbaMirrored : state.vbufRgbaNormal;
            }
            igSeparator();
            igText("Active Camera");
            igSeparator();
            igText($"Name       : {state.activeCameraName}");
            igText($"Position   : {state.activeCameraPos}");
            igText($"Resolution : {state.activeWidth} x {state.activeHeight}");
            igText($"FPS        : {state.activeFpsNum}/{state.activeFpsDen}");
            igText($"Format     : {state.activeFormat}");
            igText($"Permission : {state.permissionStatus}");
            igText($"Frame #    : {state.frameCount}");
            igEnd();
        }

        sg_begin_pass(new sg_pass { action = state.pass_action, swapchain = sglue_swapchain() });

        if (state.rgbaTexture != null && state.rgbaTexture.IsValid && state.camTexRgbaPip.id != 0)
        {
            state.camTexRgbaBind.views[VIEW_tex_rgba] = state.rgbaTexture.View;
            sg_apply_pipeline(state.camTexRgbaPip);
            sg_apply_bindings(state.camTexRgbaBind);
            sg_draw(0, 6, 1);
        }
        else if (state.nv12Texture != null && state.nv12Texture.IsValid && state.camTexPip.id != 0)
        {
            state.camTexBind.views[VIEW_tex_y]  = state.nv12Texture.YFaceFlowTexture.View;
            state.camTexBind.views[VIEW_tex_uv] = state.nv12Texture.UvFaceFlowTexture.View;

            sg_apply_pipeline(state.camTexPip);
            sg_apply_bindings(state.camTexBind);
            sg_draw(0, 6, 1);
        }

        simgui_render();

        sg_end_pass();
        sg_commit();
    }

    [UnmanagedCallersOnly]
    private static unsafe void Event(sapp_event* e)
    {
        if (e != null)
            simgui_handle_event(in *e);
    }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        if (state.camDevice != IntPtr.Zero)
        {
            cam_close(state.camDevice);
            state.camDevice = IntPtr.Zero;
        }
        cam_shutdown();
        simgui_shutdown();
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
            width = 0,
            height = 0,
            sample_count = 4,
            window_title = "Template (sokol-app)",
            icon = { sokol_default = true },
            logger = {
                func = &slog_func,
            }
        };
    }

}
