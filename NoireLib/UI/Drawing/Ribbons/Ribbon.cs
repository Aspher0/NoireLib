using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// One ribbon of a <see cref="NoireRibbonField"/>: a band following two summed sine waves across the field.
/// </summary>
/// <param name="Y">The resting height of its centre, as a fraction of the field height.</param>
/// <param name="Amplitude">The height of the main wave, as a fraction of the field height.</param>
/// <param name="Frequency">How many radians the main wave turns across the field.</param>
/// <param name="Speed">How fast the waves travel, in radians per second.</param>
/// <param name="Phase">The main wave's phase, in radians.</param>
/// <param name="Width">The half thickness at its widest, as a fraction of the field height.</param>
/// <param name="Color">The color, each channel from 0 to 1.</param>
/// <param name="Alpha">The fill opacity at the gradient's peak.</param>
public readonly record struct Ribbon(float Y, float Amplitude, float Frequency, float Speed, float Phase, float Width, Vector3 Color, float Alpha)
{
    /// <summary>
    /// A copy with its opacity multiplied, for a quieter version of the same set.
    /// </summary>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The copy.</returns>
    public Ribbon Quieter(float factor) => this with { Alpha = Alpha * factor };
}
