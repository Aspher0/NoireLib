using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Styling for a row of pips.
/// </summary>
/// <remarks>Colours left <see langword="null"/> resolve through <see cref="NoireTheme"/>; sizes are logical pixels at 100%.</remarks>
public sealed class PipStyle
{
    /// <summary>The size of one pip at 100%.</summary>
    public float Size { get; set; } = 9f;

    /// <summary>The gap between pips at 100%.</summary>
    public float Spacing { get; set; } = 4f;

    /// <summary>The shape of a pip.</summary>
    public CornerShape Shape { get; set; } = CornerShape.Rounded;

    /// <summary>The colour of a filled pip.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The colour of an empty pip.</summary>
    public Vector4? EmptyColor { get; set; }

    /// <summary>Whether empty pips are drawn as outlines rather than filled.</summary>
    public bool OutlineEmpty { get; set; }

    /// <summary>
    /// Replaces each pip's painting, called once per pip, with layout and space reservation still handled by NoireUI.
    /// </summary>
    public Action<UiPipDraw>? CustomDraw { get; set; }

    // The values a row of pips draws with, in pixels at the user's scale.

    internal float ScaledSize => NoireUI.Scaled(Size);

    internal float ScaledSpacing => NoireUI.Scaled(Spacing);

    internal Vector4 ResolveColor() => Color ?? NoireTheme.Current.Resolve(ThemeColor.Accent);

    internal Vector4 ResolveEmptyColor()
        => EmptyColor ?? NoireTheme.Current.Resolve(ThemeColor.SurfaceSunken);

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public PipStyle Clone() => (PipStyle)MemberwiseClone();
}
