using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Text at any size, without ImGui's blur.<br/>
/// An ImGui font is a bitmap atlas rasterized once at a fixed size, so <c>SetWindowFontScale</c> and a scaled font push
/// do not rasterize anything larger, they sample that bitmap larger: a heading at twice the base size is a pixel-crawled
/// upscale of a small glyph, and no ImGui setting fixes it. NoireText builds a real font at the size asked for and draws
/// with that.
/// </summary>
/// <remarks>
/// Ask for a size by role (<see cref="TextSize"/>) rather than by number wherever you can. Roles resolve through
/// <see cref="NoireTheme"/>, which is what lets a skin move the whole type scale from one place; a number at a call site
/// is a number thirty other call sites will each pick differently.<br/>
/// Sizes are logical pixels at 100%, like every other measurement in NoireUI. See <see cref="NoireUI.Scale"/>.<br/>
/// Building a size takes a moment, and until it is ready the text is drawn at the right size by scaling the font that is
/// already loaded. It is briefly soft rather than briefly the wrong size, so nothing on screen moves when the real font
/// arrives. Call <see cref="Prewarm"/> at startup to have it ready before anything is drawn.
/// </remarks>
/// <example>
/// <code>
/// NoireText.Draw("Settings", TextSize.Heading);
/// NoireText.Colored(theme.Resolve(ThemeColor.TextMuted), "3 profiles loaded", TextSize.Caption);
///
/// NoireText.At(TextSize.Display, () =>
/// {
///     ImGui.TextUnformatted("Noire");          // raw ImGui inside the scope draws at the size too
///     NoireText.Draw("Deco");
/// });
/// </code>
/// </example>
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
    /// Draws text in a color, at a named size.
    /// </summary>
    /// <param name="color">The text color.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="size">The step of the type scale to draw it at.</param>
    public static void Colored(Vector4 color, string text, TextSize size = TextSize.Body)
    {
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color);
        Draw(text, size);
    }

    /// <summary>
    /// Draws text in the theme's muted color, at a named size, for anything supporting.
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
            // Measured inside the scope, so the width comes from the font that is about to draw rather than from
            // whatever was current outside it.
            var offset = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(t).X) * 0.5f;

            if (offset > 0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

            ImGui.TextUnformatted(t);
        });
    }

    /// <summary>
    /// Draws text with some of its characters picked out in another color, for showing why a row survived a filter.
    /// </summary>
    /// <remarks>
    /// Pairs with <see cref="Helpers.FuzzyMatcher"/>, which reports exactly these positions. Showing them is most of what
    /// makes a fuzzy filter feel trustworthy rather than arbitrary: without them a list that quietly reorders itself
    /// looks like it is guessing.<br/>
    /// Drawn as a run of pieces joined with no spacing, so it sits on one line and does not wrap. That is what a
    /// filter row wants; wrap the text yourself if you need more than a line.
    /// </remarks>
    /// <param name="text">The text to draw.</param>
    /// <param name="indices">
    /// The positions to pick out, ascending. Anything out of range or out of order is ignored rather than throwing,
    /// because these usually come straight from a matcher run against a different string a frame ago.
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

    /// <summary>
    /// Draws the text as alternating plain and highlighted runs, joined with no spacing.
    /// </summary>
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
            // frame, for text whose whole purpose is to be redrawn as the user types.
            if (highlighted)
            {
                using var pushed = ImRaii.PushColor(ImGuiCol.Text, highlight);
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
    /// <remarks>
    /// Measured with the font pushed, which is the whole point: a layout built on a measurement taken in one font and
    /// drawn in another is wrong everywhere by a few pixels, and neither of the two looks like the one lying. It
    /// therefore answers for whatever would draw *right now*, including the stretched stand-in used while a size is
    /// still building.
    /// </remarks>
    /// <param name="text">The text to measure.</param>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    /// <returns>The size the text would occupy, in real pixels.</returns>
    public static Vector2 CalcSize(string text, float sizePx)
    {
        if (!NoireService.IsInitialized())
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

    /// <summary>
    /// Measures text with the right font pushed, taking whichever of the two paths applies.
    /// </summary>
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
    /// <remarks>
    /// The one text call that is safe outside a frame, and the reason it exists. Every other call here pushes a font
    /// handle and asks ImGui to measure, both of which need a frame in progress: reaching for
    /// <see cref="CalcSize(string, float)"/> from a constructor to warm the cache is a crash rather than a warm cache.
    /// This only tells the cache the size is wanted, so it can be built before the frame that needs it.<br/>
    /// Use it for sizes a host can switch to at runtime, such as a reader-facing type scale. Sizes the interface always
    /// draws are covered by <see cref="Prewarm(bool)"/>.
    /// </remarks>
    /// <param name="sizePx">The size at 100%. See <see cref="NoireUI.Scale"/>.</param>
    public static void Request(float sizePx) => UiFontCache.Get(sizePx);

    /// <summary>
    /// How many distinct pixel sizes may be built before the cache refuses more.
    /// </summary>
    /// <remarks>
    /// Every size is an atlas entry, and every rebuild re-rasterizes all of them, so this is a real budget rather than
    /// a formality. The default suits one type scale; a host offering the reader several scales has as many sizes as
    /// steps times scale and should raise it deliberately, rather than discover it as a heading that quietly stopped
    /// growing because the cache had started drawing it at the nearest size it already held.
    /// </remarks>
    public static int MaxCachedSizes
    {
        get => UiFontCache.MaxSizes;
        set => UiFontCache.MaxSizes = Math.Max(1, value);
    }

    /// <summary>
    /// Asks for several sizes to be built, without drawing or measuring anything.
    /// </summary>
    /// <remarks>
    /// The one text call that is safe outside a frame, and the reason it exists. Every other call here pushes a font
    /// handle and asks ImGui to measure, both of which need a frame in progress. This only tells the cache the sizes
    /// are wanted, so they can be built before the frame that needs them.<br/>
    /// Use it for sizes a host can switch to at runtime, such as a reader-facing type scale. Sizes the interface always
    /// draws are covered by <see cref="Prewarm(bool)"/>.
    /// </remarks>
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
    /// <remarks>
    /// A line is as tall as the font's em box, and letters do not sit in the middle of it. The box reserves room under
    /// the baseline for descenders that most labels never use, so a shape centred on the box sits visibly high against
    /// the text beside it: a tick box next to a row label, or the cross on a chip next to its tag.<br/>
    /// Measured on the capital band rather than on a particular string, so a row of labels keeps one baseline instead
    /// of each shifting by whether it happens to contain a 'g'.
    /// </remarks>
    /// <example>
    /// <code>
    /// // A mark centred on the label, rather than on the line box the label sits in.
    /// var middle = rowTop + NoireText.CenterOffset();
    /// NoireShapes.Rect(new Vector2(x, middle - (side * 0.5f)), new Vector2(x + side, middle + (side * 0.5f)), color);
    /// </code>
    /// </example>
    /// <param name="size">The step of the type scale to measure.</param>
    /// <returns>The distance from the top of the line to the text's optical centre, in real pixels.</returns>
    public static float CenterOffset(TextSize size = TextSize.Body)
        => CenterOffset(NoireTheme.Current.ResolveTextSize(size));

    /// <summary>
    /// How far below the top of a line the text drawn in it looks centred, for lining a drawn shape up with a label.
    /// </summary>
    /// <remarks>
    /// A line is as tall as the font's em box, and letters do not sit in the middle of it. The box reserves room under
    /// the baseline for descenders that most labels never use, so a shape centred on the box sits visibly high against
    /// the text beside it.<br/>
    /// Measured on the capital band rather than on a particular string, so a row of labels keeps one baseline instead
    /// of each shifting by whether it happens to contain a 'g'.
    /// </remarks>
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

    /// <summary>
    /// Reads the centre offset with the right font pushed, taking whichever of the two paths applies.
    /// </summary>
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

    /// <summary>
    /// The glyph the capital band is read off. A capital with a flat top and a flat foot gives the band exactly,
    /// where a round one would overshoot both edges.
    /// </summary>
    private const char BandGlyph = 'H';

    /// <summary>
    /// Reads the optical centre off whatever font is current.
    /// </summary>
    /// <remarks>
    /// Taken as a fraction of the font's own size and applied to the size actually being drawn, so it is correct on
    /// both paths <see cref="At{TState}(float, TState, System.Action{TState})"/> takes: glyph metrics belong to the
    /// unscaled font, while the stretched stand-in draws at a multiple of it.
    /// </remarks>
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

    /// <summary>
    /// Where the capital band sits in a line, as a fraction of the line's height.
    /// </summary>
    /// <remarks>
    /// Separated out because it is the part worth being sure about and the only part that can be checked without a
    /// font: everything around it is reading metrics off ImGui. Clamped, so a font reporting a band outside its own
    /// box moves a label by a few pixels rather than throwing the row out of the widget.
    /// </remarks>
    /// <param name="bandTop">The top of the capital band, measured down from the top of the line.</param>
    /// <param name="bandBottom">The baseline, measured down from the top of the line.</param>
    /// <param name="lineHeight">The height of the line the band was measured in.</param>
    /// <returns>The optical centre as a fraction of the line height.</returns>
    internal static float CenterRatio(float bandTop, float bandBottom, float lineHeight)
    {
        if (lineHeight <= 0f || bandBottom <= bandTop)
            return 0.5f;

        return Math.Clamp((bandTop + bandBottom) * 0.5f / lineHeight, 0.25f, 0.75f);
    }

    #endregion

    #region Font building

    /// <summary>
    /// The glyphs each size is rasterized with, as pairs of first and last codepoint terminated by zero.<br/>
    /// When <see langword="null"/>, a shipped range covering Latin with its accents, punctuation, currency, arrows and
    /// common symbols is used, plus whatever the user's Dalamud language needs.
    /// </summary>
    /// <remarks>
    /// This is the setting that decides how long a type scale takes to become available, because rasterizing is per
    /// glyph and per size. A complete font is several thousand glyphs; the shipped range is around seven hundred. Widen
    /// it when your plugin draws text the default cannot render, and expect the build to take proportionally longer.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Greek and Cyrillic on top of the usual Latin.
    /// NoireText.GlyphRanges = [0x0020, 0x00FF, 0x0370, 0x03FF, 0x0400, 0x04FF, 0];
    /// </code>
    /// </example>
    public static ushort[]? GlyphRanges { get; set; }

    /// <summary>
    /// How long the type scale must hold still before a size that is not built yet is rasterized.
    /// </summary>
    /// <remarks>
    /// This exists for the live font-size setting. A size being dragged is a different size every frame, so building
    /// the moment one is asked for spends a rasterization on every step of the drag and fills the size cache with
    /// values the user passed through on the way to the one they wanted. Held back, a whole sweep costs one build, at
    /// the size they stopped on.<br/>
    /// While the scale is moving, text draws at the right size with the stretched stand-in, which is what a slider
    /// wants to show anyway: the size is what is being chosen, and it tracks exactly. Shorten this for a scale that
    /// changes in steps rather than by dragging; lengthen it if a build is expensive enough to be worth waiting out.
    /// </remarks>
    public static TimeSpan RebuildSettleDelay { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Replaces how a size is built, for a plugin that needs a different font, different glyphs, or the icon font
    /// merged in.
    /// </summary>
    /// <remarks>
    /// Set this and NoireText stops deciding anything about the typeface: it still owns the size cache, the scale and
    /// the drawing, and the callback owns what a size actually contains. Passing the complete Dalamud font is one line,
    /// and is what NoireText did before it was made fast.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Everything the default font has, icons included. Slower, and what you want if you draw icons in headings.
    /// NoireText.FontBuilder = (toolkit, sizePx) =&gt; toolkit.AddDalamudDefaultFont(sizePx);
    /// </code>
    /// </example>
    public static Action<IFontAtlasBuildToolkitPreBuild, float>? FontBuilder { get; set; }

    /// <summary>
    /// Builds the current theme's type scale, so it is ready before anything asks to draw with it.
    /// </summary>
    /// <remarks>
    /// Optional. Without it the first draw starts the build and text is drawn at the right size with a scaled stand-in
    /// until it finishes, which is a second or two of slightly soft headings and nothing worse.<br/>
    /// Call it when your plugin loads, or after setting a theme. Safe to call repeatedly: a size already built is not
    /// built again.
    /// </remarks>
    /// <param name="wait">
    /// Whether to block until the sizes are rasterized rather than letting them arrive over the following frames.<br/>
    /// Pass <see langword="true"/> from a plugin constructor to trade a longer load for an interface that is never seen
    /// mid-build. It is a real trade: rasterizing glyphs takes as long as it takes, and that time moves to your load
    /// rather than disappearing. Do not pass it from a draw callback, where the time would come out of the frame.<br/>
    /// The rasterization runs on the calling thread, so it is finished when this returns. That is the point: an atlas
    /// left to rebuild itself is driven by an event Dalamud raises on the main thread, which has not run yet when a
    /// constructor is executing, so there would be nothing under way to wait for.
    /// </param>
    public static void Prewarm(bool wait = false) => UiFontCache.BuildScale(wait);

    #endregion

    #region Scopes

    /// <summary>
    /// Runs a block of drawing at a named size.<br/>
    /// Everything inside draws at that size, raw ImGui included, so a block of mixed text takes the size once rather
    /// than repeating it per call.
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
    /// Runs a block of drawing at a named size.<br/>
    /// Everything inside draws at that size, raw ImGui included, so a block of mixed text takes the size once rather
    /// than repeating it per call.
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

        // Every text call in the frame lands in one row. Text is the cost an interface is least likely to suspect and
        // most likely to be spending, and a size that has not been built yet is paid for here.
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

    /// <summary>
    /// Stretches the current font to a target size, for the frames before the real one at that size has been built.
    /// </summary>
    /// <remarks>
    /// This is the blurry scaling NoireText exists to replace, used deliberately and briefly. The alternative is to
    /// leave the text at whatever size the current font happens to be, which is what an unbuilt handle pushes as, and
    /// that is worse in the way that shows: every heading starts small and jumps when its font arrives, taking the
    /// layout around it along. Scaled, the text is the right size from the first frame and merely sharpens.
    /// </remarks>
    /// <param name="sizePx">The target size at 100%.</param>
    /// <returns>The window font scale to restore, or <see langword="null"/> when nothing was changed.</returns>
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
