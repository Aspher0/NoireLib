using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Where a closed effect scope laid out and where it shows this frame. Effects never move the layout: reserve the
/// difference yourself, or the largest size with <see cref="NoireMotion.Envelope(Vector2, Vector2)"/>.
/// </summary>
/// <param name="LayoutMin">The top left of what was drawn, before the effects.</param>
/// <param name="LayoutMax">The bottom right of what was drawn, before the effects.</param>
/// <param name="Min">The top left of what shows this frame.</param>
/// <param name="Max">The bottom right of what shows this frame.</param>
/// <param name="VertexCount">How many vertices the scope drew. 0 when nothing was drawn.</param>
public readonly record struct EffectResult(Vector2 LayoutMin, Vector2 LayoutMax, Vector2 Min, Vector2 Max, int VertexCount)
{
    /// <summary>The size of what was drawn, before the effects.</summary>
    public Vector2 LayoutSize => LayoutMax - LayoutMin;

    /// <summary>The size of what shows on screen this frame, after the effects.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>How much larger the drawing shows than the layout placed it, never negative.</summary>
    public Vector2 Grown => Vector2.Max(Vector2.Zero, Size - LayoutSize);

    /// <summary>How far the drawing shows from where the layout placed it, measured between the two centres.</summary>
    public Vector2 Shift => ((Min + Max) - (LayoutMin + LayoutMax)) * 0.5f;

    /// <summary>Whether the scope drew anything.</summary>
    public bool IsEmpty => VertexCount == 0;
}
