using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Letter-spaced text, which ImGui has no notion of.
/// </summary>
/// <remarks>
/// ImGui draws a string in one call at the font's own advances, so the only way to add space between characters is
/// to place each glyph individually.
/// </remarks>
public static partial class NoireText
{
    /// <summary>The tracking a caps label wants when nothing else is said, in ems.</summary>
    /// <remarks>
    /// Capitals have no ascenders or descenders to separate them, so they need noticeably more room than lower case to
    /// stop reading as one block. This is the value the demo's small caps labels use.
    /// </remarks>
    public const float CapsTracking = 0.26f;

    /// <summary>
    /// Draws text with extra space between its characters, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="tracking">
    /// Extra space per character, in ems: a fraction of the size the text is drawn at, the way CSS letter-spacing
    /// works. A fraction rather than a pixel count, so one value is right at every step of the type scale and every
    /// UI scale.
    /// </param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    /// <returns>The size the run occupies.</returns>
    public static Vector2 Tracked(string text, float tracking = CapsTracking, TextSize size = TextSize.Body)
        => Tracked(text, tracking, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Draws text with extra space between its characters, at an explicit size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="tracking">Extra space per character, in ems. See <see cref="Tracked(string, float, TextSize)"/>.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>
    /// The size the run occupies. Returned rather than left to a second
    /// <see cref="TrackedSize(string, float, float)"/> call, because measuring tracked text costs the same walk over
    /// its glyphs that drawing it does: a caller that measures and then draws pays for the string twice.
    /// </returns>
    public static Vector2 Tracked(string text, float tracking, float sizePx)
    {
        if (string.IsNullOrEmpty(text))
            return Vector2.Zero;

        NoireUI.EnsureFrameServices();

        return InFont(sizePx, text, tracking, paint: true);
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
    /// <remarks>
    /// Measured with the font pushed: a layout built on a measurement taken in one font and drawn in another is
    /// wrong everywhere by a few pixels.
    /// </remarks>
    /// <param name="text">The text to measure.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 TrackedSize(string text, float tracking, float sizePx)
    {
        // Not about the font, which InFont handles being unavailable: the walk below reads the cursor and measures
        // glyphs, both of which fault without an ImGui context. Asking the gate lets the headless harness, which
        // owns a context with no plugin behind it, still reach the measurement. See NoireText.CalcSize.
        if (string.IsNullOrEmpty(text) || !UiDraw.Available)
            return Vector2.Zero;

        return InFont(sizePx, text, tracking, paint: false);
    }

    /// <summary>
    /// Places a run with the font for a size pushed, falling back to the stretched stand-in while that size builds.
    /// </summary>
    /// <remarks>
    /// The same two paths <see cref="Highlighted"/> takes, for the same reason: a measurement and the drawing it
    /// feeds have to happen in the same font, or the layout is wrong by a few pixels everywhere.<br/>
    /// Calls <see cref="PlaceGlyphs"/> directly rather than taking a body to run: a lambda capturing the size to
    /// return would allocate a display class and a delegate on every frame a tracked label draws.
    /// </remarks>
    /// <param name="sizePx">The size at 100%.</param>
    /// <param name="text">The text to place.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="paint">Whether to actually paint, as opposed to only measuring.</param>
    /// <returns>The size the run occupies.</returns>
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

    /// <summary>
    /// Places each character in turn, and reports the run's size.
    /// </summary>
    /// <remarks>
    /// Drawn straight onto the draw list rather than as one ImGui item per character, which would put the run at
    /// the mercy of item spacing. One <see cref="ImGui.Dummy"/> at the end reserves what was drawn, so a tracked
    /// label lays out like any other item.<br/>
    /// Each glyph is still its own draw-list text call: the font atlas spreads glyphs across several textures, and
    /// the text call binds the right one per glyph, where a hand-written quad would draw noise off another page.
    /// The metrics cache only removes the measuring around those calls; a space is skipped outright rather than
    /// handed to a call that paints nothing.
    /// </remarks>
    /// <param name="text">The text to place.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="paint">Whether to actually paint, as opposed to only measuring.</param>
    /// <returns>The size the run occupies.</returns>
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

    /// <summary>
    /// Reads a glyph's advance and visibility out of the font in hand, scaled to the size it is being drawn at.
    /// </summary>
    /// <remarks>
    /// The font's glyph table holds metrics at the font's own baked size, while the run may be drawn at another
    /// during the stand-in path. A codepoint the font does not carry comes back as the font's fallback glyph,
    /// matching what ImGui's own renderer draws for it.
    /// </remarks>
    /// <param name="codepoint">The character, as a full codepoint.</param>
    /// <param name="fontSize">The size the run is drawn at, in real pixels.</param>
    /// <returns>The glyph's metrics at the drawn size.</returns>
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
