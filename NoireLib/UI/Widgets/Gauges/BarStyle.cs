using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a bar gauge is drawn: its size, its colours, the marks along it and the text over it. Colours left
/// <see langword="null"/> resolve through <see cref="NoireTheme"/>, and sizes are logical pixels at 100% scaled
/// where they are used. See <see cref="NoireUI.Scale"/>.
/// </summary>
public sealed class BarStyle
{
    /// <summary>The width at 100%. Zero, the default, fills the space available.</summary>
    public float Width { get; set; }

    /// <summary>The height at 100%.</summary>
    public float Height { get; set; } = 12f;

    /// <summary>The corner rounding at 100%.</summary>
    public float Rounding { get; set; } = 2f;

    /// <summary>The colour of the filled part. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The colour the fill runs to. When <see langword="null"/>, the fill is flat.</summary>
    public Vector4? ColorTo { get; set; }

    /// <summary>The colour of the unfilled part. When <see langword="null"/>, the theme's sunken surface.</summary>
    public Vector4? TrackColor { get; set; }

    /// <summary>Colours that take over as the value falls. See <see cref="GaugeThreshold"/>.</summary>
    public IReadOnlyList<GaugeThreshold>? Thresholds { get; set; }

    /// <summary>Fractions along the bar, from 0 to 1, to draw a hairline at.</summary>
    public IReadOnlyList<float>? Marks { get; set; }

    /// <summary>The colour of the marks. When <see langword="null"/>, the theme's border colour.</summary>
    public Vector4? MarkColor { get; set; }

    /// <summary>The text drawn over the bar. When <see langword="null"/>, the bar carries no label.</summary>
    public string? Label { get; set; }

    /// <summary>The size the label is drawn at.</summary>
    public TextSize LabelSize { get; set; } = TextSize.Caption;

    /// <summary>The colour of the label. When <see langword="null"/>, the theme's text colour.</summary>
    public Vector4? LabelColor { get; set; }

    /// <summary>Where the label sits along the bar, from 0 (left) to 1 (right). Defaults to centred.</summary>
    public float LabelAlign { get; set; } = 0.5f;

    /// <summary>
    /// Replaces the bar's own painting entirely, including the marks and the label, while NoireUI keeps doing the
    /// sizing and the space reservation.
    /// </summary>
    public Action<UiBarDraw>? CustomDraw { get; set; }

    /// <summary><see cref="Width"/> at the current scale, or zero to fill the space available.</summary>
    internal float ScaledWidth => NoireUI.Scaled(Width);

    /// <summary><see cref="Height"/> at the current scale.</summary>
    internal float ScaledHeight => NoireUI.Scaled(Height);

    /// <summary><see cref="Rounding"/> at the current scale.</summary>
    internal float ScaledRounding => NoireUI.Scaled(Rounding);

    /// <summary>Resolves the colour of the unfilled part.</summary>
    /// <returns>The track colour.</returns>
    internal Vector4 ResolveTrackColor()
        => TrackColor ?? NoireTheme.Current.Resolve(ThemeColor.SurfaceSunken);

    /// <summary>Resolves the colour of the marks.</summary>
    /// <returns>The mark colour.</returns>
    internal Vector4 ResolveMarkColor()
        => MarkColor ?? NoireTheme.Current.Resolve(ThemeColor.Border);

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public BarStyle Clone() => (BarStyle)MemberwiseClone();
}
