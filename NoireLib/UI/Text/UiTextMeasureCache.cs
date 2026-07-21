using NoireLib.Helpers;
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
    }
}
