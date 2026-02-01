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
        Dyntex,
        Drawcallperf,
        Offscreen,
        Instancing,
        Loadpng,
        CubemapJpeg,
        Box2dPhysics,
        ShaderToyApp
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
        
        // Position at top-left corner
        igSetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(100, 50), ImGuiCond.Always);
        
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
            if (igButton("← Back", new Vector2(84, 34)))
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
        Console.WriteLine("SampleBrowser Initialize() Enter");

        sg_setup(new sg_desc()
        {
            environment = sglue_environment(),
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
            }
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

        Console.WriteLine($"Starting sample: {sample.Name}");

        // Shutdown ImGui before starting sample (samples will reinitialize it if needed)
        simgui_shutdown();

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

        Console.WriteLine($"Stopping sample: {state.currentSample}");

        // Cleanup current sample
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

        sg_setup(new sg_desc()
        {
            environment = sglue_environment(),
            logger = {
                func = &slog_func,
            }
        });

        // Reinitialize ImGui for menu
        simgui_setup(new simgui_desc_t
        {
            logger = {
                func = &slog_func,
            }
        });
        
        ImGuiIO* io = igGetIO_Nil();
        io->ConfigFlags |= ImGuiConfigFlags.DockingEnable;
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

        byte open = 1;
        if (igBegin("Sokol.NET Sample Browser", ref open, 
            ImGuiWindowFlags.NoCollapse | 
            ImGuiWindowFlags.NoResize | 
            ImGuiWindowFlags.NoMove))
        {
            igText("Welcome to the Sokol.NET Sample Browser!");
            igText("Select a sample from the list below to run it.");
            igText("Press ESC while in a sample to return to this menu.");
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
                }
                
                igSpacing();
                igSeparator();
                igSpacing();
                
                igPopID();
            }

            igEnd();
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
