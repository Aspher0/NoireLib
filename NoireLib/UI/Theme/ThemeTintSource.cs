namespace NoireLib.UI;

/// <summary>
/// What decides which way a hovered or held state moves a color.
/// </summary>
/// <remarks>
/// A single fixed direction does not work across a whole palette: brightening looks right on a dark neutral button and
/// washes out a pale accent one, and darkening does the reverse. Deriving the direction per color is what lets one
/// setting cover every widget.
/// </remarks>
public enum ThemeTintSource
{
    /// <summary>
    /// Each color decides for itself: a dark color brightens, a light one darkens. The default, because it keeps every
    /// widget visibly responding whatever it is painted in.
    /// </summary>
    Item,

    /// <summary>
    /// The theme's surface decides for everything: a dark theme brightens, a light one darkens. Consistent across the
    /// interface, at the cost of washing out a color that is already close to the direction of travel.
    /// </summary>
    Surface,

    /// <summary>Always brighten, whatever the color.</summary>
    Lighten,

    /// <summary>Always darken, whatever the color.</summary>
    Darken,
}
