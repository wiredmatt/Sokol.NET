// Checkers-app.cs — Sokol.NET rendering app for Checkers / Draughts.
// Supports 8×8 and 10×10 boards, configurable rules, Human vs AI.

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
using static checkers_shader_cs.Shaders;
using Checkers;

public static unsafe class CheckersApp
{
    // -----------------------------------------------------------------------
    // GPU state
    // -----------------------------------------------------------------------
    struct _state
    {
        public sg_pipeline boardPip;
        public sg_pipeline piecePip;

        public sg_buffer boardDarkVBuf, boardDarkIBuf;
        public sg_buffer boardLightVBuf, boardLightIBuf;
        public uint      boardDarkIdxCount, boardLightIdxCount;

        public sg_buffer highlightVBuf, highlightIBuf;

        public sg_buffer frameBuf, frameIBuf;
        public uint      frameIdxCount;

        public sg_buffer pieceTopVBuf, pieceTopIBuf;
        public uint      pieceTopIdxCount;
        public sg_buffer pieceSideVBuf, pieceSideIBuf;
        public uint      pieceSideIdxCount;
        public sg_buffer crownVBuf, crownIBuf;
        public uint      crownIdxCount;
        public sg_buffer labelVBuf, labelIBuf;
        public uint      labelIdxCount;
        public bool      pieceLoaded;

        public sg_pass_action passAction;
    }

    static _state S;

    // -----------------------------------------------------------------------
    // Managed state
    // -----------------------------------------------------------------------
    static readonly CheckersGame _game = new();

    static GameRules _pendingRules  = GameRules.International;
    static bool      _humanIsLight  = true;

    static float     _mouseX, _mouseY;
    static bool      _mouseClicked;

    static Matrix4x4 _view, _proj;
    static Vector3   _cameraPos = new(0f, 11f, 7f);
    static Vector3   _lightPos  = new(3f, 14f, 8f);

    // Piece colors — editable via ImGui
    static Vector3   _lightPieceColor = new(0.90f, 0.88f, 0.82f);  // cream-white
    static Vector3   _darkPieceColor  = new(0.60f, 0.08f, 0.06f);  // deep red

    // 3×5 pixel-font bitmaps for digits 0-9 (indices 0-9) and letters A-J (indices 10-19)
    // Each int[15] is row-major: row 0 = top, col 0 = left.
    static readonly int[][] s_fontPixels =
    {
        new[]{1,1,1, 1,0,1, 1,0,1, 1,0,1, 1,1,1}, // '0'
        new[]{0,1,0, 1,1,0, 0,1,0, 0,1,0, 1,1,1}, // '1'
        new[]{1,1,1, 0,0,1, 1,1,1, 1,0,0, 1,1,1}, // '2'
        new[]{1,1,1, 0,0,1, 1,1,1, 0,0,1, 1,1,1}, // '3'
        new[]{1,0,1, 1,0,1, 1,1,1, 0,0,1, 0,0,1}, // '4'
        new[]{1,1,1, 1,0,0, 1,1,1, 0,0,1, 1,1,1}, // '5'
        new[]{1,1,1, 1,0,0, 1,1,1, 1,0,1, 1,1,1}, // '6'
        new[]{1,1,1, 0,0,1, 0,1,0, 0,1,0, 0,1,0}, // '7'
        new[]{1,1,1, 1,0,1, 1,1,1, 1,0,1, 1,1,1}, // '8'
        new[]{1,1,1, 1,0,1, 1,1,1, 0,0,1, 1,1,1}, // '9'
        new[]{0,1,0, 1,0,1, 1,1,1, 1,0,1, 1,0,1}, // 'A'
        new[]{1,1,0, 1,0,1, 1,1,0, 1,0,1, 1,1,0}, // 'B'
        new[]{0,1,1, 1,0,0, 1,0,0, 1,0,0, 0,1,1}, // 'C'
        new[]{1,1,0, 1,0,1, 1,0,1, 1,0,1, 1,1,0}, // 'D'
        new[]{1,1,1, 1,0,0, 1,1,0, 1,0,0, 1,1,1}, // 'E'
        new[]{1,1,1, 1,0,0, 1,1,0, 1,0,0, 1,0,0}, // 'F'
        new[]{0,1,1, 1,0,0, 1,0,1, 1,0,1, 0,1,1}, // 'G'
        new[]{1,0,1, 1,0,1, 1,1,1, 1,0,1, 1,0,1}, // 'H'
        new[]{1,1,1, 0,1,0, 0,1,0, 0,1,0, 1,1,1}, // 'I'
        new[]{0,1,1, 0,0,1, 0,0,1, 1,0,1, 0,1,0}, // 'J'
    };
    static int FontIdx(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'A' and <= 'J' => ch - 'A' + 10,
        _ => -1
    };

    static byte      _uiOpen  = 1;
    static bool      _inConfig = true;

    static bool      _animating    = false;
    static int       _animFromIdx  = -1;
    static int       _animToIdx    = -1;
    static int[]     _animPath     = Array.Empty<int>();  // full hop path (including From and To)
    static float     _animT        = 0f;
    static float     _animTotalDur = 0.45f;   // total duration = ANIM_HOP_DUR * number_of_hops
    const  float     ANIM_HOP_DUR  = 0.35f;  // seconds per individual hop
    static PieceColor _animColor   = PieceColor.None;
    static PieceType  _animType    = PieceType.Man;
    // _animPromotedAtSeg: first segment index where the piece should be drawn as King.
    // -1 means it was already King at move start (crown shows throughout).
    static int        _animPromotedAtSeg = -1;
    static PieceType  _animOriginalType  = PieceType.Man;  // type before ApplyMove promotion

    // Captured-piece ghosts: one entry per capture in path order
    static int[]        _animCapIdx   = Array.Empty<int>();    // board cell index of captured piece
    static PieceColor[] _animCapColor = Array.Empty<PieceColor>();
    static PieceType[]  _animCapType  = Array.Empty<PieceType>();
    // _animCapSeg[i] = the path-segment index after which capture i should disappear
    static int[]        _animCapSeg   = Array.Empty<int>();

    static float     _aiDelayTimer  = 0f;
    const  float     AI_DELAY       = 0.5f;

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    static float     HalfBoard() => _game.Board.Size * 0.5f;

    static Vector3 CellCenter(int idx)
    {
        int sz   = _game.Board.Size;
        int row  = idx / sz, col = idx % sz;
        float half = HalfBoard();
        return new Vector3(col - half + 0.5f, 0f, row - half + 0.5f);
    }

    static Matrix4x4 VP => _view * _proj;

