// BlockFall-app.cs — Tetris clone (BlockFall) using sokol_gp 2D rendering.
// UI panel via ImGui.  Overlay text via sokol_debugtext.
// Touch virtual d-pad for mobile / web.

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Sokol;
using Imgui;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SGP;
using static Sokol.SGP.sgp_blend_mode;
using static Sokol.Utils;
using static Sokol.SImgui;
using static Sokol.SDebugText;
using static Sokol.SLog;
using static Imgui.ImguiNative;
using static Imgui.ImGuiHelpers;

public static unsafe class BlockfallApp
{
    // -----------------------------------------------------------------------
    // Colors (matching the C++ Raylib reference palette)
    // -----------------------------------------------------------------------
    static readonly sg_color CLR_BG        = C(44,  44, 127);   // dark blue background
    static readonly sg_color CLR_PANEL_BG  = C(59,  85, 162);   // right panel background

    // Piece colors indexed by Block ID 0-7 (0 = empty / dark grey)
    static readonly sg_color[] CELL_COLORS = {
        C( 26,  31,  40),  // 0 — empty
        C( 47, 230,  23),  // 1 — L  green
        C(232,  18,  18),  // 2 — J  red
        C(226, 116,  17),  // 3 — I  orange
        C(237, 234,   4),  // 4 — O  yellow
        C(166,   0, 247),  // 5 — S  purple
        C( 21, 204, 209),  // 6 — T  cyan
        C( 13,  64, 216),  // 7 — Z  blue
    };

    static sg_color C(int r, int g, int b, float a = 1f) =>
        new() { r = r / 255f, g = g / 255f, b = b / 255f, a = a };

    // -----------------------------------------------------------------------
    // Layout (recomputed on resize)
    // -----------------------------------------------------------------------
    static float CELL  = 30f;   // cell size in pixels
    static float BRD_X = 11f;   // board top-left x
    static float BRD_Y = 11f;   // board top-left y
    static float PNL_X = 320f;  // right panel x
    static float PNL_W = 170f;  // right panel width

    // Panel section layout (set by ComputeLayout)
    static float PNL_Y1, PNL_H1;   // score box
    static float PNL_Y2, PNL_H2;   // level + lines box
    static float PNL_Y3, PNL_H3;   // next-piece box

    // -----------------------------------------------------------------------
    // sokol pass action
    // -----------------------------------------------------------------------
    static sg_pass_action _passAction;
    static int _lastSw, _lastSh;

    // -----------------------------------------------------------------------
    // Game
    // -----------------------------------------------------------------------
    static readonly TetrisGame _game = new();

    // -----------------------------------------------------------------------
    // Input — keyboard key-repeat
    // -----------------------------------------------------------------------
    enum GameKey { Left, Right, Down, Rotate, HardDrop, Count }
    static readonly float[] _keyHoldTimer = new float[(int)GameKey.Count];
    static readonly bool[]  _keyHeld      = new bool [(int)GameKey.Count];
    const float KEY_INITIAL_DELAY = 0.18f;
    const float KEY_REPEAT_RATE   = 0.05f;

    // -----------------------------------------------------------------------
    // Touch virtual buttons
    static float BTN_LEFT_X, BTN_LEFT_W;
    static float BTN_RIGHT_X, BTN_RIGHT_W;
    static float BTN_AREA_Y,  BTN_AREA_H;
    static bool  BTN_SHOW;
    static bool  _portrait;   // true when sh > sw (portrait orientation)

