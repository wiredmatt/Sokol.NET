using System; 
using Sokol;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Numerics;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SImgui;
using static Imgui.ImguiNative;
using Imgui;
using static Sokol.Utils;
using System.Diagnostics;
using static Sokol.SLog;

public static unsafe class SamplebrowserApp
{
    // Sample delegate types matching the Sokol callback signatures
    public static delegate* unmanaged<void> CurrentInitCallback = null;
    public static delegate* unmanaged<void> CurrentFrameCallback = null;
    public static delegate* unmanaged<sapp_event*, void> CurrentEventCallback = null;
    public static delegate* unmanaged<void> CurrentCleanupCallback = null;

    enum SampleId
    {
        None = 0,
        Cube,
        GltfViewer,
        Dyntex,
        Drawcallperf,
        Offscreen,
        Instancing,
        Loadpng,
        CubemapJpeg,
        Box2dPhysics,
        ShaderToyApp,
        // Cgltf,
        Sdf,
        // SpineInspector
    }

    struct SampleInfo
    {
        public SampleId Id;
        public string Name;
        public string Description;
        public delegate* unmanaged<void> InitCallback;
        public delegate* unmanaged<void> FrameCallback;
        public delegate* unmanaged<sapp_event*, void> EventCallback;
        public delegate* unmanaged<void> CleanupCallback;
    }

    struct _state
    {
        public sg_pass_action pass_action;
        public SampleId currentSample;
        public bool showMenu;
        public SampleId pendingSample; // Sample to start on next frame
        public bool requestStopSample; // Flag to request stopping current sample
        public bool showLicenseInfo; // Flag to show Spine license info dialog
        public bool showAttributions; // Flag to show asset attributions dialog
    }

    static _state state = new _state();
    static SampleInfo[] samples = Array.Empty<SampleInfo>();

    // Public method that samples can call to request returning to menu
    public static void RequestReturnToMenu()
    {
        if (state.currentSample != SampleId.None)
        {
            state.requestStopSample = true; // Signal to stop current sample
        }
    }

