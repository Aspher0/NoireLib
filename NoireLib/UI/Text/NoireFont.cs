using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;

namespace NoireLib.UI;

/// <summary>
/// One font face loaded from TrueType data, drawn and measured at any size with CSS sizing semantics.<br/>
/// Each size rasterizes on first use. Until then the current ImGui font draws stretched to the same line height. Draw thread only, except <see cref="Request(float)"/>.
/// </summary>
public sealed class NoireFont : IDisposable
{
    private readonly Dictionary<float, UiFaceEntry> entries = new();
    private readonly HotPathCache<FitKey, TextFit> fits = new(2048);

    private int[] planStarts = new int[64];
    private int[] planLengths = new int[64];
    private float[] planPens = new float[64];

    private readonly HotPathCache<CalibrationKey, float> ems = new(64);

    private readonly record struct CalibrationKey(nint Font, int Generation);

    private readonly record struct FitKey(string Text, nint Font, float DrawSize, float Tracking, float MaxWidth, bool Kerned, int Generation);

    // Laying a string out walks the kerning table and asks ImGui for each glyph. The plan is kept and only AddText repeats.
    private sealed class TextPlan
    {
        public int Count;
        public float[] Pen = [];
        public byte[] Utf8 = [];
        public int[] ByteStart = [];
        public int[] ByteLength = [];
    }

    private readonly record struct TextFit(Vector2 Size, int Visible, bool Ellipsis, float PrefixWidth, TextPlan Plan);

    internal static Func<NoireFont, float, ImFontPtr>? BuiltFontOverride { get; set; }

    private bool disposed;

    private readonly int cmapOffset;
    private readonly int hmtxOffset;
    private readonly int longMetrics;
    private readonly int gposOffset;
    private readonly int kernOffset;

    private readonly Dictionary<char, (int Glyph, int Units)> glyphIds = new();
    private UiFontKerning? kerning;
    private bool kerningRead;

    private NoireFont(byte[] data, string name)
    {
        Data = data;
        Name = name;

        var metrics = ReadMetrics(data);
        UnitsPerEm = metrics.UnitsPerEm;
        Ascender = metrics.Ascender;
        Descender = metrics.Descender;
        cmapOffset = metrics.CmapOffset;
        hmtxOffset = metrics.HmtxOffset;
        longMetrics = metrics.LongMetrics;
        gposOffset = metrics.GposOffset;
        kernOffset = metrics.KernOffset;
    }

