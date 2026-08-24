using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

// Remembers what a string measured. Reached only from the draw thread, since measuring needs a frame in progress.
internal static class UiTextMeasureCache
{
    private readonly record struct Key(string Text, float SizePx, float AmbientSizePx, float Scale, int Generation);

    private readonly record struct GlyphKey(int Codepoint, nint Font, float SizePx, float Scale, int Generation);

    private readonly record struct AmbientKey(string Text, nint Font, float SizePx, float Scale, int Generation);

    private readonly record struct TrackedKey(string Text, float Tracking, float SizePx, float AmbientSizePx, float Scale, int Generation);

    private const int MaxEntries = 4096;

    private static readonly HotPathCache<Key, Vector2> Sizes = new(MaxEntries);
    private static readonly HotPathCache<Key, float> CenterOffsets = new(MaxEntries);
    private static readonly HotPathCache<GlyphKey, GlyphMetrics> Glyphs = new(MaxEntries);
    private static readonly HotPathCache<AmbientKey, Vector2> AmbientSizes = new(MaxEntries);
    private static readonly HotPathCache<TrackedKey, Vector2> TrackedSizes = new(MaxEntries);

    internal static bool TryGetSize(string text, float sizePx, float ambientSizePx, out Vector2 size)
        => Sizes.TryGet(SizeKey(text, sizePx, ambientSizePx), out size);

    internal static void StoreSize(string text, float sizePx, float ambientSizePx, Vector2 size)
        => Sizes.Set(SizeKey(text, sizePx, ambientSizePx), size);

    internal static bool TryGetCenterOffset(float sizePx, float ambientSizePx, out float offset)
        => CenterOffsets.TryGet(CenterKey(sizePx, ambientSizePx), out offset);

    internal static void StoreCenterOffset(float sizePx, float ambientSizePx, float offset)
        => CenterOffsets.Set(CenterKey(sizePx, ambientSizePx), offset);

    // Known says whether the entry has been filled, telling an empty slot of the ASCII table apart from a glyph.
    internal readonly record struct GlyphMetrics(float Advance, bool Visible, bool Known);

    private readonly record struct GlyphTableKey(nint Font, float SizePx, float Scale, int Generation);

    // One metrics table per font and size for the first 128 codepoints; codepoints past it fall back to the
    // per-codepoint cache.
    private static readonly HotPathCache<GlyphTableKey, GlyphMetrics[]> AsciiGlyphs = new(64);

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

        internal bool TryGet(int codepoint, out GlyphMetrics metrics)
        {
            if ((uint)codepoint < 128u)
            {
                metrics = ascii[codepoint];
                return metrics.Known;
            }

            return TryGetGlyphMetrics(codepoint, font, sizePx, out metrics);
        }

        internal void Store(int codepoint, GlyphMetrics metrics)
        {
            if ((uint)codepoint < 128u)
                ascii[codepoint] = metrics;
            else
                StoreGlyphMetrics(codepoint, font, sizePx, metrics);
        }
    }

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

    // The codepoint is a full codepoint, so a surrogate pair is one entry.
    internal static bool TryGetGlyphMetrics(int codepoint, nint font, float sizePx, out GlyphMetrics metrics)
        => Glyphs.TryGet(
            new GlyphKey(codepoint, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            out metrics);

    internal static void StoreGlyphMetrics(int codepoint, nint font, float sizePx, GlyphMetrics metrics)
        => Glyphs.Set(
            new GlyphKey(codepoint, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            metrics);

    internal static bool TryGetAmbientSize(string text, nint font, float sizePx, out Vector2 size)
        => AmbientSizes.TryGet(
            new AmbientKey(text, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            out size);

    internal static void StoreAmbientSize(string text, nint font, float sizePx, Vector2 size)
        => AmbientSizes.Set(
            new AmbientKey(text, font, sizePx, NoireUI.Scale, UiFontCache.Generation),
            size);

    internal static bool TryGetTrackedSize(string text, float tracking, float sizePx, float ambientSizePx, out Vector2 size)
        => TrackedSizes.TryGet(
            new TrackedKey(text, tracking, sizePx, ambientSizePx, NoireUI.Scale, UiFontCache.Generation),
            out size);

    internal static void StoreTrackedSize(string text, float tracking, float sizePx, float ambientSizePx, Vector2 size)
        => TrackedSizes.Set(
            new TrackedKey(text, tracking, sizePx, ambientSizePx, NoireUI.Scale, UiFontCache.Generation),
            size);

    // The pointer is only ever compared, never followed, and is 0 when there is no current font.
    internal static unsafe nint CurrentFont()
    {
        var font = ImGui.GetFont();
        return font.IsNull ? 0 : (nint)font.Handle;
    }

    private static Key SizeKey(string text, float sizePx, float ambientSizePx)
        => new(text, sizePx, ambientSizePx, NoireUI.Scale, UiFontCache.Generation);

    // Keyed on the empty string: the offset is a property of the font rather than of any particular text, and sharing
    // the key shape keeps one invalidation rule for both caches.
    private static Key CenterKey(float sizePx, float ambientSizePx)
        => SizeKey(string.Empty, sizePx, ambientSizePx);

    // Called when the fonts are released.
    internal static void Clear()
    {
        Sizes.Clear();
        CenterOffsets.Clear();
        Glyphs.Clear();
        AsciiGlyphs.Clear();
        AmbientSizes.Clear();
        TrackedSizes.Clear();
    }
}