    // Public method to check if back button was clicked
    public static bool DrawBackButton()
    {
        bool clicked = false;
        
        // Position at bottom-left corner
        float buttonHeight = 50;
        float yPos = sapp_heightf() - buttonHeight - 10; // 10px margin from bottom
        igSetNextWindowPos(new Vector2(10, yPos), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(100, buttonHeight), ImGuiCond.Always);
        
        igPushStyleVar_Float(ImGuiStyleVar.WindowRounding, 8.0f);
        igPushStyleVar_Vec2(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        
        byte open = 1;
        if (igBegin("##BackButton", ref open, 
            ImGuiWindowFlags.NoTitleBar | 
            ImGuiWindowFlags.NoResize | 
            ImGuiWindowFlags.NoMove | 
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoCollapse))
        {
            if (igButton("<- Back", new Vector2(84, 34)))
            {
                clicked = true;
                RequestReturnToMenu();
            }
            igEnd();
        }
        
        igPopStyleVar(2);
        
        return clicked;
    }

    [UnmanagedCallersOnly]
    private static unsafe void Init()
    {
        Info("SampleBrowser Initialize() Enter");

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

        // Initialize ImGui
        simgui_setup(new simgui_desc_t
        {
            logger = {
                func = &slog_func,
            }
        });

        ImGuiIO* io = igGetIO_Nil();
        io->ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        state.pass_action = default;
        state.pass_action.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        state.pass_action.colors[0].clear_value = new sg_color { r = 0.1f, g = 0.1f, b = 0.12f, a = 1.0f };

        state.currentSample = SampleId.None;
        state.showMenu = true;
        state.requestStopSample = false;
        state.pendingSample = SampleId.None;

        // Initialize sample registry
        InitSampleRegistry();
    }

    private static void InitSampleRegistry()
    {
        samples = new SampleInfo[]
        {
            new SampleInfo
            {
                Id = SampleId.Cube,
                Name = "Rotating Cube",
                Description = "A textured rotating 3D cube with vertex colors",
                InitCallback = &CubeSapp.Init,
                FrameCallback = &CubeSapp.Frame,
                EventCallback = &CubeSapp.Event,
                CleanupCallback = &CubeSapp.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.GltfViewer,
                Name = "GLTF Viewer",
                Description = "A viewer for GLTF 3D models",
                InitCallback = &GltfViewer.Init,
                FrameCallback = &GltfViewer.Frame,
                EventCallback = &GltfViewer.Event,
                CleanupCallback = &GltfViewer.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.Dyntex,
                Name = "Dynamic Texture",
                Description = "Conway's Game of Life rendered as a dynamic texture",
                InitCallback = &DynTextApp.Init,
                FrameCallback = &DynTextApp.Frame,
                EventCallback = &DynTextApp.Event,
                CleanupCallback = &DynTextApp.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.Drawcallperf,
                Name = "Draw Call Performance",
                Description = "Performance test with many draw calls",
                InitCallback = &DrawcallPerf.Init,
                FrameCallback = &DrawcallPerf.Frame,
                EventCallback = &DrawcallPerf.Event,
                CleanupCallback = &DrawcallPerf.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.Offscreen,
                Name = "Offscreen Rendering",
                Description = "Render to texture example",
                InitCallback = &OffscreenApp.Init,
                FrameCallback = &OffscreenApp.Frame,
                EventCallback = &OffscreenApp.Event,
                CleanupCallback = &OffscreenApp.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.Instancing,
                Name = "Instanced Rendering",
                Description = "Hardware instancing example",
                InitCallback = &InstancingSApp.Init,
                FrameCallback = &InstancingSApp.Frame,
                EventCallback = &InstancingSApp.Event,
                CleanupCallback = &InstancingSApp.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.Loadpng,
                Name = "Load PNG",
                Description = "Load and display a PNG texture",
                InitCallback = &LoadPngSApp.Init,
                FrameCallback = &LoadPngSApp.Frame,
                EventCallback = &LoadPngSApp.Event,
                CleanupCallback = &LoadPngSApp.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.CubemapJpeg,
                Name = "Cubemap JPEG",
                Description = "Skybox with JPEG cubemap texture",
                InitCallback = &CubemapJpegApp.Init,
                FrameCallback = &CubemapJpegApp.Frame,
                EventCallback = &CubemapJpegApp.Event,
                CleanupCallback = &CubemapJpegApp.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.Box2dPhysics,
                Name = "Box2D Physics",
                Description = "Interactive physics simulation with Box2D - click to spawn shapes",
                InitCallback = &Box2dApp.Init,
                FrameCallback = &Box2dApp.Frame,
                EventCallback = &Box2dApp.Event,
                CleanupCallback = &Box2dApp.Cleanup
            },
            new SampleInfo
            {
                Id = SampleId.ShaderToyApp,
                Name = "ShaderToy Gallery",
                Description = "Collection of ShaderToy demos - click Prev/Next to navigate",
                InitCallback = &ShaderToyApp.Init,
                FrameCallback = &ShaderToyApp.Frame,
                EventCallback = &ShaderToyApp.Event,
                CleanupCallback = &ShaderToyApp.Cleanup
            },
            // new SampleInfo
            // {
            //     Id = SampleId.Cgltf,
            //     Name = "GLTF Model Viewer",
            //     Description = "Load and display a glTF model with PBR materials",
            //     InitCallback = &CGltfApp.Init,
            //     FrameCallback = &CGltfApp.Frame,
            //     EventCallback = &CGltfApp.Event,
            //     CleanupCallback = &CGltfApp.Cleanup
            // },
            new SampleInfo
            {
                Id = SampleId.Sdf,
                Name = "SDF Rendering",
                Description = "Signed Distance Field rendering demo with animated shapes",
                InitCallback = &SdfApp.Init,
                FrameCallback = &SdfApp.Frame,
                EventCallback = &SdfApp.Event,
                CleanupCallback = &SdfApp.Cleanup
            },
            // new SampleInfo
            // {
            //     Id = SampleId.SpineInspector,
            //     Name = "Spine Inspector",
            //     Description = "Interactive Spine skeletal animation inspector with multiple characters",
            //     InitCallback = &SpineInspectorApp.Init,
            //     FrameCallback = &SpineInspectorApp.Frame,
            //     EventCallback = &SpineInspectorApp.Event,
            //     CleanupCallback = &SpineInspectorApp.Cleanup
            // }
        };
    }

    private static void StartSample(SampleId sampleId)
    {
        // Defer sample start to next frame to avoid ImGui shutdown issues
        state.pendingSample = sampleId;
    }

    private static void StartSampleImmediate(SampleId sampleId)
    {
        if (sampleId == SampleId.None) return;

        // Find the sample
        SampleInfo sample = default;
        foreach (var s in samples)
        {
            if (s.Id == sampleId)
            {
                sample = s;
                break;
            }
        }

        if (sample.InitCallback == null) return;

        Log($"Starting sample: {sample.Name}");
        Log($"[Native] Shutting down ImGui for sample transition...");

        // Only shutdown ImGui, keep graphics context alive
        simgui_shutdown();
        
        Log($"[Native] ImGui shutdown complete");

        // Set current sample callbacks
        CurrentInitCallback = sample.InitCallback;
        CurrentFrameCallback = sample.FrameCallback;
        CurrentEventCallback = sample.EventCallback;
        CurrentCleanupCallback = sample.CleanupCallback;

        state.currentSample = sampleId;
        state.showMenu = false;

        // Initialize the sample (it will set up its own graphics state)
        CurrentInitCallback();
    }

    private static void StopCurrentSample()
    {
        if (state.currentSample == SampleId.None) return;

        Log($"Stopping sample: {state.currentSample}");

        // Cleanup current sample (samples handle their own simgui_shutdown and sg_shutdown)
        if (CurrentCleanupCallback != null)
        {
            CurrentCleanupCallback();
        }

        // Reset callbacks
        CurrentInitCallback = null;
        CurrentFrameCallback = null;
        CurrentEventCallback = null;
        CurrentCleanupCallback = null;

        state.currentSample = SampleId.None;
        state.showMenu = true;

        // Reinitialize ImGui for menu (reuse existing graphics context)
        Log($"[Native] Reinitializing ImGui for menu...");
        simgui_setup(new simgui_desc_t
        {
            logger = {
                func = &slog_func,
            }
        });
        
        ImGuiIO* io = igGetIO_Nil();
        io->ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        Log($"[Native] ImGui initialized for menu");
    }

    [UnmanagedCallersOnly]
    private static unsafe void Frame()
    {
        // Check if we need to stop current sample (triggered by back button)
        if (state.requestStopSample)
        {
            state.requestStopSample = false;
            StopCurrentSample();
            return;
        }
        
        // Check if we need to start a pending sample
        if (state.pendingSample != SampleId.None)
        {
            StartSampleImmediate(state.pendingSample);
            state.pendingSample = SampleId.None;
        }

        // If a sample is running, delegate to it
        if (state.currentSample != SampleId.None && CurrentFrameCallback != null)
        {
            CurrentFrameCallback();
            return;
        }

        // Otherwise, show the menu
        simgui_new_frame(new simgui_frame_desc_t
        {
            width = sapp_width(),
            height = sapp_height(),
            delta_time = sapp_frame_duration(),
            dpi_scale = 1
        });

        DrawMenu();

        sg_begin_pass(new sg_pass { action = state.pass_action, swapchain = sglue_swapchain() });
        simgui_render();
        sg_end_pass();
        sg_commit();
    }

    private static void DrawMenu()
    {
        float windowWidth = sapp_widthf();
        float windowHeight = sapp_heightf();
        
        igSetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.Always);
        igSetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always, Vector2.Zero);

