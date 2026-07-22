using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Remembers what a string measured, so a label that has not changed is not re-measured on every frame it is drawn.
/// </summary>
/// <remarks>
/// Measuring text is not free: it pushes a font handle, walks the string a glyph at a time, and does that after the
/// string has been marshalled from UTF-16 to UTF-8. A layout built by measuring its labels therefore pays for every
/// label, every frame, to arrive at the answer it had last frame.<br/>
/// The cache is keyed on everything that can change the answer: the text, the size asked for, the size of whatever font
/// is current (which is what the fallback path measures with), the UI scale, and the font generation, which moves
/// whenever the atlas is rebuilt and a size that was being approximated becomes real. A key that is complete is what
/// makes this safe; a stale text measurement is a layout that is wrong everywhere by a few pixels and does not look
/// like a caching bug.<br/>
/// Built on <see cref="HotPathCache{TKey, TValue}"/> rather than carrying its own dictionaries, so there is one
/// implementation of the pattern in the library rather than two that can drift apart. Reached only from the draw
/// thread, because measuring needs a frame in progress, which is the assumption that primitive is built on.
/// </remarks>
internal static class UiTextMeasureCache
{
    /// <summary>
    /// Everything a measurement depends on. Any of these moving is a different answer.
    /// </summary>
    private readonly record struct Key(string Text, float SizePx, float AmbientSizePx, float Scale, int Generation);

    /// <summary>
    /// Everything one glyph's rendering depends on.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Key"/> because a glyph is a codepoint rather than a string, and the point of caching
    /// one is to answer without a string existing at all. A tracked label measures every character it draws on every
    /// frame it is drawn, and the alphabet a plugin uses is small and fixed, so this fills in the first few frames and
    /// only hits afterwards.<br/>
    /// There is no size asked for here: the caller has already pushed the font it is measuring with, so the font in
    /// hand is the whole answer. See <see cref="AmbientKey"/> for why the font is identified rather than described.
    /// </remarks>
    private readonly record struct GlyphKey(int Codepoint, nint Font, float SizePx, float Scale, int Generation);

    /// <summary>
    /// Everything a measurement taken against the font already pushed depends on.
    /// </summary>
    /// <remarks>
    /// Carries the font itself rather than only its size, which <see cref="Key"/> does not have to. Every measurement
    /// filed under <see cref="Key"/> is taken against a font this library resolved for a size, so the size names the
    /// font. A caller measuring in the font it has pushed may have pushed anything, and two different fonts reporting
    /// the same size are not the same measurement: the icon font and the body font at the same pixel size would share
    /// a key and answer for each other.
    /// </remarks>
    private readonly record struct AmbientKey(string Text, nint Font, float SizePx, float Scale, int Generation);

    /// <summary>
    /// How many measurements are kept before the cache starts over.
    /// </summary>
    /// <remarks>
    /// An interface draws a stable set of labels, so in practice this fills once and then only hits. It is a bound
    /// rather than a budget: what it protects against is a label that is a different string every frame, such as a
    /// live counter, which would otherwise grow the dictionary without ever hitting it.
    /// </remarks>
    private const int MaxEntries = 4096;

    private static readonly HotPathCache<Key, Vector2> Sizes = new(MaxEntries);
    private static readonly HotPathCache<Key, float> CenterOffsets = new(MaxEntries);
    private static readonly HotPathCache<GlyphKey, GlyphMetrics> Glyphs = new(MaxEntries);
    private static readonly HotPathCache<AmbientKey, Vector2> AmbientSizes = new(MaxEntries);

    /// <summary>
    /// Looks up what a string measured.
    /// </summary>
    /// <param name="text">The text being measured.</param>
    /// <param name="sizePx">The size it is being measured at.</param>
    /// <param name="ambientSizePx">The size of the font currently pushed.</param>
    /// <param name="size">The remembered measurement.</param>
    /// <returns>Whether the measurement was already known.</returns>
    internal static bool TryGetSize(string text, float sizePx, float ambientSizePx, out Vector2 size)
        => Sizes.TryGet(SizeKey(text, sizePx, ambientSizePx), out size);

    /// <summary>
    /// Remembers what a string measured.
    /// </summary>
    /// <param name="text">The text that was measured.</param>
    /// <param name="sizePx">The size it was measured at.</param>
    /// <param name="ambientSizePx">The size of the font that was current.</param>
    /// <param name="size">The measurement.</param>
    internal static void StoreSize(string text, float sizePx, float ambientSizePx, Vector2 size)
        => Sizes.Set(SizeKey(text, sizePx, ambientSizePx), size);

