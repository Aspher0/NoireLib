namespace NoireLib.UI;

/// <summary>
/// What decides which way a hovered or held state moves a color.
/// </summary>
/// <remarks>
/// A single fixed direction does not work across a whole palette: brightening looks right on a dark neutral button and
/// washes out a pale accent one, and darkening does the reverse.
/// </remarks>
public enum ThemeTintSource
{
    /// <summary>
    /// Each color decides for itself (the default): a dark color brightens, a light one darkens.
    /// </summary>
    Item,

    /// <summary>
    /// The theme's surface decides for everything: a dark theme brightens, a light one darkens. Consistent across the
    /// interface, at the cost of washing out a color already close to that direction.
    /// </summary>
    Surface,

    /// <summary>Always brighten, whatever the color.</summary>
    Lighten,

    /// <summary>Always darken, whatever the color.</summary>
    Darken,
}