    /// <summary>Creates a face from TrueType or OpenType data in memory.</summary>
    /// <param name="data">The font file's bytes. Kept without a copy.</param>
    /// <param name="name">A name for logs and diagnostics.</param>
    /// <returns>The face.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not a readable font.</exception>
    public static NoireFont FromMemory(byte[] data, string name)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new NoireFont(data, name);
    }

    /// <summary>
    /// Creates a face from a font file on disk.
    /// </summary>
    /// <param name="path">The path of the .ttf or .otf file.</param>
    /// <returns>The face.</returns>
    public static NoireFont FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new NoireFont(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Creates a face from a font embedded as a manifest resource.
    /// </summary>
    /// <param name="assembly">The assembly holding the resource, usually the plugin's own.</param>
    /// <param name="resourceName">The resource's manifest name.</param>
    /// <returns>The face.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the assembly has no resource of that name.</exception>
    public static NoireFont FromManifestResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"{assembly.GetName().Name} has no manifest resource named '{resourceName}'.", resourceName);

        var data = new byte[stream.Length];
        stream.ReadExactly(data);

        return new NoireFont(data, resourceName);
    }

    #region Settings

    /// <summary>The name the face was created with.</summary>
    public string Name { get; }

    /// <summary>The font file's bytes.</summary>
    public byte[] Data { get; }

    /// <summary>The face's design units per em, read from its <c>head</c> table.</summary>
    public int UnitsPerEm { get; }

    /// <summary>The face's ascender in design units, read from its <c>hhea</c> table.</summary>
    public int Ascender { get; }

    /// <summary>The face's descender in design units (negative), read from its <c>hhea</c> table.</summary>
    public int Descender { get; }

    /// <summary>
    /// The natural line height as a multiple of the em size: what CSS <c>line-height: normal</c> resolves to for a face
    /// with no line gap.
    /// </summary>
    public float LineRatio => (Ascender - Descender) / (float)UnitsPerEm;

    /// <summary>
    /// The glyphs rasterized, as pairs of first and last codepoint terminated by zero, or <see langword="null"/> for
    /// Latin, Latin Extended-A, punctuation, currency, arrows and miscellaneous symbols. Read at each build.
    /// </summary>
    public ushort[]? GlyphRanges { get; set; }

    /// <summary>Whether the glyphs Dalamud's configured language needs are merged in from its own fonts. Read at each build.</summary>
    public bool MergeLanguageGlyphs { get; set; } = true;

    /// <summary>The horizontal oversampling, or <see langword="null"/> for Dalamud's default. Read at each build.</summary>
    public int? Oversample { get; set; }

    /// <summary>
    /// Whether the face's own pair adjustments are applied, as a browser applies them. ImGui has no notion of kerning,
    /// so this is what keeps a run of text the width the same run has in a browser.
    /// </summary>
    public bool Kerning { get; set; } = true;

    /// <summary>
    /// The granularity sizes are rasterized at, in real pixels. Two requests closer than this share one built size.
    /// </summary>
    public float SizeStep { get; set; } = 0.5f;

    /// <summary>
    /// Runs after each size of this face is added to the atlas, with the font and its line height in pixels, for
    /// merging other glyphs into it.
    /// </summary>
    public Action<IFontAtlasBuildToolkitPreBuild, ImFontPtr, float>? OnBuild { get; set; }

    /// <summary>How many face sizes may be built across every <see cref="NoireFont"/> before further ones fall back.</summary>
    public static int MaxBuiltSizes
    {
        get => UiFaceAtlas.MaxEntries;
        set => UiFaceAtlas.MaxEntries = Math.Max(1, value);
    }

    #endregion

    #region Sizes

    /// <summary>
    /// Asks for a size to be built without drawing anything. Safe outside a frame, for warming a UI before it opens.
    /// </summary>
    /// <param name="sizePx">The em size at 100%.</param>
    public void Request(float sizePx) => GetEntry(sizePx, buildNow: true);

    /// <summary>
    /// Asks for several sizes to be built without drawing anything. See <see cref="Request(float)"/>.
    /// </summary>
    /// <param name="sizesPx">The em sizes at 100%.</param>
    public void Request(ReadOnlySpan<float> sizesPx)
    {
        foreach (var size in sizesPx)
            GetEntry(size, buildNow: true);
    }

    /// <summary>Whether a size is built.</summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>True once the size is rasterized.</returns>
    public bool IsReady(float sizePx) => !Font(sizePx).IsNull;

    /// <summary>
    /// The built ImGui font for a size, or a null pointer while it is not built yet. Asks for it to be built.
    /// </summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The font, valid for the current frame.</returns>
    public ImFontPtr Font(float sizePx)
    {
        if (BuiltFontOverride is { } seam)
            return seam(this, sizePx);

        var entry = GetEntry(sizePx, buildNow: false);
        return entry == null ? default : UiFaceAtlas.Resolve(entry);
    }

    /// <summary>
    /// The em size in real pixels a logical size is drawn at, after the UI scale and <see cref="SizeStep"/>.
    /// </summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The em size in real pixels.</returns>
    public float EmPixels(float sizePx)
    {
        var real = MathF.Max(1f, sizePx * NoireUI.Scale);
        var step = SizeStep > 0f ? SizeStep : 0.01f;

        return MathF.Max(step, MathF.Round(real / step) * step);
    }

    /// <summary>
    /// The height of one line at its natural line height (ascender to descender), in real pixels.
    /// </summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The line height in real pixels.</returns>
    public float LineHeight(float sizePx) => EmPixels(sizePx) * LineRatio;

    /// <summary>
    /// The distance from the top of a line to the baseline, in real pixels.
    /// </summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The ascent in real pixels.</returns>
    public float Ascent(float sizePx) => EmPixels(sizePx) * Ascender / UnitsPerEm;

    /// <summary>The distance from where a run is drawn to the baseline it renders on, read from the built font.</summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The rendered ascent in real pixels.</returns>
    public float RenderedAscent(float sizePx)
    {
        if (!TryResolve(sizePx, out var font, out var drawSize) || font.IsNull || font.FontSize <= 0f)
            return Ascent(sizePx);

        return font.Ascent * (drawSize / font.FontSize);
    }

    /// <summary>How far below the top of a CSS line box the text starts.</summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="lineHeight">The CSS unitless line height.</param>
    /// <returns>The half-leading in real pixels. Negative when the line box is shorter than the text.</returns>
    public float HalfLeading(float sizePx, float lineHeight)
        => ((EmPixels(sizePx) * lineHeight) - LineHeight(sizePx)) * 0.5f;

    private UiFaceEntry? GetEntry(float sizePx, bool buildNow)
    {
        if (disposed)
            return null;

        var em = EmPixels(sizePx);

        lock (entries)
        {
            if (entries.TryGetValue(em, out var existing))
            {
                existing.LastUsedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                return existing;
            }
        }

        var created = UiFaceAtlas.Register(this, em, buildNow);

        if (created == null)
            return null;

        lock (entries)
            entries[em] = created;

        return created;
    }

    internal void Forget(UiFaceEntry entry)
    {
        lock (entries)
        {
            if (entries.TryGetValue(entry.EmPx, out var held) && ReferenceEquals(held, entry))
                entries.Remove(entry.EmPx);
        }
    }

    #endregion

    #region Measuring and drawing

    /// <summary>Measures text as <see cref="Draw(ImDrawListPtr, Vector2, uint, string, float, float, float)"/> would draw it.</summary>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="trackingPx">Extra space between characters at 100%, a CSS <c>letter-spacing</c>.</param>
    /// <param name="maxWidth">The width past which the text is cut with an ellipsis, in real pixels. 0 for none.</param>
    /// <returns>The drawn size in real pixels.</returns>
    public Vector2 CalcSize(string text, float sizePx, float trackingPx = 0f, float maxWidth = 0f)
    {
        if (string.IsNullOrEmpty(text) || !TryResolve(sizePx, out var font, out var drawSize))
            return string.IsNullOrEmpty(text) ? new Vector2(0f, LineHeight(sizePx)) : Vector2.Zero;

        return Fit(text, font, drawSize, EmPixels(sizePx), trackingPx * NoireUI.Scale, maxWidth, LineHeight(sizePx)).Size;
    }

    /// <summary>
    /// Whether text would be cut with an ellipsis at a width.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="trackingPx">Extra space between characters at 100%.</param>
    /// <param name="maxWidth">The available width in real pixels.</param>
    /// <returns><see langword="true"/> when the text does not fit.</returns>
    public bool IsTruncated(string text, float sizePx, float trackingPx, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f || !TryResolve(sizePx, out var font, out var drawSize))
            return false;

        return Fit(text, font, drawSize, EmPixels(sizePx), trackingPx * NoireUI.Scale, maxWidth, LineHeight(sizePx)).Ellipsis;
    }

    /// <summary>Draws text into a draw list with this face.</summary>
    /// <param name="drawList">The draw list.</param>
    /// <param name="position">The top left of the line, in screen pixels.</param>
    /// <param name="color">The packed ABGR color.</param>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="trackingPx">Extra space between characters at 100%, a CSS <c>letter-spacing</c>.</param>
    /// <param name="maxWidth">The width past which the text is cut with an ellipsis, in real pixels. 0 for none.</param>
    /// <param name="syntheticBoldPx">How far the glyphs are smeared to stand in for a missing weight. See <see cref="SyntheticBoldPixels"/>.</param>
    /// <returns>The drawn size in real pixels.</returns>
    public Vector2 Draw(ImDrawListPtr drawList, Vector2 position, uint color, string text, float sizePx, float trackingPx = 0f, float maxWidth = 0f, float syntheticBoldPx = 0f)
    {
        if (drawList.IsNull || string.IsNullOrEmpty(text) || !TryResolve(sizePx, out var font, out var drawSize))
            return Vector2.Zero;

        var tracking = trackingPx * NoireUI.Scale;
        var emPx = EmPixels(sizePx);
        var fit = Fit(text, font, drawSize, emPx, tracking, maxWidth, LineHeight(sizePx));

        if ((color & 0xFF000000u) == 0u)
            return fit.Size;

        var smear = syntheticBoldPx > 0f ? syntheticBoldPx * NoireUI.Scale : 0f;
        var passes = smear > 0f ? 2 : 1;

        for (var pass = 0; pass < passes; pass++)
        {
            var at = pass == 0 ? position : position + new Vector2(smear, 0f);

            DrawPlan(drawList, font, drawSize, at, color, fit);

            if (!fit.Ellipsis)
                continue;

            var tail = at + new Vector2(fit.PrefixWidth, 0f);
            var ellipsis = HasGlyph(font, EllipsisChar) ? EllipsisText : DotsText;

            DrawRun(drawList, font, drawSize, emPx, tail, color, ellipsis, tracking);
        }

        return fit.Size;
    }

    /// <summary>
    /// How far a browser smears a face to stand in for a bold it does not have, at a size.
    /// </summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The smear in logical pixels.</returns>
    public static float SyntheticBoldPixels(float sizePx)
    {
        // Skia's synthesised bold: a 24th of the size at 9 px, a 32nd at 36 px.
        var t = Math.Clamp((sizePx - 9f) / 27f, 0f, 1f);
        return sizePx * ((1f / 24f) + (((1f / 32f) - (1f / 24f)) * t));
    }

    /// <summary>
    /// Draws text into a draw list with this face. See <see cref="Draw(ImDrawListPtr, Vector2, uint, string, float, float, float)"/>.
    /// </summary>
    /// <param name="drawList">The draw list to draw into.</param>
    /// <param name="position">The top left of the line, in screen pixels.</param>
    /// <param name="color">The color.</param>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="trackingPx">Extra space between characters at 100%.</param>
    /// <param name="maxWidth">The width past which the text is cut with an ellipsis, in real pixels, or 0 for none.</param>
    /// <param name="syntheticBoldPx">How far the glyphs are smeared to stand in for a missing bold. See <see cref="SyntheticBoldPixels"/>.</param>
    /// <returns>The drawn size in real pixels.</returns>
    public Vector2 Draw(ImDrawListPtr drawList, Vector2 position, Vector4 color, string text, float sizePx, float trackingPx = 0f, float maxWidth = 0f, float syntheticBoldPx = 0f)
        => Draw(drawList, position, ColorHelper.Vector4ToUint(color), text, sizePx, trackingPx, maxWidth, syntheticBoldPx);

    /// <summary>
    /// Draws text into the current window's draw list with this face, without submitting an ImGui item.
    /// </summary>
    /// <param name="position">The top left of the line, in screen pixels.</param>
    /// <param name="color">The color.</param>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="trackingPx">Extra space between characters at 100%.</param>
    /// <param name="maxWidth">The width past which the text is cut with an ellipsis, in real pixels, or 0 for none.</param>
    /// <param name="syntheticBoldPx">How far the glyphs are smeared to stand in for a missing bold. See <see cref="SyntheticBoldPixels"/>.</param>
    /// <returns>The drawn size in real pixels.</returns>
    public Vector2 DrawAt(Vector2 position, Vector4 color, string text, float sizePx, float trackingPx = 0f, float maxWidth = 0f, float syntheticBoldPx = 0f)
    {
        using var draw = UiDraw.Begin();
        return Draw(draw.List, position, ColorHelper.Vector4ToUint(color), text, sizePx, trackingPx, maxWidth, syntheticBoldPx);
    }

    /// <summary>Draws text at the cursor as an ImGui item.</summary>
    /// <param name="color">The color.</param>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="trackingPx">Extra space between characters at 100%.</param>
    /// <param name="maxWidth">The width past which the text is cut with an ellipsis, in real pixels. 0 for none.</param>
    /// <param name="syntheticBoldPx">How far the glyphs are smeared to stand in for a missing weight. See <see cref="SyntheticBoldPixels"/>.</param>
    /// <returns>The drawn size in real pixels.</returns>
    public Vector2 Text(Vector4 color, string text, float sizePx, float trackingPx = 0f, float maxWidth = 0f, float syntheticBoldPx = 0f)
    {
        if (!UiDraw.Available)
            return Vector2.Zero;

        var position = ImGui.GetCursorScreenPos();
        var size = DrawAt(position, color, text, sizePx, trackingPx, maxWidth, syntheticBoldPx);

        ImGui.Dummy(new Vector2(size.X, MathF.Max(size.Y, LineHeight(sizePx))));
        return size;
    }

    /// <summary>Pushes this face at a size as the current ImGui font.</summary>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The scope to dispose.</returns>
    public NoireFontScope Push(float sizePx)
    {
        if (!UiDraw.Available)
            return default;

        var font = Font(sizePx);
        var window = ImGuiP.GetCurrentWindow();

        if (!font.IsNull)
        {
            ImGui.PushFont(font);

            var correction = font.FontSize > 0f ? DrawSizeFor(font, EmPixels(sizePx)) / font.FontSize : 1f;

            if (window.IsNull || MathF.Abs(correction - 1f) < 0.002f)
                return new NoireFontScope(pushedFont: true, restoreScale: float.NaN);

            var scale = window.FontWindowScale;
            ImGui.SetWindowFontScale(scale * correction);

            return new NoireFontScope(pushedFont: true, restoreScale: scale);
        }

        var current = ImGui.GetFontSize();

        if (current <= 0f || window.IsNull)
            return default;

        var previous = window.FontWindowScale;
        ImGui.SetWindowFontScale(previous * (LineHeight(sizePx) / current));

        return new NoireFontScope(pushedFont: false, restoreScale: previous);
    }

    private const char EllipsisChar = '…';

    private static ReadOnlySpan<char> EllipsisText => "…";

    private static ReadOnlySpan<char> DotsText => "...";

    // Kept for the frame. Only an atlas rebuild changes the answer, and it bumps the generation.
    private readonly ResolvedSize[] resolvedSizes = new ResolvedSize[16];
    private int resolvedFrame = -1;
    private int resolvedGeneration = -1;
    private int resolvedCount;

    private struct ResolvedSize
    {
        public float SizePx;
        public ImFontPtr Font;
        public float DrawSize;
        public bool Resolved;
    }

    private bool TryResolve(float sizePx, out ImFontPtr font, out float drawSize)
    {
        var frame = NoireUI.FrameCount;
        var generation = UiFaceAtlas.Generation;

        if (frame != resolvedFrame || generation != resolvedGeneration)
        {
            resolvedFrame = frame;
            resolvedGeneration = generation;
            resolvedCount = 0;
        }

        for (var i = 0; i < resolvedCount; i++)
        {
            ref var held = ref resolvedSizes[i];

            if (held.SizePx != sizePx)
                continue;

            font = held.Font;
            drawSize = held.DrawSize;

            return held.Resolved;
        }

        var answered = Resolve(sizePx, out font, out drawSize);

        if (resolvedCount < resolvedSizes.Length)
        {
            resolvedSizes[resolvedCount++] = new ResolvedSize
            {
                SizePx = sizePx,
                Font = font,
                DrawSize = drawSize,
                Resolved = answered,
            };
        }

        return answered;
    }

    private bool Resolve(float sizePx, out ImFontPtr font, out float drawSize)
    {
        drawSize = 0f;
        font = default;

        if (!UiDraw.Available)
            return false;

        font = Font(sizePx);

        if (!font.IsNull)
        {
            drawSize = DrawSizeFor(font, EmPixels(sizePx));
            return true;
        }

        font = ImGui.GetFont();
        drawSize = LineHeight(sizePx);

        return !font.IsNull;
    }

    // The host rounds the build size and the rasterizer maps it its own way. The em is read back off the built glyphs.
    private float DrawSizeFor(ImFontPtr font, float emPx)
    {
        var actual = ActualEm(font);
        return actual > 0f ? emPx * font.FontSize / actual : emPx * LineRatio;
    }

    // Built advances are whole pixels. Ninety letters average the rounding away.
    private unsafe float ActualEm(ImFontPtr font)
    {
        var key = new CalibrationKey(FontKey(font), UiFaceAtlas.Generation);

        if (ems.TryGet(key, out var cached))
            return cached;

        var built = 0f;
        var design = 0;

        for (var character = '!'; character <= '~'; character++)
        {
            var units = AdvanceUnits(character);

            if (units <= 0)
                continue;

            var glyph = font.FindGlyphNoFallback(character);

            if (glyph == null || glyph->AdvanceX <= 0f)
                continue;

            built += glyph->AdvanceX;
            design += units;
        }

        var em = design > 0 ? built * UnitsPerEm / design : font.FontSize / LineRatio;

        ems.Set(key, em);

        return em;
    }

    private TextFit Fit(string text, ImFontPtr font, float drawSize, float emPx, float tracking, float maxWidth, float height)
    {
        var key = new FitKey(text, FontKey(font), drawSize, tracking, maxWidth, Kerning, UiFaceAtlas.Generation);

        if (fits.TryGet(key, out var cached))
            return cached;

        var fit = MeasureFit(text, font, drawSize, emPx, tracking, maxWidth, height);
        fits.Set(key, fit);

        return fit;
    }

    private static unsafe nint FontKey(ImFontPtr font) => (nint)font.Handle;

    private TextFit MeasureFit(string text, ImFontPtr font, float drawSize, float emPx, float tracking, float maxWidth, float height)
    {
        var scale = font.FontSize > 0f ? drawSize / font.FontSize : 1f;
        var width = RunWidth(text, font, scale, emPx, tracking);

        if (maxWidth <= 0f || width <= maxWidth)
            return new TextFit(new Vector2(width, height), text.Length, false, width, BuildPlan(font, drawSize, emPx, text.AsSpan(), tracking));

        var ellipsis = HasGlyph(font, EllipsisChar) ? EllipsisText : DotsText;
        var ellipsisWidth = RunWidth(ellipsis, font, scale, emPx, tracking);
        var budget = maxWidth - ellipsisWidth - tracking;

        var x = 0f;
        var visible = 0;
        var fitted = 0f;

        for (var at = 0; at < text.Length;)
        {
            var length = CharLength(text, at);
            var advance = Advance(font, text, at, length, emPx, scale);

            if (at > 0)
                x += Shift(text, at, emPx, tracking);

            if (x + advance > budget)
                break;

            x += advance;
            at += length;

            // Trailing spaces are dropped before the ellipsis, like a browser.
            if (!char.IsWhiteSpace(text[at - length]))
            {
                visible = at;
                fitted = x;
            }
        }

        var prefix = visible > 0 ? fitted + tracking : 0f;
        var total = MathF.Min(maxWidth, prefix + ellipsisWidth);

        return new TextFit(new Vector2(total, height), visible, true, prefix, BuildPlan(font, drawSize, emPx, text.AsSpan(0, visible), tracking));
    }

    private float RunWidth(ReadOnlySpan<char> text, ImFontPtr font, float scale, float emPx, float tracking)
    {
        var x = 0f;

        for (var at = 0; at < text.Length;)
        {
            var length = CharLength(text, at);

            if (at > 0)
                x += Shift(text, at, emPx, tracking);

            x += Advance(font, text, at, length, emPx, scale);
            at += length;
        }

        return x;
    }

    private const float DriftTolerance = 0.12f;

    // ImGui advances by whole-pixel built advances. A run restarts where it drifted from the design advance.
    private void DrawRun(ImDrawListPtr drawList, ImFontPtr font, float drawSize, float emPx, Vector2 position, uint color, ReadOnlySpan<char> text, float tracking)
    {
        var scale = font.FontSize > 0f ? drawSize / font.FontSize : 1f;
        var pen = 0f;
        var runStart = 0;
        var runPen = 0f;
        var runDrawn = 0f;

        for (var at = 0; at < text.Length;)
        {
            var length = CharLength(text, at);

            if (at > 0)
            {
                pen += Shift(text, at, emPx, tracking);

                if (MathF.Abs(runPen + runDrawn - pen) > DriftTolerance)
                {
                    drawList.AddText(font, drawSize, position + new Vector2(runPen, 0f), color, text[runStart..at]);

                    runStart = at;
                    runPen = pen;
                    runDrawn = 0f;
                }
            }

            pen += Advance(font, text, at, length, emPx, scale);
            runDrawn += BuiltAdvance(font, text, at, length) * scale;
            at += length;
        }

        if (runStart < text.Length)
            drawList.AddText(font, drawSize, position + new Vector2(runPen, 0f), color, text[runStart..]);
    }

    private static void DrawPlan(ImDrawListPtr drawList, ImFontPtr font, float drawSize, Vector2 position, uint color, in TextFit fit)
    {
        var plan = fit.Plan;

        if (plan == null || fit.Visible <= 0)
            return;

        var utf8 = plan.Utf8.AsSpan();

        for (var i = 0; i < plan.Count; i++)
            drawList.AddText(font, drawSize, position + new Vector2(plan.Pen[i], 0f), color, utf8.Slice(plan.ByteStart[i], plan.ByteLength[i]));
    }

    private TextPlan BuildPlan(ImFontPtr font, float drawSize, float emPx, ReadOnlySpan<char> text, float tracking)
    {
        var scale = font.FontSize > 0f ? drawSize / font.FontSize : 1f;
        var pen = 0f;
        var runStart = 0;
        var runPen = 0f;
        var runDrawn = 0f;
        var count = 0;

        Grow(text.Length);

        var starts = planStarts;
        var pens = planPens;
        var lengths = planLengths;

        for (var at = 0; at < text.Length;)
        {
            var length = CharLength(text, at);

            if (at > 0)
            {
                pen += Shift(text, at, emPx, tracking);

                if (MathF.Abs(runPen + runDrawn - pen) > DriftTolerance)
                {
                    starts[count] = runStart;
                    lengths[count] = at - runStart;
                    pens[count] = runPen;
                    count++;

                    runStart = at;
                    runPen = pen;
                    runDrawn = 0f;
                }
            }

            pen += Advance(font, text, at, length, emPx, scale);
            runDrawn += BuiltAdvance(font, text, at, length) * scale;
            at += length;
        }

        if (runStart < text.Length)
        {
            starts[count] = runStart;
            lengths[count] = text.Length - runStart;
            pens[count] = runPen;
            count++;
        }

        var utf8 = new byte[System.Text.Encoding.UTF8.GetByteCount(text)];
        var byteStart = new int[count];
        var byteLength = new int[count];
        var written = 0;

        for (var i = 0; i < count; i++)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text.Slice(starts[i], lengths[i]), utf8.AsSpan(written));

            byteStart[i] = written;
            byteLength[i] = bytes;
            written += bytes;
        }

        return new TextPlan
        {
            Count = count,
            Pen = pens[..count],
            Utf8 = utf8,
            ByteStart = byteStart,
            ByteLength = byteLength,
        };
    }

    private void Grow(int length)
    {
        if (planStarts.Length >= length + 1)
            return;

        var size = Math.Max(length + 1, planStarts.Length * 2);
        planStarts = new int[size];
        planLengths = new int[size];
        planPens = new float[size];
    }

    private float Shift(ReadOnlySpan<char> text, int at, float emPx, float tracking)
        => tracking + KernPixels(text[at - 1], text[at], emPx);

    private float KernPixels(char left, char right, float emPx)
    {
        if (!Kerning || UnitsPerEm <= 0)
            return 0f;

        var pairs = KerningTable();

        if (pairs == null)
            return 0f;

        var units = pairs.Units(GlyphId(left), GlyphId(right));

        return units == 0 ? 0f : units * emPx / UnitsPerEm;
    }

    private UiFontKerning? KerningTable()
    {
        if (kerningRead)
            return kerning;

        kerningRead = true;
        kerning = UiFontKerning.Create(Data, gposOffset, kernOffset);

        return kerning;
    }

    private int GlyphId(char character) => Glyph(character).Glyph;

    private int AdvanceUnits(char character) => Glyph(character).Units;

    private (int Glyph, int Units) Glyph(char character)
    {
        if (glyphIds.TryGetValue(character, out var found))
            return found;

        var glyph = cmapOffset >= 0 ? ReadGlyphId(Data, cmapOffset, character) : 0;
        var units = glyph > 0 ? ReadAdvance(Data, hmtxOffset, longMetrics, glyph) : 0;

        found = (glyph, units);
        glyphIds[character] = found;

        return found;
    }

    private static int CharLength(ReadOnlySpan<char> text, int at)
        => char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]) ? 2 : 1;

    // A character from a merged font keeps the built advance. Its design metrics are not in this file.
    private float Advance(ImFontPtr font, ReadOnlySpan<char> text, int at, int length, float emPx, float scale)
    {
        if (length == 1)
        {
            var units = AdvanceUnits(text[at]);

            if (units > 0)
                return units * emPx / UnitsPerEm;
        }

        return BuiltAdvance(font, text, at, length) * scale;
    }

    // ImGui 1.88 fonts hold the Basic Multilingual Plane only. A surrogate pair draws as the fallback glyph.
    private static unsafe float BuiltAdvance(ImFontPtr font, ReadOnlySpan<char> text, int at, int length)
    {
        var glyph = font.FindGlyph(length == 2 ? '�' : text[at]);
        return glyph == null ? 0f : glyph->AdvanceX;
    }

    private static unsafe bool HasGlyph(ImFontPtr font, char c) => font.FindGlyphNoFallback(c) != null;

    #endregion

    #region Font file

    internal readonly record struct FaceMetrics(
        int UnitsPerEm, int Ascender, int Descender, int CmapOffset, int HmtxOffset, int LongMetrics, int GposOffset, int KernOffset);

    internal static FaceMetrics ReadMetrics(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            throw new ArgumentException("The data is too short to be a font file.", nameof(data));

        var tables = ReadUInt16(data, 4);
        int head = -1, hhea = -1, hmtx = -1, cmap = -1, gpos = -1, kern = -1;

        for (var index = 0; index < tables; index++)
        {
            var record = 12 + (index * 16);

            if (record + 16 > data.Length)
                break;

            var tag = data.Slice(record, 4);
            var offset = (int)ReadUInt32(data, record + 8);

            if (tag.SequenceEqual("head"u8))
                head = offset;
            else if (tag.SequenceEqual("hhea"u8))
                hhea = offset;
            else if (tag.SequenceEqual("hmtx"u8))
                hmtx = offset;
            else if (tag.SequenceEqual("cmap"u8))
                cmap = offset;
            else if (tag.SequenceEqual("GPOS"u8))
                gpos = offset;
            else if (tag.SequenceEqual("kern"u8))
                kern = offset;
        }

        if (head < 0 || hhea < 0 || head + 20 > data.Length || hhea + 36 > data.Length)
            throw new ArgumentException("The font has no readable head or hhea table.", nameof(data));

        var unitsPerEm = ReadUInt16(data, head + 18);
        var ascender = (short)ReadUInt16(data, hhea + 4);
        var descender = (short)ReadUInt16(data, hhea + 6);

        if (unitsPerEm == 0 || ascender - descender <= 0)
            throw new ArgumentException("The font reports empty metrics.", nameof(data));

        return new FaceMetrics(unitsPerEm, ascender, descender, cmap, hmtx, ReadUInt16(data, hhea + 34), gpos, kern);
    }

    private static int ReadAdvance(ReadOnlySpan<byte> data, int hmtx, int longMetrics, int glyph)
    {
        if (hmtx < 0 || longMetrics <= 0)
            return 0;

        var entry = hmtx + (Math.Min(glyph, longMetrics - 1) * 4);

        return entry + 2 <= data.Length ? ReadUInt16(data, entry) : 0;
    }

    // Formats 4 and 12 cover every font shipped as a web font.
    private static int ReadGlyphId(ReadOnlySpan<byte> data, int cmap, char character)
    {
        if (cmap + 4 > data.Length)
            return 0;

        var subtables = ReadUInt16(data, cmap + 2);
        var chosen = -1;

        for (var index = 0; index < subtables; index++)
        {
            var record = cmap + 4 + (index * 8);

            if (record + 8 > data.Length)
                break;

            var platform = ReadUInt16(data, record);
            var encoding = ReadUInt16(data, record + 2);
            var offset = cmap + (int)ReadUInt32(data, record + 4);

            if (offset + 2 > data.Length)
                continue;

            var unicode = (platform == 3 && encoding is 1 or 10) || platform == 0;

            if (unicode)
                chosen = offset;
        }

        if (chosen < 0)
            return 0;

        return ReadUInt16(data, chosen) switch
        {
            4 => ReadFormat4(data, chosen, character),
            12 => ReadFormat12(data, chosen, character),
            _ => 0,
        };
    }

    private static int ReadFormat4(ReadOnlySpan<byte> data, int table, char character)
    {
        var segments = ReadUInt16(data, table + 6) / 2;
        var ends = table + 14;
        var starts = ends + (segments * 2) + 2;
        var deltas = starts + (segments * 2);
        var ranges = deltas + (segments * 2);

        if (ranges + (segments * 2) > data.Length)
            return 0;

        for (var segment = 0; segment < segments; segment++)
        {
            if (ReadUInt16(data, ends + (segment * 2)) < character)
                continue;

            var start = ReadUInt16(data, starts + (segment * 2));

            if (start > character)
                return 0;

            var offset = ReadUInt16(data, ranges + (segment * 2));
            var delta = (short)ReadUInt16(data, deltas + (segment * 2));

            if (offset == 0)
                return (character + delta) & 0xFFFF;

            var at = ranges + (segment * 2) + offset + ((character - start) * 2);

            if (at + 2 > data.Length)
                return 0;

            var glyph = ReadUInt16(data, at);

            return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
        }

        return 0;
    }

    private static int ReadFormat12(ReadOnlySpan<byte> data, int table, char character)
    {
        var groups = (int)ReadUInt32(data, table + 12);

        for (var group = 0; group < groups; group++)
        {
            var at = table + 16 + (group * 12);

            if (at + 12 > data.Length)
                break;

            var start = ReadUInt32(data, at);
            var end = ReadUInt32(data, at + 4);

            if (character < start)
                return 0;

            if (character > end)
                continue;

            return (int)(ReadUInt32(data, at + 8) + (character - start));
        }

        return 0;
    }

    private static int ReadUInt16(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int at)
        => ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];

    #endregion

    /// <summary>
    /// Releases every size built for this face. Drawing with it afterwards uses the stand-in.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        UiFaceEntry[] held;

        lock (entries)
        {
            held = new UiFaceEntry[entries.Count];
            entries.Values.CopyTo(held, 0);
        }

        foreach (var entry in held)
            UiFaceAtlas.Remove(entry);

        fits.Clear();
    }
}

/// <summary>
/// A <see cref="NoireFont"/> pushed as the current ImGui font. Dispose it once to pop.
/// </summary>
public struct NoireFontScope : IDisposable
{
    private bool pushedFont;
    private float restoreScale;

    internal NoireFontScope(bool pushedFont, float restoreScale)
    {
        this.pushedFont = pushedFont;
        this.restoreScale = restoreScale;
    }

    /// <summary>Pops the font and restores the window font scale, in the order they were changed.</summary>
    public void Dispose()
    {
        if (restoreScale > 0f)
        {
            ImGui.SetWindowFontScale(restoreScale);
            restoreScale = float.NaN;
        }

        if (!pushedFont)
            return;

        ImGui.PopFont();
        pushedFont = false;
    }
}