    // -----------------------------------------------------------------------
    // Init
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger = { func = &slog_func }
        });

        sgp_setup(new sgp_desc());

        simgui_setup(new simgui_desc_t { logger = { func = &slog_func } });

        _passAction = default;
        _passAction.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        _passAction.colors[0].clear_value = CLR_BG;

        ComputeLayout(sapp_width(), sapp_height());
        _game.Reset();
    }

    // -----------------------------------------------------------------------
    // Frame
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static void Frame()
    {
        float dt = (float)sapp_frame_duration();
        int sw = sapp_width();
        int sh = sapp_height();

        // Recompute layout whenever the window size actually changes
        // (also catches silent Web canvas resizes that don't fire an event)
        if (sw != _lastSw || sh != _lastSh)
        {
            _lastSw = sw; _lastSh = sh;
            ComputeLayout(sw, sh);
        }

        // --- Game update -------------------------------------------------
        if (!_game.GameOver && !_game.Paused)
        {
            ProcessKeyRepeat(dt);
            _game.Update(dt);
        }

        // --- SGP 2D rendering -------------------------------------------
        sgp_begin(sw, sh);
        sgp_viewport(0, 0, sw, sh);
        sgp_project(0f, sw, 0f, sh);

        DrawBackground(sw, sh);
        DrawBoardCells();
        DrawLockedCells();
        DrawGhostPiece();
        DrawActivePiece();
        DrawPanelBoxes();
        DrawNextPiecePreview();
        DrawGameOverBanner();

        // Flush SGP batch inside the render pass
        sg_begin_pass(new sg_pass { action = _passAction, swapchain = sglue_swapchain() });
        sgp_flush();
        sgp_end();

        // --- ImGui score panel ------------------------------------------
        DrawImGui(dt);

        sg_end_pass();
        sg_commit();
    }

    // -----------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------

    static void DrawBackground(int sw, int sh)
    {
        sgp_set_color(CLR_BG.r, CLR_BG.g, CLR_BG.b, 1f);
        sgp_draw_filled_rect(0, 0, sw, sh);
    }

    static void DrawBoardCells()
    {
        float pad = 2f;
        float brdW = TetrisGame.Cols * CELL;
        float brdH = TetrisGame.Rows * CELL;

        // Board border
        sgp_set_color(CLR_PANEL_BG.r, CLR_PANEL_BG.g, CLR_PANEL_BG.b, 1f);
        sgp_draw_filled_rect(BRD_X - pad, BRD_Y - pad, brdW + pad * 2f, brdH + pad * 2f);

        // Empty cells
        var ec = CELL_COLORS[0];
        for (int r = 0; r < TetrisGame.Rows; r++)
            for (int c = 0; c < TetrisGame.Cols; c++)
                DrawCell(r, c, ec);
    }

    static void DrawLockedCells()
    {
        for (int r = 0; r < TetrisGame.Rows; r++)
            for (int c = 0; c < TetrisGame.Cols; c++)
            {
                int id = _game.Grid[r, c];
                if (id != 0)
                    DrawCell(r, c, CELL_COLORS[id]);
            }
    }

    static void DrawGhostPiece()
    {
        if (_game.GameOver) return;
        var ghost = _game.Current;
        ghost.Row = _game.GhostRow();

        var col = CELL_COLORS[_game.Current.Id];
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(col.r, col.g, col.b, 0.25f);
        foreach (var (r, c) in ghost.GetCells())
            DrawCellRaw(r, c);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
    }

    static void DrawActivePiece()
    {
        if (_game.GameOver) return;
        var col = CELL_COLORS[_game.Current.Id];
        sgp_set_color(col.r, col.g, col.b, 1f);
        foreach (var (r, c) in _game.Current.GetCells())
            DrawCellRaw(r, c);
    }

    // Panel background boxes (score, level/lines, next)
    static void DrawPanelBoxes()
    {
        sgp_set_color(CLR_PANEL_BG.r, CLR_PANEL_BG.g, CLR_PANEL_BG.b, 1f);
        sgp_draw_filled_rect(PNL_X, PNL_Y1, PNL_W, PNL_H1);   // score
        sgp_draw_filled_rect(PNL_X, PNL_Y2, PNL_W, PNL_H2);   // level + lines
        sgp_draw_filled_rect(PNL_X, PNL_Y3, PNL_W, PNL_H3);   // next
    }

    static void DrawNextPiecePreview()
    {
        const float LABEL_H  = 26f;   // vertical room for "NEXT" label
        float available      = PNL_H3 - LABEL_H;
        float previewCell    = MathF.Min(CELL * 0.75f, available / 4.5f);
        float offsetX        = PNL_X  + (PNL_W      - 4f * previewCell) * 0.5f;
        float offsetY        = PNL_Y3 + LABEL_H + (available - 4f * previewCell) * 0.5f;

        // GetCells() returns absolute grid positions — normalize to bounding-box origin
        var cells = _game.Next.GetCells();
        int minR = cells[0].r, minC = cells[0].c;
        foreach (var (r, c) in cells) { if (r < minR) minR = r; if (c < minC) minC = c; }

        var col = CELL_COLORS[_game.Next.Id];
        sgp_set_color(col.r, col.g, col.b, 1f);
        foreach (var (r, c) in cells)
            sgp_draw_filled_rect(
                offsetX + (c - minC) * previewCell,
                offsetY + (r - minR) * previewCell,
                previewCell - 1f,
                previewCell - 1f);
    }

    // -----------------------------------------------------------------------
    // Game-over dark band (drawn over the board via SGP)
    // -----------------------------------------------------------------------
    static void DrawGameOverBanner()
    {
        if (!_game.GameOver) return;
        float brdH  = TetrisGame.Rows * CELL;
        float bandH = brdH * 0.42f;
        float bandY = BRD_Y + (brdH - bandH) * 0.5f;
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(0f, 0f, 0f, 0.70f);
        sgp_draw_filled_rect(BRD_X - 2f, bandY, TetrisGame.Cols * CELL + 4f, bandH);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    // -----------------------------------------------------------------------
    // ImGui score panel
    // -----------------------------------------------------------------------
    static void DrawImGui(float dt)
    {
        int sw = sapp_width();
        int sh = sapp_height();
        simgui_new_frame(new simgui_frame_desc_t { width = sw, height = sh, delta_time = dt });

        // Single overlay window covering the right panel — no background, no padding
        igSetNextWindowPos(new Vector2(PNL_X, PNL_Y1), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(PNL_W, (float)sh - PNL_Y1), ImGuiCond.Always);
        igPushStyleVar_Vec2(ImGuiStyleVar.WindowPadding, new Vector2(0f, 0f));

        byte open = 1;
        igBegin("##bf", ref open,
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        igPopStyleVar(1);

        float lineH = igGetTextLineHeight();

        // --- Score section — centered vertically in PNL_Y1..PNL_Y1+PNL_H1
        // Window Y is relative to PNL_Y1 (window top)
        {
            float elemH  = lineH * 2f + 4f;
            float startY = MathF.Max(2f, (PNL_H1 - elemH) * 0.5f);
            igSetCursorPosY(startY);
            CentredLabel("SCORE");
            igSetCursorPosY(startY + lineH + 4f);
            CentredLabel(_game.Score.ToString());
        }

        // --- Level + Lines section — window-relative Y derived from layout vars
        {
            float secY   = PNL_Y2 - PNL_Y1;
            float elemH  = lineH * 4f + 9f;
            float startY = secY + MathF.Max(2f, (PNL_H2 - elemH) * 0.5f);
            igSetCursorPosY(startY);             CentredLabel("LEVEL");
            igSetCursorPosY(startY + lineH + 3f); CentredLabel(_game.Level.ToString());
            igSetCursorPosY(startY + lineH * 2f + 6f); CentredLabel("LINES");
            igSetCursorPosY(startY + lineH * 3f + 9f); CentredLabel(_game.TotalLines.ToString());
        }

        // --- NEXT label at top of next-piece box
        {
            float secY = PNL_Y3 - PNL_Y1;
            igSetCursorPosY(secY + 8f);
            CentredLabel("NEXT");
            // Move cursor past the preview box so ImGui's clip rect stays valid
            igSetCursorPosY(secY + PNL_H3 + 2f);
            igDummy(new Vector2(0f, 0f));
        }

        // --- Controls hint — pinned just above touch-button area (or bottom of window)
        {
            // Anchor hints to BTN_AREA_Y so they sit just above the buttons regardless
            // of how tall the NEXT preview box is.
            float hintRowH  = lineH + 2f;
            float hintBlock = hintRowH * 3f;
            float hintsY    = MathF.Max(
                PNL_Y3 - PNL_Y1 + PNL_H3 * 0.72f,                      // inside NEXT box
                BTN_AREA_Y - PNL_Y1 - hintBlock - (_game.GameOver ? hintRowH + 20f : 0f) - 8f);

            igSetCursorPosY(hintsY);                      igSetCursorPosX(4f); igTextDisabled("Arrows / WASD");
            igSetCursorPosY(hintsY + hintRowH);           igSetCursorPosX(4f); igTextDisabled("Space = hard drop");
            igSetCursorPosY(hintsY + hintRowH * 2f);      igSetCursorPosX(4f); igTextDisabled("P / Esc = pause");

            if (_game.GameOver)
            {
                igSetCursorPosY(hintsY + hintRowH * 3f + 8f);
                igSetCursorPosX(10f);
                if (igButton("New Game", new Vector2(PNL_W - 20f, 0)))
                    _game.Reset();
            }
        }

        igEnd();

        // --- Game-over / paused overlay — second ImGui window over the board
        if (_game.GameOver || _game.Paused)
        {
            float boardW  = TetrisGame.Cols * CELL;
            float boardH  = TetrisGame.Rows * CELL;
            float boardCx = BRD_X + boardW * 0.5f;
            float boardCy = BRD_Y + boardH * 0.5f;

            // Push default font at a big size so text is clearly visible
            const float BIG_FONT = 28.0f;
            const float SM_FONT  = 18.0f;
            float bigLineH = BIG_FONT * 1.2f;
            float smLineH  = SM_FONT  * 1.2f;
            float spacing  = 6f;

            bool isGameOver = _game.GameOver;
            const float BTN_ROW_H = 40f;   // height of the NEW GAME button row
            // game-over: GAME OVER + Score + NEW GAME button
            // paused:    PAUSED + hint
            float ovrH = isGameOver
                ? bigLineH + smLineH + spacing * 2f + BTN_ROW_H + 28f
                : bigLineH + smLineH + spacing + 20f;
            float ovrW = boardW * 0.9f;

            igSetNextWindowPos(new Vector2(boardCx - ovrW * 0.5f, boardCy - ovrH * 0.5f), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(ovrW, ovrH), ImGuiCond.Always);
            igSetNextWindowBgAlpha(0f);
            igPushStyleVar_Vec2(ImGuiStyleVar.WindowPadding, new Vector2(0f, 10f));

            byte open2 = 1;
            igBegin("##ovl", ref open2,
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings);
            igPopStyleVar(1);

            float curY = 10f;
            if (isGameOver)
            {
                igSetCursorPosY(curY);
                igPushFont(null, BIG_FONT);
                OvrCentredColored("GAME OVER", new Vector4(0.95f, 0.12f, 0.12f, 1f), ovrW);
                igPopFont();
                curY += bigLineH + spacing;

                igSetCursorPosY(curY);
                igPushFont(null, SM_FONT);
                OvrCentredColored($"Score: {_game.Score}", new Vector4(1f, 1f, 1f, 1f), ovrW);
                igPopFont();
                curY += smLineH + spacing;

                // Big tappable NEW GAME button — works on touch, mouse and keyboard
                igSetCursorPosY(curY);
                float newGameW = ovrW * 0.72f;
                igSetCursorPosX((ovrW - newGameW) * 0.5f);
                igPushFont(null, SM_FONT);
                igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.18f, 0.62f, 0.22f, 0.92f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonHovered, new Vector4(0.28f, 0.78f, 0.32f, 1.00f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonActive,  new Vector4(0.48f, 0.95f, 0.52f, 1.00f));
                if (igButton("NEW GAME##ovlbtn", new Vector2(newGameW, BTN_ROW_H)))
                    _game.Reset();
                igPopStyleColor(3);
                igPopFont();
            }
            else
            {
                igSetCursorPosY(curY);
                igPushFont(null, BIG_FONT);
                OvrCentredColored("PAUSED", new Vector4(0.95f, 0.93f, 0.08f, 1f), ovrW);
                igPopFont();
                curY += bigLineH + spacing;

                igSetCursorPosY(curY);
                igPushFont(null, SM_FONT);
                OvrCentredColored("P to resume", new Vector4(0.70f, 0.70f, 0.70f, 1f), ovrW);
                igPopFont();
            }

            igEnd();
        }

        DrawTouchButtonsImGui();
        simgui_render();
    }

    // -----------------------------------------------------------------------
    // Touch d-pad — pure ImGui, left and right of the board
    //
    //  LEFT panel  (left thumb):
    //    top  40% → [ROT]          (stretch up — frequent, reachable)
    //    bot  60% → [  <  ]        (natural thumb rest = bottom-left)
    //
    //  RIGHT panel (right thumb):
    //    top  40% → [  v  ][DROP]  (deliberate stretch — less frequent)
    //    bot  60% → [  >  ]        (natural thumb rest = bottom-right)
    //
    // ButtonRepeat flag handles hold-to-repeat natively.
    // -----------------------------------------------------------------------
    static void DrawTouchButtonsImGui()
    {
        if (!BTN_SHOW) return;

        igPushItemFlag(ImGuiItemFlags.ButtonRepeat, true);
        igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.23f, 0.33f, 0.63f, 0.85f));
        igPushStyleColor_Vec4(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.45f, 0.75f, 0.95f));
        igPushStyleColor_Vec4(ImGuiCol.ButtonActive,  new Vector4(0.55f, 0.65f, 0.95f, 1.00f));
        igPushStyleVar_Float(ImGuiStyleVar.FrameRounding, 16f);
        igPushStyleVar_Vec2(ImGuiStyleVar.WindowPadding, new Vector2(5f, 5f));
        igPushStyleVar_Vec2(ImGuiStyleVar.ItemSpacing,   new Vector2(5f, 5f));

        bool canAct = !_game.GameOver && !_game.Paused;

        var winFlags =
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoSavedSettings;

        const float pad = 5f;

        if (_portrait)
        {
            // ── Portrait: one full-width row of 4 buttons at the bottom ──
            //    [ ROT ]  [  <  ]  [  >  ]  [  v  ]
            // Left thumb naturally covers ROT+<, right thumb covers >+v.
            byte oP = 1;
            igSetNextWindowPos(new Vector2(BTN_LEFT_X, BTN_AREA_Y), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(BTN_LEFT_W, BTN_AREA_H), ImGuiCond.Always);
            igSetNextWindowBgAlpha(0f);
            igBegin("##tbP", ref oP, winFlags);
            {
                float btnH = BTN_AREA_H - 10f;
                float bw   = (BTN_LEFT_W - 14f - pad * 3f) * 0.25f;   // 4 equal buttons
                igPushFont(null, MathF.Min(28f, btnH * 0.45f));
                if (igButton("ROT##rot", new Vector2(bw, btnH)) && canAct) _game.Rotate();
                igSameLine(0f, pad);
                igPushFont(null, MathF.Min(48f, btnH * 0.70f));
                if (igButton(" < ##ml",  new Vector2(bw, btnH)) && canAct) _game.MoveLeft();
                igSameLine(0f, pad);
                if (igButton(" > ##mr",  new Vector2(bw, btnH)) && canAct) _game.MoveRight();
                igPopFont();
                igSameLine(0f, pad);
                igPushFont(null, MathF.Min(28f, btnH * 0.45f));
                if (igButton("v##dn",    new Vector2(bw, btnH)) && canAct) _game.SoftDrop();
                igPopFont();
                igPopFont();
            }
            igEnd();
        }
        else
        {
            // ── Landscape: [ROT][<] on the left, [>][v] on the right ─────
            float btnH = BTN_AREA_H - 10f;

            byte oL = 1;
            igSetNextWindowPos(new Vector2(BTN_LEFT_X, BTN_AREA_Y), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(BTN_LEFT_W, BTN_AREA_H), ImGuiCond.Always);
            igSetNextWindowBgAlpha(0f);
            igBegin("##tbL", ref oL, winFlags);
            {
                float half = (BTN_LEFT_W - 14f - pad) * 0.5f;
                igPushFont(null, 32f);
                if (igButton("ROT##rot", new Vector2(half, btnH)) && canAct) _game.Rotate();
                igSameLine(0f, pad);
                igPushFont(null, 52f);
                if (igButton(" < ##ml",  new Vector2(half, btnH)) && canAct) _game.MoveLeft();
                igPopFont();
                igPopFont();
            }
            igEnd();

            byte oR = 1;
            igSetNextWindowPos(new Vector2(BTN_RIGHT_X, BTN_AREA_Y), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(BTN_RIGHT_W, BTN_AREA_H), ImGuiCond.Always);
            igSetNextWindowBgAlpha(0f);
            igBegin("##tbR", ref oR, winFlags);
            {
                float half = (BTN_RIGHT_W - 14f - pad) * 0.5f;
                igPushFont(null, 52f);
                if (igButton(" > ##mr", new Vector2(half, btnH)) && canAct) _game.MoveRight();
                igSameLine(0f, pad);
                igPushFont(null, 32f);
                if (igButton("v##dn",   new Vector2(half, btnH)) && canAct) _game.SoftDrop();
                igPopFont();
                igPopFont();
            }
            igEnd();
        }

        igPopStyleVar(3);
        igPopStyleColor(3);
        igPopItemFlag();
    }

    static void CentredLabel(string text)
    {
        float w = igGetWindowWidth();
        Vector2 sz = default;
        igCalcTextSize(ref sz, text, null, false, -1f);
        igSetCursorPosX((w - sz.X) * 0.5f);
        igText(text);
    }

    // Centred coloured text inside the overlay window (call with correct font already pushed).
    static void OvrCentredColored(string text, Vector4 col, float windowW)
    {
        Vector2 sz = default;
        igCalcTextSize(ref sz, text, null, false, -1f);
        igSetCursorPosX(MathF.Max(0f, (windowW - sz.X) * 0.5f));
        igTextColored(col, text);
    }

    // -----------------------------------------------------------------------
    // Cell render helpers
    // -----------------------------------------------------------------------
    static void DrawCell(int row, int col, sg_color color)
    {
        if (row < 0 || row >= TetrisGame.Rows) return;
        sgp_set_color(color.r, color.g, color.b, color.a);
        DrawCellRaw(row, col);
    }

    // Assumes color already set via sgp_set_color
    static void DrawCellRaw(int row, int col)
    {
        if (row < 0 || row >= TetrisGame.Rows) return;
        float x = BRD_X + col * CELL;
        float y = BRD_Y + row * CELL;
        sgp_draw_filled_rect(x, y, CELL - 1f, CELL - 1f);
    }

    // -----------------------------------------------------------------------
    // Layout
    // -----------------------------------------------------------------------
    static void ComputeLayout(int sw, int sh)
    {
        _portrait = sh > sw;

        if (_portrait)
        {
            // ── Portrait layout ──────────────────────────────────────────
            // Buttons: fixed height anchored to the bottom of the screen.
            // Capped so they never become uncomfortably tall.
            float btnH = MathF.Min(MathF.Max(80f, sh * 0.15f), 120f);
            BTN_AREA_H  = btnH;
            BTN_AREA_Y  = sh - btnH - 4f;   // pinned to bottom, small margin
            BTN_LEFT_X  = 4f;
            BTN_LEFT_W  = sw - 8f;
            BTN_RIGHT_X = 0f;
            BTN_RIGHT_W = 0f;
            BTN_SHOW    = btnH >= 60f;

            const float TOP_MARGIN = 8f;

            // Panel width: at least 28% of screen width, and never less than 100px.
            float MIN_PNL_W = MathF.Max(100f, sw * 0.28f);

            // Cell size: constrained by both axes
            // Vertical: board must fit above BTN_AREA_Y with a small gap
            float availH   = BTN_AREA_Y - TOP_MARGIN - 8f;
            float maxCellH = availH  / TetrisGame.Rows;
            float maxCellW = (sw - MIN_PNL_W - 16f) / TetrisGame.Cols;
            CELL = MathF.Max(8f, MathF.Min(maxCellH, maxCellW));

            float boardW = TetrisGame.Cols * CELL;
            float boardH = TetrisGame.Rows * CELL;

            // Panel takes whatever is left; never narrower than the minimum
            PNL_W = MathF.Max(MIN_PNL_W, sw - boardW - 16f);

            float contentW = boardW + 10f + PNL_W;
            BRD_X = MathF.Max(4f, MathF.Floor((sw - contentW) * 0.5f));
            PNL_X = BRD_X + boardW + 10f;

            // Both score and level/lines panels are equal in height.
            // Anchor from the bottom: panels sit just above the button row.
            const float PNL_BOX_H = 76f;
            const float PNL_GAP   = 6f;
            const float PNL_BOT_MARGIN = 10f;   // gap between NEXT box and buttons

            PNL_H1 = PNL_BOX_H;
            PNL_H2 = PNL_BOX_H;
            PNL_H3 = MathF.Max(60f, boardH - PNL_H1 - PNL_H2 - 2f * PNL_GAP);

            // Stack from bottom up
            float pnlBottom = BTN_AREA_Y - PNL_BOT_MARGIN;
            PNL_Y3 = pnlBottom - PNL_H3;
            PNL_Y2 = PNL_Y3 - PNL_GAP - PNL_H2;
            PNL_Y1 = PNL_Y2 - PNL_GAP - PNL_H1;

            // Board bottom-aligns with panels — both columns end at the same Y
            BRD_Y = PNL_Y1;
        }
        else
        {
            // ── Landscape layout ─────────────────────────────────────────
            float maxH = (sh * 0.92f - 16f) / TetrisGame.Rows;
            float maxW = (sw * 0.42f)        / TetrisGame.Cols;
            CELL = MathF.Max(8f, MathF.Min(maxH, maxW));

            float boardW = TetrisGame.Cols * CELL;
            float boardH = TetrisGame.Rows * CELL;

            PNL_W = MathF.Max(120f, MathF.Min(190f, sw * 0.22f));

            float contentW = boardW + 10f + PNL_W;
            BRD_X = MathF.Max(8f, (sw - contentW) * 0.5f);
            BRD_Y = MathF.Max(8f, (sh - boardH)   * 0.5f);

            PNL_X = BRD_X + boardW + 10f;

            PNL_Y1 = BRD_Y;                  PNL_H1 = 70f;
            PNL_Y2 = PNL_Y1 + PNL_H1 + 8f;  PNL_H2 = 70f;
            PNL_Y3 = PNL_Y2 + PNL_H2 + 8f;  PNL_H3 = PNL_W;

            float leftAreaW  = BRD_X - 8f;
            float rightAreaX = PNL_X + PNL_W + 8f;
            float rightAreaW = sw - rightAreaX - 4f;

            BTN_SHOW    = leftAreaW >= 55f && rightAreaW >= 55f;
            BTN_LEFT_X  = 4f;
            BTN_LEFT_W  = leftAreaW;
            BTN_RIGHT_X = rightAreaX;
            BTN_RIGHT_W = rightAreaW;
            BTN_AREA_Y  = BRD_Y;
            BTN_AREA_H  = boardH;
        }
    }

    // -----------------------------------------------------------------------
    // Key-repeat processing
    // -----------------------------------------------------------------------
    static void ProcessKeyRepeat(float dt)
    {
        for (int ki = 0; ki < (int)GameKey.Count; ki++)
        {
            if (!_keyHeld[ki]) continue;
            _keyHoldTimer[ki] += dt;
            // Fire repeats only after initial delay, then at repeat rate
            if (_keyHoldTimer[ki] >= KEY_INITIAL_DELAY + KEY_REPEAT_RATE)
            {
                _keyHoldTimer[ki] = KEY_INITIAL_DELAY; // keep firing at rate
                DispatchGameKey((GameKey)ki);
            }
        }
    }

    static void DispatchGameKey(GameKey k)
    {
        switch (k)
        {
            case GameKey.Left:     _game.MoveLeft();  break;
            case GameKey.Right:    _game.MoveRight(); break;
            case GameKey.Down:     _game.SoftDrop();  break;
            case GameKey.Rotate:   _game.Rotate();    break;
            case GameKey.HardDrop: _game.HardDrop();  break;
        }
    }

    // -----------------------------------------------------------------------
    // Event handler
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static void Event(sapp_event* e)
    {
        simgui_handle_event(in *e);

        switch (e->type)
        {
            case sapp_event_type.SAPP_EVENTTYPE_KEY_DOWN:
                HandleKeyDown(e->key_code, e->key_repeat);
                break;

            case sapp_event_type.SAPP_EVENTTYPE_KEY_UP:
                HandleKeyUp(e->key_code);
                break;

            case sapp_event_type.SAPP_EVENTTYPE_RESIZED:
                ComputeLayout(sapp_width(), sapp_height());
                break;
        }
    }

    static void HandleKeyDown(sapp_keycode key, bool isRepeat = false)
    {
        // Restart from game over
        if (_game.GameOver)
        {
            if (key is sapp_keycode.SAPP_KEYCODE_N
                    or sapp_keycode.SAPP_KEYCODE_ENTER
                    or sapp_keycode.SAPP_KEYCODE_SPACE)
            {
                _game.Reset();
            }
            return;
        }

        // Pause toggle
        if (key is sapp_keycode.SAPP_KEYCODE_P or sapp_keycode.SAPP_KEYCODE_ESCAPE)
        {
            _game.Paused = !_game.Paused;
            return;
        }

        if (_game.Paused) return;

        GameKey? gk = key switch
        {
            sapp_keycode.SAPP_KEYCODE_LEFT  or sapp_keycode.SAPP_KEYCODE_A => GameKey.Left,
            sapp_keycode.SAPP_KEYCODE_RIGHT or sapp_keycode.SAPP_KEYCODE_D => GameKey.Right,
            sapp_keycode.SAPP_KEYCODE_DOWN  or sapp_keycode.SAPP_KEYCODE_S => GameKey.Down,
            sapp_keycode.SAPP_KEYCODE_UP    or sapp_keycode.SAPP_KEYCODE_W => GameKey.Rotate,
            sapp_keycode.SAPP_KEYCODE_SPACE => GameKey.HardDrop,
            _ => (GameKey?)null
        };

        if (gk == null) return;

        // Ignore system-level key repeat; we handle our own
        if (!isRepeat)
        {
            _keyHeld[(int)gk]      = true;
            _keyHoldTimer[(int)gk] = 0f;
            DispatchGameKey(gk.Value);
        }
    }

    static void HandleKeyUp(sapp_keycode key)
    {
        GameKey? gk = key switch
        {
            sapp_keycode.SAPP_KEYCODE_LEFT  or sapp_keycode.SAPP_KEYCODE_A => GameKey.Left,
            sapp_keycode.SAPP_KEYCODE_RIGHT or sapp_keycode.SAPP_KEYCODE_D => GameKey.Right,
            sapp_keycode.SAPP_KEYCODE_DOWN  or sapp_keycode.SAPP_KEYCODE_S => GameKey.Down,
            sapp_keycode.SAPP_KEYCODE_UP    or sapp_keycode.SAPP_KEYCODE_W => GameKey.Rotate,
            sapp_keycode.SAPP_KEYCODE_SPACE => GameKey.HardDrop,
            _ => (GameKey?)null
        };
        if (gk != null)
            _keyHeld[(int)gk] = false;
    }

    // -----------------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        simgui_shutdown();
        sgp_shutdown();
        sg_shutdown();

        if (Debugger.IsAttached)
            Environment.Exit(0);
    }

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------
    public static sapp_desc sokol_main()
    {
        return new sapp_desc
        {
            init_cb    = &Init,
            frame_cb   = &Frame,
            event_cb   = &Event,
            cleanup_cb = &Cleanup,
            width      = 480,
            height     = 720,
            window_title = "BlockFall",
            sample_count = 1,
            icon = { sokol_default = true },
            logger = { func = &slog_func },
        };
    }
}
