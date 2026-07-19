using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a ring gauge is drawn: its size, the weight of its band, where it starts, and what it says in the middle.
/// </summary>
/// <remarks>
/// Colours left <see langword="null"/> resolve through <see cref="NoireTheme"/>. Sizes are logical pixels at 100% and
/// are scaled where they are used. See <see cref="NoireUI.Scale"/>.
/// </remarks>
public sealed class RingStyle
{
    /// <summary>The outer diameter at 100%.</summary>
    public float Size { get; set; } = 40f;

    /// <summary>The thickness of the band at 100%.</summary>
    public float Thickness { get; set; } = 5f;

    /// <summary>The colour of the filled part. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The colour of the unfilled part. When <see langword="null"/>, the theme's sunken surface.</summary>
    public Vector4? TrackColor { get; set; }

    /// <summary>
    /// Colours that take over as the value falls. See <see cref="GaugeThreshold"/>.
    /// </summary>
    public IReadOnlyList<GaugeThreshold>? Thresholds { get; set; }

    /// <summary>Where the fill starts, in turns clockwise from twelve o'clock. Defaults to the top.</summary>
    public float StartTurns { get; set; }

    /// <summary>How much of the circle a full value covers, in turns. Defaults to all of it.</summary>
    /// <remarks>Set to 0.75 with a <see cref="StartTurns"/> of 0.625 for the open-bottomed dial a speedometer uses.</remarks>
    public float SweepTurns { get; set; } = 1f;

    /// <summary>Whether the fill runs clockwise. On by default.</summary>
    public bool Clockwise { get; set; } = true;

    /// <summary>The text drawn in the middle. When <see langword="null"/>, the ring carries no label.</summary>
    public string? Label { get; set; }

    /// <summary>The size the label is drawn at.</summary>
    public TextSize LabelSize { get; set; } = TextSize.Caption;

    /// <summary>The colour of the label. When <see langword="null"/>, the colour of the fill.</summary>
    public Vector4? LabelColor { get; set; }

    /// <summary>The outer diameter in pixels, at the user's scale.</summary>
    internal float ScaledSize => NoireUI.Scaled(Size);

    /// <summary>The band thickness in pixels, at the user's scale.</summary>
    internal float ScaledThickness => NoireUI.Scaled(Thickness);

    /// <summary>The colour of the unfilled part.</summary>
    internal Vector4 ResolveTrackColor()
        => TrackColor ?? NoireTheme.Current.Resolve(ThemeColor.SurfaceSunken);

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public RingStyle Clone() => (RingStyle)MemberwiseClone();
}
