// TexasHoldem-app.cs — Texas Hold'em Poker using Sokol.NET
// 2D rendering via sokol_gp; ImGui for UI; TexasHoldemGameEngine for game logic.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Sokol;
using Imgui;
using TexasHoldem.Logic.Cards;
using static Sokol.SApp;
using static Sokol.SG;
using static Sokol.SGlue;
using static Sokol.SGP;
using static Sokol.SGP.sgp_blend_mode;
using static Sokol.Utils;
using static Sokol.SImgui;
using static Sokol.SLog;
using static Imgui.ImguiNative;
using static Imgui.ImGuiHelpers;
using TexasHoldem.Logic;
using TexasHoldem.Logic.GameMechanics;

public static unsafe class TexasholdemApp
{
    // ──────────────────────────────────────────────────────────
    // Constants
    // ──────────────────────────────────────────────────────────

    // Card display sizes
    static float CARD_W    = 65f;
    static float CARD_H    = 90f;
    static float CARD_W_SM = 50f;   // AI face-down cards
    static float CARD_H_SM = 70f;

    // Table ellipse radii as fraction of TABLE area width/height
    static float TABLE_RX_FRAC = 0.41f;
    static float TABLE_RY_FRAC = 0.36f;

    // Community card center vertical position (fraction of height)
    static float COMM_Y_FRAC   = 0.47f;
    static float COMM_CARD_GAP = 6f;
    static float COMM_GROUP_GAP = 18f;  // Gap between FLOP/TURN and TURN/RIVER groups

    // Settings popup
    static bool _settingsOpen = false;

    // Action bar at the bottom (inside the table area, below it)
    static float ACTION_H = 120f;

    // Texture slots
    const int SLOT_BACKCARD   = 52;
    const int SLOT_DEALERCHIP = 53;
    const int SLOT_BETCHIP    = 54;
    const int TOTAL_SLOTS     = 55;

    // Colors
    static readonly sg_color CLR_BG      = new() { r=0.08f, g=0.08f, b=0.10f, a=1f };
    static readonly sg_color CLR_FELT    = new() { r=0.07f, g=0.35f, b=0.12f, a=1f };
    static readonly sg_color CLR_ACTIVE  = new() { r=1.00f, g=0.84f, b=0.00f, a=0.40f };
    static readonly sg_color CLR_FOLDED  = new() { r=0.30f, g=0.30f, b=0.30f, a=0.55f };

    // ──────────────────────────────────────────────────────────
    // State
    // ──────────────────────────────────────────────────────────
    struct _state
    {
        public sg_pass_action passAction;
        public sg_sampler     sampler;
    }
    static _state S;

    // Texture / view arrays indexed by card slot
    static readonly Texture?[] _textures = new Texture?[TOTAL_SLOTS];
    static readonly sg_view[]  _views    = new sg_view[TOTAL_SLOTS];
    static readonly bool[]     _loaded   = new bool[TOTAL_SLOTS];
    static int                 _loadedCount;

    // Table background
    static Texture?  _tableTexture;
    static sg_view   _tableView;
    static bool      _tableLoaded;

    // Game
    static readonly PokerGame _game = new();
    static int  _numAI       = 3;
    static int  _buyIn       = 1000;
    static int  _raiseAmount = 50;
    // Blind / rule settings (applied when starting a new game)
    static int  _sbLevel        = 0;     // index into TexasHoldemGame.SmallBlinds
    static bool _escalateBlinds = false;
    static int  _blindPeriod    = 10;    // hands between blind level increases
    // Lobby / settings state — true = settings editable (before game start or after New Game)
    static bool _inLobby    = true;
    static bool _simMode    = false;   // simulation mode: all-AI, no human player
    static int  _simHands   = 100;     // number of hands to simulate (0 = until tournament ends)

    // Animation / UI state
    static float  _globalTime       = 0f;
    static int    _actionDelayMs    = 1000;
    static int    _actionFlashSeat  = -1;
    static float  _actionFlashTimer = 0f;
    static string _prevLastAction   = "";

    // Win card animation — cards slide from their original positions to the winning strip
    static float      _winAnimT      = 0f;
    static bool       _winAnimReady  = false;
    static readonly Vector2[] _winCardSrc   = new Vector2[5];
    static readonly Vector2[] _winCardDst   = new Vector2[5];
    static readonly Vector2[] _winCardSrcSz = new Vector2[5];
    static readonly Card[]    _winAnimCards = new Card[5]!;
    static PokerPhase    _prevPhase     = PokerPhase.Idle;
    static GameRoundType _prevRoundType = GameRoundType.PreFlop;
    const  float      WIN_ANIM_DUR   = 0.65f;

    // ── Deal animation ────────────────────────────────────────────────────────
    // Each entry represents one card flying from the deck to its slot.
    // Cards fly face-down; once T reaches 1 the normal slot rendering resumes.
    struct DealCard
    {
        public int   PlayerIdx;  // seat index, or -1 = community card
        public int   Slot;       // hole: 0=card1, 1=card2  /  community: 0..4
        public float T;          // 0→1 (1 = arrived)
    }
    static readonly List<DealCard> _dealCards  = new();
    static int   _dealCur    = 0;
    static bool  _dealActive = false;
    const  float DEAL_DUR    = 0.19f;   // seconds per card flight
    static bool  _gameLaunchedThisFrame = false; // set when New Game pressed; resets anim state after frame ends

    // ImGui font pinning — keep alive until atlas is built on first simgui_new_frame
    static GCHandle _fontDataPin;
    static GCHandle _glyphRangesPin;
    // Glyph ranges: Basic Latin (0x0020–0x00FF) + Misc Symbols (0x2600–0x26FF covers ♠♣♥♦★)
    static readonly ushort[] _pokerGlyphRanges = { 0x0020, 0x00FF, 0x2600, 0x26FF, 0 };

    // UI scale — updated every frame, drives font size and all ImGui window dimensions.
    // Reference design is 720 px tall; scale = 1.0 at 720p, 1.5 at 1080p, etc.
    static float _uiScale = 1f;

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    // Width of the playfield (full screen — no side panel)
    static float TableW(int w) => (float)w;