    static void StartMoveAnimation(int from, int to, System.Collections.Generic.List<int> path, PieceColor color, PieceType type)
    {
        _animFromIdx  = from;
        _animToIdx    = to;
        // Build hop path from the move's path list (includes From and To, plus intermediate landings)
        // If path is missing/degenerate, fall back to direct from→to
        _animPath     = (path != null && path.Count >= 2) ? path.ToArray() : new[]{ from, to };
        int hops      = _animPath.Length - 1;
        _animTotalDur = ANIM_HOP_DUR * hops;
        _animT        = 0f;
        _animating    = true;
        _animColor    = color;
        _animType     = type;
        // Detect mid-sequence promotion: piece was Man at start but is King at destination.
        // Find the first intermediate path stop on the promotion row — crown only shows from there.
        _animPromotedAtSeg = -1;
        if (type == PieceType.King && _animOriginalType == PieceType.Man && _animPath.Length > 2)
        {
            int promRow = (color == PieceColor.Light) ? 0 : _game.Board.Size - 1;
            for (int i = 1; i < _animPath.Length - 1; i++)
            {
                if (_animPath[i] / _game.Board.Size == promRow)
                {
                    _animPromotedAtSeg = i;  // crown visible from segment i onwards
                    break;
                }
            }
        }
        Console.WriteLine($"[ANIM] Start animation hops={hops} path=[{string.Join("->", System.Array.ConvertAll(_animPath, idx => _game.Board.CellLabel(idx)))}] color={color} type={type} promoSeg={_animPromotedAtSeg}");
    }

    // Called BEFORE ApplyMove so Board.Cells still has the captured pieces.
    static void SaveAnimCaptures(CheckersMove move)
    {
        _animOriginalType = _game.Board.Cells[move.From].Type;  // save before mid-sequence promotion
        var board   = _game.Board;
        int capCount = move.Captures.Count;
        _animCapIdx   = new int[capCount];
        _animCapColor = new PieceColor[capCount];
        _animCapType  = new PieceType[capCount];
        _animCapSeg   = new int[capCount];
        for (int i = 0; i < capCount; i++)
        {
            int ci = move.Captures[i];
            var p  = board.Cells[ci];
            _animCapIdx[i]   = ci;
            _animCapColor[i] = p.Color;
            _animCapType[i]  = p.Type;
            // Capture[i] happens during path segment i (from Path[i] → Path[i+1])
            _animCapSeg[i]   = i;
        }
    }

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
        S.passAction.colors[0].clear_value = new sg_color { r = 0.06f, g = 0.06f, b = 0.1f, a = 1f };

        UpdateCameraMatrices();
        BuildBoardMesh();
        BuildFrameMesh();
        BuildHighlightMesh();
        CreateBoardPipeline();
        CreatePiecePipeline();
        GeneratePieceMeshes();
        GenerateCrownMesh();
        BuildLabelMesh();

