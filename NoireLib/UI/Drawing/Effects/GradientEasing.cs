namespace NoireLib.UI;

/// <summary>How a gradient moves from one color to the next between two stops.</summary>
public enum GradientEasing
{
    /// <summary>At an even pace.</summary>
    Linear,

    /// <summary>Slowly at first, then faster.</summary>
    EaseIn,

    /// <summary>Quickly at first, then slower.</summary>
    EaseOut,

    /// <summary>Slowly at both ends, faster in the middle.</summary>
    EaseInOut,

    /// <summary>Like <see cref="EaseInOut"/>, gentler.</summary>
    Smoothstep,

    /// <summary>No transition: the color jumps to the next one halfway between the two stops.</summary>
    Hard,
}
