using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The geometry, fill fraction and resolved colours handed to a <see cref="RingStyle.CustomDraw"/> hook.
/// </summary>
/// <remarks>
/// Geometry is resolved and the space reserved before the hook runs. The label is not drawn when a hook is set;
/// call <see cref="DrawLabel()"/> for it.
/// </remarks>
/// <param name="DrawList">The draw list to paint into.</param>
/// <param name="Centre">The centre of the ring, in screen pixels.</param>
/// <param name="InnerRadius">The inner radius of the band, in real pixels.</param>
/// <param name="OuterRadius">The outer radius of the band, in real pixels.</param>
/// <param name="StartTurns">Where the fill starts, in turns clockwise from twelve o'clock.</param>
/// <param name="SweepTurns">How far a full value sweeps, in turns, already negative for a counter-clockwise ring.</param>
/// <param name="Fraction">The fraction filled, already clamped to 0 to 1.</param>
/// <param name="TrackColor">The colour of the unfilled part, already resolved through the style and the theme.</param>
/// <param name="FillColor">The colour of the filled part, thresholds already applied.</param>
/// <param name="Label">The text in the middle, countdown text included, or <see langword="null"/> for none.</param>
/// <param name="LabelSize">The step of the type scale the label draws at.</param>
/// <param name="LabelColor">The colour the label draws in, already resolved.</param>
public readonly record struct UiRingDraw(
    ImDrawListPtr DrawList,
    Vector2 Centre,
    float InnerRadius,
    float OuterRadius,
    float StartTurns,
    float SweepTurns,
    float Fraction,
    Vector4 TrackColor,
    Vector4 FillColor,
    string? Label,
    TextSize LabelSize,
    Vector4 LabelColor)
{
    /// <summary>
    /// Draws the ring's own track: the full sweep in <see cref="TrackColor"/>.
    /// </summary>
    public void DrawTrack()
        => NoireShapes.Wedge(Centre, InnerRadius, OuterRadius, StartTurns, StartTurns + SweepTurns, TrackColor);

    /// <summary>
    /// Draws the ring's own fill: the swept fraction in <see cref="FillColor"/>. Nothing at zero.
    /// </summary>
    public void DrawFill()
    {
        if (Fraction > 0f)
            NoireShapes.Wedge(Centre, InnerRadius, OuterRadius, StartTurns, StartTurns + (SweepTurns * Fraction), FillColor);
    }

    /// <summary>
    /// Draws the ring's own label, centred, in <see cref="LabelColor"/>. Nothing when there is no label.
    /// </summary>
    public void DrawLabel()
    {
        if (!string.IsNullOrEmpty(Label))
            NoireGauges.DrawCentredLabel(Label, LabelSize, LabelColor, Centre);
    }
}