    // ──────────────────────────────────────────────────────────
    // Init
    // ──────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    private static void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger = { func = &slog_func },
        });

        sgp_setup(new sgp_desc());

        simgui_setup(new simgui_desc_t { logger = { func = &slog_func } });


        S.sampler = sg_make_sampler(new sg_sampler_desc
        {
            min_filter = sg_filter.SG_FILTER_LINEAR,
            mag_filter = sg_filter.SG_FILTER_LINEAR,
            wrap_u     = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
            wrap_v     = sg_wrap.SG_WRAP_CLAMP_TO_EDGE,
        });

        S.passAction = default;
        S.passAction.colors[0].load_action = sg_load_action.SG_LOADACTION_CLEAR;
        S.passAction.colors[0].clear_value = CLR_BG;

        FileSystem.Instance.Initialize();

        LoadImGuiFonts();

        // Table background
        FileSystem.Instance.LoadFile("PokerTable.png", (path, data, status) =>
        {
            if (status == FileLoadStatus.Success && data != null)
            {
                _tableTexture = Texture.LoadFromMemory(data, "PokerTable");
                if (_tableTexture != null)
                {
                    _tableView   = sgp_make_texture_view_from_image(_tableTexture.Image, "PokerTable");
                    _tableLoaded = true;
                }
            }
        });

        // Special chip/card slots
        LoadSlot(SLOT_BACKCARD,   "cards/BackCard.png",   "BackCard");
        LoadSlot(SLOT_DEALERCHIP, "chips/DealerChip.png", "DealerChip");
        LoadSlot(SLOT_BETCHIP,    "chips/BetChip.png",    "BetChip");

        // 52 face cards
        foreach (CardSuit suit in Enum.GetValues<CardSuit>())
            foreach (CardType type in Enum.GetValues<CardType>())
            {
                int    slot     = CardToSlot(suit, type);
                string filename = $"cards/{CardTypeName(type)}_of_{SuitName(suit)}.png";
                LoadSlot(slot, filename, filename);
            }

            
        AdjustTableDimensions();
        _settingsOpen = true;  // open settings on first launch so the player can configure & start
    }

    // ──────────────────────────────────────────────────────────
    // Frame
    // ──────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    private static void Frame()
    {
        FileSystem.Instance.Update();
#if WEB
        _game.TickWebDelay();
#endif

        int   fbWidth  = sapp_width();
        int   fbHeight = sapp_height();
        float dpiScale = sapp_dpi_scale();
        int   width    = (int)MathF.Round(fbWidth  / dpiScale);
        int   height   = (int)MathF.Round(fbHeight / dpiScale);
        float dt     = (float)sapp_frame_duration();
        // Scale UI relative to 720-px reference height.
        // Minimum 1.0 on desktop; 1.5 on Android so text and buttons are large enough for touch
        // (logical height in landscape is ~360pt, which would give _uiScale≈0.5 without the floor).
#if __ANDROID__
        _uiScale = Math.Clamp(height / 720f, 1.5f, 5f);
#else
        _uiScale = Math.Clamp(height / 720f, 1.0f, 4f);
#endif
        float tableW  = (float)width;
        int   iTableW = (int)tableW;
        var   snap    = _game.Snapshot;   // atomic read

        // ── Animation state update ─────────────────────────────
        _globalTime += dt;
        _game.ActionDelayMs = _actionDelayMs;
        if (snap.LastActionDescription != _prevLastAction)
        {
            _prevLastAction = snap.LastActionDescription;
            if (snap.LastActionSeatIdx >= 0)
            {
                _actionFlashSeat  = snap.LastActionSeatIdx;
                _actionFlashTimer = 1.0f;
            }
        }
        _actionFlashTimer = MathF.Max(0f, _actionFlashTimer - dt * 2f);

        // Win card animation
        if (snap.Phase == PokerPhase.HandOver && snap.WinningCards.Count == 5)
        {
            if (_prevPhase != PokerPhase.HandOver) _winAnimT = 0f;
            if (!_winAnimReady) StartWinAnimation(snap, tableW, height);
            _winAnimT = MathF.Min(1f, _winAnimT + dt / WIN_ANIM_DUR);
        }
        else
        {
            _winAnimT = 0f;
            _winAnimReady = false;
        }

        // Deal animation: trigger on round entry or new hand start
        bool _isActivePlay = snap.Phase != PokerPhase.Idle
                          && snap.Phase != PokerPhase.HandOver
                          && snap.Phase != PokerPhase.GameOver;
        bool _newPreFlop   = _isActivePlay
                          && snap.RoundType == GameRoundType.PreFlop
                          && (_prevPhase == PokerPhase.HandOver || _prevPhase == PokerPhase.Idle);
        bool _roundChanged = snap.RoundType != _prevRoundType;
        if (_newPreFlop)
            StartDealHoleCards(snap);
        else if (_isActivePlay && _roundChanged)
        {
            if      (snap.RoundType == GameRoundType.Flop)  StartDealCommunity(0, 3);
            else if (snap.RoundType == GameRoundType.Turn)  StartDealCommunity(3, 1);
            else if (snap.RoundType == GameRoundType.River) StartDealCommunity(4, 1);
        }
        else if (!_isActivePlay)
        { _dealCards.Clear(); _dealCur = 0; _dealActive = false; }
        // Advance the currently-flying card
        if (_dealActive && _dealCur < _dealCards.Count)
        {
            var dc = _dealCards[_dealCur];
            dc.T = MathF.Min(1f, dc.T + dt / DEAL_DUR);
            _dealCards[_dealCur] = dc;
            if (dc.T >= 1f)
            {
                _dealCur++;
                if (_dealCur >= _dealCards.Count) _dealActive = false;
            }
        }

        _prevPhase    = snap.Phase;
        _prevRoundType = snap.RoundType;

        if (_gameLaunchedThisFrame)
        {
            // Overwrite prevPhase to Idle so next frame's _newPreFlop trigger fires correctly,
            // regardless of what phase the old game was in when New Game was pressed.
            _prevPhase     = PokerPhase.Idle;
            _prevRoundType = GameRoundType.PreFlop;
            _dealCards.Clear(); _dealCur = 0; _dealActive = false;
            _winAnimT = 0f; _winAnimReady = false;
            _gameLaunchedThisFrame = false;
        }

        // ── SGP 2D — full screen ──────────────────────────────
        sgp_begin(fbWidth, fbHeight);
        // Viewport: full framebuffer (no side panel any more).
        sgp_viewport(0, 0, fbWidth, fbHeight);
        // Projection matches logical pixel space (SGP handles the DPI mapping).
        sgp_project(0f, tableW, 0f, (float)height);
        // Scissor: clip all SGP drawing to the play area (above the action bar).
        sgp_scissor(0, 0, fbWidth, (int)MathF.Round(PlayH(height) * dpiScale));

        DrawTableBackground(tableW, height);
        DrawCommunityCards(snap, tableW, height);
        DrawPlayerSeats(snap, tableW, height);
        DrawDealAnims(snap, tableW, height);
        DrawWinningHandStrip(snap, tableW, height);

        sg_begin_pass(new sg_pass { action = S.passAction, swapchain = sglue_swapchain() });
        sgp_flush();
        sgp_end();

        // ── ImGui ─────────────────────────────────────────────
        // Font scales with window height (no dpi_scale — simgui coordinates are logical pixels).
        // 15 px at 720p → 22 px at 1080p → 30 px at 1440p → 50 px on a 2400px Android screen.
        // No upper cap: _uiScale is already clamped to 4×, giving a max of 60 px.
        igGetStyle()->FontSizeBase = MathF.Round(15f * _uiScale);
        simgui_new_frame(new simgui_frame_desc_t
        {
            width      = fbWidth,
            height     = fbHeight,
            delta_time = dt,
            dpi_scale  = dpiScale,
        });

        DrawPlayerOverlays(snap, tableW, height);
        if (!UsePngCards) CardRenderer.FlushCardText();
        DrawPotOverlay(snap, tableW, height);
        DrawSettingsButton(width, height);
        DrawSettingsWindow(snap, height);
        DrawActionBar(snap, tableW, height);

        simgui_render();
        sg_end_pass();
        sg_commit();
    }

    static void AdjustTableDimensions()
    {
        // Always use logical points for layout, regardless of high_dpi.
        float dpiScale = sapp_dpi_scale();
        float w = sapp_width()  / dpiScale;
        float h = sapp_height() / dpiScale;

        // On small screens (phones) the logical height is much less than the 720pt reference.
        // Boost card sizes by the inverse scale so they remain a similar physical size to desktop.
        // Cap: 2.0× on Android (no high-dpi, logical = physical px) so cards are crisp & large;
        //      1.5× elsewhere (high-dpi screens scale up the physical pixels already).
#if __ANDROID__
        float cardScale = 1.4f;//Math.Min(Math.Max(720f / h, 1.0f), 2.0f);
#else
        float cardScale = Math.Min(Math.Max(720f / h, 1.0f), 1.5f);
#endif

        // Card height is the primary dimension; width derived from standard card aspect ratio (2.5:3.5 ≈ 0.714).
        CARD_H = (h/10f) * cardScale;
        CARD_W = CARD_H * 0.714f;

        CARD_W_SM = CARD_W * 0.77f;
        CARD_H_SM = CARD_H * 0.77f;
   
        COMM_CARD_GAP = CARD_W/10f;
        COMM_GROUP_GAP = CARD_W/5f;  // Gap between FLOP/TURN and TURN/RIVER groups

        // Action bar at the bottom (inside the table area, below it).
        // Floor at 70pt so the bar is always tall enough to contain its content.
        ACTION_H = MathF.Max(h / 7f, 70f);
    }

    // ──────────────────────────────────────────────────────────
    // Table background — fills the table viewport
    // ──────────────────────────────────────────────────────────
    static void DrawTableBackground(float tableW, int height)
    {
        if (_tableLoaded && _tableTexture != null)
        {
            sgp_set_blend_mode(SGP_BLENDMODE_NONE);
            sgp_set_color(1f, 1f, 1f, 1f);
            sgp_set_view(0, _tableView);
            sgp_set_sampler(0, S.sampler);
            sgp_draw_textured_rect(0,
                new sgp_rect { x = 0, y = 0, w = tableW,               h = (float)height        },
                new sgp_rect { x = 0, y = 0, w = _tableTexture.Width,  h = _tableTexture.Height });
            sgp_reset_view(0);
            sgp_reset_sampler(0);
        }
        else
        {
            sgp_set_blend_mode(SGP_BLENDMODE_NONE);
            sgp_set_color(CLR_FELT.r, CLR_FELT.g, CLR_FELT.b, 1f);
            sgp_draw_filled_rect(0, 0, tableW, (float)height);
        }
        sgp_reset_color();
    }

    // ──────────────────────────────────────────────────────────
    // Community cards — FLOP (3) | gap | TURN (1) | gap | RIVER (1)
    // ──────────────────────────────────────────────────────────
    static void DrawGoldGlow(float x, float y, float w, float h, float pad = 5f)
    {
        float pulseAlpha = 0.42f + 0.18f * MathF.Abs(MathF.Sin(_globalTime * 3.5f));
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(1.0f, 0.80f, 0.0f, pulseAlpha);
        sgp_draw_filled_rect(x - pad, y - pad, w + pad * 2f, h + pad * 2f);
        // Bright gold border
        sgp_set_color(1.0f, 0.95f, 0.1f, 0.95f);
        sgp_draw_line(x - pad,     y - pad,     x + w + pad, y - pad    );
        sgp_draw_line(x - pad,     y + h + pad, x + w + pad, y + h + pad);
        sgp_draw_line(x - pad,     y - pad,     x - pad,     y + h + pad);
        sgp_draw_line(x + w + pad, y - pad,     x + w + pad, y + h + pad);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    static void DrawCommunityCards(RenderSnapshot snap, float tableW, int height)
    {
        // Total width: 3 flop cards + 2 inner gaps + 2 group gaps + TURN + RIVER
        float totalW = 5 * CARD_W + 2 * COMM_CARD_GAP + 2 * COMM_GROUP_GAP;
        float startX = tableW * 0.5f - totalW * 0.5f;
        float startY = height * COMM_Y_FRAC - CARD_H * 0.5f;

        // Positions: [0] [1] [2]  GAP  [3]  GAP  [4]
        float[] xPos = new float[5];
        xPos[0] = startX;
        xPos[1] = xPos[0] + CARD_W + COMM_CARD_GAP;
        xPos[2] = xPos[1] + CARD_W + COMM_CARD_GAP;
        xPos[3] = xPos[2] + CARD_W + COMM_GROUP_GAP;
        xPos[4] = xPos[3] + CARD_W + COMM_GROUP_GAP;

        // During HandOver with animation, winning cards are drawn by DrawWinningHandStrip — skip them here
        bool[] isWinning = new bool[5];
        if (snap.Phase == PokerPhase.HandOver && snap.WinningCards.Count == 5)
        {
            var pool = new List<Card>(snap.WinningCards);
            for (int i = 0; i < snap.CommunityCards.Count && i < 5; i++)
            {
                int idx = pool.FindIndex(w => w.Equals(snap.CommunityCards[i]));
                if (idx >= 0) { isWinning[i] = true; pool.RemoveAt(idx); }
            }
        }

        for (int i = 0; i < 5; i++)
        {
            if (isWinning[i]) continue;       // animating to winning strip
            if (IsCommDealSuppressed(i)) continue;  // flying in from deck
            if (i < snap.CommunityCards.Count)
                DrawFaceCard(snap.CommunityCards[i], xPos[i], startY, CARD_W, CARD_H);
            else
                DrawCardPlaceholder(xPos[i], startY, CARD_W, CARD_H);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Player seats around the ellipse
    // ──────────────────────────────────────────────────────────
    static void DrawPlayerSeats(RenderSnapshot snap, float tableW, int height)
    {
        int n = snap.Players.Length;
        if (n == 0) return;

        float playH = PlayH(height);
        float cx = tableW * 0.5f;
        float cy = playH * 0.45f;
        float rx = tableW * TABLE_RX_FRAC;
        float ry = playH  * TABLE_RY_FRAC;

        for (int i = 0; i < n; i++)
        {
            var   player   = snap.Players[i];
            float angleDeg = 90f + i * (360f / n);
            float angleRad = angleDeg * MathF.PI / 180f;
            float px       = cx + rx * MathF.Cos(angleRad);
            float py       = cy + ry * MathF.Sin(angleRad);

            // Winner fluctuating glow during HandOver
            if (snap.Phase == PokerPhase.HandOver && player.IsHandWinner)
            {
                float pulse = 0.22f + 0.18f * MathF.Abs(MathF.Sin(_globalTime * 3.2f));
                float hw3 = CARD_W * 2f + 32f;
                float hh3 = CARD_H + 52f;
                float rx3 = px - hw3 * 0.5f;
                float ry3 = py - hh3 * 0.5f + 4f;
                sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
                // Inner gold fill
                sgp_set_color(1.0f, 0.82f, 0.0f, pulse);
                sgp_draw_filled_rect(rx3, ry3, hw3, hh3);
                // Bright animated border (4 lines)
                float bAlpha = MathF.Min(1f, pulse * 2.8f);
                sgp_set_color(1.0f, 0.96f, 0.1f, bAlpha);
                sgp_draw_line(rx3,        ry3,        rx3 + hw3, ry3        );
                sgp_draw_line(rx3,        ry3 + hh3,  rx3 + hw3, ry3 + hh3  );
                sgp_draw_line(rx3,        ry3,        rx3,       ry3 + hh3  );
                sgp_draw_line(rx3 + hw3,  ry3,        rx3 + hw3, ry3 + hh3  );
                sgp_set_blend_mode(SGP_BLENDMODE_NONE);
                sgp_reset_color();
            }

            // Active player highlight — pulsing
            if (player.IsCurrentTurn)
            {
                float pulse = 0.32f + 0.13f * MathF.Sin(_globalTime * 4.5f);
                sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
                sgp_set_color(CLR_ACTIVE.r, CLR_ACTIVE.g, CLR_ACTIVE.b, pulse);
                float hw = CARD_W * 2f + 20f;
                float hh = CARD_H + 36f;
                sgp_draw_filled_rect(px - hw * 0.5f, py - hh * 0.5f + 4f, hw, hh);
                sgp_set_blend_mode(SGP_BLENDMODE_NONE);
                sgp_reset_color();
            }

            // Action flash — green glow when this seat just acted
            if (i == _actionFlashSeat && _actionFlashTimer > 0f)
            {
                float flashAlpha = _actionFlashTimer * 0.45f;
                sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
                sgp_set_color(0.15f, 0.90f, 0.40f, flashAlpha);
                float hw2 = CARD_W * 2f + 36f;
                float hh2 = CARD_H + 56f;
                sgp_draw_filled_rect(px - hw2 * 0.5f, py - hh2 * 0.5f + 4f, hw2, hh2);
                sgp_set_blend_mode(SGP_BLENDMODE_NONE);
                sgp_reset_color();
            }

            if (player.IsHuman)
                DrawHumanCards(i, player, snap, px, py);
            else
                DrawAICards(i, player, snap, px, py);

            // TBD ELI , why is it needed ? 
            // Bet chip above the player's cards
            // if (player.CurrentRoundBet > 0)
            //     DrawChipIcon(SLOT_BETCHIP, px - 12f, py - CARD_H * 0.5f - 30f);
        }

        // Dealer chip — drawn on the right edge of the dealer player's name label (black rectangle).
        int   d       = snap.DealerSeatIndex % n;
        float chipSz  = MathF.Round(46f * _uiScale);
        SeatPosition(d, n, tableW, height, out float dpx, out float dpy);
        float dlabelW  = MathF.Round(162f * _uiScale);
        bool  dIsHuman = snap.Players[d].IsHuman;
        float dHalfCard = dIsHuman ? CARD_H : CARD_H_SM;
        float dLabelY   = dpy + dHalfCard * 0.5f + 4f;
        float dLabelX   = Math.Clamp(dpx - dlabelW * 0.5f, 4f, tableW - dlabelW - 4f);
        float dLabelH   = MathF.Round(62f * _uiScale);
        // Place chip centered vertically on the label, hanging off the right side
        float chipX = dLabelX + dlabelW - chipSz;
        float chipY = dLabelY + (dLabelH - chipSz) * 0.5f;
        DrawChipIcon(SLOT_DEALERCHIP, chipX, chipY);
    }

    static void DrawHumanCards(int seatIdx, PlayerRenderInfo player, RenderSnapshot snap, float px, float py)
    {
        // Once the human folds, hide their cards for the rest of the hand (including HandOver)
        if (!player.IsInHand)
            return;

        float x1 = px - CARD_W - 4f, y1 = py - CARD_H * 0.5f;
        float x2 = px + 4f,          y2 = y1;

        // Winning hole cards animate to the strip — don't draw at seat position
        bool isHO = snap.Phase == PokerPhase.HandOver && snap.WinningCards.Count == 5;
        bool c1Win = isHO && player.Card1 != null && snap.WinningCards.Any(w => w.Equals(player.Card1));
        bool c2Win = isHO && player.Card2 != null && snap.WinningCards.Any(w => w.Equals(player.Card2));

        // Cards still flying in from the deck — skip normal draw
        bool c1Dealt = IsDealSuppressed(seatIdx, 0);
        bool c2Dealt = IsDealSuppressed(seatIdx, 1);

        if (!c1Win && !c1Dealt)
        {
            if (player.Card1 != null) DrawFaceCard(player.Card1, x1, y1, CARD_W, CARD_H);
            else                      DrawCardPlaceholder(x1, y1, CARD_W, CARD_H);
        }
        if (!c2Win && !c2Dealt)
        {
            if (player.Card2 != null) DrawFaceCard(player.Card2, x2, y2, CARD_W, CARD_H);
            else                      DrawCardPlaceholder(x2, y2, CARD_W, CARD_H);
        }
    }

    static void DrawAICards(int seatIdx, PlayerRenderInfo player, RenderSnapshot snap, float px, float py)
    {
        float x1 = px - CARD_W_SM - 3f, y1 = py - CARD_H_SM * 0.5f;
        float x2 = px + 3f,             y2 = y1;

        bool showFaceUp = snap.Phase == PokerPhase.HandOver && player.Card1 != null;
        bool ai1Sup = IsDealSuppressed(seatIdx, 0);
        bool ai2Sup = IsDealSuppressed(seatIdx, 1);

        if (showFaceUp)
        {
            // Winning cards animate to the strip — skip them here
            bool ai1Win = snap.WinningCards.Count == 5 && player.Card1 != null && snap.WinningCards.Any(w => w.Equals(player.Card1));
            bool ai2Win = snap.WinningCards.Count == 5 && player.Card2 != null && snap.WinningCards.Any(w => w.Equals(player.Card2));
            if (!ai1Win && !ai1Sup && player.Card1 != null) DrawFaceCard(player.Card1, x1, y1, CARD_W_SM, CARD_H_SM);
            if (!ai2Win && !ai2Sup && player.Card2 != null) DrawFaceCard(player.Card2, x2, y2, CARD_W_SM, CARD_H_SM);
        }
        else if (player.IsInHand)
        {
            if (!ai1Sup) DrawBackCard(x1, y1, CARD_W_SM, CARD_H_SM);
            if (!ai2Sup) DrawBackCard(x2, y2, CARD_W_SM, CARD_H_SM);
        }
        else
        {
            // Folded — gray overlay
            sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
            sgp_set_color(CLR_FOLDED.r, CLR_FOLDED.g, CLR_FOLDED.b, CLR_FOLDED.a);
            sgp_draw_filled_rect(x1, y1, CARD_W_SM * 2f + 6f, CARD_H_SM);
            sgp_set_blend_mode(SGP_BLENDMODE_NONE);
            sgp_reset_color();
        }
    }

    static void DrawChipIcon(int slot, float x, float y)
    {
        if (!_loaded[slot]) return;
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(1f, 1f, 1f, 1f);
        sgp_set_view(0, _views[slot]);
        sgp_set_sampler(0, S.sampler);
        float chipSz = (slot == SLOT_DEALERCHIP) ? MathF.Round(46f * _uiScale) : MathF.Round(28f * _uiScale);
        sgp_draw_textured_rect(0,
            new sgp_rect { x = x, y = y, w = chipSz, h = chipSz },
            FullSrcRect(_textures[slot]!));
        sgp_reset_view(0);
        sgp_reset_sampler(0);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
    }

    // ──────────────────────────────────────────────────────────
    // Card helpers
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// When true, cards are drawn using high-res PNG textures.
    /// When false, cards are drawn dynamically via CardRenderer (SGP + ImGui text).
    /// </summary>
#if __ANDROID__
    static bool UsePngCards = false;
#else
    static bool UsePngCards = true;
#endif

    static void DrawFaceCard(Card card, float x, float y, float w, float h)
    {
        if (UsePngCards)
            DrawTexSlot(CardToSlot(card.Suit, card.Type), x, y, w, h);
        else
            CardRenderer.DrawFaceCard(card, x, y, w, h);
    }

    static void DrawBackCard(float x, float y, float w, float h)
    {
        if (UsePngCards)
            DrawTexSlot(SLOT_BACKCARD, x, y, w, h);
        else
            CardRenderer.DrawBackCard(x, y, w, h);
    }

    static void DrawTexSlot(int slot, float x, float y, float w, float h)
    {
        var tex = _textures[slot];
        if (tex == null) return;
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(1f, 1f, 1f, 1f);
        sgp_set_view(0, _views[slot]);
        sgp_set_sampler(0, S.sampler);
        sgp_draw_textured_rect(0,
            new sgp_rect { x = x, y = y, w = w, h = h },
            FullSrcRect(tex));
        sgp_reset_view(0);
        sgp_reset_sampler(0);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    static void DrawCardPlaceholder(float x, float y, float w, float h)
    {
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(0.7f, 0.7f, 0.7f, 0.20f);
        sgp_draw_filled_rect(x, y, w, h);
        sgp_set_color(0.9f, 0.9f, 0.9f, 0.50f);
        sgp_draw_line(x,     y,     x + w, y    );
        sgp_draw_line(x,     y + h, x + w, y + h);
        sgp_draw_line(x,     y,     x,     y + h);
        sgp_draw_line(x + w, y,     x + w, y + h);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    static sgp_rect FullSrcRect(Texture tex) =>
        new sgp_rect { x = 0, y = 0, w = tex.Width, h = tex.Height };

    // ──────────────────────────────────────────────────────────
    // ImGui overlays — coordinates are in SCREEN pixels
    // All seat positions use the same tableW-based math as SGP
    // ──────────────────────────────────────────────────────────

    // Effective height for player seats: excludes the bottom action bar
    static float PlayH(int height) => height - MathF.Round(ACTION_H * _uiScale);

    static void SeatPosition(int i, int n, float tableW, int height,
                             out float px, out float py)
    {
        float playH = PlayH(height);
        float cx = tableW * 0.5f;
        float cy = playH * 0.45f;
        float rx = tableW * TABLE_RX_FRAC;
        float ry = playH  * TABLE_RY_FRAC;
        float angleRad = (90f + i * (360f / n)) * MathF.PI / 180f;
        px = cx + rx * MathF.Cos(angleRad);
        py = cy + ry * MathF.Sin(angleRad);
    }

    static void DrawPlayerOverlays(RenderSnapshot snap, float tableW, int height)
    {
        int n = snap.Players.Length;
        if (n == 0) return;

        bool isHandOver = snap.Phase == PokerPhase.HandOver;
        float labelW = MathF.Round(162f * _uiScale);

        for (int i = 0; i < n; i++)
        {
            var player = snap.Players[i];
            SeatPosition(i, n, tableW, height, out float px, out float py);

            // Winner needs 4 rows (name + stack + hand desc + WINS POT!);
            // other HandOver players need 3 rows; active play needs 2 rows.
            float labelH = isHandOver
                ? (player.IsHandWinner ? MathF.Round(96f * _uiScale) : MathF.Round(76f * _uiScale))
                : MathF.Round(62f * _uiScale);

            float halfCard = player.IsHuman ? CARD_H : CARD_H_SM;
            float labelY   = py + halfCard * 0.5f + 4f;
            float labelX   = Math.Clamp(px - labelW * 0.5f, 4f, tableW - labelW - 4f);

            igSetNextWindowPos(new Vector2(labelX, labelY), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(labelW, labelH), ImGuiCond.Always);
            // Winner gets a gold-tinted background; recently-acted seat gets a flash boost
            float baseAlpha  = (isHandOver && player.IsHandWinner) ? 0.72f : 0.42f;
            float flashBoost = (i == _actionFlashSeat && _actionFlashTimer > 0f) ? _actionFlashTimer * 0.22f : 0f;
            igSetNextWindowBgAlpha(MathF.Min(0.80f, baseAlpha + flashBoost));

            byte open = 1;
            if (igBegin($"##player{i}", ref open,
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs))
            {
                // Row 1: name + blind badge on same line
                string tag = player.IsCurrentTurn ? $">> {player.Name} <<" :
                             (isHandOver && player.IsHandWinner) ? $"*** {player.Name} ***" : player.Name;
                if (isHandOver && player.IsHandWinner)
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1.0f, 0.9f, 0.1f, 1f));
                igText(tag);
                if (isHandOver && player.IsHandWinner)
                    igPopStyleColor(1);

                // SB / BB badge
                if (i == snap.SmallBlindSeatIdx && snap.Phase != PokerPhase.Idle)
                {
                    igSameLine(0f, 4f);
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(0.3f, 1.0f, 0.4f, 1f));
                    igText("SB");
                    igPopStyleColor(1);
                }
                else if (i == snap.BigBlindSeatIdx && snap.Phase != PokerPhase.Idle)
                {
                    igSameLine(0f, 4f);
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1.0f, 0.72f, 0.1f, 1f));
                    igText("BB");
                    igPopStyleColor(1);
                }
                else if (i == snap.DealerSeatIndex && snap.Phase != PokerPhase.Idle)
                {
                    igSameLine(0f, 4f);
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(0.85f, 0.85f, 0.85f, 0.8f));
                    igText("D");
                    igPopStyleColor(1);
                }

                // Row 2: stack
                igText($"${player.Money}");

                // Row 3: during HandOver show hand description; otherwise last action
                if (isHandOver)
                {
                    if (player.HandDescription.Length > 0)
                    {
                        var hdColor = player.IsHandWinner
                            ? new Vector4(1.0f, 0.85f, 0.1f, 1f)
                            : new Vector4(0.7f, 0.85f, 1.0f, 1f);
                        igPushStyleColor_Vec4(ImGuiCol.Text, hdColor);
                        igText(player.HandDescription);
                        igPopStyleColor(1);
                        if (player.IsHandWinner)
                            igText(player.IsHuman ? "WIN POT!" : "WINS POT!");
                    }
                    else if (!player.IsInHand)
                    {
                        igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
                        igText("folded");
                        igPopStyleColor(1);
                    }
                }
                else if (player.IsAllIn && player.IsInHand)
                {
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1.0f, 0.55f, 0.1f, 1f));
                    igText("ALL-IN");
                    igPopStyleColor(1);
                }
                else if (player.LastAction.Length > 0)
                {
                    Vector4 color;
                    if (player.LastAction == "FOLD")
                        color = new Vector4(0.6f, 0.6f, 0.6f, 1f);
                    else if (player.LastAction.StartsWith("RAISE"))
                        color = new Vector4(1.0f, 0.85f, 0.2f, 1f);
                    else
                        color = new Vector4(0.4f, 1.0f, 0.4f, 1f);
                    igPushStyleColor_Vec4(ImGuiCol.Text, color);
                    igText(player.LastAction);
                    igPopStyleColor(1);
                }
                else if (!player.IsInHand && snap.Phase != PokerPhase.Idle)
                {
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(0.55f, 0.55f, 0.55f, 1f));
                    igText("waiting...");
                    igPopStyleColor(1);
                }
            }
            igEnd();
        }
    }

    static void DrawPotOverlay(RenderSnapshot snap, float tableW, int height)
    {
        if (snap.Phase == PokerPhase.Idle || snap.Players.Length == 0) return;

        float cx    = tableW * 0.5f;
        // Place the pot label just below the top seat's info box, regardless of card size
        float _potPlayH = PlayH(height);
        float cy    = _potPlayH * 0.45f - _potPlayH * TABLE_RY_FRAC  // top-seat center Y
                      + CARD_H_SM * 0.5f + 4f                          // below its card bottom
                      + MathF.Round(70f * _uiScale);                    // below its label box

        if (snap.Phase == PokerPhase.HandOver)
        {
            // Winner banner pinned to top of table area — well clear of the cards
            string winnerName = snap.HandWinnerName;
            string handDesc   = snap.Players.FirstOrDefault(p => p.Name == winnerName)?.HandDescription ?? "";
            bool   hasDesc    = handDesc.Length > 0;

            float bigFontSz   = MathF.Round(MathF.Min(26f * _uiScale, 96f));
            float subFontSz   = MathF.Round(MathF.Min(18f * _uiScale, 64f));
            float bannerW     = MathF.Round(370f * _uiScale);
            float bannerH     = hasDesc ? MathF.Round(72f * _uiScale) : MathF.Round(46f * _uiScale);
            float bannerY     = height * 0.04f;   // top of table, never overlaps cards

            igSetNextWindowPos(new Vector2(cx - bannerW * 0.5f, bannerY), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(bannerW, bannerH), ImGuiCond.Always);
            float winPulse = 0.88f + 0.10f * MathF.Abs(MathF.Sin(_globalTime * 2.5f));
            igSetNextWindowBgAlpha(winPulse);

            byte open = 1;
            if (igBegin("##pot", ref open,
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs))
            {
                // Big winner line
                igPushFont(igGetFont(), bigFontSz);
                igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1.0f, 0.88f, 0.05f, 1f));
                string winVerb = winnerName == "You" ? "win" : "wins";
                string winLine = snap.HandWinAmount > 0
                    ? $"\u2605  {winnerName} {winVerb} ${snap.HandWinAmount}!  \u2605"
                    : $"\u2605  {winnerName} {winVerb}!  \u2605";
                float tw = 0f;
                { Vector2 _sz = default; igCalcTextSize(ref _sz, winLine, null, false, -1f); tw = _sz.X; }
                igSetCursorPosX((bannerW - tw) * 0.5f);
                igText(winLine);
                igPopStyleColor(1);
                igPopFont();
                // Hand description
                if (hasDesc)
                {
                    igPushFont(igGetFont(), subFontSz);
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(0.35f, 1.0f, 0.5f, 1f));
                    string descLine = handDesc.ToUpper();
                    float tw2 = 0f;
                    { Vector2 _sz = default; igCalcTextSize(ref _sz, descLine, null, false, -1f); tw2 = _sz.X; }
                    igSetCursorPosX((bannerW - tw2) * 0.5f);
                    igText(descLine);
                    igPopStyleColor(1);
                    igPopFont();
                }
            }
            igEnd();
        }
        else
        {
            igSetNextWindowPos(new Vector2(cx - MathF.Round(72f * _uiScale), cy), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(MathF.Round(144f * _uiScale), MathF.Round(24f * _uiScale)), ImGuiCond.Always);
            igSetNextWindowBgAlpha(0.55f);

            byte open = 1;
            if (igBegin("##pot", ref open,
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs))
            {
                igText($"Pot: ${snap.Pot}");
            }
            igEnd();
        }
    }

    // ──────────────────────────────────────────────────────────
    // Settings gear button (upper-left corner)
    // ──────────────────────────────────────────────────────────
    static void DrawSettingsButton(int width, int height)
    {
        float btnSize = MathF.Round(36f * _uiScale);
        float margin  = MathF.Round(8f  * _uiScale);
        float btnX    = btnSize + margin;

        igSetNextWindowPos(new Vector2(btnX, margin), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(btnSize, btnSize), ImGuiCond.Always);
        igSetNextWindowBgAlpha(0f);

        byte open = 1;
        if (igBegin("##settingsBtn", ref open,
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar))
        {
            // Invisible button to capture hover/click
            igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0f, 0f, 0f, 0f));
            igPushStyleColor_Vec4(ImGuiCol.ButtonHovered, new Vector4(0f, 0f, 0f, 0f));
            igPushStyleColor_Vec4(ImGuiCol.ButtonActive,  new Vector4(0f, 0f, 0f, 0f));
            if (igButton("##gearHit", new Vector2(btnSize, btnSize)))
                _settingsOpen = !_settingsOpen;
            bool hovered = igIsItemHovered(0);
            bool active  = igIsItemActive();
            igPopStyleColor(3);

            // Draw gear onto the window draw list
            var dl    = igGetWindowDrawList();
            Vector2 wpos = default;
            igGetWindowPos(ref wpos);
            float cx  = wpos.X + btnSize * 0.5f;
            float cy  = wpos.Y + btnSize * 0.5f;
            float r   = btnSize * 0.36f;
            float rot = _globalTime * 0.6f + (_settingsOpen ? MathF.PI / 8f : 0f);

            float alpha = _settingsOpen ? 1.0f : (hovered ? 0.92f : 0.68f);
            uint colGear = igGetColorU32_Vec4(new Vector4(0.92f, 0.80f, 0.35f, alpha));
            uint colHole = igGetColorU32_Vec4(new Vector4(0.08f, 0.08f, 0.08f, alpha));
            uint colRing = igGetColorU32_Vec4(new Vector4(0.60f, 0.50f, 0.15f, alpha * 0.6f));

            // Glow when settings is open
            if (_settingsOpen || hovered)
            {
                float glow = active ? 0.30f : 0.18f;
                uint colGlow = igGetColorU32_Vec4(new Vector4(1f, 0.85f, 0.3f, glow));
                ImDrawList_AddCircleFilled(dl, new Vector2(cx, cy), r * 1.7f, colGlow, 32);
            }

            int teeth = 8;
            float toothOuter = r * 1.0f;
            float toothInner = r * 0.72f;
            float toothHalfAngle = MathF.PI / (teeth * 3.0f);

            // Build tooth polygon: alternating outer/inner verts
            Span<Vector2> pts = stackalloc Vector2[teeth * 4];
            for (int i = 0; i < teeth; i++)
            {
                float baseAngle = rot + i * (MathF.PI * 2f / teeth);
                float a0 = baseAngle - toothHalfAngle * 2f;
                float a1 = baseAngle - toothHalfAngle;
                float a2 = baseAngle + toothHalfAngle;
                float a3 = baseAngle + toothHalfAngle * 2f;
                pts[i * 4 + 0] = new Vector2(cx + MathF.Cos(a0) * toothInner, cy + MathF.Sin(a0) * toothInner);
                pts[i * 4 + 1] = new Vector2(cx + MathF.Cos(a1) * toothOuter, cy + MathF.Sin(a1) * toothOuter);
                pts[i * 4 + 2] = new Vector2(cx + MathF.Cos(a2) * toothOuter, cy + MathF.Sin(a2) * toothOuter);
                pts[i * 4 + 3] = new Vector2(cx + MathF.Cos(a3) * toothInner, cy + MathF.Sin(a3) * toothInner);
            }
            ImDrawList_AddConcavePolyFilled(dl, ref pts[0], teeth * 4, colGear);

            // Inner gear body disc
            ImDrawList_AddCircleFilled(dl, new Vector2(cx, cy), toothInner, colGear, 32);
            // Rim outline
            ImDrawList_AddCircle(dl, new Vector2(cx, cy), toothInner, colRing, 32, 1.0f);
            // Centre hole
            ImDrawList_AddCircleFilled(dl, new Vector2(cx, cy), r * 0.28f, colHole, 20);
        }
        igEnd();
    }

    // ──────────────────────────────────────────────────────────
    // Settings popup window
    // ──────────────────────────────────────────────────────────
    static void DrawSettingsWindow(RenderSnapshot snap, int height)
    {
        if (!_settingsOpen)
            return;

        // Auto-enter lobby when tournament ends so settings become editable again.
        if (snap.Phase == PokerPhase.GameOver)
            _inLobby = true;

        float popW = MathF.Round(280f * _uiScale);
        float closeH = MathF.Round(28f * _uiScale);   // height reserved for the Close button row
        float popH = Math.Min(MathF.Round(500f * _uiScale), height - 1f);

        igSetNextWindowPos(new Vector2(0f, 0f), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(popW, popH), ImGuiCond.Always);
        igSetNextWindowBgAlpha(0.95f);

        byte open = 1;
        if (igBegin("Settings##popup", ref open,
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            // Scrollable content area — leave room for the Close button at the bottom
            igBeginChild_Str("settings_scroll", new Vector2(0f, popH - closeH - 8f), ImGuiChildFlags.None, ImGuiWindowFlags.None);

            igTextWrapped(snap.StatusMessage);

            if (snap.Phase != PokerPhase.Idle && snap.HandNumber > 0)
            {
                igSeparator();
                igText($"Hand: #{snap.HandNumber}  |  BB: ${snap.SmallBlind * 2}");
                igText($"Round: {snap.RoundType}");
            }

            if (snap.LastActionDescription.Length > 0)
            {
                igSeparator();
                igTextWrapped($"Last: {snap.LastActionDescription}");
            }

            igSeparator();

            // Game Rules — editable in lobby only
            bool settingsLocked = !_inLobby;
            igBeginDisabled(settingsLocked);
            igText("Game Rules");
            igText("AI Players:");
            igSetNextItemWidth(-1f);
            igSliderInt("##numAI", ref _numAI, 1, 9, "%d", 0);
            igText("Starting Buy-in:");
            igSetNextItemWidth(-1f);
            igSliderInt("##buyIn", ref _buyIn, 100, 5000, "$%d", 0);
            int sbIdx = Math.Clamp(_sbLevel, 0, TexasHoldemGame.SmallBlinds.Length - 1);
            igText($"Small Blind: ${TexasHoldemGame.SmallBlinds[sbIdx]}  BB: ${TexasHoldemGame.SmallBlinds[sbIdx] * 2}");
            igSetNextItemWidth(-1f);
            igSliderInt("##sblvl", ref _sbLevel, 0, TexasHoldemGame.SmallBlinds.Length - 1, "", 0);
            byte escalB = _escalateBlinds ? (byte)1 : (byte)0;
            igCheckbox("Escalating Blinds", ref escalB);
            _escalateBlinds = escalB != 0;
            if (_escalateBlinds)
            {
                igText("Raise blind level every N hands:");
                igSetNextItemWidth(-1f);
                igSliderInt("##bp", ref _blindPeriod, 5, 50, "%d hands", 0);
            }
            // TBD ELI  , for development and debug purposes.
            // byte simB = _simMode ? (byte)1 : (byte)0;
            // igCheckbox("Simulation Mode (AI vs AI)", ref simB);
            // _simMode = simB != 0;
            // if (_simMode)
            // {
            //     igText("Number of hands to simulate:");
            //     igSetNextItemWidth(-1f);
            //     igSliderInt("##simhands", ref _simHands, 0, 10000, _simHands == 0 ? "Unlimited" : "%d hands", 0);
            //     if (_simHands < 0) _simHands = 0;
            // }
            igEndDisabled();

            igSeparator();
            igText("Action delay (ms between AI moves):");
            igSetNextItemWidth(-1f);
            igSliderInt("##delay", ref _actionDelayMs, 100, 3000, "%dms", 0);

            igSeparator();

            string newGameLabel = _inLobby ? "Start Game" : "New Game";
            if (igButton(newGameLabel, new Vector2(-1f, 0f)))
            {
                if (_inLobby)
                {
                    _raiseAmount = Math.Max(_buyIn / 20, 10);
                    _game.InitialSmallBlindIndex = _sbLevel;
                    _game.EscalateBlinds         = _escalateBlinds;
                    _game.BlindsHandPeriod       = _blindPeriod;
                    _game.SimulationMode         = _simMode;
                    _game.SimulationHands        = _simHands;
                    _gameLaunchedThisFrame = true;
                    _game.StartGame(_numAI, _buyIn);
                    _inLobby = false;
                    _settingsOpen = false;  // close settings when game starts
                }
                else
                {
                    _game.StopGame();
                    _inLobby = true;
                }
            }

            if (snap.Phase == PokerPhase.GameOver && snap.WinnerName.Length > 0)
            {
                igSeparator();
                string msg = snap.WinnerName == "You"
                    ? "YOU WIN THE TOURNAMENT!"
                    : $"{snap.WinnerName} wins the tournament!";
                igTextWrapped(msg);
            }

            // Asset loading progress
            int toLoad = 52 + 3;
            if (_loadedCount < toLoad)
            {
                igSeparator();
                igText($"Loading {_loadedCount}/{toLoad}...");
            }

            igEndChild();  // end scrollable region

            // Close button pinned at the bottom, always visible
            igSeparator();
            if (igButton("Close", new Vector2(-1f, 0f)))
                _settingsOpen = false;
        }
        if (open == 0)
            _settingsOpen = false;
        igEnd();
    }

    // ──────────────────────────────────────────────────────────
    // Deal animation helpers
    // ──────────────────────────────────────────────────────────

    // Center of the deck stack (natural dealer area near table center-top)
    static Vector2 DeckPos(float tableW, int height)
    {
        float playH = PlayH(height);
        return new Vector2(tableW * 0.5f, playH * 0.45f - 10f);
    }

    // Compute screen position + size for a deal slot
    static Vector2 GetDealSlotPos(RenderSnapshot snap, DealCard d, float tableW, int height,
                                  out float dstW, out float dstH)
    {
        if (d.PlayerIdx < 0)
        {
            // Community card
            float totalW = 5 * CARD_W + 2 * COMM_CARD_GAP + 2 * COMM_GROUP_GAP;
            float startX = tableW * 0.5f - totalW * 0.5f;
            float startY = height * COMM_Y_FRAC - CARD_H * 0.5f;
            float[] xPos = new float[5];
            xPos[0] = startX;
            xPos[1] = xPos[0] + CARD_W + COMM_CARD_GAP;
            xPos[2] = xPos[1] + CARD_W + COMM_CARD_GAP;
            xPos[3] = xPos[2] + CARD_W + COMM_GROUP_GAP;
            xPos[4] = xPos[3] + CARD_W + COMM_GROUP_GAP;
            dstW = CARD_W; dstH = CARD_H;
            return new Vector2(xPos[d.Slot], startY);
        }

        int n = snap.Players.Length;
        bool isHuman = snap.Players[d.PlayerIdx].IsHuman;
        SeatPosition(d.PlayerIdx, n, tableW, height, out float px, out float py);

        if (isHuman)
        {
            dstW = CARD_W; dstH = CARD_H;
            float x1 = px - CARD_W - 4f, y1 = py - CARD_H * 0.5f;
            float x2 = px + 4f;
            return d.Slot == 0 ? new Vector2(x1, y1) : new Vector2(x2, y1);
        }
        else
        {
            dstW = CARD_W_SM; dstH = CARD_H_SM;
            float x1 = px - CARD_W_SM - 3f, y1 = py - CARD_H_SM * 0.5f;
            float x2 = px + 3f;
            return d.Slot == 0 ? new Vector2(x1, y1) : new Vector2(x2, y1);
        }
    }

    // Returns true if the given hole-card slot has not yet arrived from the deck
    static bool IsDealSuppressed(int playerIdx, int slot)
    {
        for (int i = _dealCur; i < _dealCards.Count; i++)
        {
            var d = _dealCards[i];
            if (d.PlayerIdx == playerIdx && d.Slot == slot) return true;
        }
        return false;
    }

    // Returns true if the given community card slot has not yet arrived
    static bool IsCommDealSuppressed(int commIdx)
    {
        for (int i = _dealCur; i < _dealCards.Count; i++)
        {
            var d = _dealCards[i];
            if (d.PlayerIdx < 0 && d.Slot == commIdx) return true;
        }
        return false;
    }

    // Build deal sequence for hole cards (PreFlop): round1 then round2 to all active players
    static void StartDealHoleCards(RenderSnapshot snap)
    {
        _dealCards.Clear();
        _dealCur = 0;
        int n = snap.Players.Length;
        for (int i = 0; i < n; i++)
            if (snap.Players[i].IsInHand)
                _dealCards.Add(new DealCard { PlayerIdx = i, Slot = 0, T = 0f });
        for (int i = 0; i < n; i++)
            if (snap.Players[i].IsInHand)
                _dealCards.Add(new DealCard { PlayerIdx = i, Slot = 1, T = 0f });
        _dealActive = _dealCards.Count > 0;
        _dealCur = 0;
    }

    // Build deal sequence for community cards (Flop/Turn/River)
    static void StartDealCommunity(int startIdx, int count)
    {
        _dealCards.Clear();
        _dealCur = 0;
        for (int i = startIdx; i < startIdx + count; i++)
            _dealCards.Add(new DealCard { PlayerIdx = -1, Slot = i, T = 0f });
        _dealActive = _dealCards.Count > 0;
        _dealCur = 0;
    }

    // Draw the deck stack + the currently-flying card
    static void DrawDealAnims(RenderSnapshot snap, float tableW, int height)
    {
        if (_dealCards.Count == 0) return;

        // Deck stack — show while any cards remain to be dealt
        Vector2 deck = DeckPos(tableW, height);
        float deckHalfW = CARD_W_SM * 0.5f;
        float deckHalfH = CARD_H_SM * 0.5f;
        int   remaining = _dealCards.Count - _dealCur;
        if (remaining > 0)
        {
            // Draw 3 stacked back-cards with small offset to give depth
            for (int k = MathF.Min(remaining - 1, 2) >= 0 ? (int)MathF.Min(remaining - 1, 2) : 0; k >= 0; k--)
            {
                float offX = k * 1.5f;
                float offY = k * 1.0f;
                DrawBackCard(deck.X - deckHalfW + offX, deck.Y - deckHalfH + offY, CARD_W_SM, CARD_H_SM);
            }
        }

        // Currently-flying card
        if (!_dealActive || _dealCur >= _dealCards.Count) return;
        var cur = _dealCards[_dealCur];
        float t  = cur.T;
        float st = t * t * (3f - 2f * t);   // smoothstep

        Vector2 dst = GetDealSlotPos(snap, cur, tableW, height, out float dstW, out float dstH);
        float srcX = deck.X - deckHalfW;
        float srcY = deck.Y - deckHalfH;
        float x = srcX + (dst.X - srcX) * st;
        float y = srcY + (dst.Y - srcY) * st;
        float w = CARD_W_SM + (dstW - CARD_W_SM) * st;
        float h = CARD_H_SM + (dstH - CARD_H_SM) * st;
        DrawBackCard(x, y, w, h);
    }

    // Returns the top-left Y of the winning hand strip — always ABOVE the community cards.
    // Computed from current height so it tracks window resizes.
    static (float x, float y) WinStripOrigin(float tableW, int height)
    {
        const float GAP = 4f;
        float stripW = 5 * CARD_W + 4 * GAP;
        float stripX = tableW * 0.5f - stripW * 0.5f;
        float stripY = height * COMM_Y_FRAC - CARD_H * 0.5f - CARD_H - 14f;  // above community cards
        return (stripX, stripY);
    }

    // ──────────────────────────────────────────────────────────
    // Win animation: capture source positions of the 5 winning cards
    // ──────────────────────────────────────────────────────────
    static void StartWinAnimation(RenderSnapshot snap, float tableW, int height)
    {
        if (snap.WinningCards.Count != 5) return;

        // Only capture card identities — destinations are computed dynamically each frame.
        for (int i = 0; i < 5; i++)
            _winAnimCards[i] = snap.WinningCards[i];

        // —— Build source lookup (card → position + size) ——
        // Community card positions (must match DrawCommunityCards exactly)
        float totalW = 5 * CARD_W + 2 * COMM_CARD_GAP + 2 * COMM_GROUP_GAP;
        float commStartX = tableW * 0.5f - totalW * 0.5f;
        float commStartY = height * COMM_Y_FRAC - CARD_H * 0.5f;
        float[] commX = new float[5];
        commX[0] = commStartX;
        commX[1] = commX[0] + CARD_W + COMM_CARD_GAP;
        commX[2] = commX[1] + CARD_W + COMM_CARD_GAP;
        commX[3] = commX[2] + CARD_W + COMM_GROUP_GAP;
        commX[4] = commX[3] + CARD_W + COMM_GROUP_GAP;

        var srcList = new List<(Card card, Vector2 pos, Vector2 sz)>();
        for (int i = 0; i < snap.CommunityCards.Count && i < 5; i++)
            srcList.Add((snap.CommunityCards[i], new Vector2(commX[i], commStartY), new Vector2(CARD_W, CARD_H)));

        // Player hole card positions (must match SeatPosition / DrawHumanCards / DrawAICards)
        int n = snap.Players.Length;
        float playH = PlayH(height);
        float pcx = tableW * 0.5f;
        float pcy = playH  * 0.45f;
        float prx = tableW * TABLE_RX_FRAC;
        float pry = playH  * TABLE_RY_FRAC;
        for (int pi = 0; pi < n; pi++)
        {
            var player = snap.Players[pi];
            float rad = (90f + pi * (360f / n)) * MathF.PI / 180f;
            float px  = pcx + prx * MathF.Cos(rad);
            float py  = pcy + pry * MathF.Sin(rad);
            float cw  = player.IsHuman ? CARD_W    : CARD_W_SM;
            float ch  = player.IsHuman ? CARD_H    : CARD_H_SM;
            float x1  = player.IsHuman ? px - CARD_W - 4f    : px - CARD_W_SM - 3f;
            float x2  = player.IsHuman ? px + 4f              : px + 3f;
            float y1  = py - ch * 0.5f;
            if (player.Card1 != null) srcList.Add((player.Card1, new Vector2(x1, y1), new Vector2(cw, ch)));
            if (player.Card2 != null) srcList.Add((player.Card2, new Vector2(x2, y1), new Vector2(cw, ch)));
        }

        // Match each winning card to its source position (remove from pool to handle duplicates)
        var pool = new List<(Card card, Vector2 pos, Vector2 sz)>(srcList);
        for (int i = 0; i < 5; i++)
        {
            int idx = pool.FindIndex(e => e.card.Equals(_winAnimCards[i]));
            if (idx >= 0)
            {
                _winCardSrc[i]   = pool[idx].pos;
                _winCardSrcSz[i] = pool[idx].sz;
                pool.RemoveAt(idx);
            }
            else
            {
                // Fallback: start at destination (computed here at init time)
                const float FGAP = 4f;
                var (fx, fy) = WinStripOrigin(tableW, height);
                _winCardSrc[i]   = new Vector2(fx + i * (CARD_W + FGAP), fy);
                _winCardSrcSz[i] = new Vector2(CARD_W, CARD_H);
            }
        }
        _winAnimReady = true;
    }

    // ──────────────────────────────────────────────────────────
    // Winning hand strip — cards animate from their original positions to a central strip
    // ──────────────────────────────────────────────────────────
    static void DrawWinningHandStrip(RenderSnapshot snap, float tableW, int height)
    {
        if (snap.Phase != PokerPhase.HandOver || snap.WinningCards.Count != 5 || !_winAnimReady) return;

        // Smoothstep easing  t ∈ [0,1]
        float t  = _winAnimT;
        float st = t * t * (3f - 2f * t);

        // Destination is recomputed from current dimensions every frame — resize-stable.
        const float GAP = 4f;
        float stripW = 5 * CARD_W + 4 * GAP;
        var (stripX, stripY) = WinStripOrigin(tableW, height);

        // Gold glow behind destination — fades in as cards arrive
        float glowFill   = (0.28f + 0.10f * MathF.Abs(MathF.Sin(_globalTime * 2.8f))) * st;
        float glowBorder = 0.85f * st;
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(1.0f, 0.80f, 0.0f, glowFill);
        sgp_draw_filled_rect(stripX - 8f, stripY - 8f, stripW + 16f, CARD_H + 16f);
        sgp_set_color(1.0f, 0.95f, 0.1f, glowBorder);
        sgp_draw_line(stripX - 8f,          stripY - 8f,           stripX + stripW + 8f, stripY - 8f          );
        sgp_draw_line(stripX - 8f,          stripY + CARD_H + 8f,  stripX + stripW + 8f, stripY + CARD_H + 8f );
        sgp_draw_line(stripX - 8f,          stripY - 8f,           stripX - 8f,          stripY + CARD_H + 8f );
        sgp_draw_line(stripX + stripW + 8f, stripY - 8f,           stripX + stripW + 8f, stripY + CARD_H + 8f );
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();

        // Draw each card at its interpolated (animated) position and size
        for (int i = 0; i < 5; i++)
        {
            if (_winAnimCards[i] == null) continue;
            float dstX = stripX + i * (CARD_W + GAP);
            float dstY = stripY;
            float x  = _winCardSrc[i].X  + (dstX - _winCardSrc[i].X)  * st;
            float y  = _winCardSrc[i].Y  + (dstY - _winCardSrc[i].Y)  * st;
            float sw = _winCardSrcSz[i].X + (CARD_W - _winCardSrcSz[i].X) * st;
            float sh = _winCardSrcSz[i].Y + (CARD_H - _winCardSrcSz[i].Y) * st;
            DrawFaceCard(_winAnimCards[i], x, y, sw, sh);
        }
    }

    static void DrawActionBar(RenderSnapshot snap, float tableW, int height)
    {
        bool isHumanTurn = snap.Phase == PokerPhase.WaitingForHuman;
        bool isHandOver  = snap.Phase == PokerPhase.HandOver;
        // Always use the full ACTION_H so the bar is flush with the SGP scissor boundary
        // (PlayH clips the table at height - ACTION_H*scale; bar must fill that exact gap).
        float barH = MathF.Round(ACTION_H * _uiScale);
        float barY = (float)height - barH;

        igSetNextWindowPos(new Vector2(0f, barY), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(tableW, barH), ImGuiCond.Always);
        igSetNextWindowBgAlpha(isHumanTurn ? 0.92f : 0.78f);

        byte open = 1;
        if (igBegin("##action", ref open,
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav))
        {
            float textLineH = igGetTextLineHeight();
            float spacingY  = igGetTextLineHeightWithSpacing() - textLineH;
            float BTN_H     = MathF.Round(36f * _uiScale);

            if (isHumanTurn)
            {
                // ── Vertical centering ────────────────────────
                // Content: status text + igSpacing + button row
                float contentH = (textLineH + spacingY) + spacingY + BTN_H;
                igSetCursorPosY(MathF.Max(4f, (barH - contentH) * 0.5f));

                // ── Status row — horizontally centered ────────
                string statusTxt = $"YOUR TURN  |  {snap.RoundType}  |  Pot: ${snap.Pot}  |  Stack: ${snap.HumanMoneyLeft}";
                { Vector2 _sz = default; igCalcTextSize(ref _sz, statusTxt, null, false, -1f);
                  igSetCursorPosX(MathF.Max(4f, (tableW - _sz.X) * 0.5f)); }
                igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1.0f, 0.95f, 0.4f, 1f));
                igText(statusTxt);
                igPopStyleColor(1);

                igSpacing();

                // ── Button row — compute total width then center ──
                float foldW  = MathF.Round(110f * _uiScale);
                float callW  = MathF.Round(150f * _uiScale);
                float gap    = MathF.Round(10f  * _uiScale);
                float allInW = MathF.Round(110f * _uiScale);
                float raiseBlockW = snap.CanRaise
                    ? MathF.Round(140f * _uiScale) + MathF.Round(8f * _uiScale) + MathF.Round(160f * _uiScale) + gap
                    : MathF.Round(110f * _uiScale) + gap;
                float totalBtnW = foldW + gap + callW + gap + raiseBlockW + allInW;
                igSetCursorPosX(MathF.Max(4f, (tableW - totalBtnW) * 0.5f));

                // FOLD button (red tint)
                igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.65f, 0.12f, 0.12f, 1f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonHovered,  new Vector4(0.85f, 0.20f, 0.20f, 1f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonActive,   new Vector4(0.50f, 0.08f, 0.08f, 1f));
                if (igButton("Fold  [F]", new Vector2(foldW, BTN_H)))
                    _game.SubmitFold();
                igPopStyleColor(3);

                igSameLine(0, gap);

                // CHECK / CALL button (green/blue tint)
                string callLabel = snap.CanCheck ? "Check  [C]" : $"Call ${snap.MoneyToCall}  [C]";
                igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.12f, 0.50f, 0.20f, 1f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonHovered,  new Vector4(0.18f, 0.70f, 0.28f, 1f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonActive,   new Vector4(0.08f, 0.38f, 0.15f, 1f));
                if (igButton(callLabel, new Vector2(callW, BTN_H)))
                    _game.SubmitCall();
                igPopStyleColor(3);

                igSameLine(0, gap);

                // RAISE slider + button (yellow tint)
                if (snap.CanRaise)
                {
                    int minR = snap.MinRaise;
                    int maxR = Math.Max(snap.MaxRaise, minR);
                    if (_raiseAmount < minR) _raiseAmount = minR;
                    if (_raiseAmount > maxR) _raiseAmount = maxR;

                    igSetNextItemWidth(MathF.Round(140f * _uiScale));
                    igSliderInt("##raise", ref _raiseAmount, minR, maxR, "$%d", 0);
                    igSameLine(0, MathF.Round(8f * _uiScale));

                    igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.60f, 0.45f, 0.05f, 1f));
                    igPushStyleColor_Vec4(ImGuiCol.ButtonHovered,  new Vector4(0.80f, 0.65f, 0.10f, 1f));
                    igPushStyleColor_Vec4(ImGuiCol.ButtonActive,   new Vector4(0.45f, 0.33f, 0.03f, 1f));
                    if (igButton($"Raise ${_raiseAmount}  [R]", new Vector2(MathF.Round(160f * _uiScale), BTN_H)))
                        _game.SubmitRaise(_raiseAmount);
                    igPopStyleColor(3);
                    igSameLine(0, gap);
                }
                else
                {
                    igBeginDisabled(true);
                    igButton("Raise N/A", new Vector2(MathF.Round(110f * _uiScale), BTN_H));
                    igEndDisabled();
                    igSameLine(0, gap);
                }
                // ALL-IN button (purple tint) — always available when chips remain
                if (snap.HumanMoneyLeft > 0)
                {
                    igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.50f, 0.10f, 0.50f, 1f));
                    igPushStyleColor_Vec4(ImGuiCol.ButtonHovered,  new Vector4(0.70f, 0.18f, 0.70f, 1f));
                    igPushStyleColor_Vec4(ImGuiCol.ButtonActive,   new Vector4(0.38f, 0.06f, 0.38f, 1f));
                    if (igButton("ALL-IN  [A]", new Vector2(allInW, BTN_H)))
                        _game.SubmitRaise(snap.HumanMoneyLeft);
                    igPopStyleColor(3);
                }
            }
            else if (snap.Phase == PokerPhase.Idle)
            {
                string txt = _inLobby ? "Configure settings and press \"Start Game\" to begin."
                                      : "Press \"New Game\" in the panel to start.";
                float contentH = textLineH;
                igSetCursorPosY(MathF.Max(4f, (barH - contentH) * 0.5f));
                { Vector2 _sz = default; igCalcTextSize(ref _sz, txt, null, false, -1f);
                  igSetCursorPosX(MathF.Max(4f, (tableW - _sz.X) * 0.5f)); }
                igText(txt);
            }
            else if (snap.Phase == PokerPhase.AITurn)
            {
                string txt = $"AI thinking...  |  {snap.RoundType}  |  Pot: ${snap.Pot}";
                float contentH = textLineH;
                igSetCursorPosY(MathF.Max(4f, (barH - contentH) * 0.5f));
                { Vector2 _sz = default; igCalcTextSize(ref _sz, txt, null, false, -1f);
                  igSetCursorPosX(MathF.Max(4f, (tableW - _sz.X) * 0.5f)); }
                igText(txt);
            }
            else if (isHandOver)
            {
                float bigFontSz = MathF.Round(MathF.Min(22f * _uiScale, 40f));
                float bigLineH  = bigFontSz;
                float contentH  = bigLineH + spacingY + BTN_H;
                igSetCursorPosY(MathF.Max(4f, (barH - contentH) * 0.5f));

                if (snap.HandWinnerName.Length > 0)
                {
                    igPushFont(igGetFont(), bigFontSz);
                    igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1.0f, 0.92f, 0.1f, 1f));
                    string winMsg = snap.HandWinnerName == "You"
                        ? $"\u2605 YOU WIN the pot (${snap.HandWinAmount})! \u2605"
                        : $"\u2605 {snap.HandWinnerName} wins the pot (${snap.HandWinAmount})! \u2605";
                    { Vector2 _sz = default; igCalcTextSize(ref _sz, winMsg, null, false, -1f);
                      igSetCursorPosX(MathF.Max(4f, (tableW - _sz.X) * 0.5f)); }
                    igText(winMsg);
                    igPopStyleColor(1);
                    igPopFont();
                }
                igSpacing();

                float BTN_W = MathF.Round(200f * _uiScale);
                igSetCursorPosX(MathF.Max(4f, (tableW - BTN_W) * 0.5f));
                igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.05f, 0.45f, 0.45f, 1f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonHovered,  new Vector4(0.10f, 0.65f, 0.65f, 1f));
                igPushStyleColor_Vec4(ImGuiCol.ButtonActive,   new Vector4(0.03f, 0.32f, 0.32f, 1f));
                igPushFont(igGetFont(), MathF.Round(MathF.Min(18f * _uiScale, 32f)));
                if (igButton("Continue  [Space]", new Vector2(BTN_W, BTN_H)))
                    _game.SubmitContinue();
                igPopFont();
                igPopStyleColor(3);
            }
            else
            {
                string txt = snap.StatusMessage;
                igSetCursorPosY(MathF.Max(4f, (barH - textLineH) * 0.5f));
                { Vector2 _sz = default; igCalcTextSize(ref _sz, txt, null, false, -1f);
                  igSetCursorPosX(MathF.Max(4f, (tableW - _sz.X) * 0.5f)); }
                igText(txt);
            }
        }
        igEnd();
    }

    // ──────────────────────────────────────────────────────────
    // Event
    // ──────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    private static void Event(sapp_event* e)
    {
        // Sokol always delivers event positions in logical points regardless of high_dpi.
        // simgui_handle_event uses the dpi_scale from simgui_setup (default 1.0), so it
        // expects logical coords — pass events directly without any scaling.
        simgui_handle_event(in *e);

        if (e->type == sapp_event_type.SAPP_EVENTTYPE_KEY_DOWN)
        {
            var snap = _game.Snapshot;
            if (snap.Phase == PokerPhase.WaitingForHuman)
            {
                if (e->key_code == sapp_keycode.SAPP_KEYCODE_F)
                    _game.SubmitFold();
                else if (e->key_code == sapp_keycode.SAPP_KEYCODE_C)
                    _game.SubmitCall();
                else if (e->key_code == sapp_keycode.SAPP_KEYCODE_R && snap.CanRaise)
                    _game.SubmitRaise(_raiseAmount);
                else if (e->key_code == sapp_keycode.SAPP_KEYCODE_A && snap.HumanMoneyLeft > 0)
                    _game.SubmitRaise(snap.HumanMoneyLeft);
            }
            else if (snap.Phase == PokerPhase.HandOver)
            {
                if (e->key_code == sapp_keycode.SAPP_KEYCODE_SPACE)
                    _game.SubmitContinue();
            }
        }
        
        if(e->type == sapp_event_type.SAPP_EVENTTYPE_RESIZED)
        {
           AdjustTableDimensions();
        }
    }

    // ──────────────────────────────────────────────────────────
    // Cleanup
    // ──────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        // Signal the game thread to stop but don't wait for it — the thread is
        // IsBackground so the OS kills it when the process exits below.
        _game.StopGame();

        for (int i = 0; i < TOTAL_SLOTS; i++)
        {
            _textures[i]?.Dispose();
            _textures[i] = null;
        }
        _tableTexture?.Dispose();
        _tableTexture = null;

        simgui_shutdown();
        sgp_shutdown();
        sg_shutdown();

        if (Debugger.IsAttached)
        {
            Environment.Exit(0);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Entry point
    // ──────────────────────────────────────────────────────────
    public static sapp_desc sokol_main()
    {
        var desc = new sapp_desc
        {
            init_cb      = &Init,
            frame_cb     = &Frame,
            event_cb     = &Event,
            cleanup_cb   = &Cleanup,
            high_dpi = true,
            width        = 1280,
            height       = 720,
            window_title = "Texas Hold''em",
            sample_count = 1,
            icon         = { sokol_default = true },
            logger       = { func = &slog_func },
        };
  
        return desc;
    }

    // ──────────────────────────────────────────────────────────
    // Asset helpers
    // ──────────────────────────────────────────────────────────

    static void LoadSlot(int slot, string path, string label)
    {
        FileSystem.Instance.LoadFile(path, (_, data, status) =>
        {
            if (status == FileLoadStatus.Success && data != null)
            {
                var tex = Texture.LoadFromMemory(data, label);
                if (tex != null)
                {
                    _textures[slot] = tex;
                    _views[slot]    = sgp_make_texture_view_from_image(tex.Image, label);
                    _loaded[slot]   = true;
                    _loadedCount++;
                }
            }
        });
    }

    // Load PokerFont.ttf via FileSystem (works on desktop, mobile, and web).
    // The font covers Basic Latin + Miscellaneous Symbols (♠♣♥♦★).
    static unsafe void LoadImGuiFonts()
    {
        FileSystem.Instance.LoadFile("fonts/PokerFont.ttf", (_, data, status) =>
        {
            if (status != FileLoadStatus.Success || data == null) return;

            // Pin managed arrays so GC cannot move them while ImGui holds the pointers.
            // ImGui reads font_data and GlyphRanges lazily on the first simgui_new_frame.
            _fontDataPin    = GCHandle.Alloc(data,               GCHandleType.Pinned);
            _glyphRangesPin = GCHandle.Alloc(_pokerGlyphRanges,  GCHandleType.Pinned);

            var io        = igGetIO_Nil();
            void*   fontPtr   = (void*)_fontDataPin.AddrOfPinnedObject();
            ushort* rangesPtr = (ushort*)_glyphRangesPin.AddrOfPinnedObject();

            var cfg = new ImFontConfig
            {
                OversampleH          = 2,
                OversampleV          = 1,
                MergeMode            = 1,              // merge symbol glyphs into ProggyClean (font[0])
                RasterizerMultiply   = 1.0f,
                RasterizerDensity    = 1.0f,
                FontDataOwnedByAtlas = 0,              // we own the memory; don't let ImGui IM_FREE it
                GlyphMaxAdvanceX     = float.MaxValue, // C++ ImFontConfig() default; 0 clamps all advances to zero
            };

          ImFont* font =   ImFontAtlas_AddFontFromMemoryTTF(
                io->Fonts, fontPtr, data.Length, 16f, &cfg, ref *rangesPtr);
        });
    }

    // ──────────────────────────────────────────────────────────
    // Card ↔ slot mapping
    // ──────────────────────────────────────────────────────────

    // Suits: Club=0, Diamond=1, Heart=2, Spade=3
    // Types: Two=2 .. Ace=14  →  offset = type-2
    static int CardToSlot(CardSuit suit, CardType type) =>
        (int)suit * 13 + ((int)type - 2);

    static string CardTypeName(CardType t) => t switch
    {
        CardType.Two   => "2",
        CardType.Three => "3",
        CardType.Four  => "4",
        CardType.Five  => "5",
        CardType.Six   => "6",
        CardType.Seven => "7",
        CardType.Eight => "8",
        CardType.Nine  => "9",
        CardType.Ten   => "10",
        CardType.Jack  => "jack",
        CardType.Queen => "queen",
        CardType.King  => "king",
        CardType.Ace   => "ace",
        _ => t.ToString().ToLower()
    };

    static string SuitName(CardSuit s) => s switch
    {
        CardSuit.Club    => "clubs",
        CardSuit.Diamond => "diamonds",
        CardSuit.Heart   => "hearts",
        CardSuit.Spade   => "spades",
        _ => s.ToString().ToLower()
    };
}