    /// <summary>
    /// Looks up a font's centre offset.
    /// </summary>
    /// <param name="sizePx">The size it is being measured at.</param>
    /// <param name="ambientSizePx">The size of the font currently pushed.</param>
    /// <param name="offset">The remembered offset.</param>
    /// <returns>Whether the offset was already known.</returns>
    internal static bool TryGetCenterOffset(float sizePx, float ambientSizePx, out float offset)
        => CenterOffsets.TryGet(CenterKey(sizePx, ambientSizePx), out offset);

    /// <summary>
    /// Remembers a font's centre offset.
    /// </summary>
    /// <param name="sizePx">The size it was measured at.</param>
    /// <param name="ambientSizePx">The size of the font that was current.</param>
    /// <param name="offset">The offset.</param>
    internal static void StoreCenterOffset(float sizePx, float ambientSizePx, float offset)
        => CenterOffsets.Set(CenterKey(sizePx, ambientSizePx), offset);

    /// <summary>
    /// What placing one glyph needs to know without asking the font again: how far it moves the pen, and whether it
    /// paints anything at all.
    /// </summary>
    /// <remarks>
    /// Deliberately not the glyph's atlas quad. The atlas spreads its glyphs across several textures and which one a
    /// glyph lives on is the renderer's business, so painting stays a draw-list text call per glyph; what this cache
    /// removes is the measuring around those calls.
    /// </remarks>
    /// <param name="Advance">How far the glyph moves the pen.</param>
    /// <param name="Visible">Whether the glyph paints anything. A space advances the pen and draws nothing.</param>
    /// <param name="Known">Whether this entry has been filled. What tells a slot of the ASCII table apart from a glyph.</param>
    internal readonly record struct GlyphMetrics(float Advance, bool Visible, bool Known);

    /// <summary>
    /// Everything an ASCII glyph table depends on: the run invariants of <see cref="GlyphKey"/>, without the codepoint.
    /// </summary>
    private readonly record struct GlyphTableKey(nint Font, float SizePx, float Scale, int Generation);

    /// <summary>
    /// One metrics table per font and size for the first 128 codepoints, which is nearly every character a label draws.
    /// </summary>
    /// <remarks>
    /// The per-codepoint cache answers correctly but answering costs a key hash and a probe per character per frame,
    /// and a tracked label asks for every character it draws. Resolving the table once per run turns the per-character
    /// ask into an array index. Codepoints past the table fall back to the per-codepoint cache.
    /// </remarks>
    private static readonly HotPathCache<GlyphTableKey, GlyphMetrics[]> AsciiGlyphs = new(64);

    /// <summary>
    /// The per-run view over the glyph caches: the ASCII table resolved once, and the shared cache behind it.
    /// </summary>
    internal readonly struct GlyphRun
    {
        private readonly GlyphMetrics[] ascii;
        private readonly nint font;
        private readonly float sizePx;

        internal GlyphRun(GlyphMetrics[] ascii, nint font, float sizePx)
        {
            this.ascii = ascii;
            this.font = font;
            this.sizePx = sizePx;
        }

        /// <summary>
        /// Looks up how one glyph renders, through the table when the codepoint fits it.
        /// </summary>
        /// <param name="codepoint">The character, as a full codepoint.</param>
        /// <param name="metrics">The remembered metrics.</param>
        /// <returns>Whether the metrics were already known.</returns>
        internal bool TryGet(int codepoint, out GlyphMetrics metrics)
        {
            if ((uint)codepoint < 128u)
            {
                metrics = ascii[codepoint];
                return metrics.Known;
            }

            return TryGetGlyphMetrics(codepoint, font, sizePx, out metrics);
        }

        /// <summary>
        /// Remembers how one glyph renders, in the table when the codepoint fits it.
        /// </summary>
        /// <param name="codepoint">The character, as a full codepoint.</param>
        /// <param name="metrics">The metrics.</param>
        internal void Store(int codepoint, GlyphMetrics metrics)
        {
            if ((uint)codepoint < 128u)
                ascii[codepoint] = metrics;
            else
                StoreGlyphMetrics(codepoint, font, sizePx, metrics);
        }
    }

    /// <summary>
    /// Opens the per-run glyph view for the font in hand, resolving the ASCII table once for the whole run.
    /// </summary>
    /// <param name="font">The font currently pushed. See <see cref="CurrentFont"/>.</param>
    /// <param name="sizePx">The size of the font currently pushed.</param>
    /// <returns>The view to look glyphs up through.</returns>
    internal static GlyphRun OpenGlyphRun(nint font, float sizePx)
    {
        var key = new GlyphTableKey(font, sizePx, NoireUI.Scale, UiFontCache.Generation);

        if (!AsciiGlyphs.TryGet(key, out var ascii))
        {
            ascii = new GlyphMetrics[128];
            AsciiGlyphs.Set(key, ascii);
        }

        return new GlyphRun(ascii, font, sizePx);
    }

