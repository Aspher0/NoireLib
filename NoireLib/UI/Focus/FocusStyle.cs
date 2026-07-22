using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How the keyboard focus mark looks. See <see cref="NoireFocus"/>.
/// </summary>
/// <example>
/// <code>
/// NoireFocus.Style = new FocusStyle { Shape = FocusShape.Corners, Color = gold, Thickness = 1f };
/// </code>
/// </example>
public sealed class FocusStyle
{
    /// <summary>Which mark is drawn. Defaults to <see cref="FocusShape.Ring"/>.</summary>
    public FocusShape Shape { get; set; } = FocusShape.Ring;

    /// <summary>The mark's colour. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The line thickness at 100%, in logical pixels. Defaults to 1.5.</summary>
    public float Thickness { get; set; } = 1.5f;

    /// <summary>
    /// How far outside the control's own edge the mark sits, at 100%. Defaults to 2.
    /// </summary>
    /// <remarks>
    /// Outside rather than on the edge, so the mark does not sit on top of the frame the control already draws and
    /// read as that frame having changed colour.
    /// </remarks>
    public float Spread { get; set; } = 2f;

    /// <summary>The corner treatment of <see cref="FocusShape.Ring"/>. Defaults to rounded.</summary>
    public CornerShape CornerShape { get; set; } = CornerShape.Rounded;

    /// <summary>
    /// The corner size of <see cref="FocusShape.Ring"/>, at 100%. When <see langword="null"/>, the theme's frame
    /// rounding, so the mark follows whatever the surrounding widgets are shaped like.
    /// </summary>
    public float? CornerSize { get; set; }

    /// <summary>
    /// Which corners <see cref="FocusShape.Ring"/> shapes and <see cref="FocusShape.Corners"/> marks. Defaults to all
    /// four.
    /// </summary>
    public RectCorners Corners { get; set; } = RectCorners.All;

    /// <summary>
    /// How far the arms of <see cref="FocusShape.Corners"/> and <see cref="FocusShape.Brackets"/> reach, as a fraction
    /// of the control's shorter side. Defaults to 0.55.
    /// </summary>
    /// <remarks>
    /// A fraction rather than a distance, because a mark that is right on a text field is stubby on a tall list box and
    /// closes into a full frame on a small icon button. <see cref="ArmLength"/> overrides it where a fixed reach is
    /// wanted.
    /// </remarks>
    public float ArmRatio { get; set; } = 0.55f;

    /// <summary>
    /// The arm reach as a fixed distance at 100%, in logical pixels. Takes precedence over <see cref="ArmRatio"/> when
    /// set, and is clamped so two arms on the same edge cannot meet.
    /// </summary>
    public float? ArmLength { get; set; }

    /// <summary>
    /// The bar thickness of <see cref="FocusShape.Underline"/>, at 100%. When <see langword="null"/>, twice
    /// <see cref="Thickness"/>, since a hairline along one edge alone reads as an artefact rather than as a mark.
    /// </summary>
    public float? UnderlineThickness { get; set; }

    /// <summary>
    /// How long the mark takes to settle onto a control that has just taken focus, in seconds. Defaults to 0.12. Zero
    /// places it immediately.
    /// </summary>
    /// <remarks>
    /// Motion on arrival, never motion at rest. The hard part of keyboard navigation is seeing <em>where focus went</em>,
    /// which a short movement answers and a continuous one does not; a mark that kept moving would also be animating
    /// underneath the text the user is in the middle of typing. It is over before typing starts, and under
    /// <see cref="NoireUI.ReducedMotion"/> it does not run at all: the mark is still drawn, in place, at full strength.
    /// Focus is the one signal that has to survive reduced motion, because the people who navigate by keyboard are
    /// exactly the people who need it.
    /// </remarks>
    public float ArrivalSeconds { get; set; } = 0.12f;

    /// <summary>
    /// How much further out the mark begins before settling to <see cref="Spread"/>, at 100%. Defaults to 3.
    /// </summary>
    public float ArrivalSpread { get; set; } = 3f;

    /// <summary>
    /// Paints the mark instead of <see cref="Shape"/>, for a look the four shapes do not cover.
    /// </summary>
    /// <remarks>
    /// Handed everything the shipped painter works from, including how far through its arrival the mark is, so a hook
    /// can animate with it rather than against it. <see cref="UiFocusDraw.DrawShape"/> draws what NoireUI would have,
    /// for a hook adding to the look rather than replacing it, and a hook that draws nothing is how one widget goes
    /// unmarked while the rest of the interface keeps its mark.
    /// </remarks>
    public Action<UiFocusDraw>? CustomDraw { get; set; }

    /// <summary>Copies the style, for a variant that differs in a field or two.</summary>
    /// <returns>An independent copy.</returns>
    public FocusStyle Clone() => (FocusStyle)MemberwiseClone();

    /// <summary>The colour the mark draws in.</summary>
    internal Vector4 ResolveColor() => Color ?? NoireTheme.Current.Resolve(ThemeColor.Accent);

    /// <summary>The line thickness in real pixels.</summary>
    internal float ScaledThickness => MathF.Max(1f, NoireUI.Scaled(Thickness));

    /// <summary>The underline's bar thickness in real pixels.</summary>
    internal float ScaledUnderlineThickness
        => MathF.Max(1f, NoireUI.Scaled(UnderlineThickness ?? Thickness * 2f));

    /// <summary>The ring's corner size in real pixels.</summary>
    internal float ResolveCornerSize()
        => CornerSize.HasValue ? NoireUI.Scaled(CornerSize.Value) : NoireTheme.Current.ResolveRounding();

    /// <summary>
    /// How far an arm reaches on a control of a given size, in real pixels.
    /// </summary>
    /// <param name="size">The control's size, already spread, in real pixels.</param>
    /// <returns>The arm reach, never long enough for two arms on one edge to meet.</returns>
    internal float ResolveArmLength(Vector2 size)
    {
        var shorter = MathF.Min(size.X, size.Y);
        var reach = ArmLength.HasValue ? NoireUI.Scaled(ArmLength.Value) : shorter * ArmRatio;

        // Two arms that meet in the middle of an edge are a closed frame drawn the expensive way, and the shape stops
        // reading as corners at all. Held clear of that on both axes.
        return MathF.Max(1f, MathF.Min(reach, MathF.Min(size.X, size.Y) * 0.45f));
    }
}
