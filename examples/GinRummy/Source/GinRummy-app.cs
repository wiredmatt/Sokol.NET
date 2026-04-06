// GinRummy-app.cs — Gin Rummy
// 2D rendering via sokol_gp; ImGui for UI; GinRummyGame for game logic.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Linq;
using Sokol;
using Imgui;
using Rummy.Logic.Cards;
using Rummy.Logic.Melds;
using Rummy.Logic.GinRummy;
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

public static unsafe class GinrummyApp
{
    // ──────────────────────────────────────────────────────────
    // Layout constants (recalculated each frame from screen size)
    // ──────────────────────────────────────────────────────────
    static float CARD_W   = 78f;
    static float CARD_H   = 108f;
    static float CARD_GAP = 6f;
    static float ACTION_H = 120f;
    const  float PILE_GAP = 80f;

    static readonly sg_color CLR_FELT = new() { r = 0.07f, g = 0.38f, b = 0.14f, a = 1f };

    // ──────────────────────────────────────────────────────────
    // App state
    // ──────────────────────────────────────────────────────────
    struct AppState
    {
        public sg_pass_action passAction;
        public sg_sampler     sampler;
    }
    static AppState S;

    static GinRummyGame? _game;
    static int           _selectedCard  = -1;
    static GinPhase      _prevGinPhase  = GinPhase.HumanDraw;
    static bool          _inMainMenu   = true;
    static int           _aiChoice     = 0;     // 0=Strategist 1=Conservator 2=Casual
    static float         _globalTime   = 0f;

    // Table texture
    static Texture?  _tableTexture;
    static sg_view   _tableView;
    static bool      _tableLoaded;

    // PNG card textures (52 face cards + 1 back = 53 slots)
    const int SLOT_BACKCARD = 52;
    const int TOTAL_SLOTS   = 53;
    static readonly Texture?[] _textures    = new Texture?[TOTAL_SLOTS];
    static readonly sg_view[]  _cardViews   = new sg_view[TOTAL_SLOTS];
    static readonly bool[]     _cardLoaded  = new bool[TOTAL_SLOTS];
    static int                 _cardLoadedCount;

    // Font pinning — must outlive process
    static GCHandle          _fontDataPin;
    static GCHandle          _glyphRangesPin;
    static readonly ushort[] _glyphRanges = { 0x0020, 0x00FF, 0x2600, 0x26FF, 0 };

    static float _uiScale = 1f;

    // ── Hand display order (display slot → actual hand index) ─────────────────
    static int[] _ginHandOrder = Array.Empty<int>();

    // ── Round-over sort animation (human hand slides into meld-sorted order) ──
    // Maps card reference → display-slot index just before round-over transition
    static Dictionary<Rummy.Logic.Cards.Card, int>? _preRoundOverSlot = null;
    // Per new-display-slot X offset that eases to zero during the animation
    static float[] _sortCardOffsets = Array.Empty<float>();
    static float   _sortAnimStart   = float.MinValue;
    const  float   SORT_DUR         = 0.45f;
    static bool    _wasRoundOver    = false;

    // ── Drag-to-reorder ───────────────────────────────────────────────────────
    static int   _dragDisplayIdx = -1;
    static float _dragX, _dragY;
    static float _dragStartX, _dragStartY;

    // ── Game log ──────────────────────────────────────────────────────────────
    static readonly List<string> _gameLog    = new(500);
    static bool                  _showLog    = false;
    static string                _logFile    = "";

    // ── AI action toast (on-screen banner) ───────────────────────────────────
    static string  _aiToastText  = "";
    static float   _aiToastTime  = -999f;  // _globalTime when toast was set

    // ── Flying card animation ─────────────────────────────────────────────────
    struct FlyAnim
    {
        public Rummy.Logic.Cards.Card Card;
        public float Sx, Sy, Ex, Ey;   // start/end positions
        public float T0, Dur;           // start globalTime and duration
        public bool  FaceUp;
        public bool  FlipAtEnd;         // flip from back→face in the last portion
        public bool  SuppressDest;      // hide the destination hand slot while card is flying in
        // Board state changes applied at DEPARTURE (when animation starts):
        public bool                    DepartDiscardApply;   // apply discard-pile change the moment card leaves it
        public Rummy.Logic.Cards.Card? DepartDiscardNewTop;  // new TopDiscard after leaving (null = pile becomes empty)
        public bool                    DepartStock;          // StockCount -= 1 the moment card leaves stock
        // Board state changes applied at ARRIVAL (when animation lands):
        public Rummy.Logic.Cards.Card? LandDiscard;    // becomes TopDiscard when card arrives at pile
    }
    static FlyAnim? _fly;
    static readonly Queue<FlyAnim> _flyQueue = new();
    static bool _suppressLastDrawn = false; // hide drawn card in hand while fly anim plays
    static (float X, float Y) _pendingDiscardSrc; // screen slot position recorded at discard click time

    // ── Display snapshot (frozen while animations are queued/playing) ─────────
    // SGP draw calls use this so the board doesn't jump ahead while cards fly.
    // Updated only once the fly queue and active fly are both empty.
    static GinSnapshot? _displaySnap;

    // ──────────────────────────────────────────────────────────
    // Init
    // ──────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Init()
    {
        sg_setup(new sg_desc
        {
            environment = sglue_environment(),
            logger      = { func = &slog_func },
        });
        sgp_setup(new sgp_desc());
        try
        {
            _logFile = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
                "gin_rummy_log.txt");
            System.IO.File.WriteAllText(_logFile,
                $"=== Gin Rummy Log {System.DateTime.Now} ==={System.Environment.NewLine}");
        }
        catch { _logFile = ""; }
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
        S.passAction.colors[0].clear_value = new sg_color { r = 0.06f, g = 0.08f, b = 0.06f, a = 1f };

        FileSystem.Instance.Initialize();
        LoadFonts();
        AdjustCardSizes(sapp_width() / sapp_dpi_scale(), sapp_height() / sapp_dpi_scale());

        FileSystem.Instance.LoadFile("table.png", (path, data, status) =>
        {
            if (status == FileLoadStatus.Success && data != null)
            {
                _tableTexture = Texture.LoadFromMemory(data, "table");
                if (_tableTexture != null)
                {
                    _tableView   = sgp_make_texture_view_from_image(_tableTexture.Image, "table");
                    _tableLoaded = true;
                }
            }
        });

        // Back card
        LoadCardSlot(SLOT_BACKCARD, "cards/BackCard.png", "BackCard");

