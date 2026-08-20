using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything an <see cref="OverlayButtonStyle.CustomDraw"/> hook needs to paint an overlay button itself: where it
/// is, what state it is in, and the colours NoireUI would have used. The hook runs with the hitbox already submitted
/// and the drag already applied, but nothing painted, so <see cref="DrawContent"/> is the only route to the button's
/// configured icon, image and text.
/// </summary>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Button">The button being painted, for its content and identity.</param>
/// <param name="Min">The top left corner of the button.</param>
/// <param name="Max">The bottom right corner of the button.</param>
/// <param name="Hovered">Whether the mouse is over the button.</param>
/// <param name="Active">Whether the button is currently pressed.</param>
/// <param name="Enabled">Whether the button reacts to the mouse at all.</param>
/// <param name="Dragging">Whether the button is being dragged to a new position.</param>
/// <param name="Background">The fill for the current state, resolved through the style with the window's opacity folded in.</param>
/// <param name="BorderColor">The border colour, resolved the same way.</param>
/// <param name="BorderSize">The border thickness in real pixels, zero for no border.</param>
/// <param name="Rounding">The corner radius the button would have used, in real pixels.</param>
public readonly record struct UiOverlayButtonDraw(
    ImDrawListPtr DrawList,
    NoireOverlayButton Button,
    Vector2 Min,
    Vector2 Max,
    bool Hovered,
    bool Active,
    bool Enabled,
    bool Dragging,
    Vector4 Background,
    Vector4 BorderColor,
    float BorderSize,
    float Rounding)
{
    /// <summary>The size of the button in pixels.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>The centre of the button in screen coordinates.</summary>
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>Draws the button's own background for the current state.</summary>
    public void DrawBackground()
        => DrawList.AddRectFilled(Min, Max, ColorHelper.Vector4ToUint(Background), Rounding);

    /// <summary>Draws the button's own border, or nothing when the thickness is zero.</summary>
    public void DrawBorder()
    {
        if (BorderSize > 0f)
            DrawList.AddRect(Min, Max, ColorHelper.Vector4ToUint(BorderColor), Rounding, ImDrawFlags.None, BorderSize);
    }

    /// <summary>Draws the button's own content: the icon, image and text it was configured with, centred.</summary>
    public void DrawContent() => Button.DrawDefaultContent(Size);
}
