using System.Numerics;

namespace NoireLib.UI;

/// <summary>One color of a gradient and where it sits.</summary>
/// <param name="Color">The color. Its alpha fades the drawing.</param>
/// <param name="Position">Where the color starts, from 0 to 1. <see cref="float.NaN"/> spreads it evenly.</param>
/// <param name="Width">How far the color holds after <paramref name="Position"/>. 0 is a single point.</param>
/// <param name="Midpoint">Where the transition to the next color is half done. 0.5 is even.</param>
/// <param name="Easing">How the transition moves. Null uses the gradient's own.</param>
public readonly record struct GradientStop(Vector4 Color, float Position = float.NaN, float Width = 0f, float Midpoint = 0.5f, GradientEasing? Easing = null)
{
    /// <summary>A color at a set position.</summary>
    /// <param name="color">The color.</param>
    /// <param name="position">Where the color sits, from 0 to 1.</param>
    /// <returns>The stop.</returns>
    public static GradientStop At(Vector4 color, float position) => new(color, position);

    /// <summary>A color that holds unchanged over a band.</summary>
    /// <param name="color">The color.</param>
    /// <param name="from">Where the band starts, from 0 to 1.</param>
    /// <param name="to">Where the band ends, from 0 to 1.</param>
    /// <returns>The stop.</returns>
    public static GradientStop Band(Vector4 color, float from, float to) => new(color, from, to - from);

    /// <summary>A color, spread evenly with its neighbours.</summary>
    /// <param name="color">The color.</param>
    public static implicit operator GradientStop(Vector4 color) => new(color);
}
