using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Remembers what a string measured, so a label that has not changed is not re-measured on every frame it is drawn.
/// </summary>
/// <remarks>
/// Measuring text pushes a font handle, walks the string a glyph at a time, and does that after marshalling
/// UTF-16 to UTF-8; unmeasured, that cost repeats every label every frame.<br/>
/// Keyed on everything that can change the answer: the text, the size asked for, the current font's size (what the
/// fallback path measures with), the UI scale, and the font generation. A stale measurement is a layout wrong
/// everywhere by a few pixels, not obviously a caching bug.<br/>
/// Built on <see cref="HotPathCache{TKey, TValue}"/> rather than its own dictionaries. Reached only from the draw
/// thread, since measuring needs a frame in progress.
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
    /// Separate from <see cref="Key"/>: a glyph is a codepoint rather than a string, and caching one answers without
    /// a string existing at all. A tracked label measures every character on every frame it draws; the alphabet a
    /// plugin uses is small and fixed, so this fills in the first few frames and only hits afterwards.<br/>
    /// No size asked for here: the caller has already pushed the font it is measuring with. See
    /// <see cref="AmbientKey"/> for why the font is identified rather than described.
    /// </remarks>
    private readonly record struct GlyphKey(int Codepoint, nint Font, float SizePx, float Scale, int Generation);

    /// <summary>
    /// Everything a measurement taken against the font already pushed depends on.
    /// </summary>
    /// <remarks>
    /// Carries the font itself rather than only its size, unlike <see cref="Key"/>: a caller measuring in the font it
    /// has pushed may have pushed anything, and two different fonts at the same size are not the same measurement.
    /// Without this, the icon font and the body font at the same pixel size would share a key and answer for each
    /// other.
    /// </remarks>
    private readonly record struct AmbientKey(string Text, nint Font, float SizePx, float Scale, int Generation);

    /// <summary>
    /// How many measurements are kept before the cache starts over.
    /// </summary>
    /// <remarks>
    /// A bound rather than a budget: an interface draws a stable set of labels, so this fills once and then only
    /// hits. Protects against a label that is a different string every frame, such as a live counter, which would
    /// otherwise grow the dictionary without ever hitting it.
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
    /// Deliberately not the glyph's atlas quad: the atlas spreads glyphs across several textures, so painting stays
    /// a draw-list text call per glyph. This cache only removes the measuring around those calls.
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
    /// The per-codepoint cache costs a key hash and a probe per character per frame; resolving this table once per
    /// run turns that into an array index. Codepoints past it fall back to the per-codepoint cache.
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
    /// The pointer rather than anything read out of the font: only ever compared, never followed. A freed and
    /// replaced font takes a new address, which is the invalidation wanted; the generation in the key covers a new
    /// font landing at a recycled address.
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
    /// Read here rather than handed to <see cref="HotPathCache{TKey, TValue}.InvalidateIfChanged"/>, which would
    /// drop the whole cache when either moved. Carried in the key instead, so a scale that moves and moves back
    /// finds its old measurements still there.
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