        // Register callback so we can snapshot captured-piece data before the board is modified
        _game.BeforeApplyMove = SaveAnimCaptures;
    }

    // -----------------------------------------------------------------------
    // Frame
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static unsafe void Frame()
    {
        float dt = (float)sapp_frame_duration();
        FileSystem.Instance.Update();

        if (!_inConfig)
        {
            // Only apply AI result when no animation is playing — prevents the AI move from
            // overwriting ghost-capture arrays or teleporting pieces mid-animation.
            // The AI can still calculate in the background while the human's animation plays.
            if (!_animating)
            {
                int prevCount = _game.MoveCount;
                _game.PollAIResult();
                if (_game.MoveCount != prevCount && _game.LastMove.HasValue)
                {
                    var m = _game.LastMove.Value;
                    var p = _game.Board.Cells[m.To];
                    if (!p.IsEmpty) StartMoveAnimation(m.From, m.To, m.Path, p.Color, p.Type);
                }
            }
            // AI delay: runs even during animation so the AI starts calculating in the background.
            if (_game.AITurnPending)
            {
                _aiDelayTimer += dt;
                if (_aiDelayTimer >= AI_DELAY) { _aiDelayTimer = 0f; _game.RequestAIMove(); }
            }
            else _aiDelayTimer = 0f;
        }

        if (_animating)
        {
            _animT += dt / _animTotalDur;
            if (_animT >= 1f) { _animT = 1f; _animating = false; }
        }

        if (_mouseClicked && !_inConfig)
        {
            _mouseClicked = false;
            if (_game.Phase == GamePhase.PlayerTurn && !_animating && !_game.AITurnPending)
            {
                int cell = PickCell(_mouseX, _mouseY);
                if (cell >= 0)
                {
                    bool moved = _game.SelectCell(cell);
                    if (moved && _game.LastMove.HasValue)
                    {
                        var m = _game.LastMove.Value;
                        var p = _game.Board.Cells[m.To];
                        if (!p.IsEmpty) StartMoveAnimation(m.From, m.To, m.Path, p.Color, p.Type);
                    }
                }
            }
        }

        simgui_new_frame(new simgui_frame_desc_t
        {
            width      = sapp_width(),
            height     = sapp_height(),
            delta_time = sapp_frame_duration()
        });

        DrawImGui();
        PrepareGameOverSdtx();

        sg_begin_pass(new sg_pass { action = S.passAction, swapchain = sglue_swapchain() });

        if (!_inConfig)
        {
            DrawBoard();
            DrawHighlights();
            if (S.pieceLoaded)
                DrawAllPieces();
            DrawBoardFrame();
            DrawBoardLabels();
        }

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
                _mouseX = e->mouse_x; _mouseY = e->mouse_y;
                break;
            case sapp_event_type.SAPP_EVENTTYPE_MOUSE_UP:
                if (e->mouse_button == sapp_mousebutton.SAPP_MOUSEBUTTON_LEFT)
                { _mouseX = e->mouse_x; _mouseY = e->mouse_y; _mouseClicked = true; }
                break;
            case sapp_event_type.SAPP_EVENTTYPE_TOUCHES_ENDED:
                if (e->num_touches > 0)
                { _mouseX = e->touches[0].pos_x; _mouseY = e->touches[0].pos_y; _mouseClicked = true; }
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
    // sokol_main
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
            window_title = "Checkers",
            icon = { sokol_default = true },
            logger = { func = &slog_func }
        };
    }

    // =======================================================================
    // Camera
    // =======================================================================
    static void UpdateCameraMatrices()
    {
        float w = MathF.Max(1f, sapp_widthf());
        float h = MathF.Max(1f, sapp_heightf());
        // Rotate 180° when human plays Dark so their pieces are always near the camera
        float camZ = _game.HumanIsLight ? 7f : -7f;
        _cameraPos = new Vector3(0f, 11f, camZ);
        _proj = Matrix4x4.CreatePerspectiveFieldOfView(
            50f * MathF.PI / 180f, w / h, 0.1f, 100f);
        _view = Matrix4x4.CreateLookAt(_cameraPos, Vector3.Zero, Vector3.UnitY);
    }

    // =======================================================================
    // Mouse picking (ray–plane at y=0)
    // =======================================================================
    static int PickCell(float mx, float my)
    {
        float w = sapp_widthf(), h = sapp_heightf();
        float ndcX = (mx / w) * 2f - 1f;
        float ndcY = 1f - (my / h) * 2f;

        if (!Matrix4x4.Invert(_proj, out var invP)) return -1;
        if (!Matrix4x4.Invert(_view, out var invV)) return -1;

        var rayEye   = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invP);
        rayEye.Z = -1f; rayEye.W = 0f;
        var rayWorld = Vector4.Transform(rayEye, invV);
        var rayDir   = Vector3.Normalize(new Vector3(rayWorld.X, rayWorld.Y, rayWorld.Z));

        if (MathF.Abs(rayDir.Y) < 1e-6f) return -1;
        float t = -_cameraPos.Y / rayDir.Y;
        if (t < 0f) return -1;
        var hit = _cameraPos + rayDir * t;

        int sz    = _game.Board.Size;
        float half = HalfBoard();
        int col   = (int)MathF.Floor(hit.X + half);
        int row   = (int)MathF.Floor(hit.Z + half);
        if (col < 0 || col >= sz || row < 0 || row >= sz) return -1;
        if (!_game.Board.IsDarkSquare(row, col)) return -1;
        int pickedIdx = row * sz + col;
        Console.WriteLine($"[PICK] mouse=({mx:F0},{my:F0}) hit=({hit.X:F2},{hit.Z:F2}) row={row} col={col} idx={pickedIdx} label={_game.Board.CellLabel(pickedIdx)}");
        return pickedIdx;
    }

    // =======================================================================
    // Board mesh
    // =======================================================================
    static void BuildBoardMesh()
    {
        if (S.boardDarkVBuf.id  != 0) { sg_destroy_buffer(S.boardDarkVBuf);  sg_destroy_buffer(S.boardDarkIBuf); }
        if (S.boardLightVBuf.id != 0) { sg_destroy_buffer(S.boardLightVBuf); sg_destroy_buffer(S.boardLightIBuf); }

        int sz   = _pendingRules.BoardSize;
        float half = sz * 0.5f;

        var darkV  = new List<float>(); var lightV = new List<float>();
        var darkI  = new List<ushort>(); var lightI = new List<ushort>();

        for (int row = 0; row < sz; row++)
        for (int col = 0; col < sz; col++)
        {
            float x0 = col - half, x1 = x0 + 1f;
            float z0 = row - half, z1 = z0 + 1f;
            bool dark = ((row + col) & 1) != 0;
            var vl = dark ? darkV : lightV;
            var il = dark ? darkI : lightI;
            ushort b = (ushort)(vl.Count / 6);
            foreach (var (px, pz) in new[]{ (x0,z0),(x1,z0),(x1,z1),(x0,z1) })
                vl.AddRange(new[]{ px, 0f, pz, 0f, 1f, 0f });
            il.AddRange(new ushort[]{ b,(ushort)(b+1),(ushort)(b+2),b,(ushort)(b+2),(ushort)(b+3) });
        }

        float[] dv = darkV.ToArray(), lv = lightV.ToArray();
        ushort[] di = darkI.ToArray(), li = lightI.ToArray();
        S.boardDarkVBuf   = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(dv), label = "bd-dark-v" });
        S.boardDarkIBuf   = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage{index_buffer=true}, data = SG_RANGE(di), label = "bd-dark-i" });
        S.boardDarkIdxCount = (uint)di.Length;
        S.boardLightVBuf  = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(lv), label = "bd-light-v" });
        S.boardLightIBuf  = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage{index_buffer=true}, data = SG_RANGE(li), label = "bd-light-i" });
        S.boardLightIdxCount = (uint)li.Length;
    }

    // =======================================================================
    // Frame border
    // =======================================================================
    static void BuildFrameMesh()
    {
        if (S.frameBuf.id != 0) { sg_destroy_buffer(S.frameBuf); sg_destroy_buffer(S.frameIBuf); }

        float half  = _pendingRules.BoardSize * 0.5f;
        float inner = half;
        float outer = half + 0.5f;
        float yTop  = 0.002f, yBot = -0.4f;

        var verts = new List<float>(); var idx = new List<ushort>();
        void AddQuad(float ax,float ay,float az, float bx,float by,float bz,
                     float cx,float cy,float cz, float dx,float dy,float dz,
                     float nx,float ny,float nz)
        {
            ushort base_ = (ushort)(verts.Count / 6);
            foreach (var (px,py,pz) in new[]{(ax,ay,az),(bx,by,bz),(cx,cy,cz),(dx,dy,dz)})
                verts.AddRange(new[]{px,py,pz,nx,ny,nz});
            idx.AddRange(new ushort[]{base_,(ushort)(base_+1),(ushort)(base_+2),base_,(ushort)(base_+2),(ushort)(base_+3)});
        }
        AddQuad(-outer,yTop,-outer,  outer,yTop,-outer,  outer,yTop,-inner, -outer,yTop,-inner, 0,1,0);
        AddQuad(-outer,yTop, inner,  outer,yTop, inner,  outer,yTop, outer, -outer,yTop, outer, 0,1,0);
        AddQuad(-outer,yTop,-inner, -inner,yTop,-inner, -inner,yTop, inner, -outer,yTop, inner, 0,1,0);
        AddQuad( inner,yTop,-inner,  outer,yTop,-inner,  outer,yTop, inner,  inner,yTop, inner, 0,1,0);
        AddQuad(-outer,yBot, outer,  outer,yBot, outer,  outer,yTop, outer, -outer,yTop, outer, 0,0,1);
        AddQuad( outer,yBot,-outer, -outer,yBot,-outer, -outer,yTop,-outer,  outer,yTop,-outer, 0,0,-1);
        AddQuad(-outer,yBot, inner, -outer,yBot,-outer, -outer,yTop,-outer, -outer,yTop, inner, -1,0,0);
        AddQuad( outer,yBot,-outer,  outer,yBot, inner,  outer,yTop, inner,  outer,yTop,-outer, 1,0,0);

        float[] va = verts.ToArray(); ushort[] ia = idx.ToArray();
        S.frameBuf      = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va), label = "frame-v" });
        S.frameIBuf     = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage{index_buffer=true}, data = SG_RANGE(ia), label = "frame-i" });
        S.frameIdxCount = (uint)ia.Length;
    }

    // =======================================================================
    // Highlight overlay quad
    // =======================================================================
    static void BuildHighlightMesh()
    {
        const float hs = 0.46f, yh = 0.004f;
        float[] hv = {
            -hs,yh,-hs, 0,1,0,
             hs,yh,-hs, 0,1,0,
             hs,yh, hs, 0,1,0,
            -hs,yh, hs, 0,1,0,
        };
        ushort[] hi = { 0,1,2, 0,2,3 };
        S.highlightVBuf = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(hv), label = "hl-v" });
        S.highlightIBuf = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage{index_buffer=true}, data = SG_RANGE(hi), label = "hl-i" });
    }

    // =======================================================================
    // Piece mesh
    // =======================================================================
    static void GeneratePieceMeshes()
    {
        const int N = 36; const float r = 0.42f, h = 0.11f;

        var topV  = new List<float>(); var sideV = new List<float>();
        var topI  = new List<ushort>(); var sideI = new List<ushort>();

        // Top cap (y=+h)
        ushort tc = (ushort)(topV.Count / 6);
        topV.AddRange(new float[]{ 0,h,0, 0,1,0 });
        for (int k = 0; k < N; k++)
        {
            float a = k * 2f * MathF.PI / N;
            topV.AddRange(new[]{ r*MathF.Cos(a),h,r*MathF.Sin(a),0f,1f,0f });
        }
        for (int k = 0; k < N; k++)
        {
            ushort a = (ushort)(tc+1+k), b = (ushort)(tc+1+(k+1)%N);
            topI.AddRange(new ushort[]{ tc,a,b });
        }
        // Bottom cap (y=-h)
        ushort bc2 = (ushort)(topV.Count / 6);
        topV.AddRange(new float[]{ 0,-h,0, 0,-1,0 });
        for (int k = 0; k < N; k++)
        {
            float a = k * 2f * MathF.PI / N;
            topV.AddRange(new[]{ r*MathF.Cos(a),-h,r*MathF.Sin(a),0f,-1f,0f });
        }
        for (int k = 0; k < N; k++)
        {
            ushort a = (ushort)(bc2+1+k), b2 = (ushort)(bc2+1+(k+1)%N);
            topI.AddRange(new ushort[]{ bc2,b2,a });
        }
        // Side ring
        int sb = sideV.Count / 6;
        for (int k = 0; k <= N; k++)
        {
            float a  = k * 2f * MathF.PI / N;
            float nx = MathF.Cos(a), nz = MathF.Sin(a);
            sideV.AddRange(new[]{ r*nx,-h,r*nz, nx,0f,nz });
            sideV.AddRange(new[]{ r*nx, h,r*nz, nx,0f,nz });
        }
        for (int k = 0; k < N; k++)
        {
            int b3 = sb + k*2;
            sideI.AddRange(new ushort[]{
                (ushort)b3,(ushort)(b3+1),(ushort)(b3+3),
                (ushort)b3,(ushort)(b3+3),(ushort)(b3+2)
            });
        }

        float[] tva = topV.ToArray();  ushort[] tia = topI.ToArray();
        float[] sva = sideV.ToArray(); ushort[] sia = sideI.ToArray();
        S.pieceTopVBuf    = sg_make_buffer(new sg_buffer_desc { data=SG_RANGE(tva), label="pt-v" });
        S.pieceTopIBuf    = sg_make_buffer(new sg_buffer_desc { usage=new sg_buffer_usage{index_buffer=true}, data=SG_RANGE(tia), label="pt-i" });
        S.pieceTopIdxCount = (uint)tia.Length;
        S.pieceSideVBuf   = sg_make_buffer(new sg_buffer_desc { data=SG_RANGE(sva), label="ps-v" });
        S.pieceSideIBuf   = sg_make_buffer(new sg_buffer_desc { usage=new sg_buffer_usage{index_buffer=true}, data=SG_RANGE(sia), label="ps-i" });
        S.pieceSideIdxCount = (uint)sia.Length;
        S.pieceLoaded = true;
    }

    // =======================================================================
    // Crown mesh (5 gold spikes on top of king pieces)
    // =======================================================================
    static void GenerateCrownMesh()
    {
        // 2-D crown silhouette lying flat on the top face of the piece (y = 0.115f).
        // Shape: a band (inner ring → outer ring) plus 3 rectangular teeth pointing outward.
        // All faces point UP (normal 0,1,0) so the top-down camera always sees them.

        const float Y    = 0.115f;  // just above piece top surface
        const float rIn  = 0.10f;   // inner ring radius of band
        const float rOut = 0.30f;   // outer ring radius of band
        const float tW   = 0.12f;   // half-width of each tooth at its base
        const float tH   = 0.08f;   // extra radial height of tooth beyond rOut
        const int   SEGS = 24;      // ring subdivisions
        const int   TEETH = 3;

        var verts = new List<float>();
        var idxs  = new List<ushort>();

        void AddTri(int a, int b, int c)
        {
            idxs.Add((ushort)a); idxs.Add((ushort)b); idxs.Add((ushort)c);
        }
        int AddVert(float x, float z)
        {
            int i = verts.Count / 6;
            verts.AddRange(new float[]{ x, Y, z, 0f, 1f, 0f });
            return i;
        }

        // ---- 1. Annular band (inner ring + outer ring)
        int innerStart = verts.Count / 6;
        for (int i = 0; i < SEGS; i++)
        {
            float a = i * 2f * MathF.PI / SEGS;
            AddVert(rIn  * MathF.Cos(a), rIn  * MathF.Sin(a));
        }
        int outerStart = verts.Count / 6;
        for (int i = 0; i < SEGS; i++)
        {
            float a = i * 2f * MathF.PI / SEGS;
            AddVert(rOut * MathF.Cos(a), rOut * MathF.Sin(a));
        }
        for (int i = 0; i < SEGS; i++)
        {
            int i0 = innerStart + i,           i1 = innerStart + (i + 1) % SEGS;
            int o0 = outerStart + i,           o1 = outerStart + (i + 1) % SEGS;
            AddTri(i0, o0, o1);
            AddTri(i0, o1, i1);
        }

        // ---- 2. Three rectangular teeth evenly spaced around the outer edge
        float toothStep = 2f * MathF.PI / TEETH;
        for (int t = 0; t < TEETH; t++)
        {
            float aMid  = t * toothStep;
            float aL    = aMid - MathF.Asin(tW / rOut);  // angle to left edge on outer ring
            float aR    = aMid + MathF.Asin(tW / rOut);  // angle to right edge on outer ring
            float tipR  = rOut + tH;

            // four corners of the tooth quad
            int tBL = AddVert(rOut * MathF.Cos(aL), rOut * MathF.Sin(aL));
            int tBR = AddVert(rOut * MathF.Cos(aR), rOut * MathF.Sin(aR));
            int tTR = AddVert(tipR * MathF.Cos(aR), tipR * MathF.Sin(aR));
            int tTL = AddVert(tipR * MathF.Cos(aL), tipR * MathF.Sin(aL));
            AddTri(tBL, tBR, tTR);
            AddTri(tBL, tTR, tTL);
        }

        float[] va = verts.ToArray(); ushort[] ia = idxs.ToArray();
        S.crownVBuf     = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(va), label = "crown-v" });
        S.crownIBuf     = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage{index_buffer=true}, data = SG_RANGE(ia), label = "crown-i" });
        S.crownIdxCount = (uint)ia.Length;
    }

    // =======================================================================
    // Board label mesh — 3×5 pixel-font glyphs on the wooden frame
    // =======================================================================
    static void BuildLabelMesh(int overrideSz = 0)
    {
        if (S.labelVBuf.id != 0) { sg_destroy_buffer(S.labelVBuf); sg_destroy_buffer(S.labelIBuf); S.labelIdxCount = 0; }

        int sz = overrideSz > 0 ? overrideSz : _pendingRules.BoardSize;
        if (sz <= 0) return;
        float half = sz * 0.5f;
        float sign  = _humanIsLight ? 1f : -1f;

        // Glyph axes: lie flat on XZ plane.
        // gRight = screen-right direction; gUp = toward screen top (= toward board centre)
        var gRight = new Vector3(sign,  0f,    0f);
        var gUp    = new Vector3(0f,    0f, -sign);

        const float PIX  = 0.038f;   // half pixel box size
        const float PIT  = 0.055f;   // pixel pitch (step between pixel centres)
        const float LY   = 0.005f;   // world Y just above frame surface
        const float CGAP = 0.22f;    // centre-to-centre gap between two-digit numerals

        var verts = new List<float>();
        var idxs  = new List<ushort>();

        void EmitPixelQuad(float wx, float wz)
        {
            ushort b = (ushort)(verts.Count / 6);
            var ctr = new Vector3(wx, LY, wz);
            var p0  = ctr - PIX * gRight - PIX * gUp;
            var p1  = ctr + PIX * gRight - PIX * gUp;
            var p2  = ctr + PIX * gRight + PIX * gUp;
            var p3  = ctr - PIX * gRight + PIX * gUp;
            foreach (var p in new[]{ p0, p1, p2, p3 })
                verts.AddRange(new float[]{ p.X, LY, p.Z, 0f, 1f, 0f });
            idxs.AddRange(new ushort[]{ b,(ushort)(b+1),(ushort)(b+2), b,(ushort)(b+2),(ushort)(b+3) });
        }

        void EmitGlyph(char ch, float cx, float cz)
        {
            int fi = FontIdx(ch);
            if (fi < 0) return;
            var pxData = s_fontPixels[fi];
            for (int r = 0; r < 5; r++)
            for (int c = 0; c < 3; c++)
            {
                if (pxData[r * 3 + c] == 0) continue;
                float hOff = (c - 1) * PIT;  // left-right offset along gRight
                float vOff = (2 - r) * PIT;  // up-down offset along gUp
                EmitPixelQuad(cx + hOff * gRight.X + vOff * gUp.X,
                              cz + hOff * gRight.Z + vOff * gUp.Z);
            }
        }

        // Column letters A-J (or A-H) on the near frame strip
        for (int col = 0; col < sz; col++)
            EmitGlyph((char)('A' + col), col - half + 0.5f, sign * (half + 0.25f));

        // Row numbers 1-N on the left frame strip (1 at the light-pieces end)
        for (int row = 0; row < sz; row++)
        {
            int  num  = sz - row;  // row=sz-1 (near light pieces) → 1, row=0 (far/dark side) → sz
            float cx  = -sign * (half + 0.25f);
            float cz  = row - half + 0.5f;
            if (num < 10)
            {
                EmitGlyph((char)('0' + num), cx, cz);
            }
            else  // "10": two digits side-by-side
            {
                EmitGlyph('1', cx - sign * CGAP * 0.5f, cz);
                EmitGlyph('0', cx + sign * CGAP * 0.5f, cz);
            }
        }

        if (idxs.Count == 0) return;
        float[]  lva = verts.ToArray();
        ushort[] lia = idxs.ToArray();
        S.labelVBuf     = sg_make_buffer(new sg_buffer_desc { data = SG_RANGE(lva), label = "lbl-v" });
        S.labelIBuf     = sg_make_buffer(new sg_buffer_desc { usage = new sg_buffer_usage { index_buffer = true }, data = SG_RANGE(lia), label = "lbl-i" });
        S.labelIdxCount = (uint)lia.Length;
    }

    // =======================================================================
    // Pipelines
    // =======================================================================
    static void CreateBoardPipeline()
    {
        var shd = sg_make_shader(board_shader_desc(sg_query_backend()));
        var pd  = default(sg_pipeline_desc);
        pd.layout.buffers[0].stride = 24;
        pd.layout.attrs[ATTR_board_position].format = SG_VERTEXFORMAT_FLOAT3;
        pd.layout.attrs[ATTR_board_normal].format   = SG_VERTEXFORMAT_FLOAT3;
        pd.shader    = shd;
        pd.index_type = SG_INDEXTYPE_UINT16;
        pd.cull_mode  = SG_CULLMODE_NONE;
        pd.depth.write_enabled = true;
        pd.depth.compare        = SG_COMPAREFUNC_LESS_EQUAL;
        pd.label = "board-pip";
        S.boardPip = sg_make_pipeline(pd);
    }

    static void CreatePiecePipeline()
    {
        var shd = sg_make_shader(piece_shader_desc(sg_query_backend()));
        var pd  = default(sg_pipeline_desc);
        pd.layout.buffers[0].stride = 24;
        pd.layout.attrs[ATTR_piece_position].format = SG_VERTEXFORMAT_FLOAT3;
        pd.layout.attrs[ATTR_piece_normal].format   = SG_VERTEXFORMAT_FLOAT3;
        pd.shader    = shd;
        pd.index_type = SG_INDEXTYPE_UINT16;
        pd.cull_mode  = SG_CULLMODE_NONE;
        pd.depth.write_enabled = true;
        pd.depth.compare        = SG_COMPAREFUNC_LESS_EQUAL;
        pd.label = "piece-pip";
        S.piecePip = sg_make_pipeline(pd);
    }

    // =======================================================================
    // Rendering
    // =======================================================================

    static void DrawBoard()
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp = identity * VP; vsP.model = identity;
        var fsP = default(board_fs_params_t);
        fsP.light_pos = _lightPos; fsP.light_color = new Vector3(1f,1f,0.95f); fsP.view_pos = _cameraPos;

        fsP.base_color = new Vector3(0.20f, 0.10f, 0.03f);
        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.boardDarkVBuf; bind.index_buffer = S.boardDarkIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.boardDarkIdxCount, 1);

        fsP.base_color = new Vector3(0.88f, 0.72f, 0.48f);
        bind.vertex_buffers[0] = S.boardLightVBuf; bind.index_buffer = S.boardLightIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.boardLightIdxCount, 1);
    }

    static void DrawBoardFrame()
    {
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp = identity * VP; vsP.model = identity;
        var fsP = default(board_fs_params_t);
        fsP.light_pos = _lightPos; fsP.light_color = new Vector3(1f,1f,0.95f);
        fsP.base_color = new Vector3(0.48f, 0.26f, 0.06f); fsP.view_pos = _cameraPos;
        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.frameBuf; bind.index_buffer = S.frameIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.frameIdxCount, 1);
    }

    static void DrawHighlights()
    {
        if (_game.Phase != GamePhase.PlayerTurn) return;
        sg_apply_pipeline(S.boardPip);
        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.highlightVBuf; bind.index_buffer = S.highlightIBuf;
        sg_apply_bindings(bind);
        var fsP = default(board_fs_params_t);
        fsP.light_pos = _lightPos; fsP.light_color = new Vector3(1f,1f,0.95f); fsP.view_pos = _cameraPos;

        // Last move (orange) — hide once the human picks up a piece
        if (_game.LastMove.HasValue && _game.SelectedPiece < 0)
        {
            fsP.base_color = new Vector3(0.9f, 0.4f, 0.1f);
            foreach (int i in new[]{ _game.LastMove.Value.From, _game.LastMove.Value.To })
                DrawHighlightCell(i, ref fsP);
        }

        // Movable pieces (green)
        var movable = _game.GetMovablePieces();
        fsP.base_color = new Vector3(0.2f, 0.8f, 0.2f);
        foreach (int i in movable)
        { if (i != _game.SelectedPiece) DrawHighlightCell(i, ref fsP); }

        // Selected piece (yellow)
        if (_game.SelectedPiece >= 0)
        {
            fsP.base_color = new Vector3(1f, 0.85f, 0.1f);
            DrawHighlightCell(_game.SelectedPiece, ref fsP);

            // Valid destinations (blue)
            fsP.base_color = new Vector3(0.2f, 0.4f, 0.95f);
            foreach (int dest in _game.GetValidDestinations())
                DrawHighlightCell(dest, ref fsP);
        }
    }

    static void DrawHighlightCell(int idx, ref board_fs_params_t fsP)
    {
        var c = CellCenter(idx);
        var t = Matrix4x4.CreateTranslation(c.X, 0f, c.Z);
        var vsP = default(board_vs_params_t);
        vsP.mvp = t * VP; vsP.model = t;
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, 6, 1);
    }

    static void DrawAllPieces()
    {
        sg_apply_pipeline(S.piecePip);
        const float pieceY = 0.11f;
        int sz = _game.Board.Size;

        // Determine current animation segment (for ghost-capture culling)
        int currentSeg = -1;
        if (_animating && _animPath.Length >= 2)
        {
            int segCount = _animPath.Length - 1;
            float totalT = _animT * segCount;
            currentSeg   = Math.Min((int)totalT, segCount - 1);
        }

        for (int idx = 0; idx < sz * sz; idx++)
        {
            var piece = _game.Board.Cells[idx];
            if (piece.IsEmpty) continue;
            if (_animating && (idx == _animFromIdx || idx == _animToIdx)) continue;
            DrawPieceAt(idx, piece, CellCenter(idx) + new Vector3(0, pieceY, 0));
        }

        // Ghost-render captured pieces that haven't been jumped over yet
        if (_animating && _animCapIdx.Length > 0)
        {
            for (int i = 0; i < _animCapIdx.Length; i++)
            {
                // The capture at path-segment _animCapSeg[i] — keep ghost until we've passed that segment
                if (currentSeg <= _animCapSeg[i])
                {
                    var ghostPiece = new Piece { Color = _animCapColor[i], Type = _animCapType[i] };
                    DrawPieceAt(-1, ghostPiece, CellCenter(_animCapIdx[i]) + new Vector3(0, pieceY, 0));
                }
            }
        }

        // Animated piece — follow multi-hop path one segment at a time
        if (_animating && _animPath.Length >= 2)
        {
            const float arcH = 1.0f;
            int   segCount   = _animPath.Length - 1;
            float totalT     = _animT * segCount;                          // maps 0..1 → 0..segCount
            int   seg        = Math.Min((int)totalT, segCount - 1);        // current segment index
            float segT       = totalT - seg;                               // 0..1 within this segment
            float smooth     = segT * segT * (3f - 2f * segT);            // smoothstep
            float arc        = MathF.Sin(segT * MathF.PI) * arcH;         // parabolic arc per hop
            var   fromPos    = CellCenter(_animPath[seg])     + new Vector3(0, pieceY, 0);
            var   toPos      = CellCenter(_animPath[seg + 1]) + new Vector3(0, pieceY, 0);
            var   pos        = Vector3.Lerp(fromPos, toPos, smooth) + new Vector3(0, arc, 0);
            // Use Man type until the animation passes the promotion square, then switch to King.
            var drawType = (_animPromotedAtSeg >= 0 && seg < _animPromotedAtSeg)
                ? PieceType.Man : _animType;
            DrawPieceAt(-1, new Piece{ Color=_animColor, Type=drawType }, pos);
        }
    }

    static void DrawPieceAt(int idx, Piece piece, Vector3 worldPos)
    {
        bool isSelected = idx >= 0 && idx == _game.SelectedPiece;
        Vector3 baseColor = (piece.Color == PieceColor.Light) ? _lightPieceColor : _darkPieceColor;

        var trans = Matrix4x4.CreateTranslation(worldPos);
        var vsP = default(piece_vs_params_t);
        vsP.mvp = trans * VP; vsP.model = trans;
        var fsP = default(piece_fs_params_t);
        fsP.light_pos   = _lightPos;
        fsP.light_color = new Vector3(1f, 1f, 0.95f);
        fsP.piece_color = baseColor;
        fsP.view_pos    = _cameraPos;
        fsP.king_factor = piece.Type == PieceType.King ? 1f : 0f;
        fsP.highlight   = isSelected ? 1f : 0f;

        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.pieceTopVBuf; bind.index_buffer = S.pieceTopIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_piece_vs_params, SG_RANGE<piece_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_piece_fs_params, SG_RANGE<piece_fs_params_t>(ref fsP));
        sg_draw(0, S.pieceTopIdxCount, 1);

        bind.vertex_buffers[0] = S.pieceSideVBuf; bind.index_buffer = S.pieceSideIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_piece_vs_params, SG_RANGE<piece_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_piece_fs_params, SG_RANGE<piece_fs_params_t>(ref fsP));
        sg_draw(0, S.pieceSideIdxCount, 1);

        // Crown on kings — bright red flat 2-D silhouette, contrasts both light and dark pieces
        if (piece.Type == PieceType.King && S.crownIdxCount > 0)
        {
            var fsCrown = default(piece_fs_params_t);
            fsCrown.light_pos   = _lightPos;
            fsCrown.light_color = new Vector3(1f, 1f, 0.95f);
            // Choose contrasting crown colour: gold on dark pieces, deep crimson on light pieces
            fsCrown.piece_color = (piece.Color == PieceColor.Dark)
                ? new Vector3(1.0f, 0.85f, 0.0f)   // gold on dark
                : new Vector3(0.85f, 0.05f, 0.05f); // crimson on light
            fsCrown.view_pos    = _cameraPos;
            fsCrown.king_factor = 0f;
            fsCrown.highlight   = 0f;
            bind.vertex_buffers[0] = S.crownVBuf; bind.index_buffer = S.crownIBuf;
            sg_apply_bindings(bind);
            sg_apply_uniforms(UB_piece_vs_params, SG_RANGE<piece_vs_params_t>(ref vsP));
            sg_apply_uniforms(UB_piece_fs_params, SG_RANGE<piece_fs_params_t>(ref fsCrown));
            sg_draw(0, S.crownIdxCount, 1);
        }
    }

    // =======================================================================
    // Board labels — drawn as 3D pixel-font mesh on the wooden frame
    // =======================================================================
    static void DrawBoardLabels()
    {
        if (S.labelIdxCount == 0) return;
        sg_apply_pipeline(S.boardPip);
        var identity = Matrix4x4.Identity;
        var vsP = default(board_vs_params_t);
        vsP.mvp = identity * VP; vsP.model = identity;
        var fsP = default(board_fs_params_t);
        fsP.light_pos   = _lightPos;
        fsP.light_color = new Vector3(1f, 1f, 0.95f);
        fsP.base_color  = new Vector3(0.95f, 0.85f, 0.45f);  // golden label colour
        fsP.view_pos    = _cameraPos;
        var bind = default(sg_bindings);
        bind.vertex_buffers[0] = S.labelVBuf;
        bind.index_buffer      = S.labelIBuf;
        sg_apply_bindings(bind);
        sg_apply_uniforms(UB_board_vs_params, SG_RANGE<board_vs_params_t>(ref vsP));
        sg_apply_uniforms(UB_board_fs_params, SG_RANGE<board_fs_params_t>(ref fsP));
        sg_draw(0, S.labelIdxCount, 1);
    }

    static Vector3 WorldToScreen(Vector3 world, int sw, int sh)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), _view * _proj);
        if (MathF.Abs(clip.W) < 1e-6f) return new Vector3(-1,-1,-1);
        float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W, ndcZ = clip.Z / clip.W;
        float sx = (ndcX * 0.5f + 0.5f) * sw;
        float sy = (1f - (ndcY * 0.5f + 0.5f)) * sh;
        return new Vector3(sx, sy, ndcZ);
    }

    // =======================================================================
    // Game-over overlay
    // Dark banner → drawn by DrawGameUI via ForegroundDrawList.
    // Large text  → drawn here via sokol_debugtext with auto-scaled canvas.
    // =======================================================================
    static void PrepareGameOverSdtx()
    {
        if (_game.Phase != GamePhase.GameOver) return;

        float w = sapp_widthf(), h = sapp_heightf();

        string line1 = _game.Winner == PieceColor.None ? "D R A W"
            : (_game.Winner == _game.HumanColor() ? "YOU WIN!" : "AI WINS!");
        int lightPieces = _game.Board.CountPieces(PieceColor.Light);
        int darkPieces  = _game.Board.CountPieces(PieceColor.Dark);
        string humanColor = _game.HumanColor() == PieceColor.Light ? "Light" : "Dark";
        int humanPieces   = _game.HumanColor() == PieceColor.Light ? lightPieces : darkPieces;
        int aiPieces      = _game.HumanColor() == PieceColor.Light ? darkPieces  : lightPieces;
        string line2 = $"You : {humanPieces}   AI: {aiPieces}";

        // Scale so each character is ~1/8th of the screen height, capped for large displays
        float scale1 = MathF.Max(2f, MathF.Min(w / 100f, h / 70f));
        float cols1  = (w / scale1) / 8f;
        float rows1  = (h / scale1) / 8f;

        sdtx_font(0);
        sdtx_canvas(w / scale1, h / scale1);
        // Colour: gold for win, red for loss, grey for draw
        if (_game.Winner == PieceColor.None)
            sdtx_color3b(200, 200, 200);
        else if (_game.Winner == _game.HumanColor())
            sdtx_color3b(255, 215, 30);
        else
            sdtx_color3b(255, 80, 60);
        sdtx_origin(MathF.Max(0f, (cols1 - line1.Length) * 0.5f), rows1 * 0.5f - 1.1f);
        sdtx_puts(line1);

        // Score line at 60% size
        float scale2 = MathF.Max(1.5f, scale1 * 0.6f);
        float cols2  = (w / scale2) / 8f;
        float rows2  = (h / scale2) / 8f;
        sdtx_canvas(w / scale2, h / scale2);
        sdtx_color3b(210, 210, 210);
        sdtx_origin(MathF.Max(0f, (cols2 - line2.Length) * 0.5f), rows2 * 0.5f + 1.3f);
        sdtx_puts(line2);
    }

    // (no longer used — overlay is handled by PrepareGameOverSdtx + DrawGameUI banner)
    static void DrawGameOverOverlay() { }

    // =======================================================================
    // ImGui
    // =======================================================================
    static void DrawImGui()
    {
        igSetNextWindowPos(new Vector2(5, 5), ImGuiCond.Once, Vector2.Zero);
        igSetNextWindowSize(new Vector2(255, 0), ImGuiCond.Always);
        igBegin("Checkers", ref _uiOpen, ImGuiWindowFlags.NoResize);
        if (_inConfig) DrawConfigUI(); else DrawGameUI();

        // Draw offer modal — must be called inside the same Begin/End block that calls OpenPopup
        if (_game.Phase == GamePhase.DrawOffer)
            igOpenPopup_Str("Draw Offered", ImGuiPopupFlags.None);

        byte popupOpen = 1;
        if (igBeginPopupModal("Draw Offered", ref popupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            igText("The game has reached a draw condition:");
            igSpacing();
            igText(_game.DrawOfferReason);
            igSeparator();
            igText("Do you accept the draw?");
            igSpacing();
            if (igButton("Accept Draw", new Vector2(110, 0)))
            {
                _game.AcceptDraw();
                igCloseCurrentPopup();
            }
            igSameLine(0, 8);
            if (igButton("Play On", new Vector2(110, 0)))
            {
                _game.DeclineDraw();
                igCloseCurrentPopup();
            }
            igEndPopup();
        }

        igEnd();
    }

    static void DrawConfigUI()
    {
        igText("Game Configuration");
        igSeparator();

        igText("Board Size:");
        if (igRadioButton_Bool("8 x 8",   _pendingRules.BoardSize == 8))  _pendingRules.BoardSize = 8;
        igSameLine(0,-1);
        if (igRadioButton_Bool("10 x 10", _pendingRules.BoardSize == 10)) _pendingRules.BoardSize = 10;

        igSeparator();
        igText("Kings:");
        if (igRadioButton_Bool("Flying Kings",    _pendingRules.Kings == KingBehavior.FlyingKings))   _pendingRules.Kings = KingBehavior.FlyingKings;
        if (igRadioButton_Bool("No Flying Kings", _pendingRules.Kings == KingBehavior.NoFlyingKings)) _pendingRules.Kings = KingBehavior.NoFlyingKings;

        igSeparator();
        igText("Capture:");
        if (igRadioButton_Bool("Mandatory - Max Men", _pendingRules.Capture == CaptureBehavior.MandatoryMaxMen)) _pendingRules.Capture = CaptureBehavior.MandatoryMaxMen;
        if (igRadioButton_Bool("Mandatory - Any",     _pendingRules.Capture == CaptureBehavior.MandatoryAny))    _pendingRules.Capture = CaptureBehavior.MandatoryAny;
        if (igRadioButton_Bool("Optional",            _pendingRules.Capture == CaptureBehavior.Optional))        _pendingRules.Capture = CaptureBehavior.Optional;

        igSeparator();
        igText("Men Capture Backwards:");
        if (igRadioButton_Bool("Allowed##bw",   _pendingRules.Backward == BackwardCapture.Allowed))   _pendingRules.Backward = BackwardCapture.Allowed;
        if (igRadioButton_Bool("Forbidden##bw", _pendingRules.Backward == BackwardCapture.Forbidden)) _pendingRules.Backward = BackwardCapture.Forbidden;

        igSeparator();
        igText("Promotion During Capture:");
        if (igRadioButton_Bool("Stop turn (English)",    _pendingRules.Promotion == PromotionCapture.Stop))     _pendingRules.Promotion = PromotionCapture.Stop;
        if (igRadioButton_Bool("Continue as King",       _pendingRules.Promotion == PromotionCapture.Continue)) _pendingRules.Promotion = PromotionCapture.Continue;

        igSeparator();
        igText("Piece Colors:");
        igColorEdit3("Light##pc", ref _lightPieceColor, ImGuiColorEditFlags.None);
        igColorEdit3("Dark##pc",  ref _darkPieceColor,  ImGuiColorEditFlags.None);

        igSeparator();
        igText("Play As:");
        if (igRadioButton_Bool("Light (first)",  _humanIsLight)) _humanIsLight = true;
        if (igRadioButton_Bool("Dark",          !_humanIsLight)) _humanIsLight = false;

        igSeparator();
        if (igButton("Start Game", new Vector2(-1, 0)))
        {
            _game.Rules        = _pendingRules;
            _game.HumanIsLight = _humanIsLight;
            BuildBoardMesh();
            BuildFrameMesh();
            BuildLabelMesh();
            UpdateCameraMatrices();
            _game.StartNewGame();
            _inConfig = false;
        }
    }

    static void DrawGameUI()
    {
        string humanName = _game.HumanColor() == PieceColor.Light ? "Light" : "Dark";
        string aiName    = _game.HumanColor() == PieceColor.Light ? "Dark"  : "Light";
        int humanWins    = _game.HumanColor() == PieceColor.Light ? _game.LightWins : _game.DarkWins;
        int aiWins       = _game.HumanColor() == PieceColor.Light ? _game.DarkWins  : _game.LightWins;

        igText($"{humanName} (You):  {humanWins} wins");
        igText($"{aiName} (AI):   {aiWins} wins");
        igSeparator();

        string phase = _game.Phase switch
        {
            GamePhase.PlayerTurn => _game.AITurnPending ? "AI preparing move..." : "Your turn — click a piece",
            GamePhase.AIThinking => "AI is thinking...",
            GamePhase.DrawOffer  => "Draw offered — see dialog",
            GamePhase.GameOver   => (_game.Winner == PieceColor.None ? "Draw!" :
                                     _game.Winner == _game.HumanColor() ? "You Win!" : "AI Wins!"),
            _ => ""
        };
        igText(phase);
        igSeparator();

        igText($"Board: {_game.Rules.BoardSize}x{_game.Rules.BoardSize}");
        igText($"Kings: {(_game.Rules.Kings == KingBehavior.FlyingKings ? "Flying" : "Normal")}");
        string capStr = _game.Rules.Capture switch
        {
            CaptureBehavior.MandatoryMaxMen => "Mandatory (max)",
            CaptureBehavior.MandatoryAny    => "Mandatory (any)",
            _                               => "Optional"
        };
        igText($"Capture: {capStr}");
        igText($"Backward: {(_game.Rules.Backward == BackwardCapture.Allowed ? "Yes" : "No")}");
        igSeparator();

        int depth = _game.AiDepth;
        igSliderInt("AI depth", ref depth, 1, 10, "%d", 0);
        _game.AiDepth = depth;

        if (igButton("New Game", new Vector2(-1, 0)))
        {
            UpdateCameraMatrices();
            BuildLabelMesh(_game.Board.Size);
            _game.StartNewGame();
        }

        byte playAsDark = _humanIsLight ? (byte)0 : (byte)1;
        if (igCheckbox("Play as Dark (AI first)", ref playAsDark))
        {
            _humanIsLight      = (playAsDark == 0);
            _game.HumanIsLight = _humanIsLight;
            UpdateCameraMatrices();
            BuildLabelMesh(_game.Board.Size);
            _game.StartNewGame();
        }

        if (igButton("Configure...", new Vector2(-1, 0)))
            _inConfig = true;
        if (_game.Phase == GamePhase.PlayerTurn)
            if (igButton("Undo", new Vector2(-1, 0)))
                _game.Undo();

        igSeparator();
        igText($"Light pieces: {_game.Board.CountPieces(PieceColor.Light)}");
        igText($"Dark pieces:  {_game.Board.CountPieces(PieceColor.Dark)}");

        if (igCollapsingHeader_TreeNodeFlags("Piece Colors", ImGuiTreeNodeFlags.None))
        {
            igColorEdit3("Light##pc", ref _lightPieceColor, ImGuiColorEditFlags.None);
            igColorEdit3("Dark##pc",  ref _darkPieceColor,  ImGuiColorEditFlags.None);
        }

        if (_game.Phase == GamePhase.GameOver)
        {
            float sw = sapp_widthf(), sh = sapp_heightf();
            float bH = sh * 0.28f, bY = (sh - bH) * 0.5f;
            var dl = igGetForegroundDrawList_ViewportPtr(igGetMainViewport());
            ImDrawList_AddRectFilled(dl, new Vector2(0f,bY), new Vector2(sw, bY+bH), 0xCC000000u, 12f, 0);
        }
    }
}
