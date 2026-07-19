namespace NoireLib.UI;

/// <summary>
/// The positioning mode used by a <see cref="UiPosition"/>.
/// </summary>
public enum UiPositionMode
{
    /// <summary>
    /// The element is positioned relative to one of the nine screen anchor points. See <see cref="UiAnchor"/>.
    /// </summary>
    Anchor,

    /// <summary>
    /// The element is positioned at absolute pixel coordinates, relative to the top left corner of the game window.
    /// </summary>
    Absolute,

    /// <summary>
    /// The element is positioned at a ratio of the screen size (e.g. 0.1 = 10% from the left/top).
    /// </summary>
    Ratio,

    /// <summary>
    /// The element is positioned relative to a native game addon, following it as the player moves or rescales it.
    /// Resolving fails while the addon is not on screen, which is what lets an element exist only alongside it.
    /// </summary>
    Addon,
}
