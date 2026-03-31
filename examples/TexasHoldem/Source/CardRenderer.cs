// CardRenderer.cs — Dynamic vector card rendering
// Phase 1 (SGP): card body / border drawn via sokol_gp.
// Phase 2 (ImGui): rank + suit text drawn via ImGui background draw list.
// Call DrawFaceCard / DrawBackCard during the SGP phase (replaces texture draws),
// then call FlushCardText() once per frame after simgui_new_frame().

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Sokol;
using Imgui;
using TexasHoldem.Logic.Cards;
using static Sokol.SGP;
using static Sokol.SGP.sgp_blend_mode;
using static Imgui.ImguiNative;

static unsafe class CardRenderer
{
    // ── Pending text queue ────────────────────────────────────────────────────
    struct PendingText { public Card Card; public float X, Y, W, H; }
    static readonly List<PendingText> _queue = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Call during SGP phase to draw the card body + queue the text label.</summary>
    public static void DrawFaceCard(Card card, float x, float y, float w, float h)
    {
        DrawCardFace(x, y, w, h,
            isRed: card.Suit == CardSuit.Heart || card.Suit == CardSuit.Diamond);
        _queue.Add(new PendingText { Card = card, X = x, Y = y, W = w, H = h });
    }

    /// <summary>Call during SGP phase to draw a face-down card back.</summary>
    public static void DrawBackCard(float x, float y, float w, float h)
    {
        const float borderPad = 2f;
        // Cream outer border (matches original texture feel)
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(0.95f, 0.92f, 0.80f, 0.85f);
        sgp_draw_filled_rect(x - borderPad, y - borderPad, w + borderPad * 2f, h + borderPad * 2f);

        // Navy blue body
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_set_color(0.10f, 0.16f, 0.38f, 1f);
        sgp_draw_filled_rect(x, y, w, h);

        // Inner decorative border (lighter blue)
        float ip = w * 0.10f;  // inset padding
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(0.40f, 0.55f, 0.90f, 0.55f);
        float bx = x + ip, by = y + ip, bw = w - ip * 2f, bh = h - ip * 2f;
        sgp_draw_line(bx,      by,      bx + bw, by      );
        sgp_draw_line(bx,      by + bh, bx + bw, by + bh );
        sgp_draw_line(bx,      by,      bx,      by + bh );
        sgp_draw_line(bx + bw, by,      bx + bw, by + bh );

        // Diagonal cross inside inner border (subtle pattern)
        sgp_set_color(0.35f, 0.50f, 0.85f, 0.30f);
        sgp_draw_line(bx, by, bx + bw, by + bh);
        sgp_draw_line(bx + bw, by, bx, by + bh);

        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    /// <summary>Call once per frame AFTER simgui_new_frame() to flush queued card text.</summary>
    public static void FlushCardText()
    {
        if (_queue.Count == 0) return;

        var vp = igGetMainViewport();
        var dl = igGetBackgroundDrawList(vp);
        var font = igGetFont();

        foreach (var p in _queue)
            DrawCardText(dl, font, p.Card, p.X, p.Y, p.W, p.H);

        _queue.Clear();
    }

    // ── SGP body ──────────────────────────────────────────────────────────────

    static void DrawCardFace(float x, float y, float w, float h, bool isRed)
    {
        // Drop shadow
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(0f, 0f, 0f, 0.30f);
        sgp_draw_filled_rect(x + 3f, y + 3f, w, h);

        // White card body
        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_set_color(0.97f, 0.97f, 0.96f, 1f);
        sgp_draw_filled_rect(x, y, w, h);

        // Thin dark border
        sgp_set_blend_mode(SGP_BLENDMODE_BLEND);
        sgp_set_color(0.20f, 0.20f, 0.20f, 0.85f);
        sgp_draw_line(x,     y,     x + w, y    );
        sgp_draw_line(x,     y + h, x + w, y + h);
        sgp_draw_line(x,     y,     x,     y + h);
        sgp_draw_line(x + w, y,     x + w, y + h);

        // Very subtle inner tint for red cards (slight warm hue top-right)
        if (isRed)
        {
            sgp_set_color(1.00f, 0.10f, 0.10f, 0.03f);
            sgp_draw_filled_rect(x, y, w, h);
        }

        sgp_set_blend_mode(SGP_BLENDMODE_NONE);
        sgp_reset_color();
    }

    // ── ImGui text ────────────────────────────────────────────────────────────

    static void DrawCardText(ImDrawList* dl, ImFont* font, Card card, float x, float y, float w, float h)
    {
        bool isRed = card.Suit == CardSuit.Heart || card.Suit == CardSuit.Diamond;
        uint textCol   = isRed ? Col32(205, 25, 25, 255) : Col32(18, 18, 18, 255);
        uint centerCol = isRed ? Col32(195, 20, 20, 235) : Col32(12, 12, 12, 230);

        string rank = RankStr(card.Type);
        string suit = SuitStr(card.Suit);

        float rankSz   = MathF.Max(MathF.Round(w * 0.30f), 9f);
        float suitSmSz = MathF.Max(MathF.Round(w * 0.27f), 8f);

        // ── Top-left corner: rank then suit ──────────────────
        float tlx = x + w * 0.06f;
        float tly = y + h * 0.04f;
        DrawText(dl, font, rankSz,   tlx, tly,                  textCol, rank);
        DrawText(dl, font, suitSmSz, tlx, tly + rankSz + 1f,    textCol, suit);

        // ── Bottom-right corner (inverted) ────────────────────
        float rankW = rankSz * (rank.Length == 2 ? 1.05f : 0.65f);
        float brx = x + w - rankW - w * 0.10f;
        float bry = y + h - (rankSz + suitSmSz + 4f) - h * 0.04f;
        DrawText(dl, font, suitSmSz, brx, bry,               textCol, suit);
        DrawText(dl, font, rankSz,   brx, bry + suitSmSz + 1f, textCol, rank);

        // ── Center: one large symbol for all card types ──
        float bigSz = MathF.Max(MathF.Round(MathF.Min(w * 0.55f, h * 0.40f)), 14f);
        string label = card.Type switch
        {
            CardType.Jack  => "J",
            CardType.Queen => "Q",
            CardType.King  => "K",
            _              => suit   // 2-10 and Ace: large suit symbol
        };
        float lhpx = bigSz * (label.Length == 1 ? 0.35f : 0.38f);
        float lhpy = bigSz * 0.52f;
        float cx = x + w * 0.5f - lhpx;
        float cy = y + h * 0.5f - lhpy;
        DrawText(dl, font, bigSz, cx, cy, centerCol, label);

        // For J/Q/K draw a smaller suit below the letter
        if (card.Type == CardType.Jack || card.Type == CardType.Queen || card.Type == CardType.King)
        {
            float suitBelowSz = MathF.Round(bigSz * 0.50f);
            float scx = x + w * 0.5f - suitBelowSz * 0.37f;
            DrawText(dl, font, suitBelowSz, scx, cy + bigSz + 2f, centerCol, suit);
        }
    }

    static void DrawText(ImDrawList* dl, ImFont* font, float sz, float x, float y, uint col, string text)
    {
        // cpu_fine_clip_rect = {0,0,0,0} would cause w<=x / h<=y → ImGui skips the draw.
        // Pass a huge rect to disable the optional extra clip.
        var noClip = new Vector4(0f, 0f, 65535f, 65535f);
        ImDrawList_AddText_FontPtr(dl, font, sz,
            new Vector2(x, y), col, text, null, 0f, ref noClip);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static uint Col32(byte r, byte g, byte b, byte a) =>
        (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    static string RankStr(CardType t) => t switch
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
        CardType.Jack  => "J",
        CardType.Queen => "Q",
        CardType.King  => "K",
        CardType.Ace   => "A",
        _              => "?"
    };

    static string SuitStr(CardSuit s) => s switch
    {
        CardSuit.Spade   => "\u2660",  // ♠
        CardSuit.Club    => "\u2663",  // ♣
        CardSuit.Heart   => "\u2665",  // ♥
        CardSuit.Diamond => "\u2666",  // ♦
        _                => "?"
    };
}
