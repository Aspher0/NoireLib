using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Globalization;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// What a value reads as on screen, remembered so that a value which has not moved is not written out again.
/// </summary>
/// <remarks>
/// A widget showing a number, a duration or a colour redraws every frame; formatting is the part of that which
/// produces a string. The value behind it rarely changes, so the frames in between were spending a string to
/// arrive at the text already on screen.<br/>
/// Every cache here is bounded rather than budgeted: at the bound it starts over, so the worst case is the cost of
/// not caching at all.<br/>
/// Reached only from the draw thread, like <see cref="UiIds"/>, so the caches need no lock.
/// </remarks>
internal static class UiValueText
{
    /// <summary>
    /// How many entries each cache keeps. Sized for a page of fields rather than for a series: the values that move
    /// every frame are the ones being dragged, and there is only ever one of those.
    /// </summary>
    private const int MaxEntries = 1024;

    /// <summary>
    /// A number, and everything that changes how it reads.
    /// </summary>
    /// <remarks>
    /// The culture is held as well as the format: the same number and format read differently under another
    /// culture, and a stale cache entry would be wrong in a way nothing else explains.
    /// </remarks>
    private readonly record struct NumberKey(float Value, string Format, string Culture);

    /// <summary>
    /// A duration, as itself. Wrapped rather than used directly, since the cache's key must be a record struct for
    /// the equality a lookup needs without boxing.
    /// </summary>
    private readonly record struct DurationKey(TimeSpan Value);

    /// <summary>
    /// A colour, and whether its alpha is written.
    /// </summary>
    private readonly record struct HexKey(Vector4 Color, bool WithAlpha);

    /// <summary>
    /// An icon, as itself. Wrapped for the reason <see cref="DurationKey"/> is: an enum is a struct but does not
    /// implement <see cref="IEquatable{T}"/> for itself, which the cache's key requires.
    /// </summary>
    private readonly record struct IconKey(FontAwesomeIcon Icon);

    /// <summary>
    /// A badge count and the ceiling it is capped at, which together decide whether it reads as a number or as an
    /// overflow.
    /// </summary>
    private readonly record struct CountKey(int Value, int MaxCount);

    private static readonly HotPathCache<NumberKey, string> Numbers = new(MaxEntries);
    private static readonly HotPathCache<DurationKey, string> Durations = new(MaxEntries);
    private static readonly HotPathCache<HexKey, string> Hexes = new(MaxEntries);
    private static readonly HotPathCache<IconKey, string> Icons = new(MaxEntries);
    private static readonly HotPathCache<CountKey, string> Counts = new(MaxEntries);

    /// <summary>
    /// Formats a number, remembering what it read last time.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The numeric format string.</param>
    /// <returns>The formatted value, in the current culture.</returns>
    internal static string Number(float value, string format)
    {
        var culture = CultureInfo.CurrentCulture;
        var key = new NumberKey(value, format, culture.Name);

        if (Numbers.TryGet(key, out var cached))
            return cached;

        var text = value.ToString(format, culture);
        Numbers.Set(key, text);

        return text;
    }

    /// <summary>
    /// Writes a duration in the shorthand a field accepts back, remembering what it read last time.
    /// </summary>
    /// <remarks>
    /// See <see cref="DurationHelper.Format"/> for the shorthand. That method composes through a
    /// <see cref="System.Text.StringBuilder"/> and is the whole per-frame cost of a duration field or a countdown.
    /// </remarks>
    /// <param name="value">The duration to write.</param>
    /// <returns>The duration in shorthand.</returns>
    internal static string Duration(TimeSpan value)
    {
        var key = new DurationKey(value);

        if (Durations.TryGet(key, out var cached))
            return cached;

        var text = DurationHelper.Format(value);
        Durations.Set(key, text);

        return text;
    }

    /// <summary>
    /// Writes a colour as hex, remembering what it read last time.
    /// </summary>
    /// <param name="color">The colour to write.</param>
    /// <param name="withAlpha">Whether the alpha channel is written.</param>
    /// <returns>The colour as <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> with alpha.</returns>
    internal static string HexColor(Vector4 color, bool withAlpha)
    {
        var key = new HexKey(color, withAlpha);

        if (Hexes.TryGet(key, out var cached))
            return cached;

        var text = withAlpha ? ColorHelper.Vector4ToHexAlpha(color) : ColorHelper.Vector4ToHex(color);
        Hexes.Set(key, text);

        return text;
    }

    /// <summary>
    /// Writes a badge count, capped at a ceiling, remembering what it read last time.
    /// </summary>
    /// <remarks>
    /// A badge is drawn on every frame the thing it marks is on screen, and the count behind it moves when something
    /// arrives. Written inline, an unread counter would spend a string sixty times a second to arrive at the digits
    /// already on the badge.
    /// </remarks>
    /// <param name="value">The count to write.</param>
    /// <param name="maxCount">The ceiling above which the count reads as an overflow. Zero or less does not cap.</param>
    /// <returns>The count, or the ceiling followed by a plus sign.</returns>
    internal static string Count(int value, int maxCount)
    {
        var key = new CountKey(value, maxCount);

        if (Counts.TryGet(key, out var cached))
            return cached;

        var text = maxCount > 0 && value > maxCount
            ? string.Create(CultureInfo.InvariantCulture, $"{maxCount}+")
            : value.ToString(CultureInfo.CurrentCulture);

        Counts.Set(key, text);

        return text;
    }

    /// <summary>
    /// The glyph an icon is drawn as, in the icon font.
    /// </summary>
    /// <remarks>
    /// <see cref="FontAwesomeIconExtensions.ToIconString"/> builds a one-character string from the enum value,
    /// measured at 24 bytes a call; an icon button pays it twice, once to size itself and once to paint. The set is
    /// an enum, so this fills once.
    /// </remarks>
    /// <param name="icon">The icon to write.</param>
    /// <returns>The glyph, as a string.</returns>
    internal static string Icon(FontAwesomeIcon icon)
    {
        var key = new IconKey(icon);

        if (Icons.TryGet(key, out var cached))
            return cached;

        var text = icon.ToIconString();
        Icons.Set(key, text);

        return text;
    }
}
