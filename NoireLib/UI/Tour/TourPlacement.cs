namespace NoireLib.UI;

/// <summary>
/// Where a step's card sits relative to the widget it explains.
/// </summary>
public enum TourPlacement
{
    /// <summary>
    /// The first side with room, tried below, above, right, then left.
    /// </summary>
    Auto,

    /// <summary>
    /// Above the widget.
    /// </summary>
    Above,

    /// <summary>
    /// Below the widget.
    /// </summary>
    Below,

    /// <summary>
    /// Left of the widget.
    /// </summary>
    Left,

    /// <summary>
    /// Right of the widget.
    /// </summary>
    Right,
}