        // 52 face cards
        foreach (CardSuit suit in Enum.GetValues<CardSuit>())
            foreach (CardType type in Enum.GetValues<CardType>())
            {
                if (suit == CardSuit.Joker || type == CardType.Joker) continue;
                int    slot     = CardToSlot(suit, type);
                string filename = $"cards/{CardTypeName(type)}_of_{SuitName(suit)}.png";
                LoadCardSlot(slot, filename, filename);
            }
    }

    static void LoadFonts()
    {
        FileSystem.Instance.LoadFile("fonts/PokerFont.ttf", (path, data, status) =>
        {
            if (status != FileLoadStatus.Success || data == null) return;

            _fontDataPin    = GCHandle.Alloc(data, GCHandleType.Pinned);
            _glyphRangesPin = GCHandle.Alloc(_glyphRanges, GCHandleType.Pinned);

            var    io     = igGetIO_Nil();
            ushort* range = (ushort*)_glyphRangesPin.AddrOfPinnedObject();

            var cfg = new ImFontConfig
            {
                OversampleH          = 2,
                OversampleV          = 1,
                MergeMode            = 1,
                RasterizerMultiply   = 1.0f,
                RasterizerDensity    = 1.0f,
                FontDataOwnedByAtlas = 0,
                GlyphMaxAdvanceX     = float.MaxValue,
            };

            ImFontAtlas_AddFontFromMemoryTTF(
                io->Fonts,
                (void*)_fontDataPin.AddrOfPinnedObject(),
                data.Length,
                18f,
                &cfg,
                ref *range);
        });
    }

    // ──────────────────────────────────────────────────────────
    // Frame
    // ──────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Frame()
    {
        FileSystem.Instance.Update();

        int   fbW = sapp_width();
        int   fbH = sapp_height();
        float dpi = sapp_dpi_scale();
        float w   = MathF.Round(fbW / dpi);
        float h   = MathF.Round(fbH / dpi);
        float dt  = (float)sapp_frame_duration();

#if __ANDROID__
        _uiScale = Math.Clamp(h / 720f, 1.5f, 5f);
#else
        _uiScale = Math.Clamp(h / 720f, 1.0f, 4f);
#endif

        _globalTime += dt;
        AdjustCardSizes(w, h);

        var snap = _game?.Snapshot;

        // ── Drain game events → log + toast + animation ──────────────────────
        if (_game != null)
        {
            while (_game.TryDequeueEvent(out var ev))
            {
                var stamp = $"[{System.DateTime.Now:HH:mm:ss}] {ev.Label}";
                _gameLog.Add(stamp);
                if (_gameLog.Count > 500) _gameLog.RemoveAt(0);
                try { System.IO.File.AppendAllText(_logFile, stamp + System.Environment.NewLine); } catch { }
                bool isAiMsg = ev.Type is GinEventType.AIDrewStock or GinEventType.AITookDiscard
                            or GinEventType.AIDiscarded or GinEventType.AIKnocked or GinEventType.AIGin;
                bool isResult = ev.Type is GinEventType.RoundResult or GinEventType.GameResult;
                if (isResult)
                    SLog.Warning(ev.Label, "GinRummy");
                else if (isAiMsg)
                    SLog.Info(ev.Label, "GinRummy AI");
                else
                    SLog.Info(ev.Label, "GinRummy");

                bool isAIEvent = ev.Type switch {
                    Rummy.Logic.GinRummy.GinEventType.AIDrewStock    => true,
                    Rummy.Logic.GinRummy.GinEventType.AITookDiscard  => true,
                    Rummy.Logic.GinRummy.GinEventType.AIDiscarded    => true,
                    Rummy.Logic.GinRummy.GinEventType.AIKnocked      => true,
                    Rummy.Logic.GinRummy.GinEventType.AIGin          => true,
                    _ => false };
                if (isAIEvent) { _aiToastText = ev.Label; _aiToastTime = _globalTime; }

                float dpiE  = sapp_dpi_scale();
                float wE    = MathF.Round(sapp_width()  / dpiE);
                float hE    = MathF.Round(sapp_height() / dpiE);
                float hudHE = 36f * _uiScale;
                float pileY = hE * 0.5f - CARD_H * 0.5f;
                float aiY   = hudHE + (pileY - hudHE - CARD_H) * 0.25f;
                float aiCX  = wE * 0.5f;
                float stLx  = wE * 0.5f - CARD_W - PILE_GAP * 0.5f;
                float diRx  = wE * 0.5f + PILE_GAP * 0.5f;
                float pcy   = hE * 0.5f - CARD_H * 0.5f;
                switch (ev.Type)
                {
                    case Rummy.Logic.GinRummy.GinEventType.AIDrewStock:
                        _flyQueue.Enqueue(new FlyAnim { Card = ev.Card ?? default, Sx = stLx,  Sy = pcy,  Ex = aiCX, Ey = aiY, T0 = 0f, Dur = 0.55f, FaceUp = false, DepartStock = true }); break;
                    case Rummy.Logic.GinRummy.GinEventType.AITookDiscard:
                        _flyQueue.Enqueue(new FlyAnim { Card = ev.Card ?? default, Sx = diRx,  Sy = pcy,  Ex = aiCX, Ey = aiY, T0 = 0f, Dur = 0.55f, FaceUp = true,  DepartDiscardApply = true, DepartDiscardNewTop = ev.NextDiscard }); break;
                    case Rummy.Logic.GinRummy.GinEventType.AIDiscarded:
                        _flyQueue.Enqueue(new FlyAnim { Card = ev.Card ?? default, Sx = aiCX,  Sy = aiY,  Ex = diRx, Ey = pcy, T0 = 0f, Dur = 0.55f, FaceUp = true,  LandDiscard = ev.Card }); break;
                    case Rummy.Logic.GinRummy.GinEventType.HumanDrewStock:
                        { float humanCY = hE - ACTION_H - CARD_H - 12f;
                          int   drawN    = 11;
                          float drawTotW = drawN * CARD_W + (drawN - 1) * CARD_GAP;
                          float drawEndX = wE * 0.5f - drawTotW * 0.5f + (drawN - 1) * (CARD_W + CARD_GAP);
                          _flyQueue.Enqueue(new FlyAnim { Card = ev.Card ?? default, Sx = stLx, Sy = pcy, Ex = drawEndX, Ey = humanCY, T0 = 0f, Dur = 0.6f, FaceUp = false, FlipAtEnd = true, SuppressDest = true, DepartStock = true }); break; }
                    case Rummy.Logic.GinRummy.GinEventType.HumanTookDiscard:
                        { float humanCY = hE - ACTION_H - CARD_H - 12f;
                          int   takeN    = 11;
                          float takeTotW = takeN * CARD_W + (takeN - 1) * CARD_GAP;
                          float takeEndX = wE * 0.5f - takeTotW * 0.5f + (takeN - 1) * (CARD_W + CARD_GAP);
                          _flyQueue.Enqueue(new FlyAnim { Card = ev.Card ?? default, Sx = diRx, Sy = pcy, Ex = takeEndX, Ey = humanCY, T0 = 0f, Dur = 0.5f, FaceUp = true, SuppressDest = true, DepartDiscardApply = true, DepartDiscardNewTop = ev.NextDiscard }); break; }
                    case Rummy.Logic.GinRummy.GinEventType.HumanDiscarded:
                        { float humanCY = hE - ACTION_H - CARD_H - 12f;
                          _flyQueue.Enqueue(new FlyAnim { Card = ev.Card ?? default, Sx = _pendingDiscardSrc.X, Sy = _pendingDiscardSrc.Y, Ex = diRx, Ey = pcy, T0 = 0f, Dur = 0.5f, FaceUp = true, LandDiscard = ev.Card }); break; }
                }
            }
        }

        if (_fly == null && _flyQueue.Count == 0 && _game != null)
            _displaySnap = _game.Snapshot;
        if (_displaySnap == null && _game != null)
            _displaySnap = _game.Snapshot;

        // ── SGP geometry pass ─────────────────────────────────
        sgp_begin(fbW, fbH);
        sgp_viewport(0, 0, fbW, fbH);
        sgp_project(0f, w, 0f, h);

        DrawTableBg(w, h);

        if (!_inMainMenu && _displaySnap != null)
        {
            DrawAIHand(_displaySnap, w, h);
            DrawPiles(_displaySnap, w, h);
            if (snap != null) DrawHumanHand(snap, w, h);
        }

        // ── Flying card animation ──────────────────────────────
        if (!_fly.HasValue && _flyQueue.Count > 0)
        {
            var n = _flyQueue.Dequeue();
            _fly = n with { T0 = _globalTime };
            _suppressLastDrawn = n.SuppressDest;
            if (_displaySnap != null)
            {
                if (n.DepartDiscardApply)
                {
                    _displaySnap.TopDiscard  = n.DepartDiscardNewTop;
                    _displaySnap.DiscardEmpty = n.DepartDiscardNewTop == null;
                }
                if (n.DepartStock)
                    _displaySnap.StockCount = Math.Max(0, _displaySnap.StockCount - 1);
            }
        }
        if (_fly.HasValue)
        {
            var f  = _fly.Value;
            float t = MathF.Min(1f, (_globalTime - f.T0) / f.Dur);
            float s = t < 0.5f ? 4f*t*t*t : 1f - MathF.Pow(-2f*t+2f, 3f)/2f;
            float arc = MathF.Sin(s * MathF.PI) * 30f;
            float fx  = f.Sx + (f.Ex - f.Sx) * s;
            float fy  = f.Sy + (f.Ey - f.Sy) * s - arc;
            float sc  = 1f + 0.12f * MathF.Sin(s * MathF.PI);
            float cw  = CARD_W  * sc;
            float ch  = CARD_H  * sc;
            float ox  = fx - (cw - CARD_W) * 0.5f;
            float oy  = fy - (ch - CARD_H) * 0.5f;
            float flipProgress = f.FlipAtEnd ? MathF.Max(0f, (t - 0.75f) / 0.25f) : 0f;
            bool  showFaceUp   = f.FaceUp || (f.FlipAtEnd && flipProgress >= 0.5f);
            float scaleX = f.FlipAtEnd && flipProgress > 0f
                ? (flipProgress < 0.5f ? 1f - 2f * flipProgress : 2f * flipProgress - 1f)
                : 1f;
            float rcw = cw * scaleX;
            float rox = ox + (cw - rcw) * 0.5f;
            float shA = 0.38f * MathF.Sin(s * MathF.PI);
            sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
            sgp_set_color(0f, 0f, 0f, shA * scaleX);
            sgp_draw_filled_rect(rox + 6f, oy + 9f, rcw, ch);
            sgp_set_blend_mode(SGP_BLENDMODE_NONE);
            sgp_reset_color();
            if (rcw > 0.5f)
            {
                if (showFaceUp && f.Card != null)
                    DrawFaceCard(f.Card, rox, oy, rcw, ch);
                else
                    DrawBackCard(rox, oy, rcw, ch);
            }
            if (t >= 1f)
            {
                if (_displaySnap != null && f.LandDiscard != null)
                {
                    _displaySnap.TopDiscard = f.LandDiscard; _displaySnap.DiscardEmpty = false;
                }
                _fly = null;
                _suppressLastDrawn = false;
            }
        }

        sg_begin_pass(new sg_pass { action = S.passAction, swapchain = sglue_swapchain() });
        sgp_flush();
        sgp_end();

        // ── ImGui UI pass ─────────────────────────────────────
        igGetStyle()->FontSizeBase = MathF.Round(15f * _uiScale);
        simgui_new_frame(new simgui_frame_desc_t
        {
            width      = fbW,
            height     = fbH,
            delta_time = dt,
            dpi_scale  = dpi,
        });

        CardRenderer.FlushCardText();

        if (_inMainMenu)
            DrawMainMenu(w, h);
        else if (snap != null)
            DrawHUD(snap, w, h);

        simgui_render();
        sg_end_pass();
        sg_commit();
    }

    // ──────────────────────────────────────────────────────────
    // SGP: table background
    // ──────────────────────────────────────────────────────────
    static void DrawTableBg(float w, float h)
    {
        if (_tableLoaded && _tableTexture != null)
        {
            sgp_set_blend_mode(SGP_BLENDMODE_NONE);
            sgp_set_color(1f, 1f, 1f, 1f);
            sgp_set_view(0, _tableView);
            sgp_set_sampler(0, S.sampler);
            sgp_draw_textured_rect(0,
                new sgp_rect { x = 0, y = 0, w = w,                   h = h                    },
                new sgp_rect { x = 0, y = 0, w = _tableTexture.Width, h = _tableTexture.Height });
            sgp_reset_view(0);
            sgp_reset_sampler(0);
        }
        else
        {
            sgp_set_blend_mode(SGP_BLENDMODE_NONE);
            sgp_set_color(CLR_FELT.r, CLR_FELT.g, CLR_FELT.b, 1f);
            sgp_draw_filled_rect(0, 0, w, h);
        }
        sgp_reset_color();
    }

    // ──────────────────────────────────────────────────────────
    // SGP: AI hand (top — face down, revealed at round end)
    // ──────────────────────────────────────────────────────────
    static void DrawAIHand(GinSnapshot snap, float w, float h)
    {
        int n = snap.AIHand.Count;
        if (n == 0) return;

        float totalW = n * CARD_W + (n - 1) * CARD_GAP;
        float startX = w * 0.5f - totalW * 0.5f;
        float hudH   = 36f * _uiScale;
        float pileY  = h * 0.5f - CARD_H * 0.5f;
        float baseY  = hudH + (pileY - hudH - CARD_H) * 0.25f;

        bool roundOver = snap.AIRevealed &&
                        (snap.Phase == GinPhase.RoundOver || snap.Phase == GinPhase.GameOver);
        var aiDwSet     = new HashSet<Card>(snap.AIDeadwoodCards, ReferenceEqualityComparer.Instance);
        var aiLaidOffSet = new HashSet<Card>(snap.AILaidOffCards,  ReferenceEqualityComparer.Instance);

        for (int i = 0; i < n; i++)
        {
            float x          = startX + i * (CARD_W + CARD_GAP);
            bool  isDeadwood = roundOver && aiDwSet.Contains(snap.AIHand[i]);
            bool  isLaidOff  = roundOver && aiLaidOffSet.Contains(snap.AIHand[i]);
            float y          = (isDeadwood || isLaidOff) ? baseY + 20f : baseY;

            if (snap.AIRevealed)
            {
                if (isDeadwood)
                {
                    sgp_set_color(0.85f, 0.08f, 0.08f, 1.0f);
                    sgp_draw_filled_rect(x - 4f, y - 4f, CARD_W + 8f, CARD_H + 8f);
                    sgp_reset_color();
                }
                if (isLaidOff)
                {
                    sgp_set_color(0.1f, 0.75f, 0.2f, 1.0f);
                    sgp_draw_filled_rect(x - 4f, y - 4f, CARD_W + 8f, CARD_H + 8f);
                    sgp_reset_color();
                }
                DrawFaceCard(snap.AIHand[i], x, y, CARD_W, CARD_H);
            }
            else
                DrawBackCard(x, baseY, CARD_W, CARD_H);
        }
    }

    // ──────────────────────────────────────────────────────────
    // SGP: stock + discard piles
    // ──────────────────────────────────────────────────────────
    static void DrawPiles(GinSnapshot snap, float w, float h)
    {
        float cy  = h * 0.5f - CARD_H * 0.5f;
        float gap = PILE_GAP;
        float lx  = w * 0.5f - CARD_W - gap * 0.5f;
        float rx  = w * 0.5f + gap * 0.5f;

        if (snap.StockCount > 0)
        {
            sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
            sgp_set_color(0f, 0f, 0f, 0.18f);
            sgp_draw_filled_rect(lx + 3f, cy + 3f, CARD_W, CARD_H);
            sgp_set_blend_mode(SGP_BLENDMODE_NONE);
            sgp_reset_color();
            DrawBackCard(lx, cy, CARD_W, CARD_H);
        }
        else
        {
            DrawEmptySlot(lx, cy, CARD_W, CARD_H);
        }

        if (!snap.DiscardEmpty && snap.TopDiscard != null)
            DrawFaceCard(snap.TopDiscard, rx, cy, CARD_W, CARD_H);
        else
            DrawEmptySlot(rx, cy, CARD_W, CARD_H);
    }

    static void DrawEmptySlot(float x, float y, float w, float h)
    {
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(0f, 0f, 0f, 0.22f);
        sgp_draw_filled_rect(x, y, w, h);
        sgp_set_color(0.6f, 0.6f, 0.6f, 0.45f);
        sgp_draw_line(x,     y,     x + w, y    );
        sgp_draw_line(x,     y + h, x + w, y + h);
        sgp_draw_line(x,     y,     x,     y + h);
        sgp_draw_line(x + w, y,     x + w, y + h);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    // ──────────────────────────────────────────────────────────
    // SGP: human hand (bottom — face up, interactive)
    // ──────────────────────────────────────────────────────────
    static void DrawHumanHand(GinSnapshot snap, float w, float h)
    {
        var  hand = snap.HumanHand;
        int  n    = hand.Count;
        if (n == 0) return;

        bool roundOver = snap.AIRevealed &&
                        (snap.Phase == GinPhase.RoundOver || snap.Phase == GinPhase.GameOver);

        if (roundOver && !_wasRoundOver)
        {
            float stride = CARD_W + CARD_GAP;
            float totalWPre = n * CARD_W + (n - 1) * CARD_GAP;
            float startXPre = w * 0.5f - totalWPre * 0.5f;

            _sortCardOffsets = new float[n];
            for (int j = 0; j < n; j++)
            {
                Card c = hand[j];
                float newX = startXPre + j * stride;
                if (_preRoundOverSlot != null && _preRoundOverSlot.TryGetValue(c, out int oldDi))
                {
                    float oldX = startXPre + oldDi * stride;
                    _sortCardOffsets[j] = oldX - newX;
                }
            }
            _sortAnimStart = _globalTime;
            _ginHandOrder  = Enumerable.Range(0, n).ToArray();
            _wasRoundOver  = true;
        }
        else if (roundOver)
        {
            if (_ginHandOrder.Length != n)
                _ginHandOrder = Enumerable.Range(0, n).ToArray();
        }
        else
        {
            SyncGinOrder(hand);
            _wasRoundOver = false;
            if (_ginHandOrder.Length == n)
            {
                _preRoundOverSlot = new Dictionary<Card, int>(ReferenceEqualityComparer.Instance);
                for (int di = 0; di < n; di++)
                {
                    int ai = _ginHandOrder[di];
                    if (ai < n) _preRoundOverSlot[hand[ai]] = di;
                }
            }
        }

        float totalW = n * CARD_W + (n - 1) * CARD_GAP;
        float startX = w * 0.5f - totalW * 0.5f;
        float baseY  = h - ACTION_H - CARD_H - 12f;

        HashSet<Card> dwSet;
        HashSet<Card> laidOffSet;
        if (roundOver)
        {
            dwSet     = new HashSet<Card>(snap.HumanDeadwoodCards, ReferenceEqualityComparer.Instance);
            laidOffSet = new HashSet<Card>(snap.HumanLaidOffCards,  ReferenceEqualityComparer.Instance);
        }
        else
        {
            MeldValidator.MinDeadwood(hand, out _, out var dwCards);
            dwSet     = new HashSet<Card>(dwCards, ReferenceEqualityComparer.Instance);
            laidOffSet = new HashSet<Card>();
        }

        bool dragging = _dragDisplayIdx >= 0 && _dragDisplayIdx < n && !roundOver;

        for (int di = 0; di < n; di++)
        {
            int   ai         = _ginHandOrder[di];
            bool  sel        = di == _selectedCard && !roundOver;
            bool  isDeadwood = dwSet.Contains(hand[ai]);
            bool  isLaidOff  = laidOffSet.Contains(hand[ai]);
            float cx         = startX + di * (CARD_W + CARD_GAP);
            if (roundOver && _sortCardOffsets.Length == n)
            {
                float elapsed = _globalTime - _sortAnimStart;
                float t       = Math.Clamp(elapsed / SORT_DUR, 0f, 1f);
                float ease    = 1f - (1f - t) * (1f - t);
                cx += _sortCardOffsets[di] * (1f - ease);
            }
            float cy         = (sel && !dragging) ? baseY - 14f : baseY;
            if (roundOver && (isDeadwood || isLaidOff)) cy -= 20f;

            if (dragging && di == _dragDisplayIdx)
            {
                DrawEmptySlot(cx, baseY, CARD_W, CARD_H);
                continue;
            }

            if (isDeadwood && !roundOver)
            {
                sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
                sgp_set_color(0.9f, 0.1f, 0.1f, 0.13f);
                sgp_draw_filled_rect(cx - 2f, cy - 2f, CARD_W + 4f, CARD_H + 4f);
                sgp_set_blend_mode(SGP_BLENDMODE_NONE);
                sgp_reset_color();
            }

            if (isDeadwood && roundOver)
            {
                sgp_set_color(0.85f, 0.08f, 0.08f, 1.0f);
                sgp_draw_filled_rect(cx - 4f, cy - 4f, CARD_W + 8f, CARD_H + 8f);
                sgp_reset_color();
            }

            if (isLaidOff && roundOver)
            {
                sgp_set_color(0.1f, 0.75f, 0.2f, 1.0f);
                sgp_draw_filled_rect(cx - 4f, cy - 4f, CARD_W + 8f, CARD_H + 8f);
                sgp_reset_color();
            }

            if (sel)
            {
                float pulse = 0.55f + 0.3f * MathF.Abs(MathF.Sin(_globalTime * 4f));
                sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
                sgp_set_color(1.0f, 0.85f, 0.0f, pulse);
                sgp_draw_filled_rect(cx - 4f, cy - 4f, CARD_W + 8f, CARD_H + 8f);
                sgp_set_blend_mode(SGP_BLENDMODE_NONE);
                sgp_reset_color();
            }

            if (_suppressLastDrawn && ai == snap.LastDrawnIdx)
            {
                DrawEmptySlot(cx, baseY, CARD_W, CARD_H);
                continue;
            }

            DrawFaceCard(hand[ai], cx, cy, CARD_W, CARD_H);

            if (ai == snap.LastDrawnIdx && !roundOver)
            {
                float g = 0.25f + 0.2f * MathF.Sin(_globalTime * 6f);
                sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
                sgp_set_color(0.2f, 1.0f, 0.2f, g);
                sgp_draw_line(cx,          cy,          cx + CARD_W, cy         );
                sgp_draw_line(cx,          cy + CARD_H, cx + CARD_W, cy + CARD_H);
                sgp_draw_line(cx,          cy,          cx,          cy + CARD_H);
                sgp_draw_line(cx + CARD_W, cy,          cx + CARD_W, cy + CARD_H);
                sgp_set_blend_mode(SGP_BLENDMODE_NONE);
                sgp_reset_color();
            }
        }

        if (dragging)
            DrawFaceCard(hand[_ginHandOrder[_dragDisplayIdx]],
                _dragX - CARD_W * 0.5f, _dragY - CARD_H * 0.4f, CARD_W, CARD_H);
    }

    // ──────────────────────────────────────────────────────────
    // ImGui: main menu
    // ──────────────────────────────────────────────────────────
    static void DrawMainMenu(float w, float h)
    {
        float mw = 480f * _uiScale;
        float mh = 340f * _uiScale;
        igSetNextWindowPos(new Vector2((w - mw) * 0.5f, (h - mh) * 0.5f),
                           ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(mw, mh), ImGuiCond.Always);
        igSetNextWindowBgAlpha(0.88f);

        byte open = 1;
        if (igBegin("MainMenu", ref open,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            igPushFont(igGetFont(), 26f * _uiScale);
            CenteredText("Gin Rummy", mw);
            igPopFont();

            igSpacing(); igSeparator(); igSpacing();

            float btnW = mw - 32f;

            igText("Opponent:");
            igSameLine(0, 10);
            if (igRadioButton_Bool("Strategist",  _aiChoice == 0)) _aiChoice = 0;
            igSameLine(0, 10);
            if (igRadioButton_Bool("Conservator", _aiChoice == 1)) _aiChoice = 1;
            igSameLine(0, 10);
            if (igRadioButton_Bool("Casual",      _aiChoice == 2)) _aiChoice = 2;

            igSpacing();
            if (igButton("Start  --  Classic Gin  (100 pts)", new Vector2(btnW, 40f * _uiScale)))
                StartGame(100);
            igSpacing();
            if (igButton("Start  --  Extended Game  (250 pts)", new Vector2(btnW, 40f * _uiScale)))
                StartGame(250);

            igSpacing(); igSeparator(); igSpacing();
            igTextWrapped("Deadwood <= 10 to Knock. Zero deadwood = Gin (+25 bonus). Undercut awards +25 to defender.");
        }
        igEnd();
    }

    static void StartGame(int targetScore)
    {
        IGinAI ai = _aiChoice switch
        {
            1 => new ConservatorAI(),
            2 => new CasualAI(),
            _ => new StrategistAI(),
        };
        _game             = new GinRummyGame(ai);
        _game.StartNewGame(targetScore);
        _selectedCard     = -1;
        _prevGinPhase     = GinPhase.HumanDraw;
        _ginHandOrder     = Array.Empty<int>();
        _wasRoundOver     = false;
        _sortCardOffsets  = Array.Empty<float>();
        _preRoundOverSlot = null;
        SortGinHand(_game.Snapshot.HumanHand);
        _inMainMenu       = false;
    }

    // ──────────────────────────────────────────────────────────
    // ImGui: in-game HUD
    // ──────────────────────────────────────────────────────────
    static void DrawHUD(GinSnapshot snap, float w, float h)
    {
        var noDecor = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav;
        var noDecoNoInput = noDecor | ImGuiWindowFlags.NoInputs;
        byte open = 1;

        // ── Score bar (top) ───────────────────────────────────
        igPushStyleVar_Vec2(ImGuiStyleVar.WindowPadding, new Vector2(8f, 0f));
        igSetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(w, 36f * _uiScale), ImGuiCond.Always);
        igSetNextWindowBgAlpha(0.72f);
        igBegin("ScoreBar", ref open, noDecor);
        igPopStyleVar(1);
        {
            float rightBtnX = w - 136f * _uiScale;
            float lineH     = igGetTextLineHeight();
            float barH      = 36f * _uiScale;

            // Measure each text piece to centre the group horizontally
            string t0 = $"Round {snap.RoundNumber}";
            string t1 = $"You: {snap.HumanScore}";
            string t2 = $"AI:  {snap.AIScore}";
            string t3 = $"Target: {snap.TargetScore}";
            bool showDead = snap.Phase == GinPhase.HumanDraw || snap.Phase == GinPhase.HumanDiscard;
            string t4 = showDead ? $"Deadwood: {snap.HumanDeadwood}" : "";
            string t5 = (showDead && snap.CanKnock) ? "[KNOCK eligible]" : "";
            Vector2 s0=default,s1=default,s2=default,s3=default,s4=default,s5=default;
            igCalcTextSize(ref s0, t0, null, false, -1f);
            igCalcTextSize(ref s1, t1, null, false, -1f);
            igCalcTextSize(ref s2, t2, null, false, -1f);
            igCalcTextSize(ref s3, t3, null, false, -1f);
            if (showDead)                  igCalcTextSize(ref s4, t4, null, false, -1f);
            if (showDead && snap.CanKnock) igCalcTextSize(ref s5, t5, null, false, -1f);
            float totalW = s0.X + 18 + s1.X + 18 + s2.X + 18 + s3.X;
            if (showDead)                  totalW += 28 + s4.X;
            if (showDead && snap.CanKnock) totalW += 10 + s5.X;
            float startX = MathF.Max(8f, (rightBtnX - totalW) * 0.5f);

            igSetCursorPosY(MathF.Max(0f, (barH - lineH) * 0.5f));
            igSetCursorPosX(startX);
            igText(t0);
            igSameLine(0, 18);
            igText(t1);
            igSameLine(0, 18);
            igText(t2);
            igSameLine(0, 18);
            igText(t3);
            if (showDead)
            {
                igSameLine(0, 28);
                igText(t4);
                if (snap.CanKnock)
                {
                    igSameLine(0, 10);
                    igTextColored(new Vector4(1f, 0.9f, 0.2f, 1f), t5);
                }
            }
            igSameLine(0, 0);
            igSetCursorPosX(rightBtnX);
            if (igButton("Menu", new Vector2(76f * _uiScale, 22f * _uiScale)))
            { _inMainMenu = true; _selectedCard = -1; }
            igSameLine(0, 8);
            if (igButton(_showLog ? "Log X" : "Log", new Vector2(48f * _uiScale, 22f * _uiScale)))
                _showLog = !_showLog;
        }
        igEnd();

        // ── Pile labels ───────────────────────────────────────
        float pcy = h * 0.5f - CARD_H * 0.5f;
        float pgap = PILE_GAP;
        float plx  = w * 0.5f - CARD_W - pgap * 0.5f;
        float prx  = w * 0.5f + pgap * 0.5f;

        float labelY = pcy + CARD_H + 6f;
        var dl = igGetForegroundDrawList_ViewportPtr(igGetMainViewport());

        string stockPrefix = "Stock ";
        string stockCount  = $"{snap.StockCount}";
        Vector2 prefixSize = default, countSize = default;
        igCalcTextSize(ref prefixSize, stockPrefix,  null, false, -1f);
        igCalcTextSize(ref countSize,  stockCount,   null, false, -1f);
        float stockTotalW = prefixSize.X + countSize.X;
        float stockX = plx + (CARD_W - stockTotalW) * 0.5f;
        ImDrawList_AddText_Vec2(dl, new Vector2(stockX, labelY), 0xFFFFFFFFu, stockPrefix, null);
        ImDrawList_AddText_Vec2(dl, new Vector2(stockX + prefixSize.X, labelY), 0xFF40E6FFu, stockCount, null);

        Vector2 discardSize = default;
        igCalcTextSize(ref discardSize, "Discard", null, false, -1f);
        float discardX = prx + (CARD_W - discardSize.X) * 0.5f;
        ImDrawList_AddText_Vec2(dl, new Vector2(discardX, labelY), 0xFFFFFFFFu, "Discard", null);

        // ── Action bar (bottom) ───────────────────────────────
        igPushStyleVar_Vec2(ImGuiStyleVar.WindowPadding, new Vector2(8f, 0f));
        igSetNextWindowPos(new Vector2(0, h - ACTION_H), ImGuiCond.Always, Vector2.Zero);
        igSetNextWindowSize(new Vector2(w, ACTION_H), ImGuiCond.Always);
        igSetNextWindowBgAlpha(0.82f);
        igBegin("ActionBar", ref open, noDecor);
        igPopStyleVar(1);
        {
            float btnH     = 38f * _uiScale;
            float lineH    = igGetTextLineHeight();
            float spacingY = igGetTextLineHeightWithSpacing() - lineH;
            float contentH = lineH + 2f * spacingY + btnH;
            float startY   = MathF.Max(4f, (ACTION_H - contentH) * 0.5f);
            igSetCursorPosY(startY);
            { Vector2 _sz = default; igCalcTextSize(ref _sz, snap.StatusMessage, null, false, -1f);
              igSetCursorPosX(MathF.Max(4f, (w - _sz.X) * 0.5f)); }
            igTextUnformatted(snap.StatusMessage, null);
            igSpacing();

            if (snap.Phase == GinPhase.HumanDiscard && _prevGinPhase != GinPhase.HumanDiscard)
            {
                int bestDispIdx = -1;
                int bestDw      = int.MaxValue;
                for (int bi = 0; bi < _ginHandOrder.Length; bi++)
                {
                    var tempHand = snap.HumanHand.ToList();
                    tempHand.RemoveAt(_ginHandOrder[bi]);
                    int dw = MeldValidator.MinDeadwood(tempHand, out _, out _);
                    if (dw < bestDw) { bestDw = dw; bestDispIdx = bi; }
                }
                if (bestDw <= 10)
                    _selectedCard = bestDispIdx;
            }
            _prevGinPhase = snap.Phase;

            switch (snap.Phase)
            {
                case GinPhase.HumanDraw:
                {
                    float sideW = 76f * _uiScale;
                    float bw    = (w - 40f - sideW - 8f) * 0.5f;
                    if (igButton("Draw from Stock", new Vector2(bw, btnH)))
                    {
                        _game!.HumanDrawFromStock();
                        int newActual = _game.Snapshot.HumanHand.Count - 1;
                        _ginHandOrder = _ginHandOrder.Append(newActual).ToArray();
                    }
                    igSameLine(0, 8);
                    bool canTake = !snap.DiscardEmpty;
                    if (!canTake) igBeginDisabled(true);
                    if (igButton("Take Discard", new Vector2(bw, btnH)) && canTake)
                    {
                        _game!.HumanTakeDiscard();
                        int newActual = _game.Snapshot.HumanHand.Count - 1;
                        _ginHandOrder = _ginHandOrder.Append(newActual).ToArray();
                    }
                    if (!canTake) igEndDisabled();
                    // igSameLine(0, 8);
                    // if (igButton("Sort ^", new Vector2(sideW, btnH)))
                    //     SortGinHand(snap.HumanHand);
                    break;
                }

                case GinPhase.HumanDiscard:
                {
                    bool  hasSel = _selectedCard >= 0;
                    float bw     = snap.CanKnock ? (w - 56f) * 0.5f : w - 24f;

                    if (!hasSel) igBeginDisabled(true);
                    if (igButton("Discard Selected", new Vector2(bw, btnH)) && hasSel)
                    {
                        int dispIdx = _selectedCard;
                        int actIdx  = _ginHandOrder[dispIdx];
                        { int   handN  = snap.HumanHand.Count;
                          float totW   = handN * CARD_W + (handN - 1) * CARD_GAP;
                          _pendingDiscardSrc = (w * 0.5f - totW * 0.5f + dispIdx * (CARD_W + CARD_GAP),
                                               h - ACTION_H - CARD_H - 12f - 14f); }
                        _game!.HumanDiscard(actIdx);
                        _ginHandOrder = _ginHandOrder
                            .Where((_, ii) => ii != dispIdx)
                            .Select(ai => ai > actIdx ? ai - 1 : ai).ToArray();
                        _selectedCard = -1;
                    }
                    if (!hasSel) igEndDisabled();

                    if (snap.CanKnock)
                    {
                        bool selWouldGin = false;
                        int  postDw      = snap.HumanDeadwood;
                        if (hasSel && _selectedCard < _ginHandOrder.Length)
                        {
                            int actIdxPreview = _ginHandOrder[_selectedCard];
                            var tempHand = snap.HumanHand.ToList();
                            tempHand.RemoveAt(actIdxPreview);
                            postDw = MeldValidator.MinDeadwood(tempHand, out _, out _);
                            selWouldGin = postDw == 0;
                        }
                        igSameLine(0, 8);
                        if (!hasSel) igBeginDisabled(true);
                        if (selWouldGin)
                        {
                            igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.05f, 0.55f, 0.85f, 1f));
                            igPushStyleColor_Vec4(ImGuiCol.ButtonHovered, new Vector4(0.15f, 0.70f, 1.00f, 1f));
                        }
                        else
                        {
                            igPushStyleColor_Vec4(ImGuiCol.Button,        new Vector4(0.75f, 0.55f, 0.05f, 1f));
                            igPushStyleColor_Vec4(ImGuiCol.ButtonHovered, new Vector4(0.90f, 0.70f, 0.15f, 1f));
                        }
                        string knockLabel = selWouldGin ? "Gin!" : $"Knock ({postDw})";
                        if (igButton(knockLabel, new Vector2(bw, btnH)) && hasSel)
                        {
                            int dispKnock = _selectedCard;
                            int actKnock  = _ginHandOrder[dispKnock];
                            { int   handN  = snap.HumanHand.Count;
                              float totW   = handN * CARD_W + (handN - 1) * CARD_GAP;
                              _pendingDiscardSrc = (w * 0.5f - totW * 0.5f + dispKnock * (CARD_W + CARD_GAP),
                                                   h - ACTION_H - CARD_H - 12f - 14f); }
                            _game!.HumanKnock(actKnock);
                            _ginHandOrder = _ginHandOrder
                                .Where((_, ii) => ii != dispKnock)
                                .Select(ai => ai > actKnock ? ai - 1 : ai).ToArray();
                            _selectedCard = -1;
                        }
                        igPopStyleColor(2);
                        if (!hasSel) igEndDisabled();
                    }
                    igSameLine(0, 8);
                    if (igButton("Sort ^##gin", new Vector2(80f * _uiScale, btnH)))
                        SortGinHand(snap.HumanHand);
                    break;
                }

                case GinPhase.AITurn:
                {
                    igBeginDisabled(true);
                    igButton("AI is thinking...", new Vector2(w - 24f, btnH));
                    igEndDisabled();
                    break;
                }

                case GinPhase.RoundOver:
                case GinPhase.GameOver:
                {
                    float bw = (w - 40f) * 0.5f;
                    if (snap.Phase == GinPhase.RoundOver)
                    {
                        if (igButton("Next Round", new Vector2(bw, btnH)))
                        {
                            _selectedCard     = -1;
                            _prevGinPhase     = GinPhase.HumanDraw;
                            _ginHandOrder     = Array.Empty<int>();
                            _wasRoundOver     = false;
                            _sortCardOffsets  = Array.Empty<float>();
                            _preRoundOverSlot = null;
                            _game!.StartNewRound();
                            SortGinHand(_game.Snapshot.HumanHand);
                        }
                    }
                    else
                    {
                        igPushStyleColor_Vec4(ImGuiCol.Button, new Vector4(0.15f, 0.55f, 0.15f, 1f));
                        if (igButton("New Game", new Vector2(bw, btnH)))
                        {
                            _selectedCard = -1;
                            _ginHandOrder = Array.Empty<int>();
                            StartGame(_game!.TargetScore);
                        }
                        igPopStyleColor(1);
                    }
                    igSameLine(0, 8);
                    if (igButton("Main Menu", new Vector2(bw, btnH)))
                    {
                        _inMainMenu   = true;
                        _selectedCard = -1;
                    }
                    igSameLine(0, 8);
                    if (igButton(_showLog ? "Log X" : "Log", new Vector2(bw * 0.7f, btnH)))
                        _showLog = !_showLog;
                    break;
                }
            }
        }
        igEnd();

        // ── Game log window ───────────────────────────────────
        if (_showLog)
        {
            igSetNextWindowPos(new Vector2(8f, 44f * _uiScale), ImGuiCond.Once, Vector2.Zero);
            igSetNextWindowSize(new Vector2(340f * _uiScale, 280f * _uiScale), ImGuiCond.Once);
            igSetNextWindowBgAlpha(0.88f);
            byte logOpen = 1;
            igBegin("Game Log", ref logOpen,
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
            for (int li = 0; li < _gameLog.Count; li++)
            {
                string entry = _gameLog[li];
                Vector4 col = entry.Contains("AI")    ? new Vector4(1f,   0.75f, 0.3f,  1f) :
                              entry.Contains("You")   ? new Vector4(0.4f, 1f,    0.5f,  1f) :
                              entry.Contains("Round") ? new Vector4(0.8f, 0.8f,  1f,    1f) :
                                                        new Vector4(0.85f,0.85f, 0.85f, 1f);
                igPushStyleColor_Vec4(ImGuiCol.Text, col);
                igTextUnformatted(entry, null);
                igPopStyleColor(1);
            }
            igSetScrollHereY(1.0f);
            igEnd();
        }

        // ── AI action toast ───────────────────────────────────
        {
            float toastAge = _globalTime - _aiToastTime;
            if (toastAge < 2.5f && _aiToastText.Length > 0)
            {
                float alpha = toastAge < 1.8f ? 1f : 1f - (toastAge - 1.8f) / 0.7f;
                igSetNextWindowPos(new Vector2(w * 0.5f, 72f * _uiScale), ImGuiCond.Always, new Vector2(0.5f, 0f));
                igSetNextWindowSize(new Vector2(0, 0), ImGuiCond.Always);
                igSetNextWindowBgAlpha(0.82f * alpha);
                igPushStyleColor_Vec4(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.05f, 0.82f * alpha));
                byte toastOpen = 1;
                igBegin("##aitst", ref toastOpen,
                    ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize);
                igPushStyleColor_Vec4(ImGuiCol.Text, new Vector4(1f, 0.9f, 0.3f, alpha));
                igText(_aiToastText);
                igPopStyleColor(1);
                igEnd();
                igPopStyleColor(1);
            }
        }

        // ── Round result overlay ──────────────────────────────
        if (snap.AIRevealed && (snap.Phase == GinPhase.RoundOver || snap.Phase == GinPhase.GameOver))
        {
            float ow = MathF.Min(460f * _uiScale, w - 40f);
            float oh = 80f * _uiScale;
            float ox = (w - ow) * 0.5f;
            float oy = (h - oh) * 0.5f;
            igSetNextWindowPos(new Vector2(ox, oy), ImGuiCond.Always, Vector2.Zero);
            igSetNextWindowSize(new Vector2(ow, oh), ImGuiCond.Always);
            igSetNextWindowBgAlpha(0.91f);
            if (igBegin("ResultOverlay", ref open, noDecoNoInput))
            {
                igPushFont(igGetFont(), 16f * _uiScale);
                igTextWrapped(snap.RoundResultMsg);
                igPopFont();
                igSpacing();
                igText($"Your deadwood: {snap.HumanDeadwood}   AI deadwood: {snap.AIDeadwood}");
            }
            igEnd();
        }
    }

    // ──────────────────────────────────────────────────────────
    // Event
    // ──────────────────────────────────────────────────────────
    [UnmanagedCallersOnly]
    static void Event(sapp_event* e)
    {
        simgui_handle_event(in *e);

        float dpi = sapp_dpi_scale();
        float mx, my;

        if (e->type == sapp_event_type.SAPP_EVENTTYPE_MOUSE_DOWN)
        {
            if (igGetIO_Nil()->WantCaptureMouse != 0) return;
            BeginDrag(e->mouse_x / dpi, e->mouse_y / dpi);
            return;
        }
        if (e->type == sapp_event_type.SAPP_EVENTTYPE_TOUCHES_BEGAN)
        {
            if (igGetIO_Nil()->WantCaptureMouse != 0) return;
            BeginDrag(e->touches[0].pos_x / dpi, e->touches[0].pos_y / dpi);
            return;
        }

        if (e->type == sapp_event_type.SAPP_EVENTTYPE_MOUSE_MOVE)
        { if (_dragDisplayIdx >= 0) { _dragX = e->mouse_x / dpi; _dragY = e->mouse_y / dpi; } return; }
        if (e->type == sapp_event_type.SAPP_EVENTTYPE_TOUCHES_MOVED)
        { if (_dragDisplayIdx >= 0) { _dragX = e->touches[0].pos_x / dpi; _dragY = e->touches[0].pos_y / dpi; } return; }

        bool isTap   = e->type == sapp_event_type.SAPP_EVENTTYPE_TOUCHES_ENDED;
        bool isClick = e->type == sapp_event_type.SAPP_EVENTTYPE_MOUSE_UP;
        if (!isTap && !isClick) return;

        if (igGetIO_Nil()->WantCaptureMouse != 0) { _dragDisplayIdx = -1; return; }

        if (isTap)
        { mx = e->touches[0].pos_x / dpi; my = e->touches[0].pos_y / dpi; }
        else
        { mx = e->mouse_x / dpi; my = e->mouse_y / dpi; }

        float w = MathF.Round(sapp_width()  / dpi);
        float h = MathF.Round(sapp_height() / dpi);
        bool dragged = _dragDisplayIdx >= 0
                    && (MathF.Abs(mx - _dragStartX) > 8f || MathF.Abs(my - _dragStartY) > 8f);
        if (dragged && TryDragDiscard(mx, my, w, h)) { /* discarded via drag */ }
        else if (dragged) EndDragReorder(mx, w, h);
        else         HandleClick(mx, my);
        _dragDisplayIdx = -1;
    }

    static void HandleClick(float mx, float my)
    {
        if (_inMainMenu) return;
        if (_game == null) return;
        var snap = _game.Snapshot;

        float dpi = sapp_dpi_scale();
        float w   = MathF.Round(sapp_width()  / dpi);
        float h   = MathF.Round(sapp_height() / dpi);

        {
            int   n      = snap.HumanHand.Count;
            float totalW = n * CARD_W + (n - 1) * CARD_GAP;
            float startX = w * 0.5f - totalW * 0.5f;
            float baseY  = h - ACTION_H - CARD_H - 12f;

            for (int i = 0; i < n; i++)
            {
                float cx    = startX + i * (CARD_W + CARD_GAP);
                float cardY = i == _selectedCard ? baseY - 14f : baseY;
                if (mx >= cx && mx <= cx + CARD_W && my >= cardY && my <= cardY + CARD_H)
                {
                    if (i == _selectedCard && snap.Phase == GinPhase.HumanDiscard)
                    {
                        int actIdx = _ginHandOrder[i];
                        _pendingDiscardSrc = (cx, cardY);
                        _game!.HumanDiscard(actIdx);
                        _ginHandOrder = _ginHandOrder
                            .Where((_, ii) => ii != i)
                            .Select(ai => ai > actIdx ? ai - 1 : ai).ToArray();
                        _selectedCard = -1;
                    }
                    else
                    {
                        _selectedCard = (i == _selectedCard) ? -1 : i;
                    }
                    return;
                }
            }
        }

        if (snap.Phase == GinPhase.HumanDraw)
        {
            float cy  = h * 0.5f - CARD_H * 0.5f;
            float lx  = w * 0.5f - CARD_W - PILE_GAP * 0.5f;
            float rx  = w * 0.5f + PILE_GAP * 0.5f;

            if (snap.StockCount > 0 &&
                mx >= lx && mx <= lx + CARD_W && my >= cy && my <= cy + CARD_H)
            {
                _game.HumanDrawFromStock();
                int newA = _game.Snapshot.HumanHand.Count - 1;
                _ginHandOrder = _ginHandOrder.Append(newA).ToArray();
                return;
            }
            if (!snap.DiscardEmpty &&
                mx >= rx && mx <= rx + CARD_W && my >= cy && my <= cy + CARD_H)
            {
                _game.HumanTakeDiscard();
                int newA = _game.Snapshot.HumanHand.Count - 1;
                _ginHandOrder = _ginHandOrder.Append(newA).ToArray();
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    // Hand order sync + sort + drag helpers
    // ──────────────────────────────────────────────────────────
    static void SyncGinOrder(System.Collections.Generic.IReadOnlyList<Card> hand)
    {
        int n = hand.Count;
        if (_ginHandOrder.Length == n) return;
        if (n == 0) { _ginHandOrder = Array.Empty<int>(); return; }

        if (_ginHandOrder.Length < n)
        {
            var present = new HashSet<int>(_ginHandOrder);
            var newOrder = _ginHandOrder.ToList();
            for (int ai = 0; ai < n; ai++)
                if (!present.Contains(ai)) newOrder.Add(ai);
            _ginHandOrder = newOrder.ToArray();
        }
        else
        {
            _ginHandOrder = _ginHandOrder.Where(ai => ai < n).ToArray();
            if (_selectedCard >= n) _selectedCard = n - 1;
        }
    }

    static int CardSortKey(Card c)
        => (c.Type == CardType.Ace ? 1 : (int)c.Type) * 10 + (int)c.Suit;

    static void SortGinHand(System.Collections.Generic.IReadOnlyList<Card> hand)
    {
        SyncGinOrder(hand);
        _ginHandOrder = _ginHandOrder.OrderBy(ai => CardSortKey(hand[ai])).ToArray();
        _selectedCard = -1;
    }

    static void BeginDrag(float mx, float my)
    {
        if (_inMainMenu) return;
        float dpi = sapp_dpi_scale();
        float w   = MathF.Round(sapp_width()  / dpi);
        float h   = MathF.Round(sapp_height() / dpi);

        if (_game == null) return;
        var snap = _game.Snapshot;
        int n = snap.HumanHand.Count;
        SyncGinOrder(snap.HumanHand);
        float totalW = n * CARD_W + (n - 1) * CARD_GAP;
        float startX = w * 0.5f - totalW * 0.5f;
        float baseY  = h - ACTION_H - CARD_H - 12f;
        for (int di = 0; di < n; di++)
        {
            float cx    = startX + di * (CARD_W + CARD_GAP);
            float cardY = di == _selectedCard ? baseY - 14f : baseY;
            if (mx >= cx && mx <= cx + CARD_W && my >= cardY && my <= cardY + CARD_H)
            {
                _dragDisplayIdx = di;
                _dragStartX = _dragX = mx;
                _dragStartY = _dragY = my;
                return;
            }
        }
    }

    static bool TryDragDiscard(float mx, float my, float w, float h)
    {
        if (_game == null) return false;
        if (_game.Snapshot.Phase != GinPhase.HumanDiscard) return false;
        if (_dragDisplayIdx < 0) return false;

        float rx = w * 0.5f + PILE_GAP * 0.5f;
        float cy = h * 0.5f - CARD_H * 0.5f;

        float hzW = CARD_W * 1.5f;
        float hzH = CARD_H * 1.5f;
        float hzX = rx + CARD_W * 0.5f - hzW * 0.5f;
        float hzY = cy + CARD_H * 0.5f - hzH * 0.5f;

        if (mx < hzX || mx > hzX + hzW || my < hzY || my > hzY + hzH) return false;

        int actIdx  = _ginHandOrder[_dragDisplayIdx];
        int dispIdx = _dragDisplayIdx;
        _pendingDiscardSrc = (_dragX - CARD_W * 0.5f, _dragY - CARD_H * 0.4f);
        _game.HumanDiscard(actIdx);
        _ginHandOrder = _ginHandOrder
            .Where((_, i) => i != dispIdx)
            .Select(ai => ai > actIdx ? ai - 1 : ai).ToArray();
        _selectedCard = -1;
        return true;
    }

    static void EndDragReorder(float mx, float w, float h)
    {
        if (_dragDisplayIdx < 0) return;
        int n = _ginHandOrder.Length;
        if (n <= 1) return;

        float remaining = n - 1;
        float totalW = remaining * CARD_W + MathF.Max(0, remaining - 1) * CARD_GAP;
        float startX = w * 0.5f - totalW * 0.5f;
        int insertAt = (int)remaining;
        for (int i = 0; i < (int)remaining; i++)
        {
            float cx = startX + i * (CARD_W + CARD_GAP) + CARD_W * 0.5f;
            if (mx < cx) { insertAt = i; break; }
        }

        var ord = _ginHandOrder.ToList();
        int elem = ord[_dragDisplayIdx];
        ord.RemoveAt(_dragDisplayIdx);
        ord.Insert(insertAt, elem);

        _ginHandOrder = ord.ToArray();
        if (_selectedCard == _dragDisplayIdx)       _selectedCard = insertAt;
        else if (_selectedCard > _dragDisplayIdx && _selectedCard <= insertAt) _selectedCard--;
        else if (_selectedCard < _dragDisplayIdx && _selectedCard >= insertAt) _selectedCard++;
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────
    static void AdjustCardSizes(float w, float h)
    {
        _ = w;
        float divisor = h < 500f ? 7.0f : 8.0f;
        CARD_H   = h / divisor;
        CARD_W   = CARD_H * 0.714f;
        CARD_GAP = CARD_W * 0.09f;
        ACTION_H = MathF.Max(h / 6f, 70f);
    }

    // ──────────────────────────────────────────────────────────
    // PNG card rendering
    // ──────────────────────────────────────────────────────────

#if __ANDROID__
    static bool UsePngCards = false;
#else
    static bool UsePngCards = true;
#endif

    static void DrawFaceCard(Card card, float x, float y, float w, float h)
    {
        if (UsePngCards)
            DrawTexCard(CardToSlot(card.Suit, card.Type), x, y, w, h);
        else
            CardRenderer.DrawFaceCard(card, x, y, w, h);
    }

    static void DrawBackCard(float x, float y, float w, float h)
    {
        if (UsePngCards)
            DrawTexCard(SLOT_BACKCARD, x, y, w, h);
        else
            CardRenderer.DrawBackCard(x, y, w, h);
    }

    static void DrawTexCard(int slot, float x, float y, float w, float h)
    {
        var tex = _textures[slot];
        if (tex == null) { DrawEmptySlot(x, y, w, h); return; }
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(1f, 1f, 1f, 1f);
        sgp_set_view(0, _cardViews[slot]);
        sgp_set_sampler(0, S.sampler);
        sgp_draw_textured_rect(0,
            new sgp_rect { x = x, y = y, w = w, h = h },
            new sgp_rect { x = 0, y = 0, w = tex.Width, h = tex.Height });
        sgp_reset_view(0);
        sgp_reset_sampler(0);
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    static void LoadCardSlot(int slot, string path, string label)
    {
        FileSystem.Instance.LoadFile(path, (_, data, status) =>
        {
            if (status == FileLoadStatus.Success && data != null)
            {
                var tex = Texture.LoadFromMemory(data, label);
                if (tex != null)
                {
                    _textures[slot]   = tex;
                    _cardViews[slot]  = sgp_make_texture_view_from_image(tex.Image, label);
                    _cardLoaded[slot] = true;
                    _cardLoadedCount++;
                }
            }
        });
    }

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

    static void CenteredText(string text, float containerW)
    {
        Vector2 sz = default;
        igCalcTextSize(ref sz, text, null, false, -1f);
        igSetCursorPosX(MathF.Max(4f, (containerW - sz.X) * 0.5f));
        igText(text);
    }

    [UnmanagedCallersOnly]
    static void Cleanup()
    {
        simgui_shutdown();
        sgp_shutdown();
        sg_shutdown();
        if (Debugger.IsAttached) Environment.Exit(0);
    }

    // ──────────────────────────────────────────────────────────
    // Entry point
    // ──────────────────────────────────────────────────────────
    public static sapp_desc sokol_main()
    {
        return new sapp_desc
        {
            init_cb        = &Init,
            frame_cb       = &Frame,
            event_cb       = &Event,
            cleanup_cb     = &Cleanup,
            width          = 1280,
            height         = 720,
            sample_count   = 4,
            window_title   = "Gin Rummy",
            high_dpi       = true,
            icon           = { sokol_default = true },
            logger         = { func = &slog_func },
        };
    }
}
