namespace NoireLib.UI;

/// <summary>
/// How a hold-to-confirm button shows its progress.
/// </summary>
/// <remarks>
/// The fill is the whole interface of a hold button: it is what tells the user that holding is doing something and how
/// much longer it will take. Which shape reads best depends on the button, so it is a setting rather than a decision
/// baked into the widget.
/// </remarks>
public enum HoldFillMode
{
    /// <summary>Fills from the left edge across to the right, like a progress bar.</summary>
    LeftToRight,

    /// <summary>Fills from the right edge across to the left.</summary>
    RightToLeft,

    /// <summary>Grows outwards from the centre towards both edges.</summary>
    CenterOut,

    /// <summary>Rises from the bottom edge to the top, which reads well on a tall or square button.</summary>
    BottomUp,

    /// <summary>Traces the outline clockwise from the top left corner, leaving the fill untouched.</summary>
    Border,
}
