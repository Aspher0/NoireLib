namespace NoireLib.UI;

/// <summary>
/// The marks <see cref="NoireWindowChrome.ChromeButton"/> can draw, none of which need an icon font.
/// </summary>
public enum ChromeGlyph
{
    /// <summary>A cross, for closing.</summary>
    Close,

    /// <summary>A chevron pointing down, for collapsing.</summary>
    Minimize,

    /// <summary>A chevron pointing up, for expanding again.</summary>
    Restore,

    /// <summary>Three bars, for a menu.</summary>
    Menu,
}
