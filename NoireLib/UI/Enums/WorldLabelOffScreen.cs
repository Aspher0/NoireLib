namespace NoireLib.UI;

/// <summary>
/// What a world-anchored element does once its world point leaves the screen.
/// </summary>
public enum WorldLabelOffScreen
{
    /// <summary>Disappear until the point comes back into view.</summary>
    Hide,

    /// <summary>Stay on screen, pinned to the nearest edge.</summary>
    Clamp,

    /// <summary>Stay pinned to the edge, with an arrow pointing the way, as a quest marker does.</summary>
    EdgeArrow,
}
