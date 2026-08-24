namespace NoireLib.UI;

/// <summary>
/// What decides which way a hovered or held state moves a color.
/// </summary>
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
