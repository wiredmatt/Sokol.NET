using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Sokol;
using Imgui;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SG.sg_vertex_format;
using static Sokol.SG.sg_index_type;
using static Sokol.SG.sg_cull_mode;
using static Sokol.SG.sg_compare_func;
using static Sokol.Utils;
using static Sokol.SImgui;
using static Sokol.SDebugText;
using static Imgui.ImguiNative;
using static Imgui.ImGuiHelpers;
using static Sokol.SLog;
using static reversi_shader_cs.Shaders;
using Reversi;

public static unsafe class ReversiApp
{
    // -----------------------------------------------------------------------
    // Unmanaged state (Sokol GPU handles only)
    // -----------------------------------------------------------------------
    struct _state
    {
        // Pipelines
        public sg_pipeline boardPip;
        public sg_pipeline discPip;

        // Board surface mesh (two-color checkerboard, two draw calls)
        public sg_buffer boardDarkVBuf, boardDarkIBuf;
        public sg_buffer boardLightVBuf, boardLightIBuf;
        public uint boardCellIdxCount;          // indices per color group = 32*6 = 192

        // Board border/frame
        public sg_buffer frameBuf, frameIBuf;
        public uint frameIdxCount;

        // Valid-move marker (small quad at origin – translated per marker)
        public sg_buffer markerVBuf, markerIBuf;

        // Hollow ring for last-placed disc highlight
        public sg_buffer ringVBuf, ringIBuf;
        public uint ringIdxCount;

        // Disc data (two sub-meshes: 0=black cap, 1=white cap)
        public sg_buffer discVBuf0, discIBuf0;
        public uint discIdxCount0;
        public sg_buffer discVBuf1, discIBuf1;
        public uint discIdxCount1;
        public bool discLoaded;

        // Pass action
        public sg_pass_action passAction;
    }

    static _state S;

    // -----------------------------------------------------------------------
    // Managed state (lives outside the unsafe struct)
    // -----------------------------------------------------------------------
    static readonly ReversiGame _game = new();
    static List<int>  _validMoves  = new();
    static float      _mouseX, _mouseY;
    static bool       _mouseClicked;
    static Matrix4x4  _view, _proj;
    static readonly Vector3 _cameraPos = new(0f, 10f, 5f);
    static readonly Vector3 _lightPos  = new(4f, 12f, 6f);

    // Per-cell rotation angle for disc rendering (0=white-up, π=black-up)
    static float[] _discAngles = new float[64];

    // Highlight the most-recently placed disc for 1.2 seconds
    static int   _placedHighlightCell    = -1;
    static float _placedHighlightTimer   =  0f;
    static byte  _showPlacedHighlight    =  1;  // 1 = enabled

    // -----------------------------------------------------------------------
    // Vertex helper: position + normal (24 bytes)
    // -----------------------------------------------------------------------
    static float[] V(float px, float py, float pz, float nx, float ny, float nz)
        => new[] { px, py, pz, nx, ny, nz };

