// C# port of ext/nanovg/example/demo.c
using System;
using System.Text;
using System.Runtime.InteropServices;
using Sokol;
using static Sokol.NanoVG;
using static Sokol.NanoVG.NVGwinding;
using static Sokol.NanoVG.NVGsolidity;
using static Sokol.NanoVG.NVGlineCap;
using static Sokol.NanoVG.NVGalign;

public static unsafe class Demo
{
    // ── Icon codepoints (Entypo) ──────────────────────────────────────────────
    const int ICON_SEARCH        = 0x1F50D;
    const int ICON_CIRCLED_CROSS = 0x2716;
    const int ICON_CHEVRON_RIGHT = 0xE75E;
    const int ICON_CHECK         = 0x2713;
    const int ICON_LOGIN         = 0xE740;
    const int ICON_TRASH         = 0xE729;

    const float NVG_PI = 3.14159265358979323846f;

    // ── Demo state ─────────────────────────────────────────────────────────────
    /// <summary>Tracks all demo assets (fonts + images). Loaded asynchronously.</summary>
    public sealed class DemoData
    {
        public int fontNormal = -1;
        public int fontBold   = -1;
        public int fontIcons  = -1;
        public int fontEmoji  = -1;
        public int[] images   = new int[12];
        /// <summary>Counts remaining pending async loads; 0 = fully ready.</summary>
        public int pendingLoads;

        public bool IsReady => pendingLoads == 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static float clampf(float a, float mn, float mx) => a < mn ? mn : (a > mx ? mx : a);
    static bool  isBlack(NVGcolor c) => c.r == 0f && c.g == 0f && c.b == 0f && c.a == 0f;

    /// <summary>Encodes a Unicode codepoint as UTF-8 into buf (must have ≥ 8 bytes). Returns byte count.</summary>
    static int CpToUTF8(int cp, byte* buf)
    {
        int n;
        if      (cp < 0x80)       n = 1;
        else if (cp < 0x800)      n = 2;
        else if (cp < 0x10000)    n = 3;
        else if (cp < 0x200000)   n = 4;
        else if (cp < 0x4000000)  n = 5;
        else                      n = 6;
        buf[n] = 0;
        switch (n)
        {
            case 6: buf[5] = (byte)(0x80 | (cp & 0x3f)); cp >>= 6; cp |= 0x4000000; goto case 5;
            case 5: buf[4] = (byte)(0x80 | (cp & 0x3f)); cp >>= 6; cp |= 0x200000;  goto case 4;
            case 4: buf[3] = (byte)(0x80 | (cp & 0x3f)); cp >>= 6; cp |= 0x10000;   goto case 3;
            case 3: buf[2] = (byte)(0x80 | (cp & 0x3f)); cp >>= 6; cp |= 0x800;     goto case 2;
            case 2: buf[1] = (byte)(0x80 | (cp & 0x3f)); cp >>= 6; cp |= 0xc0;      goto case 1;
            case 1: buf[0] = (byte)cp; break;
        }
        return n;
    }

    /// <summary>Returns the advance width of a string (null bounds variant).</summary>
    static float TextWidth(IntPtr vg, string text)
    {
        byte[] b = Encoding.UTF8.GetBytes(text);
        fixed (byte* p = b)
            return nvgTextBounds(vg, 0, 0, p, null, (float*)null);
    }

    // ── Static UTF-8 data for drawParagraph ──────────────────────────────────
    static readonly byte[] s_paragraphText = Encoding.UTF8.GetBytes(
        "This is longer chunk of text.\n  \n  Would have used lorem ipsum but she    was busy jumping over the lazy dog with the fox and all the men who came to the aid of the party.🎉");

    // ═════════════════════════════════════════════════════════════════════════
    // Draw functions
    // ═════════════════════════════════════════════════════════════════════════

    static void DrawWindow(IntPtr vg, string title, float x, float y, float w, float h)
    {
        float cornerRadius = 3.0f;

        nvgSave(vg);

        // Window
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x, y, w, h, cornerRadius);
        nvgFillColor(vg, nvgRGBA(28, 30, 34, 192));
        nvgFill(vg);

        // Drop shadow
        var shadowPaint = nvgBoxGradient(vg, x, y + 2, w, h, cornerRadius * 2, 10, nvgRGBA(0, 0, 0, 128), nvgRGBA(0, 0, 0, 0));
        nvgBeginPath(vg);
        nvgRect(vg, x - 10, y - 10, w + 20, h + 30);
        nvgRoundedRect(vg, x, y, w, h, cornerRadius);
        nvgPathWinding(vg, (int)NVG_HOLE);
        nvgFillPaint(vg, shadowPaint);
        nvgFill(vg);

