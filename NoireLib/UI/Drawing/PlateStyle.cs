using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a plate is painted: its shape, its fill, its border, the bevel that raises it and the glow that lifts it off the
/// background. Every color left <see langword="null"/> resolves through <see cref="NoireTheme"/>. Sizes are logical
/// pixels at 100% and are scaled where they are used.
/// </summary>
public sealed class PlateStyle
{
    #region Shape

    /// <summary>How the corners are cut; defaults to rounded.</summary>
    public CornerShape CornerShape { get; set; } = CornerShape.Rounded;

    /// <summary>
    /// How deep the corner cut is, at 100%; when <see langword="null"/>, the theme's surface rounding is used.
    /// </summary>
    public float? CornerSize { get; set; }

    /// <summary>Which corners are cut, the rest staying square; defaults to all four.</summary>
    public RectCorners Corners { get; set; } = RectCorners.All;

    #endregion

    #region Fill

    /// <summary>
    /// The fill color; when <see langword="null"/>, the theme's raised surface is used.
    /// </summary>
    public Vector4? Fill { get; set; }

    /// <summary>
    /// The color the fill runs to; when <see langword="null"/>, the plate is flat.
    /// </summary>
    public Vector4? FillTo { get; set; }

    /// <summary>Which way the fill gradient runs, ignored when <see cref="FillTo"/> is unset.</summary>
    public GradientAxis FillAxis { get; set; } = GradientAxis.Vertical;

    #endregion

    #region Border

    /// <summary>The border color; when <see langword="null"/>, the theme's border color is used.</summary>
    public Vector4? BorderColor { get; set; }

    /// <summary>
    /// The border thickness at 100%; when <see langword="null"/>, the theme's border size is used.
    /// </summary>
    public float? BorderSize { get; set; }

    #endregion

    #region Bevel

    /// <summary>
    /// The thickness of the lit and shaded edges at 100%, zero (the default) drawing no bevel.
    /// </summary>
    public float BevelSize { get; set; }

    /// <summary>
    /// The color of the edges facing the light; when <see langword="null"/>, a lightened form of the fill is used.
    /// </summary>
    public Vector4? BevelLight { get; set; }

    /// <summary>
    /// The color of the edges facing away from it; when <see langword="null"/>, a darkened form of the fill is used.
    /// </summary>
    public Vector4? BevelShadow { get; set; }

    /// <summary>
    /// Where the light comes from; defaults to above and to the left.
    /// </summary>
    public Vector2 BevelDirection { get; set; }

    #endregion

    #region Glow

    /// <summary>
    /// The color of the glow around the plate; when <see langword="null"/>, the theme's shadow color is used.
    /// </summary>
    public Vector4? GlowColor { get; set; }

    /// <summary>
    /// How far the glow reaches beyond the plate at 100%, zero (the default) drawing none.
    /// </summary>
    public float GlowSpread { get; set; }

    #endregion

    #region Resolution

    internal Vector4 ResolveFill() => Fill ?? NoireTheme.Current.Resolve(ThemeColor.SurfaceRaised);

    // In pixels, at the user's scale.
    internal float ResolveCornerSize()
        => CornerSize.HasValue ? NoireUI.Scaled(CornerSize.Value) : NoireTheme.Current.ResolveSurfaceRounding();

    internal Vector4 ResolveBorderColor() => BorderColor ?? NoireTheme.Current.Resolve(ThemeColor.Border);

    // In pixels, at the user's scale.
    internal float ResolveBorderSize()
        => BorderSize.HasValue ? NoireUI.Scaled(BorderSize.Value) : NoireTheme.Current.ResolveBorderSize();

    internal Vector4 ResolveBevelLight() => BevelLight ?? ColorHelper.Lighten(ResolveFill(), 0.35f);

    internal Vector4 ResolveBevelShadow() => BevelShadow ?? ColorHelper.Darken(ResolveFill(), 0.35f);

    internal Vector4 ResolveGlowColor() => GlowColor ?? NoireTheme.Current.Resolve(ThemeColor.Shadow);

    internal float ScaledBevelSize => NoireUI.Scaled(BevelSize);

    internal float ScaledGlowSpread => NoireUI.Scaled(GlowSpread);

    #endregion

    /// <summary>
    /// Creates an independent copy.
    /// </summary>
    /// <returns>The copy.</returns>
    public PlateStyle Clone() => (PlateStyle)MemberwiseClone();

    /// <summary>
    /// Copies every value of <paramref name="source"/> into this style, leaving no reference to it.
    /// </summary>
    /// <param name="source">The style to copy from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public void CopyFrom(PlateStyle source)
    {
        ArgumentNullException.ThrowIfNull(source);

        CornerShape = source.CornerShape;
        CornerSize = source.CornerSize;
        Corners = source.Corners;
        Fill = source.Fill;
        FillTo = source.FillTo;
        FillAxis = source.FillAxis;
        BorderColor = source.BorderColor;
        BorderSize = source.BorderSize;
        BevelSize = source.BevelSize;
        BevelLight = source.BevelLight;
        BevelShadow = source.BevelShadow;
        BevelDirection = source.BevelDirection;
        GlowColor = source.GlowColor;
        GlowSpread = source.GlowSpread;
    }
}
