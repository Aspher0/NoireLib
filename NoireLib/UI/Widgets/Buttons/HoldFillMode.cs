namespace NoireLib.UI;

/// <summary>How a hold-to-confirm button shows its progress.</summary>
public enum HoldFillMode
{
    /// <summary>Fills from the left edge across to the right, like a progress bar.</summary>
    LeftToRight,

    /// <summary>Fills from the right edge across to the left.</summary>
    RightToLeft,

    /// <summary>Grows outwards from the centre towards both edges.</summary>
    CenterOut,

    /// <summary>Rises from the bottom edge to the top.</summary>
    BottomUp,

    /// <summary>Traces the outline clockwise from the top left corner, leaving the fill untouched.</summary>
    Border,
}
