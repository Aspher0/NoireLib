namespace NoireLib.UI;

/// <summary>
/// The shape of a sunburst: how many rays, how wide they are, where they start and whether they fade out at the rim.<br/>
/// Radii here are fractions of the sunburst's own radius rather than pixels, and need no scaling.
/// </summary>
public sealed class SunburstStyle
{
    /// <summary>How many rays are drawn.</summary>
    public int Rays { get; set; } = 24;

    /// <summary>
    /// How much of a ray's slot the ray itself takes, from 0 to 1. A half draws rays as wide as the gaps between them;
    /// smaller values draw a finer burst.
    /// </summary>
    public float Duty { get; set; } = 0.5f;

    /// <summary>
    /// Where the rays begin, as a fraction of the radius. Zero starts them at the centre, so they converge to a
    /// point.
    /// </summary>
    public float InnerRatio { get; set; }

    /// <summary>
    /// Where the rays begin, as a distance from the centre at 100% (see <see cref="NoireUI.Scale"/>).<br/>
    /// Takes precedence over <see cref="InnerRatio"/> when set, and is clamped to stay inside the radius.
    /// </summary>
    public float? InnerSize { get; set; }

    /// <summary>
    /// How far the pattern is turned, in fractions of a full turn.
    /// </summary>
    public float RotationTurns { get; set; }

    /// <summary>
    /// Whether the rays fade out towards the rim. On by default: a burst that stops at a hard edge reads as a fan of
    /// triangles, and one that fades reads as light.
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