    /// <summary>
    /// Looks up how one glyph renders.
    /// </summary>
    /// <param name="codepoint">The character, as a full codepoint so a surrogate pair is one entry.</param>
    /// <param name="font">The font currently pushed. See <see cref="CurrentFont"/>.</param>
    /// <param name="sizePx">The size of the font currently pushed.</param>
    /// <param name="metrics">The remembered metrics.</param>
    /// <returns>Whether the metrics were already known.</returns>
    internal static bool TryGetGlyphMetrics(int codepoint, nint font, float sizePx, out GlyphMetrics metrics)
        => Glyphs.TryGet(
            new GlyphKey(codepoint, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            out metrics);

    /// <summary>
    /// Remembers how one glyph renders.
    /// </summary>
    /// <param name="codepoint">The character, as a full codepoint.</param>
    /// <param name="font">The font that was current.</param>
    /// <param name="sizePx">The size of the font that was current.</param>
    /// <param name="metrics">The metrics.</param>
    internal static void StoreGlyphMetrics(int codepoint, nint font, float sizePx, GlyphMetrics metrics)
        => Glyphs.Set(
            new GlyphKey(codepoint, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            metrics);

    /// <summary>
    /// Looks up what a string measured against the font already pushed.
    /// </summary>
    /// <param name="text">The text being measured.</param>
    /// <param name="font">The font currently pushed. See <see cref="CurrentFont"/>.</param>
    /// <param name="sizePx">The size of the font currently pushed.</param>
    /// <param name="size">The remembered measurement.</param>
    /// <returns>Whether the measurement was already known.</returns>
    internal static bool TryGetAmbientSize(string text, nint font, float sizePx, out Vector2 size)
        => AmbientSizes.TryGet(
            new AmbientKey(text, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            out size);

    /// <summary>
    /// Remembers what a string measured against the font that was pushed.
    /// </summary>
    /// <param name="text">The text that was measured.</param>
    /// <param name="font">The font that was current.</param>
    /// <param name="sizePx">The size of the font that was current.</param>
    /// <param name="size">The measurement.</param>
    internal static void StoreAmbientSize(string text, nint font, float sizePx, Vector2 size)
        => AmbientSizes.Set(
            new AmbientKey(text, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            size);

    /// <summary>
    /// Identifies the font currently pushed, for the keys that measure against it.
    /// </summary>
    /// <remarks>
    /// The pointer rather than anything read out of the font. It is only ever compared, never followed, and a font
    /// that is freed and replaced takes a new address, which is exactly the invalidation wanted. The generation in the
    /// key covers the case of a new font landing at a recycled address.
    /// </remarks>
    /// <returns>A value identifying the current font, or 0 when there is none.</returns>
    internal static unsafe nint CurrentFont()
    {
        var font = ImGui.GetFont();
        return font.IsNull ? 0 : (nint)font.Handle;
    }

    /// <summary>
    /// The key a measurement is stored under, read from the live scale and font generation.
    /// </summary>
    /// <remarks>
    /// Both are read here rather than handed to <see cref="HotPathCache{TKey, TValue}.InvalidateIfChanged"/>, which
    /// would drop the whole cache when either moved. Carrying them in the key instead means a scale that moves and
    /// moves back finds its old measurements still there, and the atlas rebuild that follows a scale change does not
    /// discard the measurements taken at the scale being returned to.
    /// </remarks>
    private static Key SizeKey(string text, float sizePx, float ambientSizePx)
        => new(text, sizePx, ambientSizePx, NoireUI.Scale, UiFontCache.Generation);

    /// <summary>
    /// Keyed on the empty string: the offset is a property of the font rather than of any particular text, and sharing
    /// the key shape keeps one invalidation rule for both caches.
    /// </summary>
    private static Key CenterKey(float sizePx, float ambientSizePx)
        => SizeKey(string.Empty, sizePx, ambientSizePx);

    /// <summary>
    /// Forgets every measurement. Called when the fonts are released.
    /// </summary>
    internal static void Clear()
    {
        Sizes.Clear();
        CenterOffsets.Clear();
        Glyphs.Clear();
        AsciiGlyphs.Clear();
        AmbientSizes.Clear();
    }
}
