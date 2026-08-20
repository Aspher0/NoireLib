using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The geometry, fill fraction and resolved colours passed to a <see cref="BarStyle.CustomDraw"/> hook.
/// </summary>
/// <remarks>
/// Geometry is resolved and the space already reserved when the hook runs. With the hook set, the marks and the label
/// are only drawn if the hook draws them.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Min">The top left corner of the bar.</param>
/// <param name="Max">The bottom right corner of the bar.</param>
/// <param name="Fraction">The fraction filled, already clamped to 0 to 1.</param>
/// <param name="Rounding">The corner radius the bar would have used, in real pixels.</param>
/// <param name="TrackColor">The colour of the unfilled part, already resolved through the style and the theme.</param>
/// <param name="FillColor">The colour of the filled part, thresholds already applied.</param>
/// <param name="FillColorTo">The colour the fill runs to, or <see langword="null"/> for a flat fill.</param>
/// <param name="Marks">Fractions along the bar carrying a hairline, or <see langword="null"/> for none.</param>
/// <param name="MarkColor">The colour of the hairlines, already resolved.</param>
/// <param name="Label">The text over the bar, countdown text included, or <see langword="null"/> for none.</param>
/// <param name="LabelSize">The step of the type scale the label draws at.</param>
/// <param name="LabelAlign">Where the label sits along the bar, from 0 (left) to 1 (right).</param>
/// <param name="LabelColor">The colour the label draws in, already resolved.</param>
public readonly record struct UiBarDraw(
    ImDrawListPtr DrawList,
    Vector2 Min,
    Vector2 Max,
    float Fraction,
    float Rounding,
    Vector4 TrackColor,
    Vector4 FillColor,
    Vector4? FillColorTo,
    IReadOnlyList<float>? Marks,
    Vector4 MarkColor,
    string? Label,
    TextSize LabelSize,
    float LabelAlign,
    Vector4 LabelColor)
{
    /// <summary>The size of the bar in pixels.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>
    /// Draws the track in <see cref="TrackColor"/>.
    /// </summary>
    public void DrawTrack()
        => NoireShapes.Rect(Min, Max, TrackColor, CornerShape.Rounded, Rounding);

    /// <summary>
    /// Draws the fill clipped to the track's rounded shape, or nothing at a zero fraction.
    /// </summary>
    public void DrawFill()
    {
        if (Fraction <= 0f)
            return;

        var fillMax = new Vector2(Min.X + (Size.X * Fraction), Max.Y);

        NoireShapes.On(DrawList, (self: this, fillMax), static state =>
        {
            ImGui.PushClipRect(state.self.Min, state.fillMax, true);

            if (state.self.FillColorTo is { } to)
            {
                NoireShapes.GradientRect(
                    state.self.Min, state.self.Max, state.self.FillColor, to, GradientAxis.Horizontal,
                    CornerShape.Rounded, state.self.Rounding);
            }
            else
            {
                NoireShapes.Rect(
                    state.self.Min, state.self.Max, state.self.FillColor, CornerShape.Rounded, state.self.Rounding);
            }

            ImGui.PopClipRect();
        });
    }

    /// <summary>
    /// Draws the hairline marks in <see cref="MarkColor"/>, or nothing when there are none.
    /// </summary>
    public void DrawMarks()
    {
        if (Marks == null || Marks.Count == 0)
            return;

        foreach (var mark in Marks)
        {
            var x = MathF.Round(Min.X + (Size.X * Math.Clamp(mark, 0f, 1f)));
            NoireShapes.Rect(new Vector2(x, Min.Y), new Vector2(x + 1f, Max.Y), MarkColor);
        }
    }

    /// <summary>
    /// Draws the label aligned along the bar in <see cref="LabelColor"/>, or nothing when there is no label.
    /// </summary>
    public void DrawLabel()
    {
        if (!string.IsNullOrEmpty(Label))
            NoireGauges.DrawBarLabel(Label, LabelSize, LabelAlign, LabelColor, Min, Size.X, Size.Y);
    }
}