    // -----------------------------------------------------------------------
    // Init
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static unsafe void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger = { func = &slog_func }
        });

        simgui_setup(new simgui_desc_t { logger = { func = &slog_func } });

        var sdtxDesc = new sdtx_desc_t();
        sdtxDesc.fonts[0] = sdtx_font_kc854();
        sdtx_setup(sdtxDesc);
        FileSystem.Instance.Initialize();

        // Pass action – dark background
        S.passAction = default;
        S.passAction.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        S.passAction.colors[0].clear_value = new sg_color { r = 0.08f, g = 0.08f, b = 0.08f, a = 1.0f };

        // Camera & projection
        UpdateCameraMatrices();

        // Build geometry
        BuildBoardMesh();
        BuildFrameMesh();
        BuildMarkerMesh();
        BuildRingMesh();

        // Pipelines
        CreateBoardPipeline();
        CreateDiscPipeline();

        // Generate disc meshes procedurally (no OBJ loading needed)
        GenerateDiscMeshes();

        // Game init
        StartNewGame();
    }

    // -----------------------------------------------------------------------
    // Frame
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static unsafe void Frame()
    {
        float dt = (float)sapp_frame_duration();

        // Update FileSystem (drives sokol-fetch callbacks)
        FileSystem.Instance.Update();

        // Poll AI result and update flip animations
        _game.PollAIResult();

        // Track newly placed disc for highlight
        if (_game.LastPlacedCell >= 0 && _game.LastPlacedCell != _placedHighlightCell)
        {
            _placedHighlightCell  = _game.LastPlacedCell;
            _placedHighlightTimer = 1.2f;
        }
        if (_placedHighlightTimer > 0f) _placedHighlightTimer -= dt;

        // Sync disc angles every frame so AI moves and resets are always reflected
        SyncDiscAngles();

        // Update flip animations
        UpdateAnimations(dt);

        // Handle mouse click (picking)
        if (_mouseClicked)
        {
            _mouseClicked = false;
            if (_game.Phase == GamePhase.PlayerTurn)
            {
                int cell = PickCell(_mouseX, _mouseY);
                if (cell >= 0)
                {
                    CellState human = _game.PlayerIsBlack ? CellState.Black : CellState.White;
                    if (_game.TryApplyMove(cell, human))
                    {
                        SyncDiscAngles();
                        _validMoves.Clear();
                    }
                }
            }
        }

        // Refresh valid moves list for rendering
        if (_game.Phase == GamePhase.PlayerTurn)
        {
            CellState hr = _game.PlayerIsBlack ? CellState.Black : CellState.White;
            _validMoves = _game.GetValidMoves(hr);
        }
        else
        {
            _validMoves.Clear();
        }

        // -- ImGui new frame
        simgui_new_frame(new simgui_frame_desc_t
        {
            width = sapp_width(),
            height = sapp_height(),
            delta_time = sapp_frame_duration()
        });
        DrawImGui();

        // Prepare game-over sdtx text (must be before sg_begin_pass)
        PrepareGameOverSdtx();

        // -- 3-D Render
        sg_begin_pass(new sg_pass { action = S.passAction, swapchain = sglue_swapchain() });

        // Board surface
        DrawBoard();

        // Valid-move markers
        if (_game.Phase == GamePhase.PlayerTurn)
            DrawMarkers();

        // Discs
        if (S.discLoaded)
            DrawDiscs();

        // Highlight the last-placed disc
        if (S.discLoaded && _showPlacedHighlight != 0 && _placedHighlightTimer > 0f)
            DrawPlacedHighlight();

        // Frame border
        DrawBoardFrame();

        simgui_render();
        sdtx_draw();
        sg_end_pass();
        sg_commit();
    }

    // -----------------------------------------------------------------------
    // Event
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static unsafe void Event(sapp_event* e)
    {
        if (e == null) return;
        simgui_handle_event(in *e);

        switch (e->type)
        {
            case sapp_event_type.SAPP_EVENTTYPE_MOUSE_MOVE:
                _mouseX = e->mouse_x;
                _mouseY = e->mouse_y;
                break;
            case sapp_event_type.SAPP_EVENTTYPE_MOUSE_UP:
                if (e->mouse_button == sapp_mousebutton.SAPP_MOUSEBUTTON_LEFT)
                {
                    _mouseX = e->mouse_x;
                    _mouseY = e->mouse_y;
                    _mouseClicked = true;
                }
                break;
            case sapp_event_type.SAPP_EVENTTYPE_TOUCHES_ENDED:
                if (e->num_touches > 0)
                {
                    _mouseX = e->touches[0].pos_x;
                    _mouseY = e->touches[0].pos_y;
                    _mouseClicked = true;
                }
                break;
            case sapp_event_type.SAPP_EVENTTYPE_RESIZED:
                UpdateCameraMatrices();
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        simgui_shutdown();
        sdtx_shutdown();
        FileSystem.Instance.Shutdown();
        sg_shutdown();
        if (Debugger.IsAttached) Environment.Exit(0);
    }

    // -----------------------------------------------------------------------
    // sokol_main descriptor
    // -----------------------------------------------------------------------
    public static SApp.sapp_desc sokol_main()
    {
        return new SApp.sapp_desc
        {
            init_cb    = &Init,
            frame_cb   = &Frame,
            event_cb   = &Event,
            cleanup_cb = &Cleanup,
            width      = 960,
            height     = 768,
            sample_count = 4,
            window_title = "Reversi",
            icon = { sokol_default = true },
            logger = { func = &slog_func }
        };
    }

    // =======================================================================
    // Camera / matrices
    // =======================================================================
    static void UpdateCameraMatrices()
    {
        float w = MathF.Max(1f, sapp_widthf());
        float h = MathF.Max(1f, sapp_heightf());
        _proj = Matrix4x4.CreatePerspectiveFieldOfView(
            50f * MathF.PI / 180f, w / h, 0.1f, 100f);
        _view = Matrix4x4.CreateLookAt(_cameraPos, new Vector3(0, 0, 0), Vector3.UnitY);
    }

    // =======================================================================
    // Procedural disc mesh generation
    // Generates two half-cylinder sub-meshes:
    //   Sub-mesh 0 = black (bottom cap + lower side band, Y in [-h, 0])
    //   Sub-mesh 1 = white (top cap   + upper side band, Y in [0, +h])
    // angle=0 → white on top (white disc visible from above)
    // angle=π → rotate 180° around X → black on top (black disc visible)
    // =======================================================================
    static void GenerateDiscMeshes()
    {
        const int N = 40;       // segments around circumference
        const float r = 0.44f;  // disc radius (fits inside 1-unit cell)
        const float h = 0.13f;  // half-height (disc = 2h = 0.26 units thick)

        var v0 = new List<float>();  var i0 = new List<ushort>();  // black (bottom)
        var v1 = new List<float>();  var i1 = new List<ushort>();  // white (top)

        ushort AddV0(float px, float py, float pz, float nx, float ny, float nz)
        {
            ushort idx = (ushort)(v0.Count / 6);
            v0.AddRange(new[] { px, py, pz, nx, ny, nz });
            return idx;
        }
        ushort AddV1(float px, float py, float pz, float nx, float ny, float nz)
        {
            ushort idx = (ushort)(v1.Count / 6);
            v1.AddRange(new[] { px, py, pz, nx, ny, nz });
            return idx;
        }

        // ── Sub-mesh 0: Black (bottom) ──────────────────────────────────
        // Bottom cap: flat circle at Y=-h, normal = (0,-1,0)
        ushort bc = AddV0(0, -h, 0, 0, -1, 0);
        for (int k = 0; k < N; k++)
        {
            float a = k * 2f * MathF.PI / N;
            AddV0(r * MathF.Cos(a), -h, r * MathF.Sin(a), 0, -1, 0);
        }
        for (int k = 0; k < N; k++)
        {
            ushort a = (ushort)(bc + 1 + k);
            ushort b = (ushort)(bc + 1 + (k + 1) % N);
            i0.AddRange(new ushort[] { bc, b, a }); // CW from below
        }
        // Lower side band: quads from Y=-h to Y=0, outward normals
        int sb0 = v0.Count / 6;
        for (int k = 0; k <= N; k++)
        {
            float a  = k * 2f * MathF.PI / N;
            float nx = MathF.Cos(a), nz = MathF.Sin(a);
            AddV0(r * nx, -h, r * nz, nx, 0, nz);
            AddV0(r * nx,  0, r * nz, nx, 0, nz);
        }
        for (int k = 0; k < N; k++)
        {
            int b = sb0 + k * 2;
            i0.AddRange(new ushort[] {
                (ushort)b, (ushort)(b+3), (ushort)(b+1),
                (ushort)b, (ushort)(b+2), (ushort)(b+3)
            });
        }

        // ── Sub-mesh 1: White (top) ─────────────────────────────────────
        // Top cap: flat circle at Y=+h, normal = (0,+1,0)
        ushort tc = AddV1(0, h, 0, 0, 1, 0);
        for (int k = 0; k < N; k++)
        {
            float a = k * 2f * MathF.PI / N;
            AddV1(r * MathF.Cos(a), h, r * MathF.Sin(a), 0, 1, 0);
        }
        for (int k = 0; k < N; k++)
        {
            ushort a = (ushort)(tc + 1 + k);
            ushort b = (ushort)(tc + 1 + (k + 1) % N);
            i1.AddRange(new ushort[] { tc, a, b }); // CCW from above
        }
        // Upper side band: quads from Y=0 to Y=+h, outward normals
        int sb1 = v1.Count / 6;
        for (int k = 0; k <= N; k++)
        {
            float a  = k * 2f * MathF.PI / N;
            float nx = MathF.Cos(a), nz = MathF.Sin(a);
            AddV1(r * nx, 0, r * nz, nx, 0, nz);
            AddV1(r * nx, h, r * nz, nx, 0, nz);
        }
        for (int k = 0; k < N; k++)
        {
            int b = sb1 + k * 2;
            i1.AddRange(new ushort[] {
                (ushort)b, (ushort)(b+1), (ushort)(b+3),
                (ushort)b, (ushort)(b+3), (ushort)(b+2)
            });
        }

        // Upload to GPU
        float[] va0 = v0.ToArray(); ushort[] ia0 = i0.ToArray();
        float[] va1 = v1.ToArray(); ushort[] ia1 = i1.ToArray();
        S.discVBuf0 = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va0), label = "disc-v0" });
        S.discIBuf0 = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer=true }, data = SG_RANGE(ia0), label = "disc-i0" });
        S.discIdxCount0 = (uint)ia0.Length;
        S.discVBuf1 = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va1), label = "disc-v1" });
        S.discIBuf1 = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer=true }, data = SG_RANGE(ia1), label = "disc-i1" });
        S.discIdxCount1 = (uint)ia1.Length;
        S.discLoaded = true;
    }

    // =======================================================================
    // Board mesh – 64 unit-quads (32 dark + 32 light)
    // =======================================================================
    static void BuildBoardMesh()
    {
        var darkV  = new List<float>();
        var lightV = new List<float>();
        var darkI  = new List<ushort>();
        var lightI = new List<ushort>();

        for (int row = 0; row < 8; row++)
        for (int col = 0; col < 8; col++)
        {
            float x0 = col - 4f, x1 = x0 + 1f;
            float z0 = row - 4f, z1 = z0 + 1f;
            const float y = 0f;
            bool dark = ((row + col) & 1) == 0;

            var vl = dark ? darkV  : lightV;
            var il = dark ? darkI  : lightI;

            ushort b = (ushort)(vl.Count / 6);
            // 4 vertices, normal = (0,1,0)
            foreach (var (px, pz) in new[] { (x0,z0),(x1,z0),(x1,z1),(x0,z1) })
                vl.AddRange(new[] { px, y, pz, 0f, 1f, 0f });
            il.AddRange(new ushort[] { b, (ushort)(b+1), (ushort)(b+2), b, (ushort)(b+2), (ushort)(b+3) });
        }

        float[] dv = darkV.ToArray(), lv = lightV.ToArray();
        ushort[] di = darkI.ToArray(), li = lightI.ToArray();

        S.boardDarkVBuf = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(dv), label = "board-dark-v" });
        S.boardDarkIBuf = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer=true }, data = SG_RANGE(di), label = "board-dark-i" });
        S.boardLightVBuf = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(lv), label = "board-light-v" });
        S.boardLightIBuf = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer=true }, data = SG_RANGE(li), label = "board-light-i" });
        S.boardCellIdxCount = 192u; // 32 quads × 6 indices
    }

    // =======================================================================
    // Frame / border mesh (thick wooden frame around the 8×8 board)
    // =======================================================================
    static void BuildFrameMesh()
    {
        float outer = 4.4f, inner = 4.0f;
        float yTop = 0.002f;   // slightly above Y=0 board to avoid z-fighting
        float yBot = -0.4f;

        var verts = new List<float>();
        var idx   = new List<ushort>();
        void AddQuad(float ax, float ay, float az, float bx, float by, float bz,
                     float cx, float cy, float cz, float dx, float dy, float dz,
                     float nx, float ny, float nz)
        {
            ushort b = (ushort)(verts.Count / 6);
            foreach (var (px,py,pz) in new[]{(ax,ay,az),(bx,by,bz),(cx,cy,cz),(dx,dy,dz)})
                verts.AddRange(new[]{px,py,pz,nx,ny,nz});
            idx.AddRange(new ushort[]{b,(ushort)(b+1),(ushort)(b+2),b,(ushort)(b+2),(ushort)(b+3)});
        }

        // Top face ring – 4 non-overlapping strips that stay OUTSIDE the 8×8 board area.
        // North strip (z < -4): full width including corners
        AddQuad(-outer,yTop,-outer,  outer,yTop,-outer,  outer,yTop,-inner, -outer,yTop,-inner, 0,1,0);
        // South strip (z >  4): full width including corners
        AddQuad(-outer,yTop, inner,  outer,yTop, inner,  outer,yTop, outer, -outer,yTop, outer, 0,1,0);
        // West strip (x < -4): middle only, corners already covered above
        AddQuad(-outer,yTop,-inner, -inner,yTop,-inner, -inner,yTop, inner, -outer,yTop, inner, 0,1,0);
        // East strip (x >  4): middle only
        AddQuad( inner,yTop,-inner,  outer,yTop,-inner,  outer,yTop, inner,  inner,yTop, inner, 0,1,0);

        // Side walls
        // South wall (front, facing +Z)
        AddQuad(-outer,yBot, outer,  outer,yBot, outer,  outer,yTop, outer, -outer,yTop, outer, 0,0,1);
        // North wall (back, facing -Z)
        AddQuad( outer,yBot,-outer, -outer,yBot,-outer, -outer,yTop,-outer,  outer,yTop,-outer, 0,0,-1);
        // West wall (facing -X)
        AddQuad(-outer,yBot, inner, -outer,yBot,-outer, -outer,yTop,-outer, -outer,yTop, inner, -1,0,0);
        // East wall (facing +X)
        AddQuad( outer,yBot,-outer,  outer,yBot, inner,  outer,yTop, inner,  outer,yTop,-outer, 1,0,0);

        float[] va = verts.ToArray();
        ushort[] ia = idx.ToArray();
        S.frameBuf  = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va), label = "frame-v" });
        S.frameIBuf = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer=true }, data = SG_RANGE(ia), label = "frame-i" });
        S.frameIdxCount = (uint)ia.Length;
    }

    // =======================================================================
    // Ring mesh — flat annulus at origin (y=0) used for the last-placed highlight
    // =======================================================================
    static void BuildRingMesh()
    {
        const int N    = 48;         // segments
        const float IR = 0.30f;      // inner radius
        const float OR = 0.44f;      // outer radius — matches disc edge
        const float Y  = 0f;

        var verts  = new float[N * 2 * 6];   // N outer + N inner, each (px,py,pz,nx,ny,nz)
        var idx    = new ushort[N * 6];       // 2 tris per segment
        for (int i = 0; i < N; i++)
        {
            float a = 2f * MathF.PI * i / N;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            int vi = i * 12;
            // outer vertex
            verts[vi+0]=c*OR; verts[vi+1]=Y; verts[vi+2]=s*OR;
            verts[vi+3]=0; verts[vi+4]=1; verts[vi+5]=0;
            // inner vertex
            verts[vi+6]=c*IR; verts[vi+7]=Y; verts[vi+8]=s*IR;
            verts[vi+9]=0; verts[vi+10]=1; verts[vi+11]=0;

            int ii = i * 6;
            int o0 = (ushort)(i * 2),     i0 = (ushort)(i * 2 + 1);
            int o1 = (ushort)(((i+1)%N)*2), i1 = (ushort)(((i+1)%N)*2+1);
            idx[ii+0]=(ushort)o0; idx[ii+1]=(ushort)o1; idx[ii+2]=(ushort)i0;
            idx[ii+3]=(ushort)i0; idx[ii+4]=(ushort)o1; idx[ii+5]=(ushort)i1;
        }
        S.ringVBuf    = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(verts), label = "ring-v" });
        S.ringIBuf    = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer=true }, data = SG_RANGE(idx), label = "ring-i" });
        S.ringIdxCount = (uint)(N * 6);
    }

    // Marker mesh – small flat diamond at origin (y=0.005)
    // =======================================================================
    static void BuildMarkerMesh()
    {
        float h = 0.005f, r = 0.22f;
        float[] mv = {
            -r, h,  0,  0,1,0,
             0, h, -r,  0,1,0,
             r, h,  0,  0,1,0,
             0, h,  r,  0,1,0,
        };
        ushort[] mi = { 0,1,2, 0,2,3 };
        S.markerVBuf = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(mv), label = "marker-v" });
        S.markerIBuf = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer=true }, data = SG_RANGE(mi), label = "marker-i" });
    }

    // =======================================================================
    // Board pipeline (Phong, no face-cull, writes depth)
    // =======================================================================
    static void CreateBoardPipeline()
    {
        var shd = sg_make_shader(board_shader_desc(sg_query_backend()));
        var pd  = default(sg_pipeline_desc);
        pd.layout.buffers[0].stride = 24;
        pd.layout.attrs[ATTR_board_position].format = SG_VERTEXFORMAT_FLOAT3;
        pd.layout.attrs[ATTR_board_normal].format   = SG_VERTEXFORMAT_FLOAT3;
        pd.shader     = shd;
        pd.index_type = SG_INDEXTYPE_UINT16;
        pd.cull_mode  = SG_CULLMODE_NONE;
        pd.depth.write_enabled = true;
        pd.depth.compare        = SG_COMPAREFUNC_LESS_EQUAL;
        pd.label = "board-pip";
        S.boardPip = sg_make_pipeline(pd);
    }

    // =======================================================================
    // Disc pipeline (Phong, back-face cull, writes depth)
    // =======================================================================
    static void CreateDiscPipeline()
    {
        var shd = sg_make_shader(disc_shader_desc(sg_query_backend()));
        var pd  = default(sg_pipeline_desc);
        pd.layout.buffers[0].stride = 24;
        pd.layout.attrs[ATTR_disc_position].format = SG_VERTEXFORMAT_FLOAT3;
        pd.layout.attrs[ATTR_disc_normal].format   = SG_VERTEXFORMAT_FLOAT3;
        pd.shader     = shd;
        pd.index_type = SG_INDEXTYPE_UINT16;
        pd.cull_mode  = SG_CULLMODE_NONE;  // depth-sorting handles hemisphere visibility
        pd.depth.write_enabled = true;
        pd.depth.compare        = SG_COMPAREFUNC_LESS_EQUAL;
        pd.label = "disc-pip";
        S.discPip = sg_make_pipeline(pd);
    }

    // =======================================================================
    // Rendering helpers
    // =======================================================================

    // Common matrices used every frame
    static Matrix4x4 VP => _view * _proj;

    /// <summary>Draw the board surface (dark + light cells).</summary>
    static void DrawBoard()
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;

        // Dark cells
        var vsP = default(board_vs_params_t);
        vsP.mvp   = identity * VP;
        vsP.model = identity;
        var fsP = default(board_fs_params_t);
        fsP.light_pos   = _lightPos;
        fsP.light_color = new Vector3(1f, 1f, 0.95f);
        fsP.base_color  = new Vector3(0.05f, 0.28f, 0.05f);
        fsP.view_pos    = _cameraPos;

        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.boardDarkVBuf;
        bind.index_buffer      = S.boardDarkIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.boardCellIdxCount, 1);

        // Light cells
        fsP.base_color = new Vector3(0.09f, 0.42f, 0.09f);
        bind.vertex_buffers[0] = S.boardLightVBuf;
        bind.index_buffer      = S.boardLightIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.boardCellIdxCount, 1);
    }

    /// <summary>Draw the wooden border frame.</summary>
    static void DrawBoardFrame()
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp   = identity * VP;
        vsP.model = identity;
        var fsP = default(board_fs_params_t);
        fsP.light_pos   = _lightPos;
        fsP.light_color = new Vector3(1f, 1f, 0.95f);
        fsP.base_color  = new Vector3(0.5f, 0.28f, 0.07f);
        fsP.view_pos    = _cameraPos;
        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.frameBuf;
        bind.index_buffer      = S.frameIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.frameIdxCount, 1);
    }

    /// <summary>Draw small yellow diamonds on valid-move cells.</summary>
    static void DrawMarkers()
    {
        sg_apply_pipeline(S.boardPip);
        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.markerVBuf;
        bind.index_buffer      = S.markerIBuf;
        sg_apply_bindings(bind);

        var fsP = default(board_fs_params_t);
        fsP.light_pos   = _lightPos;
        fsP.light_color = new Vector3(1f, 1f, 0.95f);
        fsP.base_color  = new Vector3(0.85f, 0.75f, 0.1f);
        fsP.view_pos    = _cameraPos;

        foreach (int cell in _validMoves)
        {
            int col = cell % 8, row = cell / 8;
            var t = Matrix4x4.CreateTranslation(col - 3.5f, 0f, row - 3.5f);
            var vsP = default(board_vs_params_t);
            vsP.mvp   = t * VP;
            vsP.model = t;
            sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
            sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
            sg_draw(0, 6, 1);
        }
    }

    /// <summary>Draw a pulsing hollow ring above the most recently placed disc.</summary>
    static void DrawPlacedHighlight()
    {
        if (_placedHighlightCell < 0) return;

        // Fade out smoothly over the 1.2 s lifetime; also do a single gentle pulse
        float t      = _placedHighlightTimer / 1.2f;          // 1 → 0
        float pulse  = 0.7f + 0.3f * MathF.Sin(t * MathF.PI * 2f);  // one pulse cycle
        float alpha  = t * pulse;                              // overall brightness

        int   col = _placedHighlightCell % 8;
        int   row = _placedHighlightCell / 8;
        float cx  = col - 3.5f;
        float cz  = row - 3.5f;

        const float discH = 0.13f;
        float yRing = discH * 2f + 0.012f;  // just above top face of disc

        sg_apply_pipeline(S.boardPip);
        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.ringVBuf;
        bind.index_buffer      = S.ringIBuf;
        sg_apply_bindings(bind);

        var fsP = default(board_fs_params_t);
        fsP.light_pos   = _lightPos;
        fsP.light_color = new Vector3(1f, 1f, 0.95f);
        fsP.base_color  = new Vector3(alpha, alpha * 0.55f, 0f);  // orange, fades out
        fsP.view_pos    = _cameraPos;

        var trans = Matrix4x4.CreateTranslation(cx, yRing, cz);
        var vsP = default(board_vs_params_t);
        vsP.mvp   = trans * VP;
        vsP.model = trans;
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.ringIdxCount, 1);
    }

    /// <summary>Draw all discs currently on the board.</summary>
    static void DrawDiscs()
    {
        sg_apply_pipeline(S.discPip);

        // Disc vertices are in world-scale units already (r=0.44, h=0.13)
        // Translate center to (cx, h, cz) so the disc bottom sits on the board
        const float discH = 0.13f;

        for (int i = 0; i < 64; i++)
        {
            var cell = _game.Cells[i];
            if (cell == CellState.Empty) continue;

            int col = i % 8, row = i / 8;
            float cx = col - 3.5f, cz = row - 3.5f;

            float angle = _discAngles[i];

            // Check if this disc is actively flipping
            var flipAnim = FindFlipAnim(i);
            if (flipAnim.HasValue)
            {
                float fromAngle = (flipAnim.Value.TargetColor == CellState.Black) ? 0f : MathF.PI;
                angle = fromAngle + flipAnim.Value.Progress * MathF.PI;
            }

            var rot   = Matrix4x4.CreateRotationX(angle);
            var trans = Matrix4x4.CreateTranslation(cx, discH, cz);
            var model = rot * trans;   // rotate first (around local Y center), then translate
            var mvp   = model * VP;

            // Black sub-mesh
            var vsP = default(disc_vs_params_t);
            vsP.mvp   = mvp;
            vsP.model = model;
            var fsP = default(disc_fs_params_t);
            fsP.light_pos   = _lightPos;
            fsP.light_color = new Vector3(1f, 1f, 0.95f);
            fsP.disc_color  = new Vector3(0.05f, 0.05f, 0.05f);
            fsP.view_pos    = _cameraPos;

            var bind = default(sg_bindings);
            bind.vertex_buffers[0] = S.discVBuf0;
            bind.index_buffer      = S.discIBuf0;
            sg_apply_bindings(bind);
            sg_apply_uniforms(UB_disc_vs_params, SG_RANGE<disc_vs_params_t>(ref vsP));
            sg_apply_uniforms(UB_disc_fs_params, SG_RANGE<disc_fs_params_t>(ref fsP));
            sg_draw(0, S.discIdxCount0, 1);

            // White sub-mesh
            fsP.disc_color  = new Vector3(0.92f, 0.92f, 0.92f);
            bind.vertex_buffers[0] = S.discVBuf1;
            bind.index_buffer      = S.discIBuf1;
            sg_apply_bindings(bind);
            sg_apply_uniforms(UB_disc_vs_params, SG_RANGE<disc_vs_params_t>(ref vsP));
            sg_apply_uniforms(UB_disc_fs_params, SG_RANGE<disc_fs_params_t>(ref fsP));
            sg_draw(0, S.discIdxCount1, 1);
        }
    }

    static FlipAnimation? FindFlipAnim(int cellIndex)
    {
        foreach (var a in _game.FlipAnimations)
            if (a.CellIndex == cellIndex) return a;
        return null;
    }

    // =======================================================================
    // Game state helpers
    // =======================================================================

    /// <summary>Sync _discAngles[] to the current Cells[] state (no animation).</summary>
    // =======================================================================
    // Game-over big text (sokol_debugtext)
    // =======================================================================
    static void PrepareGameOverSdtx()
    {
        if (_game.Phase != GamePhase.GameOver) return;

        float w = sapp_widthf(), h = sapp_heightf();

        bool isDraw   = _game.BlackScore == _game.WhiteScore;
        bool humanWon = !isDraw && ((_game.PlayerIsBlack  && _game.BlackScore > _game.WhiteScore)
                                 || (!_game.PlayerIsBlack && _game.WhiteScore > _game.BlackScore));

        string line1 = isDraw ? "D R A W" : (humanWon ? "YOU WIN!" : "AI WINS!");
        int humanScore = _game.PlayerIsBlack ? _game.BlackScore : _game.WhiteScore;
        int aiScore    = _game.PlayerIsBlack ? _game.WhiteScore : _game.BlackScore;
        string line2   = $"You: {humanScore}   AI: {aiScore}";

        // Scale so each character is ~1/10th of screen height, capped for large displays
        float scale1 = MathF.Max(2f, MathF.Min(w / 100f, h / 70f));
        float cols1  = (w / scale1) / 8f;
        float rows1  = (h / scale1) / 8f;

        sdtx_font(0);
        sdtx_canvas(w / scale1, h / scale1);
        sdtx_color3b(255, 210, 30);   // gold
        sdtx_origin(MathF.Max(0f, (cols1 - line1.Length) * 0.5f), rows1 * 0.5f - 1.1f);
        sdtx_puts(line1);

        // Score line – 60% of main scale so it's visibly smaller
        float scale2 = MathF.Max(1.5f, scale1 * 0.6f);
        float cols2  = (w / scale2) / 8f;
        float rows2  = (h / scale2) / 8f;

        sdtx_canvas(w / scale2, h / scale2);
        sdtx_color3b(210, 210, 210);  // light grey
        sdtx_origin(MathF.Max(0f, (cols2 - line2.Length) * 0.5f), rows2 * 0.5f + 1.0f);
        sdtx_puts(line2);
    }

    static void StartNewGame()
    {
        _game.Reset();
        _placedHighlightCell  = -1;
        _placedHighlightTimer =  0f;
        SyncDiscAngles();
        _validMoves.Clear();
        // If human plays White, AI (Black) moves first
        if (!_game.PlayerIsBlack)
            _game.RequestAIMove();
    }

    static void SyncDiscAngles()
    {
        for (int i = 0; i < 64; i++)
        {
            _discAngles[i] = _game.Cells[i] == CellState.Black ? MathF.PI : 0f;
        }
    }

    /// <summary>Advance flip animations; uses game built-in transitions.</summary>
    static void UpdateAnimations(float dt)
    {
        _game.UpdateAnimations(dt);
    }

    // =======================================================================
    // Picking – convert mouse position to board cell index
    // =======================================================================
    static int PickCell(float mx, float my)
    {
        float w = sapp_widthf(), h = sapp_heightf();
        float ndcX =  2f * mx / w - 1f;
        float ndcY = -2f * my / h + 1f;

        Matrix4x4 vp = _view * _proj;
        if (!Matrix4x4.Invert(vp, out Matrix4x4 invVP)) return -1;

        var near = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVP);
        var far  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVP);
        near /= near.W;
        far  /= far.W;

        var dir = Vector3.Normalize(new Vector3(far.X - near.X, far.Y - near.Y, far.Z - near.Z));
        var origin = new Vector3(near.X, near.Y, near.Z);

        if (MathF.Abs(dir.Y) < 1e-6f) return -1;
        float t = -origin.Y / dir.Y;
        if (t < 0f) return -1;

        float wx = origin.X + t * dir.X;
        float wz = origin.Z + t * dir.Z;

        int col = (int)MathF.Floor(wx + 4f);
        int row = (int)MathF.Floor(wz + 4f);
        if (col < 0 || col > 7 || row < 0 || row > 7) return -1;
        return row * 8 + col;
    }

    static byte _uiOpen = 1;

    // =======================================================================
    // ImGui UI
    // =======================================================================
    static void DrawImGui()
    {
        igSetNextWindowPos(new Vector2(5, 5), ImGuiCond.Once, Vector2.Zero);
        igSetNextWindowSize(new Vector2(220, 0), ImGuiCond.Always);
        igBegin("Reversi", ref _uiOpen, ImGuiWindowFlags.NoResize );

        string humanColor = _game.PlayerIsBlack ? "Black" : "White";
        string aiColor    = _game.PlayerIsBlack ? "White" : "Black";
        igText($"{humanColor} (You): {(_game.PlayerIsBlack ? _game.BlackScore : _game.WhiteScore)}");
        igText($"{aiColor} (AI):  {(_game.PlayerIsBlack ? _game.WhiteScore : _game.BlackScore)}");
        igSeparator();

        string phaseStr = _game.Phase switch
        {
            GamePhase.PlayerTurn   => "Your turn",
            GamePhase.AIThinking   => "AI thinking...",
            GamePhase.AnimatingFlip => "Flipping...",
            GamePhase.GameOver     => "Game Over",
            _ => ""
        };
        igText(phaseStr);

        if (_game.Phase == GamePhase.GameOver)
        {
            // Draw semi-transparent dark banner across the centre of the screen
            float sw = sapp_widthf(), sh = sapp_heightf();
            float bannerH = sh * 0.28f;
            float bannerY = (sh - bannerH) * 0.5f;
            var dl = igGetForegroundDrawList_ViewportPtr(igGetMainViewport());
            ImDrawList_AddRectFilled(dl,
                new Vector2(0f, bannerY),
                new Vector2(sw, bannerY + bannerH),
                0xCC000000u, 14f, 0);
        }

        igSeparator();

        int depth = _game.AiDepth;
        igSliderInt("AI depth", ref depth, 1, 10, "%d", 0);
        _game.AiDepth = depth;

        igCheckbox("Show last-move marker", ref _showPlacedHighlight);

        byte playWhite = _game.PlayerIsBlack ? (byte)0 : (byte)1;
        if (igCheckbox("Play as White (AI first)", ref playWhite))
        {
            _game.PlayerIsBlack = (playWhite == 0);
            StartNewGame();
        }

        if (igButton("New Game", new Vector2(-1, 0)))
            StartNewGame();

        if (_game.Phase != GamePhase.AIThinking && _game.Phase != GamePhase.AnimatingFlip)
        {
            if (igButton("Undo", new Vector2(-1, 0)))
            {
                _game.Undo();
                SyncDiscAngles();
                _validMoves = _game.GetValidMoves(
                    _game.PlayerIsBlack ? CellState.Black : CellState.White);
            }
        }

        igSeparator();
        igText(_game.Phase == GamePhase.PlayerTurn
            ? "Click a gold marker to place"
            : "");

        if (!S.discLoaded)
            igText("Loading disc model...");

        igEnd();
    }
}

