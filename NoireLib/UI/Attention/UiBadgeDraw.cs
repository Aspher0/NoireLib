using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The placement, text and resolved colours passed to a <see cref="BadgeStyle.CustomDraw"/> hook.
/// </summary>
/// <remarks>
/// <see cref="Text"/> is <see langword="null"/> for a dot badge. Placement and measurement are done, and the pulse is
/// already applied to every colour.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Bounds">The badge's own rectangle, in screen pixels.</param>
/// <param name="Text">The count as it would be shown, or <see langword="null"/> for a dot badge.</param>
/// <param name="TextSizePx">The logical size the text draws at, with the badge scale already applied.</param>
/// <param name="Color">The badge colour, already resolved and pulse-dimmed.</param>
/// <param name="OutlineColor">The colour of the separating ring, already resolved and pulse-dimmed.</param>
/// <param name="OutlineThickness">The ring thickness in real pixels. Zero means no ring.</param>
/// <param name="TextColor">The colour of the count, already resolved and pulse-dimmed.</param>
/// <param name="Radius">The corner radius of the pill, in real pixels.</param>
/// <param name="Alpha">The pulse multiplier the colours already carry, for tinting additional content.</param>
public readonly record struct UiBadgeDraw(
    ImDrawListPtr DrawList,
    UiRect Bounds,
    string? Text,
    float TextSizePx,
    Vector4 Color,
    Vector4 OutlineColor,
    float OutlineThickness,
    Vector4 TextColor,
    float Radius,
    float Alpha)
{
    /// <summary>
    /// Draws the separating ring, then the pill.
    /// </summary>
    public void DrawPlate()
    {
        NoireShapes.On(DrawList, this, static self =>
        {
            if (self.OutlineThickness > 0f)
            {
                NoireShapes.Rect(
                    self.Bounds.Position - new Vector2(self.OutlineThickness),
                    self.Bounds.Max + new Vector2(self.OutlineThickness),
                    self.OutlineColor,
                    CornerShape.Rounded,
                    self.Radius + self.OutlineThickness);
            }

            NoireShapes.Rect(self.Bounds.Position, self.Bounds.Max, self.Color, CornerShape.Rounded, self.Radius);
        });
    }

    /// <summary>
    /// Draws the count centred on the plate, or nothing for a dot badge.
    /// </summary>
    /// <remarks>Written onto the draw list rather than as an ImGui item, so it does not affect the row layout.</remarks>
    public void DrawLabel()
    {
        if (string.IsNullOrEmpty(Text))
            return;

        var textSize = NoireText.CalcSize(Text, TextSizePx);
        var textAt = Bounds.Center - (textSize * 0.5f);
        var color = ColorHelper.Vector4ToUint(TextColor);

        NoireText.At(TextSizePx, (textAt, color, text: Text), static state =>
        {
            using var draw = UiDraw.Begin();

            if (!draw.List.IsNull)
                draw.List.AddText(state.textAt, state.color, state.text);
        });
    }
}
