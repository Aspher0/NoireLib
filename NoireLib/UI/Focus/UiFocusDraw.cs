using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="FocusStyle.CustomDraw"/> hook needs to mark the focused control itself: where the mark
/// belongs, how far through its arrival it is, and the colour NoireUI would have used.
/// </summary>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left of the mark, with <see cref="FocusStyle.Spread"/> and the arrival already applied.</param>
/// <param name="Max">The bottom right of the mark, likewise.</param>
/// <param name="Target">The control's own rectangle, before any spread.</param>
/// <param name="Color">The mark colour, already faded by the arrival.</param>
/// <param name="Arrival">How far the mark has settled, from 0 to 1, already eased.</param>
/// <param name="Style">The style being drawn with.</param>
public readonly record struct UiFocusDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    UiRect Target,
    Vector4 Color,
    float Arrival,
    FocusStyle Style)
{
    /// <summary>The size of the mark, in real pixels.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>Draws the mark NoireUI would have drawn.</summary>
    public void DrawShape() => NoireFocus.Paint(this);
}