        // Make scrollbar thicker
        igPushStyleVar_Float(ImGuiStyleVar.ScrollbarSize, 20.0f);

        byte open = 1;
        if (igBegin("Sokol.NET Sample Browser", ref open, 
            ImGuiWindowFlags.NoTitleBar|
            ImGuiWindowFlags.NoCollapse | 
            ImGuiWindowFlags.NoResize | 
            ImGuiWindowFlags.NoMove))
        {
            igText("Sokol.NET Sample Browser");
            igSpacing();
            igText("Cross-platform graphics framework for C# with .NET NativeAOT");
            igText("Desktop | Mobile | Web | Direct3D | Metal | OpenGL | WebGL");
            igSpacing();
            igText("Interactive demos featuring 3D graphics, physics engines (Jolt, Box2D),");
            igText("model loading (glTF, Assimp), shader effects, and more");
            igText("all running with near-native performance.");
            igSpacing();
            igText("Open source (MIT License) | 38 examples | Full source available");
            igText("https://github.com/elix22/Sokol.NET");
            igSpacing();
            if (igButton("Attributions", new Vector2(120, 0)))
            {
                state.showAttributions = true;
            }
            igSpacing();
            igText("Select a demo below to explore:");
#if __ANDROID__ || __IOS__
            igText("Tap the Back button to return to this menu.");
#else
            igText("Press ESC to return to this menu.");
#endif
            igSeparator();
            igSpacing();

            // Sample list
            foreach (var sample in samples)
            {
                igPushID_Int((int)sample.Id);
                
                igText(sample.Description);
                igSpacing();
                
                if (igButton(sample.Name, new Vector2(windowWidth - 40, 0)))
                {
                    StartSample(sample.Id);
                    state.showAttributions = false;
                }
                
                igSpacing();
                igSeparator();
                igSpacing();
                
                igPopID();
            }

            igEnd();
        }
        
