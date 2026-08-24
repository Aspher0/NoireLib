using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Text at any size, without ImGui's blur.
/// </summary>
/// <remarks>Sizes are logical pixels at 100%. See <see cref="NoireUI.Scale"/>.</remarks>
[NoireFacade]
public static partial class NoireText
{
    #region Drawing

    /// <summary>
    /// Draws text at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Draw(string text, TextSize size = TextSize.Body)
        => Draw(text, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Draws text at an explicit size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    public static void Draw(string text, float sizePx)
        => At(sizePx, text, static t => ImGui.TextUnformatted(t));

    /// <summary>
    /// Draws text at an exact screen position without submitting an ImGui item, at a named size.
    /// </summary>
    /// <param name="position">Where the top left of the text goes, in screen pixels.</param>
    /// <param name="color">The text color.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void DrawAt(Vector2 position, Vector4 color, string text, TextSize size = TextSize.Body)
        => DrawAt(position, color, text, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Draws text at an exact screen position without submitting an ImGui item, at an explicit size.
    /// </summary>
    /// <param name="position">Where the top left of the text goes, in screen pixels.</param>
    /// <param name="color">The text color.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    public static void DrawAt(Vector2 position, Vector4 color, string text, float sizePx)
    {
        if (string.IsNullOrEmpty(text))
            return;

        At(sizePx, (position, packed: Helpers.ColorHelper.Vector4ToUint(color), text), static state =>
        {
            using var draw = UiDraw.Begin();

            if (!draw.List.IsNull)
                draw.List.AddText(state.position, state.packed, state.text);
        });
    }

    /// <summary>
    /// Draws text in a color, at a named size.
    /// </summary>
    /// <param name="color">The text color.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Colored(Vector4 color, string text, TextSize size = TextSize.Body)
    {
        using var pushed = UiPush.Color(ImGuiCol.Text, color);
        Draw(text, size);
    }

    /// <summary>
    /// Draws text in the theme's muted color, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Muted(string text, TextSize size = TextSize.Body)
        => Colored(NoireTheme.Current.Resolve(ThemeColor.TextMuted), text, size);

    /// <summary>
    /// Draws text in the theme's disabled color, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Disabled(string text, TextSize size = TextSize.Body)
        => Colored(NoireTheme.Current.Resolve(ThemeColor.TextDisabled), text, size);

    /// <summary>
    /// Draws text that wraps at the current wrap position, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Wrapped(string text, TextSize size = TextSize.Body)
        => At(NoireTheme.Current.ResolveTextSize(size), text, static t => ImGui.TextWrapped(t));

    /// <summary>
    /// Draws text that wraps at a given width, at a named size.
    /// </summary>
    /// <param name="width">The width to wrap at, in real pixels. Usually a measured one, so it is not scaled.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Wrapped(float width, string text, TextSize size = TextSize.Body)
    {
        At(NoireTheme.Current.ResolveTextSize(size), (width, text), static state =>
            NoireLayout.WrapText(state.width, state.text, static t => ImGui.TextUnformatted(t)));
    }

    /// <summary>
    /// Draws text that wraps at a given width, at an explicit size.
    /// </summary>
    /// <param name="width">The width to wrap at, in real pixels. Usually a measured one, so it is not scaled.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    public static void Wrapped(float width, string text, float sizePx)
    {
        At(sizePx, (width, text), static state =>
            NoireLayout.WrapText(state.width, state.text, static t => ImGui.TextUnformatted(t)));
    }

    /// <summary>
    /// Draws a bulleted line at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Bullet(string text, TextSize size = TextSize.Body)
        => At(NoireTheme.Current.ResolveTextSize(size), text, static t => ImGui.BulletText(t));

    /// <summary>
    /// Draws text centred in the space remaining on the current line, at a named size.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Centered(string text, TextSize size = TextSize.Body)
    {
        At(NoireTheme.Current.ResolveTextSize(size), text, static t =>
        {
            // Measured inside the scope, so the width comes from the font about to draw. CalcSize is not used here
            // since it resolves and pushes a font of its own.
            var offset = (ImGui.GetContentRegionAvail().X - CalcSizeInCurrentFont(t).X) * 0.5f;

            if (offset > 0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

            ImGui.TextUnformatted(t);
        });
    }

    /// <summary>
    /// Draws text with some of its characters picked out in another color.
    /// </summary>
    /// <remarks>Sits on one line and does not wrap.</remarks>
    /// <param name="text">The text to draw.</param>
    /// <param name="indices">
    /// The positions to pick out, ascending; anything out of range or out of order is ignored rather than throwing.
    /// </param>
    /// <param name="highlight">The color of the picked-out characters. When <see langword="null"/>, the theme's accent.</param>
    /// <param name="size">The step of the type scale to draw at.</param>
    public static void Highlighted(string text, ReadOnlySpan<int> indices, Vector4? highlight = null, TextSize size = TextSize.Body)
    {
        if (string.IsNullOrEmpty(text))
            return;

        NoireUI.EnsureFrameServices();

        var accent = highlight ?? NoireTheme.Current.Resolve(ThemeColor.Accent);
        var sizePx = NoireTheme.Current.ResolveTextSize(size);
        var handle = UiFontCache.Get(sizePx);

        // The font is pushed here rather than through At, because a span cannot be carried into a lambda. The two
        // paths are otherwise the same ones At takes, including the stretched stand-in while a size is building.
        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            DrawRuns(text, indices, accent);
            return;
        }

        var restore = PushApproximateSize(sizePx);

        try
        {
            DrawRuns(text, indices, accent);
        }
        finally
        {
            if (restore.HasValue)
                ImGui.SetWindowFontScale(restore.Value);
        }
    }

    private static void DrawRuns(string text, ReadOnlySpan<int> indices, Vector4 highlight)
    {
        var next = 0;
        var at = 0;
        var first = true;

        while (at < text.Length)
        {
            // Out-of-order or out-of-range positions are skipped rather than trusted, so a stale set of indices
            // degrades to plain text instead of splitting the string in the wrong places.
            while (next < indices.Length && indices[next] < at)
                next++;

            var highlighted = next < indices.Length && indices[next] == at;
            var start = at;

            while (at < text.Length)
            {
                while (next < indices.Length && indices[next] < at)
                    next++;

                var hit = next < indices.Length && indices[next] == at;

                if (hit != highlighted)
                    break;

                if (hit)
                    next++;

                at++;
            }

            if (!first)
                ImGui.SameLine(0f, 0f);

            first = false;

            // The span is handed over as a span. Interpolating it into a string here would allocate one per run, per
            // frame.
            if (highlighted)
            {
                using var pushed = UiPush.Color(ImGuiCol.Text, highlight);
                ImGui.TextUnformatted(text.AsSpan(start, at - start));
            }
            else
            {
                ImGui.TextUnformatted(text.AsSpan(start, at - start));
            }
        }
    }

    #endregion

    #region Measuring

    /// <summary>
    /// Measures text as it would be drawn at a named size.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="size">The step of the type scale to measure at.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 CalcSize(string text, TextSize size = TextSize.Body)
        => CalcSize(text, NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// Measures text as it would be drawn at an explicit size.
    /// </summary>
    /// <remarks>Needs a frame in progress.</remarks>
    /// <param name="text">The text to measure.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 CalcSize(string text, float sizePx)
    {
        // Asked of the gate rather than the service directly: the reads below fault rather than fail with no
        // context. In a plugin the two answers are the same; the difference is the headless harness, which owns a
        // real context with no plugin behind it.
        if (!UiDraw.Available)
            return Vector2.Zero;

        text ??= string.Empty;

        // Read before anything is pushed. On the stand-in path this is the font the measurement is actually taken with,
        // so it belongs to the key rather than to the answer.
        var ambient = ImGui.GetFontSize();

        if (UiTextMeasureCache.TryGetSize(text, sizePx, ambient, out var cached))
            return cached;

        var measured = MeasureText(text, sizePx);
        UiTextMeasureCache.StoreSize(text, sizePx, ambient, measured);

        return measured;
    }

    internal static Vector2 CalcSizeInCurrentFont(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Vector2.Zero;

        var font = UiTextMeasureCache.CurrentFont();
        var sizePx = ImGui.GetFontSize();

        if (UiTextMeasureCache.TryGetAmbientSize(text, font, sizePx, out var cached))
            return cached;

        var measured = ImGui.CalcTextSize(text);
        UiTextMeasureCache.StoreAmbientSize(text, font, sizePx, measured);

        return measured;
    }

    private static Vector2 MeasureText(string text, float sizePx)
    {
        var handle = UiFontCache.Get(sizePx);

        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            return ImGui.CalcTextSize(text);
        }

        var restore = PushApproximateSize(sizePx);

        try
        {
            return ImGui.CalcTextSize(text);
        }
        finally
        {
            if (restore.HasValue)
                ImGui.SetWindowFontScale(restore.Value);
        }
    }

    /// <summary>
    /// Asks for a size to be built, without drawing or measuring anything.
    /// </summary>
    /// <remarks>The one text call safe outside a frame; every other call here needs one in progress.</remarks>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    public static void Request(float sizePx) => UiFontCache.Get(sizePx);

    /// <summary>
    /// How many distinct pixel sizes may be built before the cache refuses more.
    /// </summary>
    public static int MaxCachedSizes
    {
        get => UiFontCache.MaxSizes;
        set => UiFontCache.MaxSizes = Math.Max(1, value);
    }

    /// <summary>
    /// Asks for several sizes to be built, without drawing or measuring anything.
    /// </summary>
    /// <remarks>The one text call safe outside a frame; see <see cref="Request(float)"/>.</remarks>
    /// <param name="sizesPx">The sizes at 100%.</param>
    public static void Request(ReadOnlySpan<float> sizesPx)
    {
        foreach (var size in sizesPx)
            UiFontCache.Get(size);
    }

    /// <summary>
    /// The height of one line at a named size, for reserving space before drawing into it.
    /// </summary>
    /// <param name="size">The step of the type scale to measure.</param>
    /// <returns>The line height in real pixels.</returns>
    public static float LineHeight(TextSize size = TextSize.Body)
        => CalcSize(" ", size).Y;

    /// <summary>
    /// How far below the top of a line the text drawn in it looks centred, for lining a drawn shape up with a label.
    /// </summary>
    /// <param name="size">The step of the type scale to measure.</param>
    /// <returns>The distance from the top of the line to the text's optical centre, in real pixels.</returns>
    public static float CenterOffset(TextSize size = TextSize.Body)
        => CenterOffset(NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// How far below the top of a line the text drawn in it looks centred, for lining a drawn shape up with a label.
    /// </summary>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>The distance from the top of the line to the text's optical centre, in real pixels.</returns>
    public static float CenterOffset(float sizePx)
    {
        if (!NoireService.IsInitialized())
            return 0f;

        var ambient = ImGui.GetFontSize();

        if (UiTextMeasureCache.TryGetCenterOffset(sizePx, ambient, out var cached))
            return cached;

        var measured = MeasureCenterOffsetAt(sizePx);
        UiTextMeasureCache.StoreCenterOffset(sizePx, ambient, measured);

        return measured;
    }

    private static float MeasureCenterOffsetAt(float sizePx)
    {
        var handle = UiFontCache.Get(sizePx);

        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            return MeasureCenterOffset();
        }

        var restore = PushApproximateSize(sizePx);

        try
        {
            return MeasureCenterOffset();
        }
        finally
        {
            if (restore.HasValue)
                ImGui.SetWindowFontScale(restore.Value);
        }
    }

    // A capital with a flat top and a flat foot gives the band exactly, where a round one would overshoot both edges.
    private const char BandGlyph = 'H';

    private static unsafe float MeasureCenterOffset()
    {
        var drawnSize = ImGui.GetFontSize();
        var font = ImGui.GetFont();

        if (font.IsNull || drawnSize <= 0f)
            return drawnSize * 0.5f;

        var glyph = font.FindGlyph(BandGlyph);

        return glyph == null
            ? drawnSize * 0.5f
            : CenterRatio(glyph->Y0, glyph->Y1, font.FontSize) * drawnSize;
    }

    // Clamped, so a font reporting a band outside its own box cannot throw the row out of the widget.
    internal static float CenterRatio(float bandTop, float bandBottom, float lineHeight)
    {
        if (lineHeight <= 0f || bandBottom <= bandTop)
            return 0.5f;

        return Math.Clamp((bandTop + bandBottom) * 0.5f / lineHeight, 0.25f, 0.75f);
    }

    #endregion

    #region Font building

    /// <summary>
    /// The glyphs each size is rasterized with, as pairs of first and last codepoint terminated by zero, or
    /// <see langword="null"/> for a shipped range covering Latin plus whatever the user's Dalamud language needs.
    /// </summary>
    public static ushort[]? GlyphRanges { get; set; }

    /// <summary>
    /// How long the type scale must hold still before a size that is not built yet is rasterized.
    /// </summary>
    public static TimeSpan RebuildSettleDelay { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Replaces how a size is built, for a plugin that needs a different font, different glyphs, or the icon font
    /// merged in.
    /// </summary>
    public static Action<IFontAtlasBuildToolkitPreBuild, float>? FontBuilder { get; set; }

    /// <summary>
    /// Builds the current theme's type scale, so it is ready before anything asks to draw with it.
    /// </summary>
    /// <param name="wait">
    /// Whether to block until the sizes are rasterized, rather than letting them arrive over the following frames.
    /// </param>
    public static void Prewarm(bool wait = false) => UiFontCache.BuildScale(wait);

    #endregion

    #region Scopes

    /// <summary>
    /// Runs a block of drawing at a named size; everything inside draws at that size, raw ImGui included.
    /// </summary>
    /// <param name="size">The step of the type scale to draw at.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void At(TextSize size, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        At(NoireTheme.Current.ResolveTextSize(size), body, static b => b());
    }

    /// <summary>
    /// Runs a block of drawing at a named size; everything inside draws at that size, raw ImGui included.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="size">The step of the type scale to draw at.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to run.</param>
    public static void At<TState>(TextSize size, TState state, Action<TState> body)
        => At(NoireTheme.Current.ResolveTextSize(size), state, body);

    /// <summary>
    /// Runs a block of drawing at an explicit size.
    /// </summary>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void At(float sizePx, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        At(sizePx, body, static b => b());
    }

    /// <summary>
    /// Runs a block of drawing at an explicit size.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void At<TState>(float sizePx, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        NoireUI.EnsureFrameServices();

        // Every text call in the frame lands in one row; a size not yet built is paid for here.
        using var draw = UiDraw.Begin();

        var handle = UiFontCache.Get(sizePx);

        if (handle is { Available: true })
        {
            using var pushed = handle.Push();
            UiScope.Run(nameof(NoireText), state, body);
            return;
        }

        // Either this is the host's own size and needs no font of its own, or the real one is still building. Both draw
        // with the font already loaded, stretched to the size that was asked for.
        var restore = PushApproximateSize(sizePx);

        try
        {
            UiScope.Run(nameof(NoireText), state, body);
        }
        finally
        {
            if (restore.HasValue)
                ImGui.SetWindowFontScale(restore.Value);
        }
    }

    // Stretches the current font to a target size, for the frames before the real one at that size has been built.
    // Returns the window font scale to restore, or null when nothing was changed.
    private static float? PushApproximateSize(float sizePx)
    {
        if (!NoireService.IsInitialized())
            return null;

        var current = ImGui.GetFontSize();
        if (current <= 0f)
            return null;

        var factor = NoireUI.Scaled(MathF.Max(1f, sizePx)) / current;

        // A size within a pixel or so of the current font is not worth a stretch, and this is the path the host's own
        // body size takes on every single call.
        if (MathF.Abs(factor - 1f) < 0.02f)
            return null;

        // The window font scale is absolute, so restoring it means knowing what it was. Measuring can legitimately
        // happen before anything has been begun, and there is no window to read it off then.
        var window = ImGuiP.GetCurrentWindow();
        if (window.IsNull)
            return null;

        var previous = window.FontWindowScale;
        ImGui.SetWindowFontScale(previous * factor);

        return previous;
    }

    #endregion
}
