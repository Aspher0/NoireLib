using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Letter-spaced text, which ImGui has no notion of.
/// </summary>
public static partial class NoireText
{
    /// <summary>The tracking a caps label wants when nothing else is said, in ems.</summary>
    public const float CapsTracking = 0.26f;

    /// <summary>
    /// Draws text with extra space between its characters, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    /// <returns>The size the run occupies.</returns>
    public static Vector2 Tracked(string text, float tracking = CapsTracking, TextSize size = TextSize.Body)
        => Tracked(text, tracking, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Draws text with extra space between its characters, at an explicit size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="sizePx">The size at 100%.</param>
    /// <returns>The size the run occupies.</returns>
    public static Vector2 Tracked(string text, float tracking, float sizePx)
    {
        if (string.IsNullOrEmpty(text))
            return Vector2.Zero;

        NoireUI.EnsureFrameServices();

        // Read before the font is pushed, so the key matches the one a measurement of the same run is asked under.
        var ambient = ImGui.GetFontSize();
        var size = InFont(sizePx, text, tracking, paint: true);

        // Remembered although this call did not need it. Placing the glyphs measures the run as a by-product, and a
        // window that draws a label and states its width elsewhere, such as a heading with a rule running off the end
        // of it, would otherwise measure it a second time and push its font again to do so.
        UiTextMeasureCache.StoreTrackedSize(text, tracking, sizePx, ambient, size);

        return size;
    }

    /// <summary>
    /// Measures text as it would be drawn with tracking, at a named size.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="size">The step of the type scale to measure at.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 TrackedSize(string text, float tracking = CapsTracking, TextSize size = TextSize.Body)
        => TrackedSize(text, tracking, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Measures text as it would be drawn with tracking, at an explicit size.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="sizePx">The size at 100%.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 TrackedSize(string text, float tracking, float sizePx)
    {
        // Not about the font, which InFont handles being unavailable: the walk below reads the cursor and measures
        // glyphs, both of which fault without an ImGui context. Asking the gate lets the headless harness, which
        // owns a context with no plugin behind it, still reach the measurement. See NoireText.CalcSize.
        if (string.IsNullOrEmpty(text) || !UiDraw.Available)
            return Vector2.Zero;

        var ambient = ImGui.GetFontSize();

        if (UiTextMeasureCache.TryGetTrackedSize(text, tracking, sizePx, ambient, out var cached))
            return cached;

        var size = InFont(sizePx, text, tracking, paint: false);
        UiTextMeasureCache.StoreTrackedSize(text, tracking, sizePx, ambient, size);

        return size;
    }

    // Falls back to the stretched stand-in while the font for the size builds.
    private static Vector2 InFont(float sizePx, string text, float tracking, bool paint)
    {
        var handle = UiFontCache.Get(sizePx);

        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            return PlaceGlyphs(text, tracking, paint);
        }

        var restore = PushApproximateSize(sizePx);

        try
        {
            return PlaceGlyphs(text, tracking, paint);
        }
        finally
        {
            if (restore.HasValue)
                ImGui.SetWindowFontScale(restore.Value);
        }
    }

    private static Vector2 PlaceGlyphs(string text, float tracking, bool paint)
    {
        // Named for the type rather than for this method, so a tracked label lands in the same row as every other piece
        // of text in the frame. See the note on NoireText.At.
        using var draw = UiDraw.Begin();

        var font = UiTextMeasureCache.CurrentFont();
        var fontSize = ImGui.GetFontSize();
        var spacing = tracking * fontSize;
        var height = ImGui.GetTextLineHeight();
        var run = UiTextMeasureCache.OpenGlyphRun(font, fontSize);

        var list = paint ? draw.List : ImDrawListPtr.Null;
        var painting = paint && !list.IsNull;

        var origin = Vector2.Zero;
        var color = 0u;

        if (painting)
        {
            origin = ImGui.GetCursorScreenPos();
            color = ImGui.GetColorU32(ImGuiCol.Text);
        }

        Span<char> glyph = stackalloc char[2];
        var x = 0f;

        for (var at = 0; at < text.Length;)
        {
            // A surrogate pair is one character and has to be measured and drawn as one, or it is two replacement
            // boxes with tracking helpfully applied between the halves.
            var length = char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1])
                ? 2
                : 1;

            var codepoint = length == 2 ? char.ConvertToUtf32(text[at], text[at + 1]) : text[at];

            // Remembered once per character per font size rather than re-measured per frame: a label's glyphs do
            // not change between frames, and an interface's alphabet is small and fixed, so this fills in the
            // first frames and only hits afterwards.
            if (!run.TryGet(codepoint, out var metrics))
            {
                metrics = BuildGlyphMetrics(codepoint, fontSize);
                run.Store(codepoint, metrics);
            }

            if (painting && metrics.Visible)
            {
                glyph[0] = text[at];

                if (length == 2)
                    glyph[1] = text[at + 1];

                list.AddText(origin + new Vector2(x, 0f), color, (ReadOnlySpan<char>)glyph[..length]);
            }

            x += metrics.Advance + spacing;
            at += length;
        }

        // The trailing gap belongs after the last character, not the run: left in, every tracked label would sit a
        // gap left of where it should when centred or right-aligned.
        var size = new Vector2(MathF.Max(0f, x - spacing), height);

        if (paint)
            ImGui.Dummy(size);

        return size;
    }

    // Reads out of the font currently in hand, scaled to the size it is being drawn at.
    private static unsafe UiTextMeasureCache.GlyphMetrics BuildGlyphMetrics(int codepoint, float fontSize)
    {
        var font = ImGui.GetFont();
        var glyph = font.FindGlyph((char)codepoint);

        if (glyph == null)
            return new UiTextMeasureCache.GlyphMetrics(0f, false, true);

        var scale = font.FontSize > 0f ? fontSize / font.FontSize : 1f;

        return new UiTextMeasureCache.GlyphMetrics(glyph->AdvanceX * scale, glyph->Visible != 0, true);
    }
}