        igPopStyleVar(1);
        
        // Draw asset attributions dialog
        DrawAttributionsDialog(windowWidth, windowHeight);
    }

    private static void DrawAttributionsDialog(float windowWidth, float windowHeight)
    {
        if (!state.showAttributions) return;
        
        float dialogWidth = 700;
        float dialogHeight = 500;
        igSetNextWindowSize(new Vector2(dialogWidth, dialogHeight), ImGuiCond.Always);
        igSetNextWindowPos(new Vector2(windowWidth / 2 - dialogWidth / 2, windowHeight / 2 - dialogHeight / 2), ImGuiCond.Always, Vector2.Zero);
        
        byte attributionsOpen = 1;
        if (igBegin("Asset Attributions", ref attributionsOpen, 
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse))
        {
            igText("This application uses the following open source assets:");
            igSpacing();
            igSeparator();
            igSpacing();
            
            // 3D Models
            igTextColored(new Vector4(0.2f, 0.8f, 1.0f, 1.0f), "3D Models:");
            igSpacing();
            
            // Car Concept
            igBulletText("Car Concept");
            igText("   Artist: Eric Chadwick");
            igText("   Owner: Darmstadt Graphics Group GmbH");
            igText("   License: CC BY 4.0");
            igSpacing();
            
            // Chronograph Watch
            igBulletText("Chronograph Watch");
            igText("   Artist: Eric Chadwick");
            igText("   Owner: Darmstadt Graphics Group GmbH");
            igText("   License: CC BY 4.0");
            igSpacing();
            
            // Commercial Refrigerator
            igBulletText("Commercial Refrigerator");
            igText("   Artist: Eric Chadwick (conversion)");
            igText("   Based on work by: Sean Thomas");
            igText("   License: CC BY 4.0");
            igSpacing();
        
            
            // Littlest Tokyo
            igBulletText("Littlest Tokyo");
            igText("   Artist: 3D Models Low Poly");
            igText("   Source: Sketchfab");
            igText("   License: CC BY 4.0");
            igSpacing();
            
            // BoomBox
            igBulletText("BoomBox");
            igText("   Artist: Microsoft");
            igText("   License: CC0 (Public Domain)");
            igSpacing();
            
            // Water Bottle
            igBulletText("Water Bottle");
            igText("   Artist: Microsoft");
            igText("   License: CC0 (Public Domain)");
            igSpacing();
            
            // Glass Vase Flowers
            igBulletText("Glass Vase with Flowers");
            igText("   Artists: Eric Chadwick & Rico Cilliers");
            igText("   License: CC0 (Public Domain)");
            igSpacing();
            
            // Diffuse Transmission Plant
            igBulletText("Diffuse Transmission Plant");
            igText("   Artists: Eric Chadwick & Rico Cilliers");
            igText("   Owner: Darmstadt Graphics Group GmbH");
            igText("   License: CC BY 4.0 / CC0");
            igSpacing();
            
            // Glass Hurricane Candle Holder
            igBulletText("Glass Hurricane Candle Holder");
            igText("   Artist: Eric Chadwick");
            igText("   Owner: Wayfair, LLC");
            igText("   License: CC BY 4.0");
            igSpacing();
            
            // Morph Stress Test
            igBulletText("Morph Stress Test");
            igText("   Artist: Ed Mackey");
            igText("   Owner: Analytical Graphics, Inc.");
            igText("   License: CC BY 4.0");
            igSpacing();
            
            // ShaderToy Shaders
            igTextColored(new Vector4(0.2f, 0.8f, 1.0f, 1.0f), "ShaderToy Shaders:");
            igSpacing();
            
            // Raymarching Primitives
            igBulletText("Raymarching Primitives");
            igText("   Author: Inigo Quilez");
            igText("   License: MIT");
            igSpacing();
            
            // Procedural Ocean
            igBulletText("Procedural Ocean");
            igText("   Author: afl_ext");
            igText("   License: MIT");
            igSpacing();
            
            igSeparator();
            igSpacing();
            igText("All CC BY 4.0 licensed content:");
            igText("https://creativecommons.org/licenses/by/4.0/");
            igSpacing();
            
            if (igButton("Close", new Vector2(100, 30)))
            {
                state.showAttributions = false;
            }
            
            igEnd();
        }
        
        if (attributionsOpen == 0)
        {
            state.showAttributions = false;
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe void Event(sapp_event* e)
    {
        // If a sample is running, check for back/escape
        if (state.currentSample != SampleId.None)
        {
            // ESC key or Back button to return to menu
            if (e->type == sapp_event_type.SAPP_EVENTTYPE_KEY_DOWN && 
                e->key_code == sapp_keycode.SAPP_KEYCODE_ESCAPE)
            {
                StopCurrentSample();
                return;
            }

            // Delegate event to current sample
            if (CurrentEventCallback != null)
            {
                CurrentEventCallback(e);
            }
            return;
        }

        // Handle ImGui events when in menu
        if (state.showMenu)
        {
            simgui_handle_event(*e);
        }
    }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        // Cleanup current sample if any
        if (state.currentSample != SampleId.None && CurrentCleanupCallback != null)
        {
            CurrentCleanupCallback();
        }
        else
        {
            // Cleanup ImGui if in menu mode
            if (state.showMenu)
            {
                simgui_shutdown();
            }

            sg_shutdown();
        }

        // Force garbage collection to clean up any remaining managed objects
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Force a complete shutdown if debugging
        if (Debugger.IsAttached)
        {
            Environment.Exit(0);
        }
    }

    public static SApp.sapp_desc sokol_main()
    {
        return new SApp.sapp_desc()
        {
            init_cb = &Init,
            frame_cb = &Frame,
            event_cb = &Event,
            cleanup_cb = &Cleanup,
            width = 1024,
            height = 768,
            sample_count = 4,
            window_title = "Sokol.NET Sample Browser",
            icon = { sokol_default = true },
            logger = {
                func = &slog_func,
            }
        };
    }
}
