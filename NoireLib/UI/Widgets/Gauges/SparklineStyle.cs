using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a sparkline is drawn: the trace, the area under it, and the end mark. Colours left <see langword="null"/>
/// resolve through <see cref="NoireTheme"/>, and sizes are logical pixels at 100%, scaled by
/// <see cref="NoireUI.Scale"/> where they are used.
/// </summary>
public sealed class SparklineStyle
{
    /// <summary>The width at 100%, or zero to fill the space available.</summary>
    public float Width { get; set; }

    /// <summary>The height at 100%.</summary>
    public float Height { get; set; } = 32f;

    /// <summary>The thickness of the trace at 100%.</summary>
    public float Thickness { get; set; } = 1.5f;

    /// <summary>The colour of the trace, or <see langword="null"/> for the theme's accent.</summary>
    public Vector4? Color { get; set; }

    /// <summary>
    /// The colour of the area under the trace, or <see langword="null"/> for the trace colour at low opacity; a fully
    /// transparent value leaves a bare line.
    /// </summary>
    public Vector4? FillColor { get; set; }

    /// <summary>The colour behind the trace, or <see langword="null"/> to draw nothing behind it.</summary>
    public Vector4? Background { get; set; }

    /// <summary>Whether the last point is marked with a dot.</summary>
    public bool MarkLast { get; set; } = true;

    /// <summary>The radius of the end dot at 100%.</summary>
    public float MarkSize { get; set; } = 2.5f;

    /// <summary>The value to draw a horizontal rule at, in the same units as the data, or <see langword="null"/> for none.</summary>
    public float? Baseline { get; set; }

    /// <summary>The colour of the baseline rule, or <see langword="null"/> for the theme's border colour.</summary>
    public Vector4? BaselineColor { get; set; }

    /// <summary>The value at the bottom of the plot, or <see langword="null"/> to scale each trace to its own lowest value.</summary>
    public float? Min { get; set; }

    /// <summary>The value at the top of the plot, or <see langword="null"/> to scale each trace to its own highest value.</summary>
    public float? Max { get; set; }

    /// <summary>Replaces the painting of the area, line and end mark, while NoireUI keeps the layout, background and baseline.</summary>
    public Action<UiSparklineDraw>? CustomDraw { get; set; }

    /// <summary>The width in scaled pixels, or zero to fill the space available.</summary>
    internal float ScaledWidth => NoireUI.Scaled(Width);

    /// <summary>The height in scaled pixels.</summary>
    internal float ScaledHeight => NoireUI.Scaled(Height);

    /// <summary>The trace thickness in scaled pixels.</summary>
    internal float ScaledThickness => NoireUI.Scaled(Thickness);

    /// <summary>The end-dot radius in scaled pixels.</summary>
    internal float ScaledMarkSize => NoireUI.Scaled(MarkSize);

    /// <summary>Resolves the baseline rule's colour, falling back to the theme's border colour.</summary>
    /// <returns>The baseline colour.</returns>
    internal Vector4 ResolveBaselineColor()
        => BaselineColor ?? NoireTheme.Current.Resolve(ThemeColor.Border);

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public SparklineStyle Clone() => (SparklineStyle)MemberwiseClone();
}
