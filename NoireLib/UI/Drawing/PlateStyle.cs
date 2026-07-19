using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a plate is painted: its shape, its fill, its border, the bevel that raises it and the glow that lifts it off the
/// background.
/// </summary>
/// <remarks>
/// Every color left <see langword="null"/> resolves through <see cref="NoireTheme"/>, so a plate with no style at all
/// already matches the interface around it.<br/>
/// Sizes are logical pixels at 100% and are scaled where they are used, like every other measurement NoireUI ships a
/// default for. See <see cref="NoireUI.Scale"/>.
/// </remarks>
/// <example>
/// <code>
/// // An art deco plate: chamfered on the diagonal, lit from above, sitting in its own glow.
/// var style = new PlateStyle
/// {
///     CornerShape = CornerShape.Notched,
///     CornerSize = 14f,
///     Corners = RectCorners.Diagonal,
///     BevelSize = 2f,
///     GlowSpread = 10f,
/// };
/// </code>
/// </example>
public sealed class PlateStyle
{
    #region Shape

    /// <summary>How the corners are cut. Defaults to rounded.</summary>
    public CornerShape CornerShape { get; set; } = CornerShape.Rounded;

    /// <summary>
    /// How deep the corner cut is, at 100%. When <see langword="null"/>, the theme's surface rounding is used.
    /// </summary>
    public float? CornerSize { get; set; }

    /// <summary>Which corners are cut. The rest stay square. Defaults to all four.</summary>
    public RectCorners Corners { get; set; } = RectCorners.All;

    #endregion

    #region Fill

    /// <summary>
    /// The fill color. When <see langword="null"/>, the theme's raised surface is used.<br/>
    /// A fully transparent value is a real setting, and the way to ask for an outline with nothing behind it.
    /// </summary>
    public Vector4? Fill { get; set; }

    /// <summary>
    /// The color the fill runs to. When <see langword="null"/>, the plate is flat.
    /// </summary>
    public Vector4? FillTo { get; set; }

    /// <summary>Which way the fill gradient runs. Ignored when <see cref="FillTo"/> is unset.</summary>
    public GradientAxis FillAxis { get; set; } = GradientAxis.Vertical;

    #endregion

    #region Border

    /// <summary>The border color. When <see langword="null"/>, the theme's border color is used.</summary>
    public Vector4? BorderColor { get; set; }

    /// <summary>
    /// The border thickness at 100%. When <see langword="null"/>, the theme's border size is used, which is zero on a
    /// theme that asks for a flat look.
    /// </summary>
    public float? BorderSize { get; set; }

    #endregion

    #region Bevel

    /// <summary>
    /// The thickness of the lit and shaded edges at 100%. Zero, the default, draws no bevel.
    /// </summary>
    public float BevelSize { get; set; }

    /// <summary>
    /// The color of the edges facing the light. When <see langword="null"/>, a lightened form of the fill is used.
    /// </summary>
    public Vector4? BevelLight { get; set; }

    /// <summary>
    /// The color of the edges facing away from it. When <see langword="null"/>, a darkened form of the fill is used.
    /// </summary>
    public Vector4? BevelShadow { get; set; }

    /// <summary>
    /// Where the light comes from. Defaults to above and to the left.
    /// </summary>
    public Vector2 BevelDirection { get; set; }

    #endregion

    #region Glow

    /// <summary>
    /// The color of the glow around the plate. When <see langword="null"/>, the theme's shadow color is used, which
    /// makes the default a drop shadow rather than a glow.
    /// </summary>
    public Vector4? GlowColor { get; set; }

    /// <summary>
    /// How far the glow reaches beyond the plate at 100%. Zero, the default, draws none.
    /// </summary>
    public float GlowSpread { get; set; }

    #endregion

    #region Resolution

    /// <summary>The fill color to paint with.</summary>
    internal Vector4 ResolveFill() => Fill ?? NoireTheme.Current.Resolve(ThemeColor.SurfaceRaised);

    /// <summary>The corner depth in pixels, at the user's scale.</summary>
    internal float ResolveCornerSize()
        => CornerSize.HasValue ? NoireUI.Scaled(CornerSize.Value) : NoireTheme.Current.ResolveSurfaceRounding();

    /// <summary>The border color to paint with.</summary>
    internal Vector4 ResolveBorderColor() => BorderColor ?? NoireTheme.Current.Resolve(ThemeColor.Border);

    /// <summary>The border thickness in pixels, at the user's scale.</summary>
    internal float ResolveBorderSize()
        => BorderSize.HasValue ? NoireUI.Scaled(BorderSize.Value) : NoireTheme.Current.ResolveBorderSize();

    /// <summary>The lit bevel color, derived from the fill when it was not given.</summary>
    internal Vector4 ResolveBevelLight() => BevelLight ?? ColorHelper.Lighten(ResolveFill(), 0.35f);

    /// <summary>The shaded bevel color, derived from the fill when it was not given.</summary>
    internal Vector4 ResolveBevelShadow() => BevelShadow ?? ColorHelper.Darken(ResolveFill(), 0.35f);

    /// <summary>The glow color to paint with.</summary>
    internal Vector4 ResolveGlowColor() => GlowColor ?? NoireTheme.Current.Resolve(ThemeColor.Shadow);

    /// <summary>The bevel thickness in pixels, at the user's scale.</summary>
    internal float ScaledBevelSize => NoireUI.Scaled(BevelSize);

    /// <summary>The glow reach in pixels, at the user's scale.</summary>
    internal float ScaledGlowSpread => NoireUI.Scaled(GlowSpread);

    #endregion

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public PlateStyle Clone() => (PlateStyle)MemberwiseClone();
}
