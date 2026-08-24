using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a bar gauge is drawn: its size, its colours, the marks along it and the text over it. Colours left
/// <see langword="null"/> resolve through <see cref="NoireTheme"/>, and sizes are logical pixels at 100% scaled
/// where they are used.
/// </summary>
public sealed class BarStyle
{
    /// <summary>The width at 100%; zero fills the space available.</summary>
    public float Width { get; set; }

    /// <summary>The height at 100%.</summary>
    public float Height { get; set; } = 12f;

    /// <summary>The corner rounding at 100%.</summary>
    public float Rounding { get; set; } = 2f;

    /// <summary>The colour of the filled part; the theme's accent when <see langword="null"/>.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The colour the fill runs to; the fill is flat when <see langword="null"/>.</summary>
    public Vector4? ColorTo { get; set; }

    /// <summary>The colour of the unfilled part; the theme's sunken surface when <see langword="null"/>.</summary>
    public Vector4? TrackColor { get; set; }

    /// <summary>Colours that take over as the value falls.</summary>
    public IReadOnlyList<GaugeThreshold>? Thresholds { get; set; }

    /// <summary>Fractions along the bar, from 0 to 1, to draw a hairline at.</summary>
    public IReadOnlyList<float>? Marks { get; set; }

    /// <summary>The colour of the marks; the theme's border colour when <see langword="null"/>.</summary>
    public Vector4? MarkColor { get; set; }

    /// <summary>The text drawn over the bar; the bar carries no label when <see langword="null"/>.</summary>
    public string? Label { get; set; }

    /// <summary>The size the label is drawn at.</summary>
    public TextSize LabelSize { get; set; } = TextSize.Caption;

    /// <summary>The colour of the label; the theme's text colour when <see langword="null"/>.</summary>
    public Vector4? LabelColor { get; set; }

    /// <summary>Where the label sits along the bar, from 0 (left) to 1 (right).</summary>
    public float LabelAlign { get; set; } = 0.5f;

    /// <summary>
    /// Replaces the bar's own painting entirely, including the marks and the label, while NoireUI keeps doing the
    /// sizing and the space reservation.
    /// </summary>
    public Action<UiBarDraw>? CustomDraw { get; set; }

    internal float ScaledWidth => NoireUI.Scaled(Width);

    internal float ScaledHeight => NoireUI.Scaled(Height);

    internal float ScaledRounding => NoireUI.Scaled(Rounding);

    internal Vector4 ResolveTrackColor()
        => TrackColor ?? NoireTheme.Current.Resolve(ThemeColor.SurfaceSunken);

    internal Vector4 ResolveMarkColor()
        => MarkColor ?? NoireTheme.Current.Resolve(ThemeColor.Border);

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public BarStyle Clone() => (BarStyle)MemberwiseClone();
}
