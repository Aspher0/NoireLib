using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Letter-spaced text, which ImGui has no notion of.
/// </summary>
/// <remarks>
/// Tracking is what makes a short label read as a heading rather than as small body text, and it is the whole of the
/// difference between a caps label that looks designed and one that looks shouted. ImGui draws a string in one call at
/// the font's own advances, so the only way to open them up is to place each glyph.
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
    /// Extra space per character, in **ems**: a fraction of the size the text is drawn at, the way CSS letter-spacing
    /// works. Being a fraction rather than a pixel count is what makes one value right at every step of the type scale
    /// and at every UI scale, so it is never scaled and never restated.
    /// </param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    /// <returns>The size the run occupies, so a caller placing something beside it need not measure it again.</returns>
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

        var size = Vector2.Zero;
        InFont(sizePx, text, tracking, (t, track) => size = PlaceGlyphs(t, track, draw: true));

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
    /// <inheritdoc cref="CalcSize(string, float)" path="/remarks"/>
    /// <param name="text">The text to measure.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 TrackedSize(string text, float tracking, float sizePx)
    {
        if (string.IsNullOrEmpty(text) || !NoireService.IsInitialized())
            return Vector2.Zero;

        var measured = Vector2.Zero;
        InFont(sizePx, text, tracking, (t, track) => measured = PlaceGlyphs(t, track, draw: false));

        return measured;
    }

    /// <summary>
    /// Runs a body with the font for a size pushed, falling back to the stretched stand-in while that size builds.
    /// </summary>
    /// <remarks>
    /// The same two paths <see cref="Highlighted"/> takes, for the same reason: a measurement and the drawing it feeds
    /// have to happen in the same font or the layout is wrong by a few pixels everywhere, with neither of the two
    /// looking like the one that lied.
    /// </remarks>
    private static void InFont(float sizePx, string text, float tracking, Action<string, float> body)
    {
        var handle = UiFontCache.Get(sizePx);

        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            body(text, tracking);
            return;
        }

        var restore = PushApproximateSize(sizePx);

        try
        {
            body(text, tracking);
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
    /// Drawn straight onto the draw list rather than as one ImGui item per character: a text item per glyph would put
    /// the whole run at the mercy of item spacing and make every label a different width from its own measurement. One
    /// <see cref="ImGui.Dummy"/> at the end reserves what was actually drawn, so a tracked label lays out like any
    /// other item.<br/>
    /// A character is measured and drawn through a one-element span rather than a substring, so a label costs no
    /// allocation per glyph per frame.
    /// </remarks>
    /// <param name="text">The text to place.</param>
    /// <param name="tracking">Extra space per character, in ems.</param>
    /// <param name="draw">Whether to actually paint, as opposed to only measuring.</param>
    /// <returns>The size the run occupies.</returns>
    private static Vector2 PlaceGlyphs(string text, float tracking, bool draw)
    {
        var origin = ImGui.GetCursorScreenPos();
        var spacing = tracking * ImGui.GetFontSize();
        var height = ImGui.GetTextLineHeight();
        var color = ImGui.GetColorU32(ImGuiCol.Text);
        var list = ImGui.GetWindowDrawList();

        Span<char> glyph = stackalloc char[2];
        var x = 0f;

        for (var index = 0; index < text.Length;)
        {
            // A surrogate pair is one character and has to be measured and drawn as one, or it is two replacement
            // boxes with tracking helpfully applied between the halves.
            var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? 2
                : 1;

            glyph[0] = text[index];

            if (length == 2)
                glyph[1] = text[index + 1];

            var piece = (ReadOnlySpan<char>)glyph[..length];

            if (draw)
                list.AddText(origin + new Vector2(x, 0f), color, piece);

            x += ImGui.CalcTextSize(piece).X + spacing;
            index += length;
        }

        // The trailing gap belongs after the last character and is not part of the run, exactly as a trailing space
        // would not be. Leaving it in makes every tracked label sit a gap left of where it should when it is centred
        // or right-aligned.
        var size = new Vector2(MathF.Max(0f, x - spacing), height);

        if (draw)
            ImGui.Dummy(size);

        return size;
    }
}
