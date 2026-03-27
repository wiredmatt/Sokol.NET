// Chess-app.cs — Chess game using sokol_gp 2D rendering + Lynx AI engine.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using Sokol;
using Imgui;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SG.sg_vertex_format;
using static Sokol.SG.sg_blend_factor;
using static Sokol.SGlue;
using static Sokol.SGP;
using static Sokol.Utils;
using static Sokol.SImgui;
using static Sokol.SDebugText;
using static Imgui.ImguiNative;
using static Imgui.ImGuiHelpers;
using static Sokol.SLog;
using Lynx.Model;

public static unsafe class ChessApp
{
    enum HistoryNotation
    {
        Uci,
        Algebraic
    }

    readonly record struct MoveHistoryEntry(string Side, string Uci, string Algebraic);

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    const int BOARD_SIZE = 8;
    static float CELL_SIZE = 80f;
    static float BORDER = 40f;
    static float WINDOW_SIZE = BOARD_SIZE * CELL_SIZE + BORDER * 2f;
    static float UI_PANEL_WIDTH = 270f;
    static float UI_GAP = 16f;
    static float UI_MIN_WIDTH = 280f;
    static float UI_MAX_WIDTH = 380f;
    static float WINDOW_WIDTH = WINDOW_SIZE + UI_PANEL_WIDTH + UI_GAP + 16f;
    static float WINDOW_HEIGHT = WINDOW_SIZE;

    // Board colors (classic Lichess palette)
    static readonly sg_color CLR_LIGHT = new() { r = 0.941f, g = 0.851f, b = 0.710f, a = 1f };
    static readonly sg_color CLR_DARK = new() { r = 0.710f, g = 0.533f, b = 0.388f, a = 1f };
    static readonly sg_color CLR_SELECTED = new() { r = 0.20f, g = 0.75f, b = 0.20f, a = 0.55f };
    static readonly sg_color CLR_VALID = new() { r = 0.20f, g = 0.75f, b = 0.20f, a = 0.38f };
    static readonly sg_color CLR_LAST_MOVE = new() { r = 0.97f, g = 0.81f, b = 0.12f, a = 0.55f };
    static readonly sg_color CLR_CHECK = new() { r = 0.90f, g = 0.10f, b = 0.10f, a = 0.55f };
    static readonly sg_color CLR_BORDER = new() { r = 0.50f, g = 0.33f, b = 0.22f, a = 1f };

    // Checkers-style 3x5 pixel glyphs for digits 0-9 and letters A-H.
    static readonly int[][] s_fontPixels =
    {
        new[]{1,1,1, 1,0,1, 1,0,1, 1,0,1, 1,1,1}, // 0
        new[]{0,1,0, 1,1,0, 0,1,0, 0,1,0, 1,1,1}, // 1
        new[]{1,1,1, 0,0,1, 1,1,1, 1,0,0, 1,1,1}, // 2
        new[]{1,1,1, 0,0,1, 1,1,1, 0,0,1, 1,1,1}, // 3
        new[]{1,0,1, 1,0,1, 1,1,1, 0,0,1, 0,0,1}, // 4
        new[]{1,1,1, 1,0,0, 1,1,1, 0,0,1, 1,1,1}, // 5
        new[]{1,1,1, 1,0,0, 1,1,1, 1,0,1, 1,1,1}, // 6
        new[]{1,1,1, 0,0,1, 0,1,0, 0,1,0, 0,1,0}, // 7
        new[]{1,1,1, 1,0,1, 1,1,1, 1,0,1, 1,1,1}, // 8
        new[]{1,1,1, 1,0,1, 1,1,1, 0,0,1, 1,1,1}, // 9
        new[]{0,1,0, 1,0,1, 1,1,1, 1,0,1, 1,0,1}, // A
        new[]{1,1,0, 1,0,1, 1,1,0, 1,0,1, 1,1,0}, // B
        new[]{0,1,1, 1,0,0, 1,0,0, 1,0,0, 0,1,1}, // C
        new[]{1,1,0, 1,0,1, 1,0,1, 1,0,1, 1,1,0}, // D
        new[]{1,1,1, 1,0,0, 1,1,0, 1,0,0, 1,1,1}, // E
        new[]{1,1,1, 1,0,0, 1,1,0, 1,0,0, 1,0,0}, // F
        new[]{0,1,1, 1,0,0, 1,0,1, 1,0,1, 0,1,1}, // G
        new[]{1,0,1, 1,0,1, 1,1,1, 1,0,1, 1,0,1}, // H
    };

    static int FontIdx(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'A' and <= 'H' => ch - 'A' + 10,
        _ => -1
    };

