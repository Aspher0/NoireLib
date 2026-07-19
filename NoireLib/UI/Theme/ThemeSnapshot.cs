using NoireLib.Helpers;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// The inert form of a <see cref="NoireTheme"/>, used as the payload of a theme share code.
/// </summary>
/// <remarks>
/// A share code is authored by a stranger, so decoding never targets the live theme directly. This type holds plain
/// strings and numbers, has no behaviour, and is turned into a theme by <see cref="ToTheme"/> only after it has been
/// read successfully. Colors travel as HEX strings rather than four floats, which keeps a code short and makes it
/// readable if anyone ever inspects one.
/// </remarks>
public sealed class ThemeSnapshot
{
    /// <summary>The theme's colors, keyed by <see cref="ThemeColor"/> name, as HEX strings with alpha.</summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>The theme's extra named colors, as HEX strings with alpha.</summary>
    public Dictionary<string, string> CustomColors { get; set; } = new();

    /// <summary>The corner radius of framed widgets, if the theme set one.</summary>
    public float? Rounding { get; set; }

    /// <summary>The corner radius of raised surfaces, if the theme set one.</summary>
    public float? SurfaceRounding { get; set; }

    /// <summary>The border thickness of widgets, if the theme set one.</summary>
    public float? BorderSize { get; set; }

    /// <summary>How far the hovered state moves a color.</summary>
    /// <remarks>
    /// The four derivation values below carry the same defaults as <see cref="NoireTheme"/> rather than starting at
    /// zero, so a code written before one of them existed decodes to a usable theme instead of one with everything
    /// faded to nothing.
    /// </remarks>
    public float HoverShift { get; set; } = 0.12f;

    /// <summary>How far the held state moves a color.</summary>
    public float ActiveShift { get; set; } = 0.22f;

    /// <summary>The opacity applied to secondary and inactive elements.</summary>
    public float MutedAlpha { get; set; } = 0.65f;

    /// <summary>The opacity applied to a widget that is switched off or unavailable.</summary>
    public float DisabledAlpha { get; set; } = 0.45f;

    /// <summary>What decides which way a hovered or held state moves a color, by name.</summary>
    public string TintSource { get; set; } = nameof(ThemeTintSource.Item);

    /// <summary>
    /// Captures a theme.
    /// </summary>
    /// <param name="theme">The theme to capture.</param>
    /// <returns>The snapshot.</returns>
    public static ThemeSnapshot From(NoireTheme theme)
    {
        var snapshot = new ThemeSnapshot
        {
            Rounding = theme.Rounding,
            SurfaceRounding = theme.SurfaceRounding,
            BorderSize = theme.BorderSize,
            HoverShift = theme.HoverShift,
            ActiveShift = theme.ActiveShift,
            MutedAlpha = theme.MutedAlpha,
            DisabledAlpha = theme.DisabledAlpha,
            TintSource = theme.TintSource.ToString(),
        };

        foreach (var entry in theme.Colors)
            snapshot.Colors[entry.Key.ToString()] = ColorHelper.Vector4ToHexAlpha(entry.Value);

        foreach (var entry in theme.CustomColors)
            snapshot.CustomColors[entry.Key] = ColorHelper.Vector4ToHexAlpha(entry.Value);

        return snapshot;
    }

    /// <summary>
    /// Builds a theme from this snapshot.<br/>
    /// A color name this version of the library does not know, or a HEX value that does not parse, is skipped rather
    /// than failing the whole theme: a code written by a newer version still applies everything it has in common with
    /// this one.
    /// </summary>
    /// <returns>The theme.</returns>
    public NoireTheme ToTheme()
    {
        var theme = new NoireTheme
        {
            Rounding = Rounding,
            SurfaceRounding = SurfaceRounding,
            BorderSize = BorderSize,
            HoverShift = HoverShift,
            ActiveShift = ActiveShift,
            MutedAlpha = MutedAlpha,
            DisabledAlpha = DisabledAlpha,
            TintSource = System.Enum.TryParse<ThemeTintSource>(TintSource, out var tint) ? tint : ThemeTintSource.Item,
        };

        foreach (var entry in Colors)
        {
            if (System.Enum.TryParse<ThemeColor>(entry.Key, out var token) && TryParseColor(entry.Value, out var color))
                theme.Colors[token] = color;
        }

        foreach (var entry in CustomColors)
        {
            if (TryParseColor(entry.Value, out var color))
                theme.CustomColors[entry.Key] = color;
        }

        return theme;
    }

    private static bool TryParseColor(string hex, out System.Numerics.Vector4 color)
    {
        try
        {
            color = ColorHelper.HexToVector4(hex);
            return true;
        }
        catch (System.ArgumentException)
        {
            color = default;
            return false;
        }
    }
}
