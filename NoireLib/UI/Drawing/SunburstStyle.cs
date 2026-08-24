namespace NoireLib.UI;

/// <summary>
/// The shape of a sunburst: how many rays, how wide they are, where they start and whether they fade out at the rim.
/// Radii here are fractions of the sunburst's own radius rather than pixels, and need no scaling.
/// </summary>
public sealed class SunburstStyle
{
    /// <summary>How many rays are drawn.</summary>
    public int Rays { get; set; } = 24;

    /// <summary>
    /// How much of a ray's slot the ray itself takes, from 0 to 1.
    /// </summary>
    public float Duty { get; set; } = 0.5f;

    /// <summary>
    /// Where the rays begin, as a fraction of the radius.
    /// </summary>
    public float InnerRatio { get; set; }

    /// <summary>
    /// Where the rays begin, as a distance from the centre at 100% (see <see cref="NoireUI.Scale"/>), taking
    /// precedence over <see cref="InnerRatio"/> when set.
    /// </summary>
    public float? InnerSize { get; set; }

    /// <summary>
    /// How far the pattern is turned, in fractions of a full turn.
    /// </summary>
    public float RotationTurns { get; set; }

    /// <summary>
    /// Whether the rays fade out towards the rim.
    /// </summary>
    public bool Fade { get; set; } = true;

    /// <summary>
    /// How much of each ray's width is spent fading out at its sides, from 0 to 1.
    /// </summary>
    public float Softness { get; set; } = 0.35f;

    /// <summary>
    /// Creates an independent copy.
    /// </summary>
    /// <returns>The copy.</returns>
    public SunburstStyle Clone() => (SunburstStyle)MemberwiseClone();
}
