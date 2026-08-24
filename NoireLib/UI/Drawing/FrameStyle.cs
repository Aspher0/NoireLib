using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a hairline frame is drawn: its shape, its weight, the second line that turns it into a rule, and the corner
/// ticks. Every color left <see langword="null"/> resolves through <see cref="NoireTheme"/>. Sizes are logical pixels
/// at 100%. See <see cref="NoireUI.Scale"/>.
/// </summary>
public sealed class FrameStyle
{
    #region Shape

    /// <summary>How the corners are cut. Defaults to square.</summary>
    public CornerShape CornerShape { get; set; } = CornerShape.Square;

    /// <summary>
    /// How deep the corner cut is, at 100%. When <see langword="null"/>, the theme's surface rounding is used.
    /// </summary>
    public float? CornerSize { get; set; }

    /// <summary>Which corners are cut. The rest stay square. Defaults to all four.</summary>
    public RectCorners Corners { get; set; } = RectCorners.All;

    /// <summary>
    /// How far inside the given rectangle the frame is drawn, at 100%.
    /// </summary>
    public float Inset { get; set; }

    #endregion

    #region Line

    /// <summary>The line color. When <see langword="null"/>, the theme's border color is used.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The line thickness at 100%.</summary>
    public float Thickness { get; set; } = 1f;

    /// <summary>
    /// The gap between the frame and a second line drawn inside it, at 100%. Zero, the default, draws one line.
    /// </summary>
    public float DoubleGap { get; set; }

    #endregion

    #region Corner ticks

    /// <summary>
    /// How long each arm of the corner brackets is, at 100%. Zero, the default, draws no ticks.
    /// </summary>
    public float TickLength { get; set; }

    /// <summary>How far inside the corner each bracket sits, at 100%.</summary>
    public float TickInset { get; set; } = 5f;

    /// <summary>
    /// The bracket thickness at 100%. When <see langword="null"/>, <see cref="Thickness"/> is used.
    /// </summary>
    public float? TickThickness { get; set; }

    /// <summary>
    /// The bracket color. When <see langword="null"/>, <see cref="Color"/> is used.
    /// </summary>
    public Vector4? TickColor { get; set; }

    /// <summary>Which corners get a bracket. Defaults to all four.</summary>
    public RectCorners TickCorners { get; set; } = RectCorners.All;

    /// <summary>
    /// What to draw when the rect is too small for corner ticks. Defaults to <see cref="TickFallback.None"/>.
    /// </summary>
    public TickFallback TickFallback { get; set; } = TickFallback.None;

    #endregion

    #region Resolution

    internal Vector4 ResolveColor() => Color ?? NoireTheme.Current.Resolve(ThemeColor.Border);

    internal Vector4 ResolveTickColor() => TickColor ?? ResolveColor();

    internal float ResolveCornerSize()
        => CornerSize.HasValue ? NoireUI.Scaled(CornerSize.Value) : NoireTheme.Current.ResolveSurfaceRounding();

    internal float ResolveTickThickness() => NoireUI.Scaled(TickThickness ?? Thickness);

    internal float ScaledThickness => NoireUI.Scaled(Thickness);

    internal float ScaledInset => NoireUI.Scaled(Inset);

    internal float ScaledDoubleGap => NoireUI.Scaled(DoubleGap);

    internal float ScaledTickLength => NoireUI.Scaled(TickLength);

    internal float ScaledTickInset => NoireUI.Scaled(TickInset);

    #endregion

    /// <summary>
    /// Creates an independent copy.
    /// </summary>
    /// <returns>The copy.</returns>
    public FrameStyle Clone() => (FrameStyle)MemberwiseClone();
}
