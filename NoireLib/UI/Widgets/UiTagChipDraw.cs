using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// What a <see cref="NoireTagInput.ChipDraw"/> hook needs to paint one chip: where it sits, its state, and the
/// colors NoireUI would have used.
/// </summary>
/// <remarks>
/// Called once per visible chip, with layout, hit testing, removal and the off-screen cull already handled. The
/// chip's size is measured from the tag and the theme before painting, so the hook cannot change it.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left corner of the chip.</param>
/// <param name="Max">The bottom right corner of the chip.</param>
/// <param name="Index">Which chip this is, from 0.</param>
/// <param name="Tag">The text the chip holds.</param>
/// <param name="Hovered">Whether the mouse is over the chip.</param>
/// <param name="Accent">The theme accent the chip's colors derive from.</param>
/// <param name="FillColor">The resolved pill fill for the current state.</param>
/// <param name="OutlineColor">The resolved pill outline for the current state.</param>
/// <param name="Rounding">The corner radius that makes the chip a pill, in real pixels.</param>
/// <param name="Padding">The padding the label and cross inset by, in real pixels.</param>
/// <param name="CrossCentre">The centre of the remove cross, in screen pixels.</param>
/// <param name="CrossRadius">Half the span of the cross, in real pixels.</param>
/// <param name="CrossColor">The resolved cross color for the current state.</param>
public readonly record struct UiTagChipDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    int Index,
    string Tag,
    bool Hovered,
    Vector4 Accent,
    Vector4 FillColor,
    Vector4 OutlineColor,
    float Rounding,
    Vector2 Padding,
    Vector2 CrossCentre,
    float CrossRadius,
    Vector4 CrossColor)
{
    /// <summary>The size of the chip in pixels.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>Draws the chip's default pill: the fill and the outline for the current state.</summary>
    public void DrawPill()
    {
        NoireShapes.Rect(Min, Max, FillColor, CornerShape.Rounded, Rounding);
        NoireShapes.RectOutline(Min, Max, OutlineColor, 1f, CornerShape.Rounded, Rounding);
    }

    /// <summary>
    /// Draws the chip's default label, hung off the text's optical centre with wrapping disabled, as measured.
    /// </summary>
    public void DrawLabel()
    {
        ImGui.SetCursorScreenPos(new Vector2(Min.X + Padding.X, ((Min.Y + Max.Y) * 0.5f) - NoireText.CenterOffset()));

        ImGui.PushTextWrapPos(-1f);
        NoireText.Draw(Tag);
        ImGui.PopTextWrapPos();
    }

    /// <summary>Draws the chip's default remove cross.</summary>
    public void DrawCross()
    {
        Span<Vector2> down = [CrossCentre - new Vector2(CrossRadius), CrossCentre + new Vector2(CrossRadius)];
        Span<Vector2> up = [CrossCentre + new Vector2(-CrossRadius, CrossRadius), CrossCentre + new Vector2(CrossRadius, -CrossRadius)];

        NoireShapes.Stroke(down, CrossColor, NoireUI.Scaled(1.4f), closed: false);
        NoireShapes.Stroke(up, CrossColor, NoireUI.Scaled(1.4f), closed: false);
    }
}
