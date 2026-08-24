using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How the keyboard focus mark looks. See <see cref="NoireFocus"/>.
/// </summary>
public sealed class FocusStyle
{
    /// <summary>Which mark is drawn. Defaults to <see cref="FocusShape.Ring"/>.</summary>
    public FocusShape Shape { get; set; } = FocusShape.Ring;

    /// <summary>The mark's colour. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The line thickness at 100%, in logical pixels. Defaults to 1.5.</summary>
    public float Thickness { get; set; } = 1.5f;

    /// <summary>
    /// How far outside the control's own edge the mark sits, at 100%; defaults to 2.
    /// </summary>
    public float Spread { get; set; } = 2f;

    /// <summary>The corner treatment of <see cref="FocusShape.Ring"/>. Defaults to rounded.</summary>
    public CornerShape CornerShape { get; set; } = CornerShape.Rounded;

    /// <summary>
    /// The corner size of <see cref="FocusShape.Ring"/>, at 100%. When <see langword="null"/>, the theme's frame
    /// rounding.
    /// </summary>
    public float? CornerSize { get; set; }

    /// <summary>
    /// Which corners <see cref="FocusShape.Ring"/> shapes and <see cref="FocusShape.Corners"/> marks. Defaults to all
    /// four.
    /// </summary>
    public RectCorners Corners { get; set; } = RectCorners.All;

    /// <summary>
    /// How far the arms of <see cref="FocusShape.Corners"/> and <see cref="FocusShape.Brackets"/> reach, as a fraction
    /// of the control's shorter side; defaults to 0.55.
    /// </summary>
    public float ArmRatio { get; set; } = 0.55f;

    /// <summary>
    /// The arm reach as a fixed distance at 100%, in logical pixels. Takes precedence over <see cref="ArmRatio"/> when
    /// set, and is clamped so two arms on the same edge cannot meet.
    /// </summary>
    public float? ArmLength { get; set; }

    /// <summary>
    /// The bar thickness of <see cref="FocusShape.Underline"/>, at 100%. When <see langword="null"/>, twice
    /// <see cref="Thickness"/>.
    /// </summary>
    public float? UnderlineThickness { get; set; }

    /// <summary>
    /// How long the mark takes to settle onto a control that has just taken focus, in seconds; defaults to 0.12, and
    /// zero places it immediately.
    /// </summary>
    /// <remarks>Under <see cref="NoireUI.ReducedMotion"/> it does not run at all.</remarks>
    public float ArrivalSeconds { get; set; } = 0.12f;

    /// <summary>
    /// How much further out the mark begins before settling to <see cref="Spread"/>, at 100%; defaults to 3.
    /// </summary>
    public float ArrivalSpread { get; set; } = 3f;

    /// <summary>
    /// Paints the mark instead of <see cref="Shape"/>, for a look the four shapes do not cover.
    /// </summary>
    public Action<UiFocusDraw>? CustomDraw { get; set; }

    /// <summary>Copies the style, for a variant that differs in a field or two.</summary>
    /// <returns>An independent copy.</returns>
    public FocusStyle Clone() => (FocusStyle)MemberwiseClone();

    internal Vector4 ResolveColor() => Color ?? NoireTheme.Current.Resolve(ThemeColor.Accent);

    internal float ScaledThickness => MathF.Max(1f, NoireUI.Scaled(Thickness));

    internal float ScaledUnderlineThickness
        => MathF.Max(1f, NoireUI.Scaled(UnderlineThickness ?? Thickness * 2f));

    internal float ResolveCornerSize()
        => CornerSize.HasValue ? NoireUI.Scaled(CornerSize.Value) : NoireTheme.Current.ResolveRounding();

    // Takes the control's size, already spread, in real pixels.
    internal float ResolveArmLength(Vector2 size)
    {
        var shorter = MathF.Min(size.X, size.Y);
        var reach = ArmLength.HasValue ? NoireUI.Scaled(ArmLength.Value) : shorter * ArmRatio;

        // Two arms that meet in the middle of an edge are a closed frame drawn the expensive way, and the shape stops
        // reading as corners at all. Held clear of that on both axes.
        return MathF.Max(1f, MathF.Min(reach, MathF.Min(size.X, size.Y) * 0.45f));
    }
}
