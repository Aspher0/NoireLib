namespace NoireLib.UI;

/// <summary>
/// The space two neighbouring colors of a gradient are mixed in, which decides what the colors between them look like.
/// </summary>
public enum GradientColorSpace
{
    /// <summary>The channels as stored. The same result as most tools, and a slightly dark middle between far apart colors.</summary>
    Srgb,

    /// <summary>Linear light. Brighter middles, closer to how light itself mixes.</summary>
    LinearRgb,

    /// <summary>OKLab. The smoothest looking transitions, with no muddy or greyed middle.</summary>
    Oklab,

    /// <summary>Hue, saturation and value, turning the hue the short way round the color wheel.</summary>
    HsvShortest,

    /// <summary>Hue, saturation and value, turning the hue the long way round, through every hue in between.</summary>
    HsvLongest,
}
