namespace NoireLib.UI;

/// <summary>What a gradient shows past its last color.</summary>
public enum GradientRepeat
{
    /// <summary>The first and last colors carry on beyond each end.</summary>
    None,

    /// <summary>The gradient starts over from its first color.</summary>
    Repeat,

    /// <summary>The gradient runs back from its last color to its first, then forward again.</summary>
    Mirror,
}
