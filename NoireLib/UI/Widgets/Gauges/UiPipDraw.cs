using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="PipStyle.CustomDraw"/> hook needs to paint one pip itself: where it sits in the row, its
/// state, and the colour NoireUI would have used.
/// </summary>
/// <remarks>Called once per pip, with the row already laid out and its space already reserved.</remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left corner of the pip.</param>
/// <param name="Max">The bottom right corner of the pip.</param>
/// <param name="Index">Which pip this is, from 0.</param>
/// <param name="Total">How many pips the row has.</param>
/// <param name="Filled">Whether this pip is filled.</param>
/// <param name="Color">The colour for this pip's state, already resolved through the style and the theme.</param>
/// <param name="Outlined">Whether an empty pip draws as an outline rather than filled.</param>
/// <param name="Shape">The corner shape the pip would have used.</param>
/// <param name="Rounding">The corner radius the pip would have used, in real pixels.</param>
public readonly record struct UiPipDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    int Index,
    int Total,
    bool Filled,
    Vector4 Color,
    bool Outlined,
    CornerShape Shape,
    float Rounding)
{
    /// <summary>The centre of the pip in screen coordinates.</summary>
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// Draws the pip NoireUI would have drawn: filled, or an outline for an empty pip when the style asks for one.
    /// </summary>
    public void DrawPip()
    {
        if (Outlined)
            NoireShapes.RectOutline(Min, Max, Color, 1f, Shape, Rounding);
        else
            NoireShapes.Rect(Min, Max, Color, Shape, Rounding);
    }
}
