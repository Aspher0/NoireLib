using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a <see cref="FocusStyle.CustomDraw"/> hook needs to mark the focused control itself: where the mark
/// belongs, how far through its arrival it is, and the colour NoireUI would have used.
/// </summary>
/// <remarks>
/// The hook is called only for the control that actually holds focus, with the arrival already resolved, so it never
/// has to ask which control this is or how long ago focus landed. Drawing nothing is a valid hook, and is how a
/// consumer suppresses the mark on one widget while keeping it everywhere else.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left of the mark, with <see cref="FocusStyle.Spread"/> and the arrival already applied.</param>
/// <param name="Max">The bottom right of the mark, likewise.</param>
/// <param name="Target">The control's own rectangle, before any spread.</param>
/// <param name="Color">The mark colour, already faded by the arrival.</param>
/// <param name="Arrival">
/// How far the mark has settled, from 0 the instant focus lands to 1 once it is at rest, already eased. Held at 1 under
/// <see cref="NoireUI.ReducedMotion"/>.
/// </param>
/// <param name="Style">The style being drawn with, for the thickness, corners and arm reach.</param>
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

    /// <summary>
    /// Draws the mark NoireUI would have drawn, for a hook adding to the shipped look rather than replacing it.
    /// </summary>
    public void DrawShape() => NoireFocus.Paint(this);
}
