// ConnectFour-app.cs — Main Sokol rendering app for Connect 4.
// 7-column × 6-row board, top-down 3D view.
// Human (Red, Player1) vs AI (Yellow, Player2).

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
using static connectfour_shader_cs.Shaders;
using ConnectFour;

public static unsafe class ConnectfourApp
{
    // -----------------------------------------------------------------------
    // Public constants used by ConnectFourGame for animation parameters
    // -----------------------------------------------------------------------
    public const float DISC_REST_Y  = 0.13f;    // Y where disc sits on the board
    public const float DROP_START_Y = 5.5f;     // Y where disc starts falling from
    public const float DROP_SPEED   = 11.0f;    // units per second

    // Board geometry constants
    // Cell center: x = col - 3.0f,  z = 2.5f - row  (row 0 = bottom = near camera)
    // Board bounds: X in [-3.5, 3.5],  Z in [-3.0, 3.0]
    const int   COLS      = ConnectFourGame.COLS;   // 7
    const int   ROWS      = ConnectFourGame.ROWS;   // 6

    static float CellX(int col) => col - 3.0f;
    static float CellZ(int row) => 2.5f - row;

    // -----------------------------------------------------------------------
    // GPU state (unmanaged pipeline handles / buffers)
    // -----------------------------------------------------------------------
    struct _state
    {
        public sg_pipeline boardPip;
        public sg_pipeline discPip;

        // Board: 7 column quads in one buffer (draw col c with sg_draw(c*6, 6, 1))
        public sg_buffer boardVBuf, boardIBuf;

        // Column separators: 6 thin quads between columns
        public sg_buffer sepVBuf, sepIBuf;
        public uint      sepIdxCount;

        // Board frame (wooden border)
        public sg_buffer frameBuf, frameIBuf;
        public uint      frameIdxCount;

        // Ring (win highlight)
        public sg_buffer ringVBuf, ringIBuf;
        public uint      ringIdxCount;

        // Disc sub-meshes
        //   sub-mesh 0 = lower half (bottom cap + lower side band, Y in [-h, 0])
        //   sub-mesh 1 = upper half (top cap + upper side band, Y in [0, +h])
        public sg_buffer discVBuf0, discIBuf0;
        public uint      discIdxCount0;
        public sg_buffer discVBuf1, discIBuf1;
        public uint      discIdxCount1;
        public bool      discLoaded;

        public sg_pass_action passAction;
    }

    static _state S;

    // -----------------------------------------------------------------------
    // Managed state
    // -----------------------------------------------------------------------
    static readonly ConnectFourGame _game = new();
    static float _mouseX, _mouseY;
    static bool  _mouseClicked;
    static int   _hoveredCol = -1;

    static Matrix4x4     _view, _proj;
    static readonly Vector3 _cameraPos = new(0f, 9.5f, 5.5f);
    static readonly Vector3 _lightPos  = new(2f, 12f, 5f);

    // Win-ring pulse timer
    static float _winRingTimer = 0f;

    static byte _uiOpen = 1;

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

        S.passAction = default;
        S.passAction.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        S.passAction.colors[0].clear_value = new sg_color { r = 0.04f, g = 0.04f, b = 0.08f, a = 1f };

