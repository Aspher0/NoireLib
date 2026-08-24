namespace NoireLib.UI;

/// <summary>
/// The shape of a guilloche: the interlaced rosette engraved on banknotes and watch dials.
/// Everything here is scale free except <see cref="Thickness"/>, which is a logical pixel value at 100%.
/// </summary>
public sealed class GuillocheStyle
{
    /// <summary>How many petals the rosette has.</summary>
    public int Lobes { get; set; } = 7;

    /// <summary>
    /// How pronounced the petals are, from 0 to 1.
    /// </summary>
    public float Depth { get; set; } = 0.6f;

    /// <summary>How many concentric copies are drawn, each inside the last.</summary>
    public int Rings { get; set; } = 1;

    /// <summary>
    /// How much smaller each ring is than the one outside it, as a fraction of the radius.
    /// </summary>
    public float RingSpacing { get; set; } = 0.12f;

    /// <summary>
    /// How far each ring is turned relative to the one outside it, in fractions of a full turn: half a lobe produces
    /// the interlaced look.
    /// </summary>
    public float RingRotationTurns { get; set; }

    /// <summary>The line thickness at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public float Thickness { get; set; } = 1f;

    /// <summary>How far the whole pattern is turned, in fractions of a full turn.</summary>
    public float RotationTurns { get; set; }

    /// <summary>
    /// How many line segments each ring is drawn with; zero, the default, scales the count to each ring's own radius.
    /// </summary>
    public int Segments { get; set; }

    /// <summary>
    /// Creates an independent copy.
    /// </summary>
    /// <returns>The copy.</returns>
    public GuillocheStyle Clone() => (GuillocheStyle)MemberwiseClone();
}
