namespace NoireLib.UI;

/// <summary>
/// The shape cut out of the dimmed screen around a step's widget.
/// </summary>
public enum TourSpotlight
{
    /// <summary>
    /// A rectangle with square corners.
    /// </summary>
    Rectangle,

    /// <summary>
    /// A rectangle with rounded corners.
    /// </summary>
    Rounded,

    /// <summary>
    /// A circle enclosing the widget.
    /// </summary>
    Circle,

    /// <summary>
    /// No outline, the dim still opening around the widget.
    /// </summary>
    None,
}