        UpdateCameraMatrices();
        BuildColumnMeshes();
        BuildSeparatorMesh();
        BuildFrameMesh();
        BuildRingMesh();
        CreateBoardPipeline();
        CreateDiscPipeline();
        GenerateDiscMeshes();
        StartNewGame();
    }

    // -----------------------------------------------------------------------
    // Frame
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static unsafe void Frame()
    {
        float dt = (float)sapp_frame_duration();

        FileSystem.Instance.Update();

        // Tick game logic
        _game.UpdateDropAnimation(dt);
        _game.PollAIResult();

        // Advance win-ring animation
        if (_game.Phase == GamePhase.GameOver && _game.Winner != CellState.Empty)
            _winRingTimer += dt;

        // Recompute hovered column from mouse (only during player's turn)
        _hoveredCol = (_game.Phase == GamePhase.PlayerTurn)
            ? PickColumn(_mouseX, _mouseY)
            : -1;

        // Process click
        if (_mouseClicked)
        {
            _mouseClicked = false;
            if (_game.Phase == GamePhase.PlayerTurn && _hoveredCol >= 0)
            {
                CellState human = _game.PlayerIsPlayer1 ? CellState.Player1 : CellState.Player2;
                _game.TryDropPiece(_hoveredCol, human);
            }
        }

        // ImGui new frame
        simgui_new_frame(new simgui_frame_desc_t
        {
            width      = sapp_width(),
            height     = sapp_height(),
            delta_time = sapp_frame_duration()
        });
        DrawImGui();

        // Game-over overlay text
        PrepareGameOverSdtx();

        // 3-D pass
        sg_begin_pass(new sg_pass { action = S.passAction, swapchain = sglue_swapchain() });

        DrawBoard();
        DrawSeparators();
        if (_hoveredCol >= 0 && !_game.IsColumnFull(_hoveredCol))
            DrawColumnHover(_hoveredCol);

        if (S.discLoaded)
        {
            DrawDiscs();
            if (_game.DropAnimCol >= 0)
                DrawDropAnimDisc();
            if (_hoveredCol >= 0 && _game.Phase == GamePhase.PlayerTurn && !_game.IsColumnFull(_hoveredCol))
                DrawGhostDisc(_hoveredCol);
        }

        if (_game.Phase == GamePhase.GameOver && _game.WinCells.Length == 4)
            DrawWinRings();

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
    public static SApp.sapp_desc sokol_main() => new SApp.sapp_desc
    {
        init_cb    = &Init,
        frame_cb   = &Frame,
        event_cb   = &Event,
        cleanup_cb = &Cleanup,
        width      = 960,
        height     = 720,
        sample_count = 4,
        window_title = "Connect 4",
        icon = { sokol_default = true },
        logger = { func = &slog_func }
    };

    // =======================================================================
    // Camera / projection
    // =======================================================================
    static void UpdateCameraMatrices()
    {
        float w = MathF.Max(1f, sapp_widthf());
        float h = MathF.Max(1f, sapp_heightf());
        _proj = Matrix4x4.CreatePerspectiveFieldOfView(
            50f * MathF.PI / 180f, w / h, 0.1f, 100f);
        _view = Matrix4x4.CreateLookAt(_cameraPos, new Vector3(0f, 0f, 0.5f), Vector3.UnitY);
    }

    static Matrix4x4 VP => _view * _proj;

    // =======================================================================
    // Geometry builders
    // =======================================================================

    // Board: 7 column quads in one shared vertex + index buffer.
    // Column c occupies: x in [c-3.5, c-2.5],  z in [-3.0, 3.0],  y = 0.
    // Draw column c with:  sg_draw(c * 6, 6, 1)
    static void BuildColumnMeshes()
    {
        var verts = new List<float>();
        var idx   = new List<ushort>();

        for (int c = 0; c < COLS; c++)
        {
            float x0 = c - 3.5f, x1 = c - 2.5f;
            float z0 = -3.0f,    z1 =  3.0f;
            float y  =  0f;

            ushort b = (ushort)(verts.Count / 6);
            foreach (var (px, pz) in new[]{ (x0,z0),(x1,z0),(x1,z1),(x0,z1) })
                verts.AddRange(new[]{ px, y, pz, 0f, 1f, 0f });
            idx.AddRange(new ushort[]{ b,(ushort)(b+1),(ushort)(b+2), b,(ushort)(b+2),(ushort)(b+3) });
        }

        float[]  va = verts.ToArray();
        ushort[] ia = idx.ToArray();
        S.boardVBuf = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va), label = "board-v" });
        S.boardIBuf = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer = true }, data = SG_RANGE(ia), label = "board-i" });
    }

    // Column separators: 6 thin vertical strips between columns
    static void BuildSeparatorMesh()
    {
        var verts = new List<float>();
        var idx   = new List<ushort>();

        float hw = 0.025f;   // half-width
        float y  = 0.003f;   // slightly above board surface

        for (int sep = 1; sep < COLS; sep++)
        {
            float xc = sep - 3.5f;
            float x0 = xc - hw, x1 = xc + hw;
            float z0 = -3.0f,   z1 =  3.0f;

            ushort b = (ushort)(verts.Count / 6);
            foreach (var (px, pz) in new[]{ (x0,z0),(x1,z0),(x1,z1),(x0,z1) })
                verts.AddRange(new[]{ px, y, pz, 0f, 1f, 0f });
            idx.AddRange(new ushort[]{ b,(ushort)(b+1),(ushort)(b+2), b,(ushort)(b+2),(ushort)(b+3) });
        }

        float[]  va = verts.ToArray();
        ushort[] ia = idx.ToArray();
        S.sepVBuf     = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va), label = "sep-v" });
        S.sepIBuf     = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer = true }, data = SG_RANGE(ia), label = "sep-i" });
        S.sepIdxCount = (uint)ia.Length;
    }

    // Board frame: wooden border around the 7×6 grid
    static void BuildFrameMesh()
    {
        float innerX = 3.5f, innerZ = 3.0f;
        float outerX = 4.0f, outerZ = 3.5f;
        float yTop   = 0.002f;
        float yBot   = -0.35f;

        var verts = new List<float>();
        var idx   = new List<ushort>();

        void AddQuad(float ax, float ay, float az, float bx, float by, float bz,
                     float cx, float cy, float cz, float dx, float dy, float dz,
                     float nx, float ny, float nz)
        {
            ushort b0 = (ushort)(verts.Count / 6);
            foreach (var (px,py,pz) in new[]{ (ax,ay,az),(bx,by,bz),(cx,cy,cz),(dx,dy,dz) })
                verts.AddRange(new[]{ px, py, pz, nx, ny, nz });
            idx.AddRange(new ushort[]{ b0,(ushort)(b0+1),(ushort)(b0+2), b0,(ushort)(b0+2),(ushort)(b0+3) });
        }

        // Top face – 4 border strips (exclude board area)
        AddQuad(-outerX,yTop,-outerZ,  outerX,yTop,-outerZ,  outerX,yTop,-innerZ, -outerX,yTop,-innerZ, 0,1,0);  // far
        AddQuad(-outerX,yTop, innerZ,  outerX,yTop, innerZ,  outerX,yTop, outerZ, -outerX,yTop, outerZ, 0,1,0);  // near
        AddQuad(-outerX,yTop,-innerZ, -innerX,yTop,-innerZ, -innerX,yTop, innerZ, -outerX,yTop, innerZ, 0,1,0);  // left
        AddQuad( innerX,yTop,-innerZ,  outerX,yTop,-innerZ,  outerX,yTop, innerZ,  innerX,yTop, innerZ, 0,1,0);  // right

        // Side walls
        AddQuad(-outerX,yBot, outerZ,  outerX,yBot, outerZ,  outerX,yTop, outerZ, -outerX,yTop, outerZ, 0,0,1);
        AddQuad( outerX,yBot,-outerZ, -outerX,yBot,-outerZ, -outerX,yTop,-outerZ,  outerX,yTop,-outerZ, 0,0,-1);
        AddQuad(-outerX,yBot, innerZ, -outerX,yBot,-outerZ, -outerX,yTop,-outerZ, -outerX,yTop, innerZ, -1,0,0);
        AddQuad( outerX,yBot,-outerZ,  outerX,yBot, innerZ,  outerX,yTop, innerZ,  outerX,yTop,-outerZ, 1,0,0);

        float[]  va = verts.ToArray();
        ushort[] ia = idx.ToArray();
        S.frameBuf      = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va), label = "frame-v" });
        S.frameIBuf     = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer = true }, data = SG_RANGE(ia), label = "frame-i" });
        S.frameIdxCount = (uint)ia.Length;
    }

    // Ring mesh (win-cell highlights)
    static void BuildRingMesh()
    {
        const int   N  = 48;
        const float IR = 0.30f;
        const float OR = 0.44f;
        const float Y  = 0f;

        var verts = new float[N * 2 * 6];
        var idx   = new ushort[N * 6];

        for (int i = 0; i < N; i++)
        {
            float a = 2f * MathF.PI * i / N;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            int vi = i * 12;
            verts[vi+0]=c*OR; verts[vi+1]=Y;  verts[vi+2]=s*OR; verts[vi+3]=0; verts[vi+4]=1; verts[vi+5]=0;
            verts[vi+6]=c*IR; verts[vi+7]=Y;  verts[vi+8]=s*IR; verts[vi+9]=0; verts[vi+10]=1; verts[vi+11]=0;

            int ii = i * 6;
            int o0 = i * 2,        in0 = i * 2 + 1;
            int o1 = ((i+1)%N)*2,  in1 = ((i+1)%N)*2+1;
            idx[ii+0]=(ushort)o0; idx[ii+1]=(ushort)o1; idx[ii+2]=(ushort)in0;
            idx[ii+3]=(ushort)in0;idx[ii+4]=(ushort)o1; idx[ii+5]=(ushort)in1;
        }

        S.ringVBuf     = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(verts), label = "ring-v" });
        S.ringIBuf     = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer = true }, data = SG_RANGE(idx), label = "ring-i" });
        S.ringIdxCount = (uint)(N * 6);
    }

    // Disc meshes: two sub-meshes (bottom half + top half), identical to Reversi approach.
    static void GenerateDiscMeshes()
    {
        const int   N = 40;
        const float r = 0.42f;
        const float h = DISC_REST_Y;

        var v0 = new List<float>(); var i0 = new List<ushort>();
        var v1 = new List<float>(); var i1 = new List<ushort>();

        ushort AddV0(float px,float py,float pz,float nx,float ny,float nz)
        { ushort idx=(ushort)(v0.Count/6); v0.AddRange(new[]{px,py,pz,nx,ny,nz}); return idx; }
        ushort AddV1(float px,float py,float pz,float nx,float ny,float nz)
        { ushort idx=(ushort)(v1.Count/6); v1.AddRange(new[]{px,py,pz,nx,ny,nz}); return idx; }

        // Sub-mesh 0: bottom cap + lower side band
        ushort bc = AddV0(0,-h,0, 0,-1,0);
        for (int k=0;k<N;k++) { float a=k*2f*MathF.PI/N; AddV0(r*MathF.Cos(a),-h,r*MathF.Sin(a),0,-1,0); }
        for (int k=0;k<N;k++)
        { ushort a=(ushort)(bc+1+k), b=(ushort)(bc+1+(k+1)%N); i0.AddRange(new ushort[]{bc,b,a}); }
        int sb0=v0.Count/6;
        for (int k=0;k<=N;k++) { float a=k*2f*MathF.PI/N; float nx=MathF.Cos(a),nz=MathF.Sin(a);
            AddV0(r*nx,-h,r*nz,nx,0,nz); AddV0(r*nx,0,r*nz,nx,0,nz); }
        for (int k=0;k<N;k++) { int bb=sb0+k*2;
            i0.AddRange(new ushort[]{(ushort)bb,(ushort)(bb+3),(ushort)(bb+1),(ushort)bb,(ushort)(bb+2),(ushort)(bb+3)}); }

        // Sub-mesh 1: top cap + upper side band
        ushort tc = AddV1(0,h,0,0,1,0);
        for (int k=0;k<N;k++) { float a=k*2f*MathF.PI/N; AddV1(r*MathF.Cos(a),h,r*MathF.Sin(a),0,1,0); }
        for (int k=0;k<N;k++)
        { ushort a=(ushort)(tc+1+k), b=(ushort)(tc+1+(k+1)%N); i1.AddRange(new ushort[]{tc,a,b}); }
        int sb1=v1.Count/6;
        for (int k=0;k<=N;k++) { float a=k*2f*MathF.PI/N; float nx=MathF.Cos(a),nz=MathF.Sin(a);
            AddV1(r*nx,0,r*nz,nx,0,nz); AddV1(r*nx,h,r*nz,nx,0,nz); }
        for (int k=0;k<N;k++) { int bb=sb1+k*2;
            i1.AddRange(new ushort[]{(ushort)bb,(ushort)(bb+1),(ushort)(bb+3),(ushort)bb,(ushort)(bb+3),(ushort)(bb+2)}); }

        float[]  va0=v0.ToArray(); ushort[] ia0=i0.ToArray();
        float[]  va1=v1.ToArray(); ushort[] ia1=i1.ToArray();
        S.discVBuf0=sg_make_buffer(new sg_buffer_desc{data=SG_RANGE(va0),label="disc-v0"});
        S.discIBuf0=sg_make_buffer(new sg_buffer_desc{usage=new sg_buffer_usage{index_buffer=true},data=SG_RANGE(ia0),label="disc-i0"});
        S.discIdxCount0=(uint)ia0.Length;
        S.discVBuf1=sg_make_buffer(new sg_buffer_desc{data=SG_RANGE(va1),label="disc-v1"});
        S.discIBuf1=sg_make_buffer(new sg_buffer_desc{usage=new sg_buffer_usage{index_buffer=true},data=SG_RANGE(ia1),label="disc-i1"});
        S.discIdxCount1=(uint)ia1.Length;
        S.discLoaded=true;
    }

    // =======================================================================
    // Pipeline creation
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

    static void CreateDiscPipeline()
    {
        var shd = sg_make_shader(disc_shader_desc(sg_query_backend()));
        var pd  = default(sg_pipeline_desc);
        pd.layout.buffers[0].stride = 24;
        pd.layout.attrs[ATTR_disc_position].format = SG_VERTEXFORMAT_FLOAT3;
        pd.layout.attrs[ATTR_disc_normal].format   = SG_VERTEXFORMAT_FLOAT3;
        pd.shader     = shd;
        pd.index_type = SG_INDEXTYPE_UINT16;
        pd.cull_mode  = SG_CULLMODE_NONE;
        pd.depth.write_enabled = true;
        pd.depth.compare        = SG_COMPAREFUNC_LESS_EQUAL;
        pd.label = "disc-pip";
        S.discPip = sg_make_pipeline(pd);
    }

    // =======================================================================
    // Rendering helpers
    // =======================================================================

    // Disc / board color palette
    static readonly Vector3 ColorPlayer1    = new(0.85f, 0.12f, 0.08f);  // Red
    static readonly Vector3 ColorPlayer2    = new(0.90f, 0.78f, 0.04f);  // Yellow
    static readonly Vector3 ColorBoardDark  = new(0.06f, 0.10f, 0.48f);  // Dark blue column
    static readonly Vector3 ColorBoardMid   = new(0.08f, 0.13f, 0.56f);  // Alternate blue column
    static readonly Vector3 ColorBoardHover = new(0.12f, 0.22f, 0.75f);  // Hover highlight
    static readonly Vector3 ColorSeparator  = new(0.03f, 0.05f, 0.28f);  // Column separator
    static readonly Vector3 ColorFrame      = new(0.45f, 0.24f, 0.06f);  // Wooden frame

    static board_fs_params_t MakeBoardFsParams(Vector3 color) => new board_fs_params_t
    {
        light_pos   = _lightPos,
        light_color = new Vector3(1f, 1f, 0.95f),
        base_color  = color,
        view_pos    = _cameraPos,
    };

    static disc_fs_params_t MakeDiscFsParams(Vector3 color) => new disc_fs_params_t
    {
        light_pos   = _lightPos,
        light_color = new Vector3(1f, 1f, 0.95f),
        disc_color  = color,
        view_pos    = _cameraPos,
    };

    // Draw the 7-column board surface
    static void DrawBoard()
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp   = identity * VP;
        vsP.model = identity;

        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.boardVBuf;
        bind.index_buffer      = S.boardIBuf;
        sg_apply_bindings(bind);

        for (int col = 0; col < COLS; col++)
        {
            Vector3 color = (col % 2 == 0) ? ColorBoardDark : ColorBoardMid;
            var fsP = MakeBoardFsParams(color);
            sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
            sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
            sg_draw((uint)(col * 6), 6u, 1u);
        }
    }

    // Draw the column separators
    static void DrawSeparators()
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp   = identity * VP;
        vsP.model = identity;
        var fsP = MakeBoardFsParams(ColorSeparator);

        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.sepVBuf;
        bind.index_buffer      = S.sepIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.sepIdxCount, 1);
    }

    static void DrawColumnHover(int col)
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp   = identity * VP;
        vsP.model = identity;
        var fsP = MakeBoardFsParams(ColorBoardHover);

        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.boardVBuf;
        bind.index_buffer      = S.boardIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw((uint)(col * 6), 6u, 1u);
    }

    static void DrawBoardFrame()
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp   = identity * VP;
        vsP.model = identity;
        var fsP = MakeBoardFsParams(ColorFrame);

        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.frameBuf;
        bind.index_buffer      = S.frameIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.frameIdxCount, 1);
    }

    // Draw all placed discs (skip the one currently animating)
    static void DrawDiscs()
    {
        sg_apply_pipeline(S.discPip);

        for (int row = 0; row < ROWS; row++)
        for (int col = 0; col < COLS; col++)
        {
            var cell = _game.Get(row, col);
            if (cell == CellState.Empty) continue;
            // Skip the currently animating disc
            if (_game.DropAnimCol == col && _game.DropAnimY >= 0 && row == _game.LastPlacedRow) continue;

            DrawDiscAt(CellX(col), DISC_REST_Y, CellZ(row), cell, ghost: false);
        }
    }

    // Draw the falling disc during drop animation
    static void DrawDropAnimDisc()
    {
        if (_game.DropAnimCol < 0 || _game.DropAnimY < 0) return;
        DrawDiscAt(CellX(_game.DropAnimCol), _game.DropAnimY, CellZ(_game.LastPlacedRow), _game.DropAnimColor, ghost: false);
    }

    // Draw a dim ghost disc at the landing position of the hovered column
    static void DrawGhostDisc(int col)
    {
        int nextRow = _game.NextRow(col);
        if (nextRow < 0) return;
        CellState human = _game.PlayerIsPlayer1 ? CellState.Player1 : CellState.Player2;
        DrawDiscAt(CellX(col), DISC_REST_Y + 0.55f, CellZ(nextRow), human, ghost: true);
    }

    // Core disc rendering (draws both sub-meshes with the player's color)
    static void DrawDiscAt(float worldX, float worldY, float worldZ, CellState player, bool ghost)
    {
        Vector3 baseColor = (player == CellState.Player1) ? ColorPlayer1 : ColorPlayer2;
        Vector3 color     = ghost ? baseColor * 0.30f : baseColor;

        var trans = Matrix4x4.CreateTranslation(worldX, worldY, worldZ);
        var mvp   = trans * VP;

        var vsP = default(disc_vs_params_t);
        vsP.mvp   = mvp;
        vsP.model = trans;
        var fsP = MakeDiscFsParams(color);

        var bind = default(sg_bindings);

        // Sub-mesh 0 (bottom half)
        bind.vertex_buffers[0] = S.discVBuf0;
        bind.index_buffer      = S.discIBuf0;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_disc_vs_params, SG_RANGE<disc_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_disc_fs_params, SG_RANGE<disc_fs_params_t>(ref fsP));
        sg_draw(0, S.discIdxCount0, 1);

        // Sub-mesh 1 (top half)
        bind.vertex_buffers[0] = S.discVBuf1;
        bind.index_buffer      = S.discIBuf1;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_disc_vs_params, SG_RANGE<disc_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_disc_fs_params, SG_RANGE<disc_fs_params_t>(ref fsP));
        sg_draw(0, S.discIdxCount1, 1);
    }

    // Draw pulsing rings around the 4 winning discs
    static void DrawWinRings()
    {
        if (_game.WinCells.Length != 4) return;
        sg_apply_pipeline(S.boardPip);

        float pulse   = 0.65f + 0.35f * MathF.Sin(_winRingTimer * 5.0f);
        float ringY   = DISC_REST_Y * 2f + 0.015f;

        CellState winner   = _game.Winner;
        Vector3   winColor = (winner == CellState.Player1) ? ColorPlayer1 : ColorPlayer2;

        foreach (int cellIdx in _game.WinCells)
        {
            int row = cellIdx / COLS;
            int col = cellIdx % COLS;

            var trans = Matrix4x4.CreateTranslation(CellX(col), ringY, CellZ(row));
            var vsP = default(board_vs_params_t);
            vsP.mvp   = trans * VP;
            vsP.model = trans;
            var fsP   = MakeBoardFsParams(winColor * pulse);

            var bind = default(sg_bindings);
            bind.vertex_buffers[0] = S.ringVBuf;
            bind.index_buffer      = S.ringIBuf;
            sg_apply_bindings(bind);
            sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
            sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
            sg_draw(0, S.ringIdxCount, 1);
        }
    }

    // =======================================================================
    // Column picking (ray-cast to Y=0 plane, extract column from X)
    // =======================================================================
    static int PickColumn(float mx, float my)
    {
        float w = sapp_widthf(), h = sapp_heightf();
        if (w < 1f || h < 1f) return -1;

        float ndcX =  2f * mx / w - 1f;
        float ndcY = -2f * my / h + 1f;

        Matrix4x4 vp = _view * _proj;
        if (!Matrix4x4.Invert(vp, out Matrix4x4 invVP)) return -1;

        var nearV = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVP);
        var farV  = Vector4.Transform(new Vector4(ndcX, ndcY,  1f, 1f), invVP);
        nearV /= nearV.W; farV /= farV.W;

        var dir    = Vector3.Normalize(new Vector3(farV.X - nearV.X, farV.Y - nearV.Y, farV.Z - nearV.Z));
        var origin = new Vector3(nearV.X, nearV.Y, nearV.Z);

        if (MathF.Abs(dir.Y) < 1e-6f) return -1;
        float t = -origin.Y / dir.Y;
        if (t < 0f) return -1;

        float wx = origin.X + t * dir.X;
        float wz = origin.Z + t * dir.Z;

        // Must land within board Z bounds
        if (wz < -3.0f || wz > 3.0f) return -1;

        int col = (int)MathF.Floor(wx + 3.5f);
        if (col < 0 || col >= COLS) return -1;
        return col;
    }

    // =======================================================================
    // Game-over overlay text
    // =======================================================================
    static void PrepareGameOverSdtx()
    {
        if (_game.Phase != GamePhase.GameOver) return;

        float sw = sapp_widthf(), sh = sapp_heightf();
        bool isDraw   = _game.IsDraw;
        bool humanWon = !isDraw &&
            ((_game.PlayerIsPlayer1  && _game.Winner == CellState.Player1) ||
             (!_game.PlayerIsPlayer1 && _game.Winner == CellState.Player2));

        string line1 = isDraw ? "D R A W" : (humanWon ? "YOU WIN!" : "AI WINS!");
        string line2 = $"Red: {_game.Player1Wins}   Yellow: {_game.Player2Wins}";

        float scale1 = MathF.Max(2f, MathF.Min(sw / 100f, sh / 70f));
        float cols1  = (sw / scale1) / 8f;
        float rows1  = (sh / scale1) / 8f;

        sdtx_font(0);
        sdtx_canvas(sw / scale1, sh / scale1);
        if (isDraw)        sdtx_color3b(200, 200, 200);
        else if (humanWon) sdtx_color3b(50, 230, 50);
        else               sdtx_color3b(230, 60, 60);
        sdtx_origin(MathF.Max(0f, (cols1 - line1.Length) * 0.5f), rows1 * 0.5f - 1.1f);
        sdtx_puts(line1);

        float scale2 = MathF.Max(1.5f, scale1 * 0.55f);
        float cols2  = (sw / scale2) / 8f;
        float rows2  = (sh / scale2) / 8f;
        sdtx_canvas(sw / scale2, sh / scale2);
        sdtx_color3b(200, 200, 200);
        sdtx_origin(MathF.Max(0f, (cols2 - line2.Length) * 0.5f), rows2 * 0.5f + 1.0f);
        sdtx_puts(line2);
    }

    static void StartNewGame()
    {
        _winRingTimer = 0f;
        _game.Reset();
        if (!_game.PlayerIsPlayer1)
            _game.RequestAIMove();
    }

    // =======================================================================
    // ImGui UI panel
    // =======================================================================
    static void DrawImGui()
    {
        igSetNextWindowPos(new Vector2(5, 5), ImGuiCond.Once, Vector2.Zero);
        igSetNextWindowSize(new Vector2(230, 0), ImGuiCond.Always);
        igBegin("Connect 4", ref _uiOpen, ImGuiWindowFlags.NoResize);

        string humanColor = _game.PlayerIsPlayer1 ? "Red" : "Yellow";
        string aiColor    = _game.PlayerIsPlayer1 ? "Yellow" : "Red";
        int humanWins     = _game.PlayerIsPlayer1 ? _game.Player1Wins : _game.Player2Wins;
        int aiWins        = _game.PlayerIsPlayer1 ? _game.Player2Wins : _game.Player1Wins;

        igText($"{humanColor} (You): {humanWins} wins");
        igText($"{aiColor} (AI):   {aiWins} wins");
        igSeparator();

        string phaseStr = _game.Phase switch
        {
            GamePhase.PlayerTurn => "Your turn — click a column",
            GamePhase.AIThinking => "AI thinking...",
            GamePhase.Dropping   => "Dropping...",
            GamePhase.GameOver   => _game.IsDraw ? "Draw!" :
                                    (_game.Winner == (_game.PlayerIsPlayer1 ? CellState.Player1 : CellState.Player2)
                                        ? "You win!" : "AI wins!"),
            _ => ""
        };
        igText(phaseStr);

        if (_game.Phase == GamePhase.GameOver)
        {
            float sw = sapp_widthf(), sh = sapp_heightf();
            float bH = sh * 0.24f;
            float bY = (sh - bH) * 0.5f;
            var dl = igGetForegroundDrawList_ViewportPtr(igGetMainViewport());
            ImDrawList_AddRectFilled(dl,
                new Vector2(0f, bY), new Vector2(sw, bY + bH),
                0xCC000000u, 12f, 0);
        }

        igSeparator();

        int depth = _game.AiDepth;
        igSliderInt("AI depth", ref depth, 1, 10, "%d", 0);
        _game.AiDepth = depth;

        byte playAsYellow = _game.PlayerIsPlayer1 ? (byte)0 : (byte)1;
        if (igCheckbox("Play as Yellow (AI first)", ref playAsYellow))
        {
            _game.PlayerIsPlayer1 = (playAsYellow == 0);
            StartNewGame();
        }

        if (igButton("New Game", new Vector2(-1, 0)))
            StartNewGame();

        if (_game.Phase != GamePhase.AIThinking && _game.Phase != GamePhase.Dropping)
        {
            if (igButton("Undo", new Vector2(-1, 0)))
                _game.Undo();
        }

        igSeparator();
        if (_hoveredCol >= 0)
            igText(_game.IsColumnFull(_hoveredCol) ? "Column full!" : $"Column {_hoveredCol + 1}");

        igEnd();
    }
}
