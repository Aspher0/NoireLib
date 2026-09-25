namespace NoireLib.UI;

/// <summary>Which way a linear gradient runs, from its first color to its last.</summary>
public enum GradientDirection
{
    /// <summary>From the left edge to the right edge.</summary>
    LeftToRight,

    /// <summary>From the right edge to the left edge.</summary>
    RightToLeft,

    /// <summary>From the top edge to the bottom edge.</summary>
    TopToBottom,

    /// <summary>From the bottom edge to the top edge.</summary>
    BottomToTop,

    /// <summary>From the top left corner to the bottom right corner.</summary>
    TopLeftToBottomRight,

    /// <summary>From the bottom right corner to the top left corner.</summary>
    BottomRightToTopLeft,

    /// <summary>From the top right corner to the bottom left corner.</summary>
    TopRightToBottomLeft,

    /// <summary>From the bottom left corner to the top right corner.</summary>
    BottomLeftToTopRight,
}