    // -----------------------------------------------------------------------
    // Piece texture indices  (12 textures: White P N B R Q K, Black p n b r q k)
    // Lynx Piece enum:       P=0 N=1 B=2 R=3 Q=4 K=5  p=6 n=7 b=8 r=9 q=10 k=11
    // -----------------------------------------------------------------------
    static readonly string[] PIECE_ASSETS = {
        "Chess_plt60.png",  // 0  White Pawn
        "Chess_nlt60.png",  // 1  White Knight
        "Chess_blt60.png",  // 2  White Bishop
        "Chess_rlt60.png",  // 3  White Rook
        "Chess_qlt60.png",  // 4  White Queen
        "Chess_klt60.png",  // 5  White King
        "Chess_pdt60.png",  // 6  Black Pawn
        "Chess_ndt60.png",  // 7  Black Knight
        "Chess_bdt60.png",  // 8  Black Bishop
        "Chess_rdt60.png",  // 9  Black Rook
        "Chess_qdt60.png",  // 10 Black Queen
        "Chess_kdt60.png",  // 11 Black King
    };

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------
    struct _state
    {
        public sg_pass_action passAction;
        public sg_sampler sampler;
    }

    static _state S;

    // Piece textures
    static readonly Texture?[] _pieceTextures = new Texture?[12];
    static readonly sg_view[] _pieceViews = new sg_view[12];
    static readonly sg_image[] _pieceImages = new sg_image[12];
    static readonly bool[] _pieceLoaded = new bool[12];
    static int _piecesLoadedCount = 0;

    // Game
    static readonly ChessGame _game = new();
    static readonly List<MoveHistoryEntry> _moveHistory = new();
    static string? _lastRecordedMoveUci;
    static float _historyCopiedToastSec = 0f;
    static string _historyCopiedToast = string.Empty;
    static HistoryNotation _historyNotation = HistoryNotation.Uci;

    // Optional time-control mode
    static bool _useTimeControl = false;
    static int _timeMinutesPerSide = 5;
    static float _whiteTimeSec = 5 * 60f;
    static float _blackTimeSec = 5 * 60f;
    static bool _clockStarted = false;
    static bool _timeExpired = false;
    static string _timeExpiredStatus = string.Empty;
    static int _humanWins = 0;
    static int _aiWins = 0;
    static bool _resultRecordedForCurrentGame = false;

    // Last move highlight squares
    static int _lastMoveFrom = -1, _lastMoveTo = -1;

    // UI
    static byte _showUI = 1;
    static float _mouseX, _mouseY;
    static bool _mouseClicked;
    static bool _flipBoard = false;

    static string _statusText = "White to move";

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

        var sdtxDesc = new sdtx_desc_t();
        sdtxDesc.fonts[0] = sdtx_font_kc854();
        sdtx_setup(sdtxDesc);

        FileSystem.Instance.Initialize();

        // Linear clamp sampler for piece sprites
        S.sampler = sg_make_sampler(new sg_sampler_desc
        {
            min_filter = sg_filter.SG_FILTER_LINEAR,
            mag_filter = sg_filter.SG_FILTER_LINEAR,
            wrap_u = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
            wrap_v = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
        });

        // Pass action — dark background
        S.passAction = default;
        S.passAction.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        S.passAction.colors[0].clear_value = new sg_color { r = 0.15f, g = 0.15f, b = 0.15f, a = 1f };

        // Load piece textures asynchronously
        for (int i = 0; i < 12; i++)
        {
            int idx = i;
            FileSystem.Instance.LoadFile(PIECE_ASSETS[idx], (path, data, status) =>
            {
                if (status == FileLoadStatus.Success && data != null)
                {
                    var tex = Texture.LoadFromMemory(data, path);
                    if (tex != null)
                    {
                        // Keep a strong reference alive for the whole app lifetime.
                        // Texture finalizer destroys sg_image/sg_view if object gets GC-collected.
                        _pieceTextures[idx] = tex;
                        _pieceImages[idx] = tex.Image;
                        _pieceViews[idx] = sgp_make_texture_view_from_image(tex.Image, PIECE_ASSETS[idx]);
                        _pieceLoaded[idx] = true;
                        _piecesLoadedCount++;
                    }
                }
            });
        }

        float size = Math.Min(sapp_height(), sapp_width());
        CELL_SIZE = (size * 0.75f) / 8f;