        // Header
        var headerPaint = nvgLinearGradient(vg, x, y, x, y + 15, nvgRGBA(255, 255, 255, 8), nvgRGBA(0, 0, 0, 16));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 1, y + 1, w - 2, 30, cornerRadius - 1);
        nvgFillPaint(vg, headerPaint);
        nvgFill(vg);
        nvgBeginPath(vg);
        nvgMoveTo(vg, x + 0.5f, y + 0.5f + 30);
        nvgLineTo(vg, x + 0.5f + w - 1, y + 0.5f + 30);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 32));
        nvgStroke(vg);

        nvgFontSize(vg, 15.0f);
        nvgFontFace(vg, "sans-bold");
        nvgTextAlign(vg, (int)(NVG_ALIGN_CENTER | NVG_ALIGN_MIDDLE));

        nvgFontBlur(vg, 2);
        nvgFillColor(vg, nvgRGBA(0, 0, 0, 128));
        nvgText(vg, x + w / 2, y + 16 + 1, title, null);

        nvgFontBlur(vg, 0);
        nvgFillColor(vg, nvgRGBA(220, 220, 220, 160));
        nvgText(vg, x + w / 2, y + 16, title, null);

        nvgRestore(vg);
    }

    static void DrawSearchBox(IntPtr vg, string text, float x, float y, float w, float h)
    {
        float cornerRadius = h / 2 - 1;
        byte* icon = stackalloc byte[8];

        var bg = nvgBoxGradient(vg, x, y + 1.5f, w, h, h / 2, 5, nvgRGBA(0, 0, 0, 16), nvgRGBA(0, 0, 0, 92));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x, y, w, h, cornerRadius);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        nvgFontSize(vg, h * 1.3f);
        nvgFontFace(vg, "icons");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 64));
        nvgTextAlign(vg, (int)(NVG_ALIGN_CENTER | NVG_ALIGN_MIDDLE));
        CpToUTF8(ICON_SEARCH, icon);
        nvgText(vg, x + h * 0.55f, y + h * 0.55f, icon, null);

        nvgFontSize(vg, 17.0f);
        nvgFontFace(vg, "sans");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 32));
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_MIDDLE));
        nvgText(vg, x + h * 1.05f, y + h * 0.5f, text, null);

        nvgFontSize(vg, h * 1.3f);
        nvgFontFace(vg, "icons");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 32));
        nvgTextAlign(vg, (int)(NVG_ALIGN_CENTER | NVG_ALIGN_MIDDLE));
        CpToUTF8(ICON_CIRCLED_CROSS, icon);
        nvgText(vg, x + w - h * 0.55f, y + h * 0.55f, icon, null);
    }

    static void DrawDropDown(IntPtr vg, string text, float x, float y, float w, float h)
    {
        float cornerRadius = 4.0f;
        byte* icon = stackalloc byte[8];

        var bg = nvgLinearGradient(vg, x, y, x, y + h, nvgRGBA(255, 255, 255, 16), nvgRGBA(0, 0, 0, 16));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 1, y + 1, w - 2, h - 2, cornerRadius - 1);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 0.5f, y + 0.5f, w - 1, h - 1, cornerRadius - 0.5f);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 48));
        nvgStroke(vg);

        nvgFontSize(vg, 17.0f);
        nvgFontFace(vg, "sans");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 160));
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_MIDDLE));
        nvgText(vg, x + h * 0.3f, y + h * 0.5f, text, null);

        nvgFontSize(vg, h * 1.3f);
        nvgFontFace(vg, "icons");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 64));
        nvgTextAlign(vg, (int)(NVG_ALIGN_CENTER | NVG_ALIGN_MIDDLE));
        CpToUTF8(ICON_CHEVRON_RIGHT, icon);
        nvgText(vg, x + w - h * 0.5f, y + h * 0.5f, icon, null);
    }

    static void DrawLabel(IntPtr vg, string text, float x, float y, float w, float h)
    {
        nvgFontSize(vg, 15.0f);
        nvgFontFace(vg, "sans");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 128));
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_MIDDLE));
        nvgText(vg, x, y + h * 0.5f, text, null);
    }

    static void DrawEditBoxBase(IntPtr vg, float x, float y, float w, float h)
    {
        var bg = nvgBoxGradient(vg, x + 1, y + 1 + 1.5f, w - 2, h - 2, 3, 4, nvgRGBA(255, 255, 255, 32), nvgRGBA(32, 32, 32, 32));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 1, y + 1, w - 2, h - 2, 4 - 1);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 0.5f, y + 0.5f, w - 1, h - 1, 4 - 0.5f);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 48));
        nvgStroke(vg);
    }

    static void DrawEditBox(IntPtr vg, string text, float x, float y, float w, float h)
    {
        DrawEditBoxBase(vg, x, y, w, h);
        nvgFontSize(vg, 17.0f);
        nvgFontFace(vg, "sans");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 64));
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_MIDDLE));
        nvgText(vg, x + h * 0.3f, y + h * 0.5f, text, null);
    }

    static void DrawEditBoxNum(IntPtr vg, string text, string units, float x, float y, float w, float h)
    {
        DrawEditBoxBase(vg, x, y, w, h);
        float uw = TextWidth(vg, units);

        nvgFontSize(vg, 15.0f);
        nvgFontFace(vg, "sans");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 64));
        nvgTextAlign(vg, (int)(NVG_ALIGN_RIGHT | NVG_ALIGN_MIDDLE));
        nvgText(vg, x + w - h * 0.3f, y + h * 0.5f, units, null);

        nvgFontSize(vg, 17.0f);
        nvgFontFace(vg, "sans");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 128));
        nvgTextAlign(vg, (int)(NVG_ALIGN_RIGHT | NVG_ALIGN_MIDDLE));
        nvgText(vg, x + w - uw - h * 0.5f, y + h * 0.5f, text, null);
    }

    static void DrawCheckBox(IntPtr vg, string text, float x, float y, float w, float h)
    {
        byte* icon = stackalloc byte[8];

        nvgFontSize(vg, 15.0f);
        nvgFontFace(vg, "sans");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 160));
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_MIDDLE));
        nvgText(vg, x + 28, y + h * 0.5f, text, null);

        var bg = nvgBoxGradient(vg, x + 1, y + (int)(h * 0.5f) - 9 + 1, 18, 18, 3, 3, nvgRGBA(0, 0, 0, 32), nvgRGBA(0, 0, 0, 92));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 1, y + (int)(h * 0.5f) - 9, 18, 18, 3);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        nvgFontSize(vg, 33);
        nvgFontFace(vg, "icons");
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 128));
        nvgTextAlign(vg, (int)(NVG_ALIGN_CENTER | NVG_ALIGN_MIDDLE));
        CpToUTF8(ICON_CHECK, icon);
        nvgText(vg, x + 9 + 2, y + h * 0.5f, icon, null);
    }

    static void DrawButton(IntPtr vg, int preicon, string text, float x, float y, float w, float h, NVGcolor col)
    {
        float cornerRadius = 4.0f;
        float tw = 0, iw = 0;
        byte* icon = stackalloc byte[8];

        var bg = nvgLinearGradient(vg, x, y, x, y + h,
            nvgRGBA(255, 255, 255, isBlack(col) ? (byte)16 : (byte)32),
            nvgRGBA(0, 0, 0,       isBlack(col) ? (byte)16 : (byte)32));

        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 1, y + 1, w - 2, h - 2, cornerRadius - 1);
        if (!isBlack(col))
        {
            nvgFillColor(vg, col);
            nvgFill(vg);
        }
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + 0.5f, y + 0.5f, w - 1, h - 1, cornerRadius - 0.5f);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 48));
        nvgStroke(vg);

        nvgFontSize(vg, 17.0f);
        nvgFontFace(vg, "sans-bold");
        tw = TextWidth(vg, text);
        if (preicon != 0)
        {
            nvgFontSize(vg, h * 1.3f);
            nvgFontFace(vg, "icons");
            CpToUTF8(preicon, icon);
            iw = nvgTextBounds(vg, 0, 0, icon, null, (float*)null) + h * 0.15f;
        }

        if (preicon != 0)
        {
            nvgFontSize(vg, h * 1.3f);
            nvgFontFace(vg, "icons");
            nvgFillColor(vg, nvgRGBA(255, 255, 255, 96));
            nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_MIDDLE));
            CpToUTF8(preicon, icon);
            nvgText(vg, x + w * 0.5f - tw * 0.5f - iw * 0.75f, y + h * 0.5f, icon, null);
        }

        nvgFontSize(vg, 17.0f);
        nvgFontFace(vg, "sans-bold");
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_MIDDLE));
        nvgFillColor(vg, nvgRGBA(0, 0, 0, 160));
        nvgText(vg, x + w * 0.5f - tw * 0.5f + iw * 0.25f, y + h * 0.5f - 1, text, null);
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 160));
        nvgText(vg, x + w * 0.5f - tw * 0.5f + iw * 0.25f, y + h * 0.5f, text, null);
    }

    static void DrawSlider(IntPtr vg, float pos, float x, float y, float w, float h)
    {
        float cy = y + (int)(h * 0.5f);
        float kr = (int)(h * 0.25f);

        nvgSave(vg);

        // Slot
        var bg = nvgBoxGradient(vg, x, cy - 2 + 1, w, 4, 2, 2, nvgRGBA(0, 0, 0, 32), nvgRGBA(0, 0, 0, 128));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x, cy - 2, w, 4, 2);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        // Knob shadow
        bg = nvgRadialGradient(vg, x + (int)(pos * w), cy + 1, kr - 3, kr + 3, nvgRGBA(0, 0, 0, 64), nvgRGBA(0, 0, 0, 0));
        nvgBeginPath(vg);
        nvgRect(vg, x + (int)(pos * w) - kr - 5, cy - kr - 5, kr * 2 + 5 + 5, kr * 2 + 5 + 5 + 3);
        nvgCircle(vg, x + (int)(pos * w), cy, kr);
        nvgPathWinding(vg, (int)NVG_HOLE);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        // Knob
        var knob = nvgLinearGradient(vg, x, cy - kr, x, cy + kr, nvgRGBA(255, 255, 255, 16), nvgRGBA(0, 0, 0, 16));
        nvgBeginPath(vg);
        nvgCircle(vg, x + (int)(pos * w), cy, kr - 1);
        nvgFillColor(vg, nvgRGBA(40, 43, 48, 255));
        nvgFill(vg);
        nvgFillPaint(vg, knob);
        nvgFill(vg);

        nvgBeginPath(vg);
        nvgCircle(vg, x + (int)(pos * w), cy, kr - 0.5f);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 92));
        nvgStroke(vg);

        nvgRestore(vg);
    }

    static void DrawEyes(IntPtr vg, float x, float y, float w, float h, float mx, float my, float t)
    {
        float ex = w * 0.23f;
        float ey = h * 0.5f;
        float lx = x + ex, ly = y + ey;
        float rx = x + w - ex, ry = y + ey;
        float dx, dy, d;
        float br = (ex < ey ? ex : ey) * 0.5f;
        float blink = 1 - (float)Math.Pow(Math.Sin(t * 0.5f), 200) * 0.8f;

        var bg = nvgLinearGradient(vg, x, y + h * 0.5f, x + w * 0.1f, y + h, nvgRGBA(0, 0, 0, 32), nvgRGBA(0, 0, 0, 16));
        nvgBeginPath(vg);
        nvgEllipse(vg, lx + 3.0f, ly + 16.0f, ex, ey);
        nvgEllipse(vg, rx + 3.0f, ry + 16.0f, ex, ey);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        bg = nvgLinearGradient(vg, x, y + h * 0.25f, x + w * 0.1f, y + h, nvgRGBA(220, 220, 220, 255), nvgRGBA(128, 128, 128, 255));
        nvgBeginPath(vg);
        nvgEllipse(vg, lx, ly, ex, ey);
        nvgEllipse(vg, rx, ry, ex, ey);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        dx = (mx - rx) / (ex * 10);
        dy = (my - ry) / (ey * 10);
        d = (float)Math.Sqrt(dx * dx + dy * dy);
        if (d > 1.0f) { dx /= d; dy /= d; }
        dx *= ex * 0.4f;
        dy *= ey * 0.5f;
        nvgBeginPath(vg);
        nvgEllipse(vg, lx + dx, ly + dy + ey * 0.25f * (1 - blink), br, br * blink);
        nvgFillColor(vg, nvgRGBA(32, 32, 32, 255));
        nvgFill(vg);

        dx = (mx - rx) / (ex * 10);
        dy = (my - ry) / (ey * 10);
        d = (float)Math.Sqrt(dx * dx + dy * dy);
        if (d > 1.0f) { dx /= d; dy /= d; }
        dx *= ex * 0.4f;
        dy *= ey * 0.5f;
        nvgBeginPath(vg);
        nvgEllipse(vg, rx + dx, ry + dy + ey * 0.25f * (1 - blink), br, br * blink);
        nvgFillColor(vg, nvgRGBA(32, 32, 32, 255));
        nvgFill(vg);

        var gloss = nvgRadialGradient(vg, lx - ex * 0.25f, ly - ey * 0.5f, ex * 0.1f, ex * 0.75f, nvgRGBA(255, 255, 255, 128), nvgRGBA(255, 255, 255, 0));
        nvgBeginPath(vg);
        nvgEllipse(vg, lx, ly, ex, ey);
        nvgFillPaint(vg, gloss);
        nvgFill(vg);

        gloss = nvgRadialGradient(vg, rx - ex * 0.25f, ry - ey * 0.5f, ex * 0.1f, ex * 0.75f, nvgRGBA(255, 255, 255, 128), nvgRGBA(255, 255, 255, 0));
        nvgBeginPath(vg);
        nvgEllipse(vg, rx, ry, ex, ey);
        nvgFillPaint(vg, gloss);
        nvgFill(vg);
    }

    static void DrawGraph(IntPtr vg, float x, float y, float w, float h, float t)
    {
        float* samples = stackalloc float[6];
        float* sx = stackalloc float[6];
        float* sy = stackalloc float[6];
        float dx = w / 5.0f;

        samples[0] = (1 + (float)Math.Sin(t * 1.2345f + Math.Cos(t * 0.33457f) * 0.44f)) * 0.5f;
        samples[1] = (1 + (float)Math.Sin(t * 0.68363f + Math.Cos(t * 1.3f) * 1.55f)) * 0.5f;
        samples[2] = (1 + (float)Math.Sin(t * 1.1642f + Math.Cos(t * 0.33457) * 1.24f)) * 0.5f;
        samples[3] = (1 + (float)Math.Sin(t * 0.56345f + Math.Cos(t * 1.63f) * 0.14f)) * 0.5f;
        samples[4] = (1 + (float)Math.Sin(t * 1.6245f + Math.Cos(t * 0.254f) * 0.3f)) * 0.5f;
        samples[5] = (1 + (float)Math.Sin(t * 0.345f + Math.Cos(t * 0.03f) * 0.6f)) * 0.5f;

        for (int i = 0; i < 6; i++)
        {
            sx[i] = x + i * dx;
            sy[i] = y + h * samples[i] * 0.8f;
        }

        // Graph background
        var bg = nvgLinearGradient(vg, x, y, x, y + h, nvgRGBA(0, 160, 192, 0), nvgRGBA(0, 160, 192, 64));
        nvgBeginPath(vg);
        nvgMoveTo(vg, sx[0], sy[0]);
        for (int i = 1; i < 6; i++)
            nvgBezierTo(vg, sx[i - 1] + dx * 0.5f, sy[i - 1], sx[i] - dx * 0.5f, sy[i], sx[i], sy[i]);
        nvgLineTo(vg, x + w, y + h);
        nvgLineTo(vg, x, y + h);
        nvgFillPaint(vg, bg);
        nvgFill(vg);

        // Graph line shadow
        nvgBeginPath(vg);
        nvgMoveTo(vg, sx[0], sy[0] + 2);
        for (int i = 1; i < 6; i++)
            nvgBezierTo(vg, sx[i - 1] + dx * 0.5f, sy[i - 1] + 2, sx[i] - dx * 0.5f, sy[i] + 2, sx[i], sy[i] + 2);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 32));
        nvgStrokeWidth(vg, 3.0f);
        nvgStroke(vg);

        // Graph line
        nvgBeginPath(vg);
        nvgMoveTo(vg, sx[0], sy[0]);
        for (int i = 1; i < 6; i++)
            nvgBezierTo(vg, sx[i - 1] + dx * 0.5f, sy[i - 1], sx[i] - dx * 0.5f, sy[i], sx[i], sy[i]);
        nvgStrokeColor(vg, nvgRGBA(0, 160, 192, 255));
        nvgStrokeWidth(vg, 3.0f);
        nvgStroke(vg);

        // Sample position shadows
        for (int i = 0; i < 6; i++)
        {
            bg = nvgRadialGradient(vg, sx[i], sy[i] + 2, 3.0f, 8.0f, nvgRGBA(0, 0, 0, 32), nvgRGBA(0, 0, 0, 0));
            nvgBeginPath(vg);
            nvgRect(vg, sx[i] - 10, sy[i] - 10 + 2, 20, 20);
            nvgFillPaint(vg, bg);
            nvgFill(vg);
        }

        nvgBeginPath(vg);
        for (int i = 0; i < 6; i++)
            nvgCircle(vg, sx[i], sy[i], 4.0f);
        nvgFillColor(vg, nvgRGBA(0, 160, 192, 255));
        nvgFill(vg);
        nvgBeginPath(vg);
        for (int i = 0; i < 6; i++)
            nvgCircle(vg, sx[i], sy[i], 2.0f);
        nvgFillColor(vg, nvgRGBA(220, 220, 220, 255));
        nvgFill(vg);

        nvgStrokeWidth(vg, 1.0f);
    }

    static void DrawSpinner(IntPtr vg, float cx, float cy, float r, float t)
    {
        float a0 = 0.0f + t * 6;
        float a1 = NVG_PI + t * 6;
        float r0 = r;
        float r1 = r * 0.75f;
        float ax = cx + (float)Math.Cos(a0) * (r0 + r1) * 0.5f;
        float ay = cy + (float)Math.Sin(a0) * (r0 + r1) * 0.5f;
        float bx = cx + (float)Math.Cos(a1) * (r0 + r1) * 0.5f;
        float by = cy + (float)Math.Sin(a1) * (r0 + r1) * 0.5f;

        nvgSave(vg);
        nvgBeginPath(vg);
        nvgArc(vg, cx, cy, r0, a0, a1, (int)NVG_CW);
        nvgArc(vg, cx, cy, r1, a1, a0, (int)NVG_CCW);
        nvgClosePath(vg);
        var paint = nvgLinearGradient(vg, ax, ay, bx, by, nvgRGBA(0, 0, 0, 0), nvgRGBA(0, 0, 0, 128));
        nvgFillPaint(vg, paint);
        nvgFill(vg);
        nvgRestore(vg);
    }

    static void DrawThumbnails(IntPtr vg, float x, float y, float w, float h, int[] images, int nimages, float t)
    {
        float cornerRadius = 3.0f;
        float thumb = 60.0f;
        float arry = 30.5f;
        int imgw, imgh;
        float stackh = (nimages / 2) * (thumb + 10) + 10;
        float u  = (1 + (float)Math.Cos(t * 0.5f)) * 0.5f;
        float u2 = (1 - (float)Math.Cos(t * 0.2f)) * 0.5f;
        float ix, iy, iw, ih;
        float scrollh, dv;

        nvgSave(vg);

        // Drop shadow
        var shadowPaint = nvgBoxGradient(vg, x, y + 4, w, h, cornerRadius * 2, 20, nvgRGBA(0, 0, 0, 128), nvgRGBA(0, 0, 0, 0));
        nvgBeginPath(vg);
        nvgRect(vg, x - 10, y - 10, w + 20, h + 30);
        nvgRoundedRect(vg, x, y, w, h, cornerRadius);
        nvgPathWinding(vg, (int)NVG_HOLE);
        nvgFillPaint(vg, shadowPaint);
        nvgFill(vg);

        // Window
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x, y, w, h, cornerRadius);
        nvgMoveTo(vg, x - 10, y + arry);
        nvgLineTo(vg, x + 1, y + arry - 11);
        nvgLineTo(vg, x + 1, y + arry + 11);
        nvgFillColor(vg, nvgRGBA(200, 200, 200, 255));
        nvgFill(vg);

        nvgSave(vg);
        nvgScissor(vg, x, y, w, h);
        nvgTranslate(vg, 0, -(stackh - h) * u);

        dv = 1.0f / (float)(nimages - 1);

        for (int i = 0; i < nimages; i++)
        {
            float tx = x + 10;
            float ty = y + 10;
            tx += (i % 2) * (thumb + 10);
            ty += (i / 2) * (thumb + 10);

            imgw = 0; imgh = 0;
            nvgImageSize(vg, images[i], ref imgw, ref imgh);
            if (imgw < imgh)
            {
                iw = thumb; ih = iw * (float)imgh / (float)imgw;
                ix = 0; iy = -(ih - thumb) * 0.5f;
            }
            else
            {
                ih = thumb; iw = ih * (float)imgw / (float)imgh;
                ix = -(iw - thumb) * 0.5f; iy = 0;
            }

            float v = i * dv;
            float a = clampf((u2 - v) / dv, 0, 1);

            if (a < 1.0f)
                DrawSpinner(vg, tx + thumb / 2, ty + thumb / 2, thumb * 0.25f, t);

            var imgPaint = nvgImagePattern(vg, tx + ix, ty + iy, iw, ih, 0.0f / 180.0f * NVG_PI, images[i], a);
            nvgBeginPath(vg);
            nvgRoundedRect(vg, tx, ty, thumb, thumb, 5);
            nvgFillPaint(vg, imgPaint);
            nvgFill(vg);

            shadowPaint = nvgBoxGradient(vg, tx - 1, ty, thumb + 2, thumb + 2, 5, 3, nvgRGBA(0, 0, 0, 128), nvgRGBA(0, 0, 0, 0));
            nvgBeginPath(vg);
            nvgRect(vg, tx - 5, ty - 5, thumb + 10, thumb + 10);
            nvgRoundedRect(vg, tx, ty, thumb, thumb, 6);
            nvgPathWinding(vg, (int)NVG_HOLE);
            nvgFillPaint(vg, shadowPaint);
            nvgFill(vg);

            nvgBeginPath(vg);
            nvgRoundedRect(vg, tx + 0.5f, ty + 0.5f, thumb - 1, thumb - 1, 4 - 0.5f);
            nvgStrokeWidth(vg, 1.0f);
            nvgStrokeColor(vg, nvgRGBA(255, 255, 255, 192));
            nvgStroke(vg);
        }
        nvgRestore(vg);

        // Hide fades
        var fadePaint = nvgLinearGradient(vg, x, y, x, y + 6, nvgRGBA(200, 200, 200, 255), nvgRGBA(200, 200, 200, 0));
        nvgBeginPath(vg);
        nvgRect(vg, x + 4, y, w - 8, 6);
        nvgFillPaint(vg, fadePaint);
        nvgFill(vg);

        fadePaint = nvgLinearGradient(vg, x, y + h, x, y + h - 6, nvgRGBA(200, 200, 200, 255), nvgRGBA(200, 200, 200, 0));
        nvgBeginPath(vg);
        nvgRect(vg, x + 4, y + h - 6, w - 8, 6);
        nvgFillPaint(vg, fadePaint);
        nvgFill(vg);

        // Scroll bar
        shadowPaint = nvgBoxGradient(vg, x + w - 12 + 1, y + 4 + 1, 8, h - 8, 3, 4, nvgRGBA(0, 0, 0, 32), nvgRGBA(0, 0, 0, 92));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + w - 12, y + 4, 8, h - 8, 3);
        nvgFillPaint(vg, shadowPaint);
        nvgFill(vg);

        scrollh = (h / stackh) * (h - 8);
        shadowPaint = nvgBoxGradient(vg, x + w - 12 - 1, y + 4 + (h - 8 - scrollh) * u - 1, 8, scrollh, 3, 4, nvgRGBA(220, 220, 220, 255), nvgRGBA(128, 128, 128, 255));
        nvgBeginPath(vg);
        nvgRoundedRect(vg, x + w - 12 + 1, y + 4 + 1 + (h - 8 - scrollh) * u, 8 - 2, scrollh - 2, 2);
        nvgFillPaint(vg, shadowPaint);
        nvgFill(vg);

        nvgRestore(vg);
    }

    static void DrawColorwheel(IntPtr vg, float x, float y, float w, float h, float t)
    {
        float r0, r1, ax, ay, bx, by, cx, cy, aeps, r;
        float hue = (float)Math.Sin(t * 0.12f);
        cx = x + w * 0.5f;
        cy = y + h * 0.5f;
        r1 = (w < h ? w : h) * 0.5f - 5.0f;
        r0 = r1 - 20.0f;
        aeps = 0.5f / r1;

        nvgSave(vg);

        for (int i = 0; i < 6; i++)
        {
            float a0 = (float)i / 6.0f * NVG_PI * 2.0f - aeps;
            float a1 = (float)(i + 1.0f) / 6.0f * NVG_PI * 2.0f + aeps;
            nvgBeginPath(vg);
            nvgArc(vg, cx, cy, r0, a0, a1, (int)NVG_CW);
            nvgArc(vg, cx, cy, r1, a1, a0, (int)NVG_CCW);
            nvgClosePath(vg);
            ax = cx + (float)Math.Cos(a0) * (r0 + r1) * 0.5f;
            ay = cy + (float)Math.Sin(a0) * (r0 + r1) * 0.5f;
            bx = cx + (float)Math.Cos(a1) * (r0 + r1) * 0.5f;
            by = cy + (float)Math.Sin(a1) * (r0 + r1) * 0.5f;
            var paint = nvgLinearGradient(vg, ax, ay, bx, by,
                nvgHSLA(a0 / (NVG_PI * 2), 1.0f, 0.55f, 255),
                nvgHSLA(a1 / (NVG_PI * 2), 1.0f, 0.55f, 255));
            nvgFillPaint(vg, paint);
            nvgFill(vg);
        }

        nvgBeginPath(vg);
        nvgCircle(vg, cx, cy, r0 - 0.5f);
        nvgCircle(vg, cx, cy, r1 + 0.5f);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 64));
        nvgStrokeWidth(vg, 1.0f);
        nvgStroke(vg);

        // Selector
        nvgSave(vg);
        nvgTranslate(vg, cx, cy);
        nvgRotate(vg, hue * NVG_PI * 2);

        nvgStrokeWidth(vg, 2.0f);
        nvgBeginPath(vg);
        nvgRect(vg, r0 - 1, -3, r1 - r0 + 2, 6);
        nvgStrokeColor(vg, nvgRGBA(255, 255, 255, 192));
        nvgStroke(vg);

        var shadowPaint = nvgBoxGradient(vg, r0 - 3, -5, r1 - r0 + 6, 10, 2, 4, nvgRGBA(0, 0, 0, 128), nvgRGBA(0, 0, 0, 0));
        nvgBeginPath(vg);
        nvgRect(vg, r0 - 2 - 10, -4 - 10, r1 - r0 + 4 + 20, 8 + 20);
        nvgRect(vg, r0 - 2, -4, r1 - r0 + 4, 8);
        nvgPathWinding(vg, (int)NVG_HOLE);
        nvgFillPaint(vg, shadowPaint);
        nvgFill(vg);

        // Center triangle
        r = r0 - 6;
        ax = (float)Math.Cos(120.0f / 180.0f * NVG_PI) * r;
        ay = (float)Math.Sin(120.0f / 180.0f * NVG_PI) * r;
        bx = (float)Math.Cos(-120.0f / 180.0f * NVG_PI) * r;
        by = (float)Math.Sin(-120.0f / 180.0f * NVG_PI) * r;
        nvgBeginPath(vg);
        nvgMoveTo(vg, r, 0);
        nvgLineTo(vg, ax, ay);
        nvgLineTo(vg, bx, by);
        nvgClosePath(vg);
        var triPaint = nvgLinearGradient(vg, r, 0, ax, ay, nvgHSLA(hue, 1.0f, 0.5f, 255), nvgRGBA(255, 255, 255, 255));
        nvgFillPaint(vg, triPaint);
        nvgFill(vg);
        triPaint = nvgLinearGradient(vg, (r + ax) * 0.5f, (0 + ay) * 0.5f, bx, by, nvgRGBA(0, 0, 0, 0), nvgRGBA(0, 0, 0, 255));
        nvgFillPaint(vg, triPaint);
        nvgFill(vg);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 64));
        nvgStroke(vg);

        // Select circle on triangle
        ax = (float)Math.Cos(120.0f / 180.0f * NVG_PI) * r * 0.3f;
        ay = (float)Math.Sin(120.0f / 180.0f * NVG_PI) * r * 0.4f;
        nvgStrokeWidth(vg, 2.0f);
        nvgBeginPath(vg);
        nvgCircle(vg, ax, ay, 5);
        nvgStrokeColor(vg, nvgRGBA(255, 255, 255, 192));
        nvgStroke(vg);

        var radPaint = nvgRadialGradient(vg, ax, ay, 7, 9, nvgRGBA(0, 0, 0, 64), nvgRGBA(0, 0, 0, 0));
        nvgBeginPath(vg);
        nvgRect(vg, ax - 20, ay - 20, 40, 40);
        nvgCircle(vg, ax, ay, 7);
        nvgPathWinding(vg, (int)NVG_HOLE);
        nvgFillPaint(vg, radPaint);
        nvgFill(vg);

        nvgRestore(vg);
        nvgRestore(vg);
    }

    static void DrawLines(IntPtr vg, float x, float y, float w, float h, float t)
    {
        float pad = 5.0f, s = w / 9.0f - pad * 2;
        float* pts = stackalloc float[8];
        int* joins = stackalloc int[3] { (int)NVG_MITER, (int)NVG_ROUND, (int)NVG_BEVEL };
        int* caps  = stackalloc int[3] { (int)NVG_BUTT,  (int)NVG_ROUND, (int)NVG_SQUARE };

        nvgSave(vg);
        pts[0] = -s * 0.25f + (float)Math.Cos(t * 0.3f) * s * 0.5f;
        pts[1] = (float)Math.Sin(t * 0.3f) * s * 0.5f;
        pts[2] = -s * 0.25f;
        pts[3] = 0;
        pts[4] = s * 0.25f;
        pts[5] = 0;
        pts[6] = s * 0.25f + (float)Math.Cos(-t * 0.3f) * s * 0.5f;
        pts[7] = (float)Math.Sin(-t * 0.3f) * s * 0.5f;

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                float fx = x + s * 0.5f + (i * 3 + j) / 9.0f * w + pad;
                float fy = y - s * 0.5f + pad;

                nvgLineCap(vg, caps[i]);
                nvgLineJoin(vg, joins[j]);

                nvgStrokeWidth(vg, s * 0.3f);
                nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 160));
                nvgBeginPath(vg);
                nvgMoveTo(vg, fx + pts[0], fy + pts[1]);
                nvgLineTo(vg, fx + pts[2], fy + pts[3]);
                nvgLineTo(vg, fx + pts[4], fy + pts[5]);
                nvgLineTo(vg, fx + pts[6], fy + pts[7]);
                nvgStroke(vg);

                nvgLineCap(vg, (int)NVG_BUTT);
                nvgLineJoin(vg, (int)NVG_BEVEL);

                nvgStrokeWidth(vg, 1.0f);
                nvgStrokeColor(vg, nvgRGBA(0, 192, 255, 255));
                nvgBeginPath(vg);
                nvgMoveTo(vg, fx + pts[0], fy + pts[1]);
                nvgLineTo(vg, fx + pts[2], fy + pts[3]);
                nvgLineTo(vg, fx + pts[4], fy + pts[5]);
                nvgLineTo(vg, fx + pts[6], fy + pts[7]);
                nvgStroke(vg);
            }
        }

        nvgRestore(vg);
    }

    static void DrawParagraph(IntPtr vg, float x, float y, float width, float height, float mx, float my)
    {
        const int MAX_ROWS   = 3;
        const int MAX_GLYPHS = 100;
        float* bounds = stackalloc float[4];
        float lineh = 0;
        int lnum = 0;
        float gx = 0, gy = 0;
        int gutter = 0;
        const string hoverText = "Hover your mouse over the text to see calculated caret position.";

        nvgSave(vg);
        nvgFontSize(vg, 15.0f);
        nvgFontFace(vg, "sans");
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_TOP));
        float _asc = 0f, _desc = 0f;
        nvgTextMetrics(vg, ref _asc, ref _desc, ref lineh);

        fixed (byte* pText = s_paragraphText)
        {
            byte* start = pText;
            byte* end   = pText + s_paragraphText.Length;

            NVGtextRowRaw* rows = stackalloc NVGtextRowRaw[MAX_ROWS];
            int nrows;
            while ((nrows = nvgTextBreakLines(vg, start, end, width, rows, MAX_ROWS)) != 0)
            {
                for (int i = 0; i < nrows; i++)
                {
                    NVGtextRowRaw* row = &rows[i];
                    bool hit = mx > x && mx < (x + width) && my >= y && my < (y + lineh);

                    nvgBeginPath(vg);
                    nvgFillColor(vg, nvgRGBA(255, 255, 255, hit ? (byte)64 : (byte)16));
                    nvgRect(vg, x + row->minx, y, row->maxx - row->minx, lineh);
                    nvgFill(vg);

                    nvgFillColor(vg, nvgRGBA(255, 255, 255, 255));
                    nvgText(vg, x, y, row->start, row->end);

                    if (hit)
                    {
                        float caretx = (mx < x + row->width / 2) ? x : x + row->width;
                        float px = x;
                        NVGglyphPositionRaw* glyphs = stackalloc NVGglyphPositionRaw[MAX_GLYPHS];
                        int nglyphs = nvgTextGlyphPositions(vg, x, y, row->start, row->end, glyphs, MAX_GLYPHS);
                        for (int j = 0; j < nglyphs; j++)
                        {
                            float x0 = glyphs[j].x;
                            float x1 = (j + 1 < nglyphs) ? glyphs[j + 1].x : x + row->width;
                            float gxc = x0 * 0.3f + x1 * 0.7f;
                            if (mx >= px && mx < gxc)
                                caretx = glyphs[j].x;
                            px = gxc;
                        }
                        nvgBeginPath(vg);
                        nvgFillColor(vg, nvgRGBA(255, 192, 0, 255));
                        nvgRect(vg, caretx, y, 1, lineh);
                        nvgFill(vg);

                        gutter = lnum + 1;
                        gx = x - 10;
                        gy = y + lineh / 2;
                    }
                    lnum++;
                    y += lineh;
                }
                start = rows[nrows - 1].next;
            }
        }

        if (gutter != 0)
        {
            string txt = gutter.ToString();
            nvgFontSize(vg, 12.0f);
            nvgTextAlign(vg, (int)(NVG_ALIGN_RIGHT | NVG_ALIGN_MIDDLE));

            byte[] txtBytes = Encoding.UTF8.GetBytes(txt);
            fixed (byte* pTxt = txtBytes)
                nvgTextBounds(vg, gx, gy, pTxt, null, bounds);

            nvgBeginPath(vg);
            nvgFillColor(vg, nvgRGBA(255, 192, 0, 255));
            nvgRoundedRect(vg,
                (int)bounds[0] - 4,   (int)bounds[1] - 2,
                (int)(bounds[2] - bounds[0]) + 8,
                (int)(bounds[3] - bounds[1]) + 4,
                ((int)(bounds[3] - bounds[1]) + 4) / 2 - 1);
            nvgFill(vg);

            nvgFillColor(vg, nvgRGBA(32, 32, 32, 255));
            nvgText(vg, gx, gy, txt, null);
        }

        y += 20.0f;

        nvgFontSize(vg, 11.0f);
        nvgTextAlign(vg, (int)(NVG_ALIGN_LEFT | NVG_ALIGN_TOP));
        nvgTextLineHeight(vg, 1.2f);

        byte[] hoverBytes = Encoding.UTF8.GetBytes(hoverText);
        fixed (byte* pHover = hoverBytes)
            nvgTextBoxBounds(vg, x, y, 150, pHover, null, bounds);

        float ga = (float)Math.Sqrt(
            Math.Pow(clampf(mx, bounds[0], bounds[2]) - mx, 2) +
            Math.Pow(clampf(my, bounds[1], bounds[3]) - my, 2)) / 30.0f;
        ga = clampf(ga, 0, 1);
        nvgGlobalAlpha(vg, ga);

        nvgBeginPath(vg);
        nvgFillColor(vg, nvgRGBA(220, 220, 220, 255));
        nvgRoundedRect(vg, bounds[0] - 2, bounds[1] - 2,
            (int)(bounds[2] - bounds[0]) + 4, (int)(bounds[3] - bounds[1]) + 4, 3);
        float px2 = (int)((bounds[2] + bounds[0]) / 2);
        nvgMoveTo(vg, px2, bounds[1] - 10);
        nvgLineTo(vg, px2 + 7, bounds[1] + 1);
        nvgLineTo(vg, px2 - 7, bounds[1] + 1);
        nvgFill(vg);

        nvgFillColor(vg, nvgRGBA(0, 0, 0, 220));
        fixed (byte* pHover = hoverBytes)
            nvgTextBox(vg, x, y, 150, pHover, null);

        nvgRestore(vg);
    }

    static void DrawWidths(IntPtr vg, float x, float y, float width)
    {
        nvgSave(vg);
        nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 255));
        for (int i = 0; i < 20; i++)
        {
            float w2 = (i + 0.5f) * 0.1f;
            nvgStrokeWidth(vg, w2);
            nvgBeginPath(vg);
            nvgMoveTo(vg, x, y);
            nvgLineTo(vg, x + width, y + width * 0.3f);
            nvgStroke(vg);
            y += 10;
        }
        nvgRestore(vg);
    }

    static void DrawCaps(IntPtr vg, float x, float y, float width)
    {
        int* caps = stackalloc int[3] { (int)NVG_BUTT, (int)NVG_ROUND, (int)NVG_SQUARE };
        float lineWidth = 8.0f;

        nvgSave(vg);

        nvgBeginPath(vg);
        nvgRect(vg, x - lineWidth / 2, y, width + lineWidth, 40);
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 32));
        nvgFill(vg);

        nvgBeginPath(vg);
        nvgRect(vg, x, y, width, 40);
        nvgFillColor(vg, nvgRGBA(255, 255, 255, 32));
        nvgFill(vg);

        nvgStrokeWidth(vg, lineWidth);
        for (int i = 0; i < 3; i++)
        {
            nvgLineCap(vg, caps[i]);
            nvgStrokeColor(vg, nvgRGBA(0, 0, 0, 255));
            nvgBeginPath(vg);
            nvgMoveTo(vg, x, y + i * 10 + 5);
            nvgLineTo(vg, x + width, y + i * 10 + 5);
            nvgStroke(vg);
        }

        nvgRestore(vg);
    }

    static void DrawScissor(IntPtr vg, float x, float y, float t)
    {
        nvgSave(vg);

        nvgTranslate(vg, x, y);
        nvgRotate(vg, nvgDegToRad(5));
        nvgBeginPath(vg);
        nvgRect(vg, -20, -20, 60, 40);
        nvgFillColor(vg, nvgRGBA(255, 0, 0, 255));
        nvgFill(vg);
        nvgScissor(vg, -20, -20, 60, 40);

        nvgTranslate(vg, 40, 0);
        nvgRotate(vg, t);

        // Unscissored orange overlay
        nvgSave(vg);
        nvgResetScissor(vg);
        nvgBeginPath(vg);
        nvgRect(vg, -20, -10, 60, 30);
        nvgFillColor(vg, nvgRGBA(255, 128, 0, 64));
        nvgFill(vg);
        nvgRestore(vg);

        // Scissored orange fill
        nvgIntersectScissor(vg, -20, -10, 60, 30);
        nvgBeginPath(vg);
        nvgRect(vg, -20, -10, 60, 30);
        nvgFillColor(vg, nvgRGBA(255, 128, 0, 255));
        nvgFill(vg);

        nvgRestore(vg);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Begin async loading of demo fonts and images via FileSystem.
    /// Check <see cref="DemoData.IsReady"/> each frame; rendering before that is safe
    /// (missing assets are simply absent).
    /// </summary>
    public static DemoData LoadDemoData(IntPtr vg)
    {
        var data = new DemoData();
        // 4 fonts + 12 images
        data.pendingLoads = 16;

        // ── Fonts (loaded into NanoVG from memory so paths are asset-relative) ──
        FileSystem.Instance.LoadFile("fonts/entypo.ttf", (path, bytes, status) =>
        {
            if (status == FileLoadStatus.Success && bytes != null)
                fixed (byte* ptr = bytes)
                    data.fontIcons = nvgCreateFontMem(vg, "icons", ptr, bytes.Length, 0);
            data.pendingLoads--;
        }, 512 * 1024);

        FileSystem.Instance.LoadFile("fonts/Roboto-Regular.ttf", (path, bytes, status) =>
        {
            if (status == FileLoadStatus.Success && bytes != null)
                fixed (byte* ptr = bytes)
                    data.fontNormal = nvgCreateFontMem(vg, "sans", ptr, bytes.Length, 0);
            data.pendingLoads--;
        }, 512 * 1024);

        FileSystem.Instance.LoadFile("fonts/Roboto-Bold.ttf", (path, bytes, status) =>
        {
            if (status == FileLoadStatus.Success && bytes != null)
                fixed (byte* ptr = bytes)
                    data.fontBold = nvgCreateFontMem(vg, "sans-bold", ptr, bytes.Length, 0);
            data.pendingLoads--;
        }, 512 * 1024);

        FileSystem.Instance.LoadFile("fonts/NotoEmoji-Regular.ttf", (path, bytes, status) =>
        {
            if (status == FileLoadStatus.Success && bytes != null)
            {
                fixed (byte* ptr = bytes)
                    data.fontEmoji = nvgCreateFontMem(vg, "emoji", ptr, bytes.Length, 0);
                // Add emoji as fallback for normal and bold once they are known
                if (data.fontNormal != -1) nvgAddFallbackFontId(vg, data.fontNormal, data.fontEmoji);
                if (data.fontBold   != -1) nvgAddFallbackFontId(vg, data.fontBold,   data.fontEmoji);
            }
            data.pendingLoads--;
        }, 2 * 1024 * 1024);

        // ── Images ───────────────────────────────────────────────────────────────
        for (int idx = 0; idx < 12; idx++)
        {
            int capturedIdx = idx;
            string imgPath = $"images/image{idx + 1}.jpg";
            FileSystem.Instance.LoadFile(imgPath, (path, bytes, status) =>
            {
                if (status == FileLoadStatus.Success && bytes != null)
                    fixed (byte* ptr = bytes)
                        data.images[capturedIdx] = nvgCreateImageMem(vg, 0, ptr, bytes.Length);
                data.pendingLoads--;
            }, 512 * 1024);
        }

        return data;
    }

    /// <summary>Releases all NanoVG image handles owned by DemoData.</summary>
    public static void FreeDemoData(IntPtr vg, DemoData data)
    {
        if (vg == IntPtr.Zero || data == null) return;
        for (int i = 0; i < data.images.Length; i++)
            if (data.images[i] != 0)
                nvgDeleteImage(vg, data.images[i]);
    }

    /// <summary>
    /// Main demo render call — equivalent to renderDemo() in demo.c.
    /// Safe to call before all assets are loaded; missing assets produce blank slots.
    /// </summary>
    public static void RenderDemo(IntPtr vg, float mx, float my, float width, float height, float t, bool blowup, DemoData data)
    {
        DrawEyes(vg, width - 250, 50, 150, 100, mx, my, t);
        DrawParagraph(vg, width - 450, 50, 150, 100, mx, my);
        DrawGraph(vg, 0, height / 2, width, height / 2, t);
        DrawColorwheel(vg, width - 300, height - 300, 250.0f, 250.0f, t);

        DrawLines(vg, 120, height - 50, 600, 50, t);
        DrawWidths(vg, 10, 50, 30);
        DrawCaps(vg, 10, 300, 30);
        DrawScissor(vg, 50, height - 80, t);

        nvgSave(vg);
        if (blowup)
        {
            nvgRotate(vg, (float)Math.Sin(t * 0.3f) * 5.0f / 180.0f * NVG_PI);
            nvgScale(vg, 2.0f, 2.0f);
        }

        // Widgets
        DrawWindow(vg, "Widgets `n Stuff", 50, 50, 300, 400);
        float wx = 60, wy = 95;
        DrawSearchBox(vg, "Search", wx, wy, 280, 25);
        wy += 40;
        DrawDropDown(vg, "Effects", wx, wy, 280, 28);
        float popy = wy + 14;
        wy += 45;

        DrawLabel(vg, "Login", wx, wy, 280, 20);
        wy += 25;
        DrawEditBox(vg, "Email", wx, wy, 280, 28);
        wy += 35;
        DrawEditBox(vg, "Password", wx, wy, 280, 28);
        wy += 38;
        DrawCheckBox(vg, "Remember me", wx, wy, 140, 28);
        DrawButton(vg, ICON_LOGIN, "Sign in", wx + 138, wy, 140, 28, nvgRGBA(0, 96, 128, 255));
        wy += 45;

        DrawLabel(vg, "Diameter", wx, wy, 280, 20);
        wy += 25;
        DrawEditBoxNum(vg, "123.00", "px", wx + 180, wy, 100, 28);
        DrawSlider(vg, 0.4f, wx, wy, 170, 28);
        wy += 55;

        DrawButton(vg, ICON_TRASH,  "Delete", wx,       wy, 160, 28, nvgRGBA(128, 16, 8, 255));
        DrawButton(vg, 0,           "Cancel", wx + 170, wy, 110, 28, nvgRGBA(0,   0,  0, 0));

        DrawThumbnails(vg, 365, popy - 30, 160, 300, data.images, 12, t);

        nvgRestore(vg);
    }
}
