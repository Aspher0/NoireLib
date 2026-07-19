using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a row of pips is drawn: the count that is small enough to read at a glance rather than measure.
/// </summary>
/// <remarks>
/// Colours left <see langword="null"/> resolve through <see cref="NoireTheme"/>. Sizes are logical pixels at 100% and
/// are scaled where they are used. See <see cref="NoireUI.Scale"/>.
/// </remarks>
public sealed class PipStyle
{
    /// <summary>The size of one pip at 100%.</summary>
    public float Size { get; set; } = 9f;

    /// <summary>The gap between pips at 100%.</summary>
    public float Spacing { get; set; } = 4f;

    /// <summary>The shape of a pip. Defaults to rounded, which reads as a dot at this size.</summary>
    public CornerShape Shape { get; set; } = CornerShape.Rounded;

    /// <summary>The colour of a filled pip. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The colour of an empty pip. When <see langword="null"/>, the theme's sunken surface.</summary>
    public Vector4? EmptyColor { get; set; }

    /// <summary>Whether empty pips are drawn as outlines rather than filled. Off by default.</summary>
    public bool OutlineEmpty { get; set; }

    /// <summary>The size of one pip in pixels, at the user's scale.</summary>
    internal float ScaledSize => NoireUI.Scaled(Size);

    /// <summary>The gap between pips in pixels, at the user's scale.</summary>
    internal float ScaledSpacing => NoireUI.Scaled(Spacing);

    /// <summary>The colour of a filled pip.</summary>
    internal Vector4 ResolveColor() => Color ?? NoireTheme.Current.Resolve(ThemeColor.Accent);

    /// <summary>The colour of an empty pip.</summary>
    internal Vector4 ResolveEmptyColor()
        => EmptyColor ?? NoireTheme.Current.Resolve(ThemeColor.SurfaceSunken);

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public PipStyle Clone() => (PipStyle)MemberwiseClone();
}