        BORDER = CELL_SIZE;
        WINDOW_SIZE = BOARD_SIZE * CELL_SIZE + BORDER * 2f;
        UI_PANEL_WIDTH = 270f;
        UI_GAP = 16f;
        UI_MIN_WIDTH = 280f;
        UI_MAX_WIDTH = 380f;
        WINDOW_WIDTH = WINDOW_SIZE + UI_PANEL_WIDTH + UI_GAP + 16f;
        WINDOW_HEIGHT = WINDOW_SIZE;
    }

    // -----------------------------------------------------------------------
    // Frame
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static void Frame()
    {
        FileSystem.Instance.Update();
        ChessAI.PumpCompleted();

        int width = sapp_width();
        int height = sapp_height();
        float dt = (float)sapp_frame_duration();

        // Trigger AI if needed
        if (!_timeExpired && _game.Phase == GamePhase.AIThinking && !ChessAI.IsPending)
        {
            ChessAI.RequestMove(_game, () =>
            {
                UpdateLastMove();
                RefreshStatus();
            });
        }

        // Process pending mouse click
        bool clicked = _mouseClicked;
        _mouseClicked = false;

        if (!_timeExpired && clicked && _game.Phase == GamePhase.PlayerTurn)
        {
            int sq = ScreenToSquare(_mouseX, _mouseY, width, height);
            if (sq >= 0)
            {
                _game.SelectSquare(sq);
                if (_game.Phase == GamePhase.AIThinking)
                    UpdateLastMove();
                RefreshStatus();
            }
        }

        // Also trigger AI for new-game-as-black / immediate-transition scenario
        if (!_timeExpired && _game.Phase == GamePhase.AIThinking && !ChessAI.IsPending)
        {
            ChessAI.RequestMove(_game, () =>
            {
                UpdateLastMove();
                RefreshStatus();
            });
        }

        UpdateMoveHistory();
        UpdateTimeControl(dt);
        TryRecordGameResult();

        // --- SGP 2D frame ---
        sgp_begin(width, height);
        sgp_viewport(0, 0, width, height);
        sgp_project(0, width, 0, height);

        float boardPx = BOARD_SIZE * CELL_SIZE;
        ComputeBoardOrigin(width, height, boardPx, out float boardLeft, out float boardTop);

        // Border frame around the board
        sgp_set_color(CLR_BORDER.r, CLR_BORDER.g, CLR_BORDER.b, 1f);
        sgp_draw_filled_rect(boardLeft - BORDER, boardTop - BORDER,
                             boardPx + BORDER * 2f, boardPx + BORDER * 2f);

        DrawBoard(boardLeft, boardTop);
        DrawPieces(boardLeft, boardTop, width, height);
        DrawBoardCoordinates(boardLeft, boardTop);
        DrawGameOverBanner(width, height);

        PrepareGameOverSdtx(width, height);

        sg_begin_pass(new sg_pass { action = S.passAction, swapchain = sglue_swapchain() });
        sgp_flush();
        sgp_end();

        if (_showUI != 0)
            DrawImGui(dt);

        sdtx_draw();

        sg_end_pass();
        sg_commit();
    }

    // -----------------------------------------------------------------------
    // Board drawing
    // -----------------------------------------------------------------------
    static void DrawBoard(float bx, float by)
    {
        int selectedSq = _game.SelectedSquare;

        // Find king square when in check
        int checkKingSq = -1;
        if (_game.Phase != GamePhase.AIThinking && _game.IsInCheck())
        {
            var sideToMove = _game.CurrentSideToMove;
            var kingPiece = sideToMove == Side.White ? Piece.K : Piece.k;
            for (int sq = 0; sq < 64; sq++)
            {
                if (_game.GetPieceAt(sq) == kingPiece)
                {
                    checkKingSq = sq;
                    break;
                }
            }
        }

        var validDests = new HashSet<int>();
        foreach (var m in _game.LegalMovesFromSelected)
            validDests.Add(m.TargetSquare());

        for (int rank = 0; rank < 8; rank++)
        {
            for (int file = 0; file < 8; file++)
            {
                int sq = SquareFromRankFile(rank, file);
                SquareToScreen(sq, bx, by, out float sx, out float sy);

                // Base color
                bool light = ((rank + file) % 2) == 0;
                var col = light ? CLR_LIGHT : CLR_DARK;
                sgp_set_color(col.r, col.g, col.b, col.a);
                sgp_draw_filled_rect(sx, sy, CELL_SIZE, CELL_SIZE);

                // Last move
                if (sq == _lastMoveFrom || sq == _lastMoveTo)
                {
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);
                    sgp_set_color(CLR_LAST_MOVE.r, CLR_LAST_MOVE.g, CLR_LAST_MOVE.b, CLR_LAST_MOVE.a);
                    sgp_draw_filled_rect(sx, sy, CELL_SIZE, CELL_SIZE);
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_NONE);
                }

                // King in check
                if (sq == checkKingSq)
                {
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);
                    sgp_set_color(CLR_CHECK.r, CLR_CHECK.g, CLR_CHECK.b, CLR_CHECK.a);
                    sgp_draw_filled_rect(sx, sy, CELL_SIZE, CELL_SIZE);
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_NONE);
                }

                // Selected square
                if (sq == selectedSq)
                {
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);
                    sgp_set_color(CLR_SELECTED.r, CLR_SELECTED.g, CLR_SELECTED.b, CLR_SELECTED.a);
                    sgp_draw_filled_rect(sx, sy, CELL_SIZE, CELL_SIZE);
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_NONE);
                }

                // Valid destination dot
                if (validDests.Contains(sq))
                {
                    float dotSize = CELL_SIZE * 0.28f;
                    float dotOff = (CELL_SIZE - dotSize) * 0.5f;
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);
                    sgp_set_color(CLR_VALID.r, CLR_VALID.g, CLR_VALID.b, CLR_VALID.a);
                    sgp_draw_filled_rect(sx + dotOff, sy + dotOff, dotSize, dotSize);
                    sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_NONE);
                }
            }
        }
        sgp_reset_color();
    }

    static void DrawPieces(float bx, float by, int w, int h)
    {
        if (_piecesLoadedCount == 0)
        {
            return;
        }

        sgp_reset_viewport();
        sgp_reset_scissor();
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);

        int activeTexIdx = -1;
        var srcRect = new sgp_rect { x = 0f, y = 0f, w = 60f, h = 60f };

        for (int sq = 0; sq < 64; sq++)
        {
            var piece = _game.GetPieceAt(sq);
            if (piece == Piece.None || piece == Piece.Unknown) continue;

            int texIdx = (int)piece;
            if (texIdx < 0 || texIdx >= 12 || !_pieceLoaded[texIdx]) continue;

            SquareToScreen(sq, bx, by, out float sx, out float sy);
            float pad = CELL_SIZE * 0.05f;
            float size = CELL_SIZE - pad * 2f;

            if (texIdx != activeTexIdx)
            {
                sgp_set_view(0, _pieceViews[texIdx]);
                sgp_set_sampler(0, S.sampler);
                activeTexIdx = texIdx;
            }

            sgp_draw_textured_rect(
                0,
                new sgp_rect { x = sx + pad, y = sy + pad, w = size, h = size },
                srcRect);

        }

        sgp_reset_view(0);
        sgp_reset_sampler(0);
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_NONE);
    }

    // -----------------------------------------------------------------------
    // ImGui panel
    // -----------------------------------------------------------------------
    static void DrawImGui(float dt)
    {
        if (_historyCopiedToastSec > 0f)
        {
            _historyCopiedToastSec = MathF.Max(0f, _historyCopiedToastSec - dt);
            if (_historyCopiedToastSec <= 0f)
            {
                _historyCopiedToast = string.Empty;
            }
        }

        simgui_new_frame(new simgui_frame_desc_t
        {
            width = sapp_width(),
            height = sapp_height(),
            delta_time = dt
        });

        float boardPx = BOARD_SIZE * CELL_SIZE;
        ComputeBoardOrigin(sapp_width(), sapp_height(), boardPx, out float boardLeft, out float boardTop);

        float panelWidth = GetUiPanelWidth(sapp_widthf());
        float panelHeight = sapp_heightf();
        float panelX = 10f;
        float panelY = 0f;

        igSetNextWindowPos(new Vector2(panelX, panelY), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(panelWidth, panelHeight), ImGuiCond.Always);

        byte _open = 1;
        if (igBegin("Chess", ref _open,
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse))
        {
            bool gameplaySettingsDisabled = (_moveHistory.Count > 0 || _game.Phase == GamePhase.AIThinking)
                                           && _game.Phase != GamePhase.GameOver
                                           && !_timeExpired;

            igText(_statusText);
            igSeparator();

            int depth = _game.AiDepth;
            igBeginDisabled(gameplaySettingsDisabled);
            if (igSliderInt("AI Depth", ref depth, 1, 12, "%d", 0))
                _game.AiDepth = depth;
            igEndDisabled();

            {
                byte smpEnabled = _game.SearchMode == AISearchMode.MultithreadedSearcher ? (byte)1 : (byte)0;
                igBeginDisabled(gameplaySettingsDisabled);
                int displayThreads = smpEnabled != 0 ? _game.SearcherThreadCount : 1;
                if (igCheckbox($"Multithreading ({displayThreads} threads)", ref smpEnabled))
                    _game.SearchMode = smpEnabled != 0 ? AISearchMode.MultithreadedSearcher : AISearchMode.SingleEngine;
                igEndDisabled();
            }

            igSeparator();

            byte tcEnabled = _useTimeControl ? (byte)1 : (byte)0;
            igBeginDisabled(gameplaySettingsDisabled);
            if (igCheckbox("Use Time Limit", ref tcEnabled))
            {
                _useTimeControl = tcEnabled != 0;
                ResetClocks();
                _clockStarted = _useTimeControl && _moveHistory.Count > 0;
                _timeExpired = false;
                _timeExpiredStatus = string.Empty;
                RefreshStatus();
            }
            igEndDisabled();

            int mins = _timeMinutesPerSide;
            igBeginDisabled(gameplaySettingsDisabled);
            if (igSliderInt("Minutes/Side", ref mins, 1, 30, "%d", 0))
            {
                _timeMinutesPerSide = mins;
                if (_useTimeControl)
                {
                    ResetClocks();
                    _clockStarted = _moveHistory.Count > 0;
                    _timeExpired = false;
                    _timeExpiredStatus = string.Empty;
                    RefreshStatus();
                }
            }
            igEndDisabled();

            igText($"White: {FormatClock(_whiteTimeSec)}");
            igText($"Black: {FormatClock(_blackTimeSec)}");
            bool resetClocksDisabled = _useTimeControl && _clockStarted && !_timeExpired && _game.Phase != GamePhase.GameOver;
            igBeginDisabled(resetClocksDisabled);
            if (igButton("Reset Clocks", new Vector2(-1, 0)))
            {
                ResetClocks();
                _clockStarted = false;
                _timeExpired = false;
                _timeExpiredStatus = string.Empty;
                RefreshStatus();
            }
            igEndDisabled();

            igSeparator();

            byte flipByte = _flipBoard ? (byte)1 : (byte)0;
            igBeginDisabled(gameplaySettingsDisabled);
            if (igCheckbox("Flip Board", ref flipByte))
                _flipBoard = (flipByte != 0);
            igEndDisabled();

            igSeparator();

            if (igButton("New Game (White)", new Vector2(-1, 0)))
                StartNewGame(Side.White);

            if (igButton("New Game (Black)", new Vector2(-1, 0)))
                StartNewGame(Side.Black);

            if (_game.Phase == GamePhase.AIThinking)
            {
                igSeparator();
                igText("AI thinking...");
            }

            if (_piecesLoadedCount < 12)
            {
                igSeparator();
                igText($"Loading pieces {_piecesLoadedCount}/12...");
            }

            igSeparator();
            igText("Move History");

            byte uciSel = _historyNotation == HistoryNotation.Uci ? (byte)1 : (byte)0;
            if (igRadioButton_Bool("UCI", uciSel != 0))
            {
                _historyNotation = HistoryNotation.Uci;
            }
            igSameLine(0, 8f);
            byte algSel = _historyNotation == HistoryNotation.Algebraic ? (byte)1 : (byte)0;
            if (igRadioButton_Bool("Algebraic", algSel != 0))
            {
                _historyNotation = HistoryNotation.Algebraic;
            }

            if (igButton("Copy Move History", new Vector2(-1, 0)))
            {
                string payload = BuildMoveHistoryClipboardText();
                sapp_set_clipboard_string(payload);
                _historyCopiedToast = "Move history copied";
                _historyCopiedToastSec = 1.8f;
            }

            if (_historyCopiedToastSec > 0f)
            {
                igText(_historyCopiedToast);
            }

            Vector2 remaining = default;
            igGetContentRegionAvail(ref remaining);
            float historyHeight = MathF.Max(80f, remaining.Y);
            igBeginChild_Str("move_history", new Vector2(0, historyHeight), ImGuiChildFlags.Borders, ImGuiWindowFlags.None);
            if (_moveHistory.Count == 0)
            {
                igTextDisabled("No moves yet");
            }
            else
            {
                if (_historyNotation == HistoryNotation.Algebraic)
                {
                    string pgnLike = BuildAlgebraicHistoryText(includeResult: true);
                    igTextWrapped(pgnLike);
                }
                else
                {
                    for (int i = 0; i < _moveHistory.Count; i++)
                    {
                        var item = _moveHistory[i];
                        igText($"{item.Side}: {item.Uci}");
                    }
                }
            }
            igEndChild();
        }
        igEnd();
        simgui_render();
    }

    static string BuildMoveHistoryClipboardText()
    {
        if (_moveHistory.Count == 0)
        {
            return "No moves yet";
        }

        if (_historyNotation == HistoryNotation.Algebraic)
        {
            return BuildAlgebraicHistoryText(includeResult: true);
        }

        var lines = new List<string>(_moveHistory.Count);
        for (int i = 0; i < _moveHistory.Count; i++)
        {
            var item = _moveHistory[i];
            lines.Add($"{item.Side}: {item.Uci}");
        }
        return string.Join("\n", lines);
    }

    static string BuildAlgebraicHistoryText(bool includeResult)
    {
        if (_moveHistory.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();

        for (int i = 0; i < _moveHistory.Count; i += 2)
        {
            int moveNumber = (i / 2) + 1;
            sb.Append(moveNumber);
            sb.Append('.');
            sb.Append(_moveHistory[i].Algebraic);

            if (i + 1 < _moveHistory.Count)
            {
                sb.Append(' ');
                sb.Append(_moveHistory[i + 1].Algebraic);
            }

            if (i + 2 < _moveHistory.Count)
            {
                sb.Append(' ');
            }
        }

        if (includeResult)
        {
            string result = GetPgnResultSuffix();
            if (!string.IsNullOrEmpty(result))
            {
                sb.Append(' ');
                sb.Append(result);
            }
        }

        return sb.ToString();
    }

    static string GetPgnResultSuffix()
    {
        if (_game.Phase != GamePhase.GameOver)
        {
            return "";
        }

        return _game.OverReason switch
        {
            GameOverReason.Checkmate => _game.CurrentSideToMove == Side.White ? "0-1" : "1-0",
            GameOverReason.Stalemate => "1/2-1/2",
            GameOverReason.FiftyMoveRule => "1/2-1/2",
            GameOverReason.InsufficientMaterial => "1/2-1/2",
            _ => "*"
        };
    }

    static void StartNewGame(Side humanSide)
    {
        _game.Reset(humanSide);
        _flipBoard = (humanSide == Side.Black);
        _lastMoveFrom = -1;
        _lastMoveTo = -1;
        _moveHistory.Clear();
        _lastRecordedMoveUci = null;
        ResetClocks();
        _clockStarted = false;
        _timeExpired = false;
        _timeExpiredStatus = string.Empty;
        _resultRecordedForCurrentGame = false;

        if (humanSide == Side.Black && _game.Phase == GamePhase.AIThinking)
        {
            _statusText = "AI starts as White...";
        }
        else
        {
            RefreshStatus();
        }
    }

    // -----------------------------------------------------------------------
    // Event handling
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    private static void Event(sapp_event* e)
    {
        simgui_handle_event(in *e);

        if (e->type == sapp_event_type.SAPP_EVENTTYPE_MOUSE_UP
            && e->mouse_button == sapp_mousebutton.SAPP_MOUSEBUTTON_LEFT)
        {
            _mouseX = e->mouse_x;
            _mouseY = e->mouse_y;
            _mouseClicked = true;
        }

        if (e->type == sapp_event_type.SAPP_EVENTTYPE_TOUCHES_ENDED)
        {
            if (e->num_touches > 0)
            {
                _mouseX = e->touches[0].pos_x;
                _mouseY = e->touches[0].pos_y;
                _mouseClicked = true;
            }
        }

        if (e->type == sapp_event_type.SAPP_EVENTTYPE_KEY_DOWN)
        {
            if (e->key_code == sapp_keycode.SAPP_KEYCODE_ESCAPE)
                _showUI = (byte)(_showUI == 0 ? 1 : 0);
            bool gameplaySettingsDisabled = (_moveHistory.Count > 0 || _game.Phase == GamePhase.AIThinking)
                                           && _game.Phase != GamePhase.GameOver
                                           && !_timeExpired;
            if (e->key_code == sapp_keycode.SAPP_KEYCODE_F && !gameplaySettingsDisabled)
                _flipBoard = !_flipBoard;
        }

        if(e->type == sapp_event_type.SAPP_EVENTTYPE_RESIZED)
        {
            float size = Math.Min(sapp_height(), sapp_width());
            CELL_SIZE = (size * 0.75f) / 8f;

            BORDER = CELL_SIZE;
            WINDOW_SIZE = BOARD_SIZE * CELL_SIZE + BORDER * 2f;
            WINDOW_WIDTH = WINDOW_SIZE + GetUiPanelWidth(sapp_widthf()) + UI_GAP + 16f;
            WINDOW_HEIGHT = WINDOW_SIZE;
        }
    }

    // -----------------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------------
    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        for (int i = 0; i < _pieceTextures.Length; i++)
        {
            _pieceTextures[i]?.Dispose();
            _pieceTextures[i] = null;
        }

        simgui_shutdown();
        sdtx_shutdown();
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
            init_cb = &Init,
            frame_cb = &Frame,
            event_cb = &Event,
            cleanup_cb = &Cleanup,
            width = 1200,
            height = 800,
            window_title = "Chess",
            sample_count = 1,
            enable_clipboard = true,
            clipboard_size = 8192,
            icon = { sokol_default = true },
            logger = { func = &slog_func },
        };
    }

    // -----------------------------------------------------------------------
    // Coordinate helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Lynx big-endian squares: a8=0..h8=7, ..., a1=56..h1=63.
    /// screenRow 0 = top of window.
    /// </summary>
    static int SquareFromRankFile(int screenRow, int screenFile)
    {
        int lynxRank = _flipBoard ? (7 - screenRow) : screenRow;
        int lynxFile = _flipBoard ? (7 - screenFile) : screenFile;
        return lynxRank * 8 + lynxFile;
    }

    static void SquareToScreen(int square, float bx, float by, out float sx, out float sy)
    {
        int lynxRank = square / 8;
        int lynxFile = square % 8;
        int screenRow = _flipBoard ? (7 - lynxRank) : lynxRank;
        int screenFile = _flipBoard ? (7 - lynxFile) : lynxFile;
        sx = bx + screenFile * CELL_SIZE;
        sy = by + screenRow * CELL_SIZE;
    }

    static int ScreenToSquare(float mx, float my, int width, int height)
    {
        float boardPx = BOARD_SIZE * CELL_SIZE;
        ComputeBoardOrigin(width, height, boardPx, out float boardLeft, out float boardTop);
        float lx = mx - boardLeft;
        float ly = my - boardTop;
        if (lx < 0 || ly < 0 || lx >= boardPx || ly >= boardPx) return -1;
        int screenFile = (int)(lx / CELL_SIZE);
        int screenRow = (int)(ly / CELL_SIZE);
        return SquareFromRankFile(screenRow, screenFile);
    }

    static void ComputeBoardOrigin(int width, int height, float boardPx, out float boardLeft, out float boardTop)
    {
        float leftInset = 0f;
        if (_showUI != 0)
        {
            leftInset = GetUiPanelWidth(width) + UI_GAP + 10f;
        }

        float availableWidth = MathF.Max(1f, width - leftInset);
        boardLeft = leftInset + MathF.Max(0f, (availableWidth - boardPx) * 0.5f);
        boardTop = MathF.Max(0f, (height - boardPx) * 0.5f);
    }

    static float GetUiPanelWidth(float windowWidth)
    {
        return Math.Clamp(windowWidth * 0.24f, UI_MIN_WIDTH, UI_MAX_WIDTH);
    }

    // -----------------------------------------------------------------------
    // Status / highlight helpers
    // -----------------------------------------------------------------------
    static void UpdateLastMove()
    {
        var uci = _game.LastMoveUCI;
        if (uci != null && uci.Length >= 4)
        {
            int fromFile = uci[0] - 'a';
            int fromRank = 8 - (uci[1] - '0');
            int toFile = uci[2] - 'a';
            int toRank = 8 - (uci[3] - '0');
            _lastMoveFrom = fromRank * 8 + fromFile;
            _lastMoveTo = toRank * 8 + toFile;
        }
    }

    static void RefreshStatus()
    {
        if (_timeExpired)
        {
            _statusText = _timeExpiredStatus;
            return;
        }

        _statusText = _game.Phase switch
        {
            GamePhase.GameOver => _game.OverReason switch
            {
                GameOverReason.Checkmate => _game.CurrentSideToMove == Side.White
                                               ? "Checkmate — Black wins!"
                                               : "Checkmate — White wins!",
                GameOverReason.Stalemate => "Stalemate — Draw",
                GameOverReason.FiftyMoveRule => "Draw (50-move rule)",
                _ => "Game over"
            },
            GamePhase.AIThinking => "AI is thinking...",
            _ => _game.CurrentSideToMove == Side.White ? "White to move" : "Black to move"
        };
    }

    static void UpdateMoveHistory()
    {
        var uci = _game.LastMoveUCI;
        if (string.IsNullOrEmpty(uci) || uci == _lastRecordedMoveUci)
        {
            return;
        }

        Side mover = _game.CurrentSideToMove == Side.White ? Side.Black : Side.White;
        string side = mover == Side.White ? "White" : "Black";
        string algebraic = string.IsNullOrWhiteSpace(_game.LastMoveAlgebraic) ? uci : _game.LastMoveAlgebraic!;
        _moveHistory.Add(new MoveHistoryEntry(side, uci, algebraic));
        _lastRecordedMoveUci = uci;
        if (_useTimeControl)
        {
            _clockStarted = true;
        }
    }

    static void UpdateTimeControl(float dt)
    {
        if (!_useTimeControl || !_clockStarted || _timeExpired || _game.Phase == GamePhase.GameOver)
        {
            return;
        }

        if (_game.CurrentSideToMove == Side.White)
        {
            _whiteTimeSec -= dt;
            if (_whiteTimeSec <= 0f)
            {
                _whiteTimeSec = 0f;
                _timeExpired = true;
                _timeExpiredStatus = "Black wins on time";
                RefreshStatus();
            }
        }
        else
        {
            _blackTimeSec -= dt;
            if (_blackTimeSec <= 0f)
            {
                _blackTimeSec = 0f;
                _timeExpired = true;
                _timeExpiredStatus = "White wins on time";
                RefreshStatus();
            }
        }
    }

    static void ResetClocks()
    {
        float t = _timeMinutesPerSide * 60f;
        _whiteTimeSec = t;
        _blackTimeSec = t;
    }

    static string FormatClock(float seconds)
    {
        int total = Math.Max(0, (int)MathF.Floor(seconds));
        int mm = total / 60;
        int ss = total % 60;
        return $"{mm:00}:{ss:00}";
    }

    static void DrawBoardCoordinates(float bx, float by)
    {
        const float pixelPitch = 4.5f;
        const float pixelSize = 3.2f;
        const float labelMargin = 7f;

        void EmitPixel(float x, float y)
        {
            sgp_draw_filled_rect(x - pixelSize * 0.5f, y - pixelSize * 0.5f, pixelSize, pixelSize);
        }

        void EmitGlyph(char ch, float cx, float cy)
        {
            int fi = FontIdx(ch);
            if (fi < 0) return;
            var px = s_fontPixels[fi];
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (px[r * 3 + c] == 0) continue;
                    float x = cx + (c - 1) * pixelPitch;
                    float y = cy + (r - 2) * pixelPitch;
                    EmitPixel(x, y);
                }
            }
        }

        for (int i = 0; i < 8; i++)
        {
            // Files A-H on bottom wooden frame strip.
            int file = _flipBoard ? (7 - i) : i;
            char fileCh = (char)('A' + file);
            float fileX = bx + i * CELL_SIZE + CELL_SIZE * 0.5f;
            float fileY = by + BOARD_SIZE * CELL_SIZE + labelMargin + 10f;
            sgp_set_color(0.93f, 0.84f, 0.45f, 1f);
            EmitGlyph(fileCh, fileX, fileY);

            // Ranks 1-8 on left wooden frame strip.
            int rank = _flipBoard ? (i + 1) : (8 - i);
            float rankX = bx - labelMargin - 9f;
            float rankY = by + i * CELL_SIZE + CELL_SIZE * 0.5f;
            sgp_set_color(0.93f, 0.84f, 0.45f, 1f);
            EmitGlyph((char)('0' + rank), rankX, rankY);
        }

        sgp_reset_color();
    }

    static void DrawGameOverBanner(int width, int height)
    {
        if (_game.Phase != GamePhase.GameOver && !_timeExpired)
        {
            return;
        }

        float w = width;
        float h = height;
        float bandH = h * 0.28f;
        float bandY = (h - bandH) * 0.5f;

        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_BLEND);
        sgp_set_color(0f, 0f, 0f, 0.78f);
        sgp_draw_filled_rect(0f, bandY, w, bandH);
        sgp_set_blend_mode(sgp_blend_mode.SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    static void PrepareGameOverSdtx(int width, int height)
    {
        if (_game.Phase != GamePhase.GameOver && !_timeExpired)
        {
            return;
        }

        float w = width;
        float h = height;

        string line1;
        string line2;

        if (_timeExpired)
        {
            bool whiteFlagged = _whiteTimeSec <= 0f;
            line1 = whiteFlagged ? "BLACK WINS ON TIME" : "WHITE WINS ON TIME";
            line2 = BuildWinsScoreText();
        }
        else
        {
            if (_game.OverReason == GameOverReason.Checkmate)
            {
                var winner = _game.CurrentSideToMove == Side.White ? Side.Black : Side.White;
                bool humanWon = winner == _game.HumanSide;
                line1 = humanWon ? "CHECKMATE - YOU WIN!" : "CHECKMATE - AI WINS!";
                line2 = BuildWinsScoreText();
            }
            else
            {
                line1 = _game.OverReason switch
                {
                    GameOverReason.Stalemate => "STALEMATE",
                    GameOverReason.FiftyMoveRule => "DRAW (50-MOVE RULE)",
                    GameOverReason.InsufficientMaterial => "DRAW (INSUFFICIENT MATERIAL)",
                    _ => "GAME OVER"
                };
                line2 = BuildWinsScoreText();
            }
        }

        // Choose a large readable scale, but clamp it so the text always fits the screen.
        float baseScale1 = MathF.Max(2f, MathF.Min(w / 110f, h / 70f));
        float fitScale1W = w / (8f * (line1.Length + 2));
        float fitScale1H = h / 20f;
        float scale1 = MathF.Max(1f, MathF.Min(baseScale1, MathF.Min(fitScale1W, fitScale1H)));
        float cols1  = (w / scale1) / 8f;
        float rows1  = (h / scale1) / 8f;

        sdtx_font(0);
        sdtx_canvas(w / scale1, h / scale1);
        if (_timeExpired)
        {
            sdtx_color3b(255, 180, 60);
        }
        else if (_game.OverReason == GameOverReason.Checkmate)
        {
            var winner = _game.CurrentSideToMove == Side.White ? Side.Black : Side.White;
            bool humanWon = winner == _game.HumanSide;
            if (humanWon) sdtx_color3b(255, 215, 30);
            else sdtx_color3b(255, 90, 70);
        }
        else
        {
            sdtx_color3b(200, 200, 200);
        }
        sdtx_origin(MathF.Max(0f, (cols1 - line1.Length) * 0.5f), rows1 * 0.5f - 1.1f);
        sdtx_puts(line1);

        float baseScale2 = MathF.Max(1.5f, scale1 * 0.6f);
        float fitScale2W = w / (8f * (line2.Length + 2));
        float fitScale2H = h / 22f;
        float scale2 = MathF.Max(1f, MathF.Min(baseScale2, MathF.Min(fitScale2W, fitScale2H)));
        float cols2  = (w / scale2) / 8f;
        float rows2  = (h / scale2) / 8f;
        sdtx_canvas(w / scale2, h / scale2);
        sdtx_color3b(210, 210, 210);
        sdtx_origin(MathF.Max(0f, (cols2 - line2.Length) * 0.5f), rows2 * 0.5f + 1.3f);
        sdtx_puts(line2);
    }

    static void TryRecordGameResult()
    {
        if (_resultRecordedForCurrentGame)
        {
            return;
        }

        if (_timeExpired)
        {
            bool whiteFlagged = _whiteTimeSec <= 0f;
            Side winner = whiteFlagged ? Side.Black : Side.White;
            RecordWinner(winner);
            _resultRecordedForCurrentGame = true;
            return;
        }

        if (_game.Phase != GamePhase.GameOver)
        {
            return;
        }

        if (_game.OverReason == GameOverReason.Checkmate)
        {
            Side winner = _game.CurrentSideToMove == Side.White ? Side.Black : Side.White;
            RecordWinner(winner);
        }

        _resultRecordedForCurrentGame = true;
    }

    static void RecordWinner(Side winner)
    {
        if (winner == _game.HumanSide)
        {
            _humanWins++;
        }
        else
        {
            _aiWins++;
        }
    }

    static string BuildWinsScoreText()
    {
        return $"Human wins: {_humanWins}  AI wins: {_aiWins}";
    }
}
