using Dalamud.Interface;
using NoireLib.Helpers;
using System;
using System.Globalization;
using System.Numerics;

namespace NoireLib.UI;

// What a value reads as on screen. Reached only from the draw thread, so the caches need no lock.
internal static class UiValueText
{
    // Sized for a page of fields rather than for a series: the values that move every frame are the ones being
    // dragged, and there is only ever one of those.
    private const int MaxEntries = 1024;

    private readonly record struct NumberKey(float Value, string Format, string Culture);

    // Wrapped rather than used directly, since the cache's key must be a record struct for the equality a lookup
    // needs without boxing.
    private readonly record struct DurationKey(TimeSpan Value);

    private readonly record struct HexKey(Vector4 Color, bool WithAlpha);

    // Wrapped for the reason DurationKey is: an enum is a struct but does not implement IEquatable<T> for itself,
    // which the cache's key requires.
    private readonly record struct IconKey(FontAwesomeIcon Icon);

    private readonly record struct CountKey(int Value, int MaxCount);

    private static readonly HotPathCache<NumberKey, string> Numbers = new(MaxEntries);
    private static readonly HotPathCache<DurationKey, string> Durations = new(MaxEntries);
    private static readonly HotPathCache<HexKey, string> Hexes = new(MaxEntries);
    private static readonly HotPathCache<IconKey, string> Icons = new(MaxEntries);
    private static readonly HotPathCache<CountKey, string> Counts = new(MaxEntries);

    // Formatted in the current culture.
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

    // Written in the shorthand a field accepts back; see DurationHelper.Format.
    internal static string Duration(TimeSpan value)
    {
        var key = new DurationKey(value);

        if (Durations.TryGet(key, out var cached))
            return cached;

        var text = DurationHelper.Format(value);
        Durations.Set(key, text);

        return text;
    }

    // Written as #RRGGBB, or #RRGGBBAA with alpha.
    internal static string HexColor(Vector4 color, bool withAlpha)
    {
        var key = new HexKey(color, withAlpha);

        if (Hexes.TryGet(key, out var cached))
            return cached;

        var text = withAlpha ? ColorHelper.Vector4ToHexAlpha(color) : ColorHelper.Vector4ToHex(color);
        Hexes.Set(key, text);

        return text;
    }

    // A maxCount of zero or less does not cap; above the ceiling the count reads as the ceiling and a plus sign.
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

    // The glyph an icon is drawn as, in the icon font.
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
