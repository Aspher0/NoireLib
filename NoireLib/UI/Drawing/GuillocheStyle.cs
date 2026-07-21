namespace NoireLib.UI;

/// <summary>
/// The shape of a guilloche: the interlaced rosette engraved on banknotes and watch dials, and one of the few patterns
/// that reads as craftsmanship rather than as decoration.
/// </summary>
/// <remarks>
/// The curve is a hypotrochoid, the shape a pen traces through a hole in a small circle rolling inside a larger one.
/// <see cref="Lobes"/> is the ratio between the two circles and <see cref="Depth"/> is how far out the pen sits.<br/>
/// Everything here is scale free except <see cref="Thickness"/>, which is a logical pixel value at 100%.
/// </remarks>
public sealed class GuillocheStyle
{
    /// <summary>How many petals the rosette has.</summary>
    public int Lobes { get; set; } = 7;

    /// <summary>
    /// How pronounced the petals are, from 0 to 1. Towards zero the curve relaxes into a circle; at one the petals
    /// come to points.
    /// </summary>
    public float Depth { get; set; } = 0.6f;

    /// <summary>How many concentric copies are drawn, each inside the last.</summary>
    public int Rings { get; set; } = 1;

    /// <summary>
    /// How much smaller each ring is than the one outside it, as a fraction of the radius.
    /// </summary>
    public float RingSpacing { get; set; } = 0.12f;

    /// <summary>
    /// How far each ring is turned relative to the one outside it, in fractions of a full turn. Half a lobe is what
    /// produces the interlaced look.
    /// </summary>
    public float RingRotationTurns { get; set; }

    /// <summary>The line thickness at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public float Thickness { get; set; } = 1f;

    /// <summary>How far the whole pattern is turned, in fractions of a full turn.</summary>
    public float RotationTurns { get; set; }

    /// <summary>
    /// How many line segments each ring is drawn with. Zero, the default, picks a count from each ring's own radius, so
    /// a small rosette is not drawn with segments a fraction of a pixel long and an inner ring costs less than the one
    /// around it.
    /// </summary>
    /// <remarks>
    /// Setting this fixes the count for every ring regardless of size, which is worth doing only when you want two
    /// patterns of different sizes drawn with exactly the same geometry.
    /// </remarks>
    public int Segments { get; set; }

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public GuillocheStyle Clone() => (GuillocheStyle)MemberwiseClone();
}
