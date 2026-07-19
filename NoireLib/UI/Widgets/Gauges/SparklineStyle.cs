using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a sparkline is drawn: the shape of the trace, what sits under it, and which end is worth pointing at.
/// </summary>
/// <remarks>
/// Colours left <see langword="null"/> resolve through <see cref="NoireTheme"/>. Sizes are logical pixels at 100% and
/// are scaled where they are used. See <see cref="NoireUI.Scale"/>.
/// </remarks>
public sealed class SparklineStyle
{
    /// <summary>The width at 100%. Zero, the default, fills the space available.</summary>
    public float Width { get; set; }

    /// <summary>The height at 100%.</summary>
    public float Height { get; set; } = 32f;

    /// <summary>The thickness of the trace at 100%.</summary>
    public float Thickness { get; set; } = 1.5f;

    /// <summary>The colour of the trace. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? Color { get; set; }

    /// <summary>
    /// The colour of the area under the trace. When <see langword="null"/>, the trace colour at low opacity.<br/>
    /// A fully transparent value is a real setting, and the way to ask for a bare line.
    /// </summary>
    public Vector4? FillColor { get; set; }

    /// <summary>The colour behind the trace. When <see langword="null"/>, nothing is drawn behind it.</summary>
    public Vector4? Background { get; set; }

    /// <summary>Whether the last point is marked with a dot. On by default, because it is the current value.</summary>
    public bool MarkLast { get; set; } = true;

    /// <summary>The radius of that dot at 100%.</summary>
    public float MarkSize { get; set; } = 2.5f;

    /// <summary>
    /// A value to draw a horizontal rule at, in the same units as the data. When <see langword="null"/>, none is drawn.
    /// </summary>
    public float? Baseline { get; set; }

    /// <summary>The colour of the baseline. When <see langword="null"/>, the theme's border colour.</summary>
    public Vector4? BaselineColor { get; set; }

    /// <summary>
    /// The value at the bottom of the plot. When <see langword="null"/>, the lowest value in the data.
    /// </summary>
    /// <remarks>
    /// Fixing both ends is what makes two sparklines comparable. Left to the data, every trace fills its own box and
    /// a flat line and a violent one look identical.
    /// </remarks>
    public float? Min { get; set; }

    /// <summary>The value at the top of the plot. When <see langword="null"/>, the highest value in the data.</summary>
    public float? Max { get; set; }

    /// <summary>The width in pixels at the user's scale, or zero to fill the space available.</summary>
    internal float ScaledWidth => NoireUI.Scaled(Width);

    /// <summary>The height in pixels, at the user's scale.</summary>
    internal float ScaledHeight => NoireUI.Scaled(Height);

    /// <summary>The trace thickness in pixels, at the user's scale.</summary>
    internal float ScaledThickness => NoireUI.Scaled(Thickness);

    /// <summary>The end-dot radius in pixels, at the user's scale.</summary>
    internal float ScaledMarkSize => NoireUI.Scaled(MarkSize);

    /// <summary>The colour of the baseline rule.</summary>
    internal Vector4 ResolveBaselineColor()
        => BaselineColor ?? NoireTheme.Current.Resolve(ThemeColor.Border);

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public SparklineStyle Clone() => (SparklineStyle)MemberwiseClone();
}
