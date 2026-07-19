namespace NoireLib.UI;

/// <summary>
/// The shape of a sunburst: how many rays, how wide they are, where they start and whether they fade out at the rim.
/// </summary>
/// <remarks>
/// Radii here are fractions of the sunburst's own radius rather than pixels, so one style reads the same at any size
/// and needs no scaling.
/// </remarks>
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
    /// Where the rays begin, as a fraction of the radius. Zero starts them at the centre, which is what makes them
    /// converge to a point.
    /// </summary>
    public float InnerRatio { get; set; }

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
    /// <remarks>
    /// Zero leaves the sides hard, and they are then only as smooth as the one pixel of antialiasing the fill itself
    /// provides. That is enough for a handful of wide rays and visibly stepped once there are many narrow ones, which
    /// is the case this exists for. It is also the more truthful look: light does not have an edge.
    /// </remarks>
    public float Softness { get; set; } = 0.35f;

    /// <summary>
    /// Creates an independent copy, so a variant can be adjusted without touching the original.
    /// </summary>
    /// <returns>The copy.</returns>
    public SunburstStyle Clone() => (SunburstStyle)MemberwiseClone();
}
