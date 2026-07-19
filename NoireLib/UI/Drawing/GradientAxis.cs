namespace NoireLib.UI;

/// <summary>
/// The direction a gradient runs across a rectangle.
/// </summary>
/// <remarks>
/// A shorthand for the two points every gradient is really defined by. Any other angle is reachable by giving those
/// points directly. See <see cref="NoireShapes.Gradient(System.Numerics.Vector2, System.Numerics.Vector2, System.Numerics.Vector4, System.Numerics.Vector4, System.Action)"/>.
/// </remarks>
public enum GradientAxis
{
    /// <summary>Top to bottom.</summary>
    Vertical,

    /// <summary>Left to right.</summary>
    Horizontal,

    /// <summary>Top left to bottom right.</summary>
    Diagonal,

    /// <summary>Bottom left to top right.</summary>
    Antidiagonal,
}
