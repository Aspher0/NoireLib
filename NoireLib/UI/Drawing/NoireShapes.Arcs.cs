using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The round shapes: arcs, rings, wedges, and the two pattern fills a bespoke panel is decorated with.
/// </summary>
/// <remarks>
/// Angles here are turns rather than radians: 0 is twelve o'clock and a quarter is three o'clock, so a three quarter
/// arc is written as 0.75 and a gauge reading 40 percent is written as 0.4. That matches how every other fraction in
/// NoireUI is expressed, and it removes the question of which direction zero points in.
/// </remarks>
public static partial class NoireShapes
{
    /// <summary>
    /// The most points a curve is drawn with. Past this the segments are already shorter than a pixel.
    /// </summary>
    private const int MaxCurvePoints = 512;

    private static readonly SunburstStyle DefaultSunburstStyle = new();
    private static readonly GuillocheStyle DefaultGuillocheStyle = new();

    #region Arcs

    /// <summary>
    /// The most points <see cref="ArcPath"/> can ever write, so a caller can size a buffer once and stop thinking
    /// about it.
    /// </summary>
    public const int MaxArcPathPoints = 257;

    /// <summary>
    /// Writes the points along an arc, and says whether the arc comes all the way round.
    /// </summary>
    /// <remarks>
    /// The primitive every round shape here is drawn from, public for the same reason <see cref="RectPath"/> is: a
    /// dial or a segmented ring NoireUI does not ship is this path, adjusted, handed to <see cref="Fill"/>,
    /// <see cref="Stroke"/> or <see cref="Bevel"/>.<br/>
    /// A closed path stops one step short of coming back to its first point, because both stroking and filling close
    /// the loop themselves. See <paramref name="closed"/>.
    /// </remarks>
    /// <param name="points">Receives the path. At least <see cref="MaxArcPathPoints"/> long is always enough.</param>
    /// <param name="centre">The centre of the circle, in screen space.</param>
    /// <param name="radius">The radius, in real pixels.</param>
    /// <param name="fromTurns">Where the arc starts, in turns clockwise from twelve o'clock.</param>
    /// <param name="toTurns">Where it ends.</param>
    /// <param name="closed">
    /// Whether the path is a closed loop, which it is once the sweep is a full turn. Pass it on to
    /// <see cref="Stroke"/>: the path deliberately does not repeat its first point, so stroking it open leaves the
    /// loop with a gap.
    /// </param>
    /// <returns>How many points were written, or zero when the sweep is empty or the buffer is too small.</returns>
    public static int ArcPath(Span<Vector2> points, Vector2 centre, float radius, float fromTurns, float toTurns, out bool closed)
    {
        closed = false;

        if (radius <= 0f)
            return 0;

        var sweep = toTurns - fromTurns;

        if (MathF.Abs(sweep) < 0.0001f)
            return 0;

        closed = MathF.Abs(sweep) >= 0.999f;

        var segments = SegmentsFor(sweep, radius);

        // A closed loop must not repeat its first point. The edge back to the start is the one the stroke or the fill
        // adds for itself, so a repeated point leaves that edge zero length: it has no direction to build a join from,
        // and it draws as a spike at the seam rather than as nothing.
        var count = closed ? segments : segments + 1;

        if (count > points.Length)
            return 0;

        var start = Radians(fromTurns);
        var span = sweep * MathF.Tau;

        for (var step = 0; step < count; step++)
            points[step] = centre + (Direction(start + (span * step / segments)) * radius);

        return count;
    }

    /// <summary>
    /// Strokes an arc.
    /// </summary>
    /// <param name="centre">The centre of the circle, in screen space.</param>
    /// <param name="radius">The radius, in real pixels.</param>
    /// <param name="fromTurns">Where the arc starts, in turns clockwise from twelve o'clock.</param>
    /// <param name="toTurns">Where it ends.</param>
    /// <param name="color">The line color.</param>
    /// <param name="thickness">The line thickness, in real pixels.</param>
    public static void Arc(Vector2 centre, float radius, float fromTurns, float toTurns, Vector4 color, float thickness = 1f)
    {
        if (thickness <= 0f || color.W <= 0f)
            return;

        Span<Vector2> points = stackalloc Vector2[MaxArcPathPoints];

        // Closed only when the arc comes all the way round, so a gauge does not draw a chord across its own opening.
        var count = ArcPath(points, centre, radius, fromTurns, toTurns, out var closed);

        if (count > 0)
            Stroke(points[..count], color, thickness, closed);
    }

    /// <summary>
    /// Strokes a full circle.
    /// </summary>
    /// <param name="centre">The centre, in screen space.</param>
    /// <param name="radius">The radius, in real pixels.</param>
    /// <param name="color">The line color.</param>
    /// <param name="thickness">The line thickness, in real pixels.</param>
    public static void Ring(Vector2 centre, float radius, Vector4 color, float thickness = 1f)
        => Arc(centre, radius, 0f, 1f, color, thickness);

    /// <summary>
    /// Fills a slice of a ring, or a slice of a disc when <paramref name="innerRadius"/> is zero.
    /// </summary>
    /// <remarks>
    /// The shape a radial gauge is made of. An inner radius is drawn as a thick arc rather than as a filled band, which
    /// is what keeps the ends square and the edges antialiased however far round it goes.
    /// </remarks>
    /// <param name="centre">The centre of the circle, in screen space.</param>
    /// <param name="innerRadius">The inner radius, in real pixels. Zero fills to the centre.</param>
    /// <param name="outerRadius">The outer radius, in real pixels.</param>
    /// <param name="fromTurns">Where the slice starts, in turns clockwise from twelve o'clock.</param>
    /// <param name="toTurns">Where it ends.</param>
    /// <param name="color">The fill color.</param>
    public static void Wedge(Vector2 centre, float innerRadius, float outerRadius, float fromTurns, float toTurns, Vector4 color)
    {
        if (outerRadius <= 0f || color.W <= 0f)
            return;

        var sweep = toTurns - fromTurns;

        if (MathF.Abs(sweep) < 0.0001f)
            return;

        if (innerRadius > 0f)
        {
            Arc(centre, (innerRadius + outerRadius) * 0.5f, fromTurns, toTurns, color, outerRadius - innerRadius);
            return;
        }

        // A slice wider than half a turn has a reflex angle at the centre and is no longer convex, so it is drawn as
        // two halves that each are. A full turn is a disc, which is convex on its own and needs no split.
        var magnitude = MathF.Abs(sweep);
        var pieces = magnitude > 0.5f && magnitude < 0.999f ? 2 : 1;

        for (var piece = 0; piece < pieces; piece++)
        {
            var pieceFrom = fromTurns + (sweep * piece / pieces);
            var pieceTo = fromTurns + (sweep * (piece + 1) / pieces);
            Pie(centre, outerRadius, pieceFrom, pieceTo, color);
        }
    }

    /// <summary>
    /// Fills one convex slice of a disc.
    /// </summary>
    private static void Pie(Vector2 centre, float radius, float fromTurns, float toTurns, Vector4 color)
    {
        // One past the arc's own limit, for the centre vertex a partial slice needs in front of it.
        Span<Vector2> points = stackalloc Vector2[MaxArcPathPoints + 1];

        var count = ArcPath(points[1..], centre, radius, fromTurns, toTurns, out var closed);

        if (count == 0)
            return;

        // A full turn is a disc, and a disc has no centre vertex: including one would fold the fan back through the
        // middle and leave a seam along the radius where it starts and ends.
        if (closed)
        {
            Fill(points.Slice(1, count), color);
            return;
        }

        points[0] = centre;
        Fill(points[..(count + 1)], color);
    }

    /// <summary>
    /// How many segments a curve of a given sweep and radius needs before the facets stop being visible.
    /// </summary>
    private static int SegmentsFor(float sweepTurns, float radius)
        => Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweepTurns) * radius * 0.6f) + 2, 3, 256);

    /// <summary>
    /// Turns clockwise from twelve o'clock, as an angle in the draw list's coordinate space.
    /// </summary>
    private static float Radians(float turns) => (turns - 0.25f) * MathF.Tau;

    #endregion

    #region Patterns

    /// <summary>
    /// Draws a sunburst: rays radiating from a point, fading out at the rim.
    /// </summary>
    /// <remarks>
    /// Drawn as geometry rather than rendered into a cached texture. Rays are a few dozen triangles, so there is
    /// nothing to cache that would be cheaper than drawing them, and geometry stays sharp at every scale and takes no
    /// texture memory.
    /// </remarks>
    /// <param name="centre">Where the rays converge, in screen space.</param>
    /// <param name="radius">How far they reach, in real pixels.</param>
    /// <param name="color">The ray color at the centre.</param>
    /// <param name="style">The shape of the burst. When <see langword="null"/>, an even twenty four ray burst.</param>
    public static void Sunburst(Vector2 centre, float radius, Vector4 color, SunburstStyle? style = null)
    {
        style ??= DefaultSunburstStyle;

        if (radius <= 0f || color.W <= 0f || style.Rays <= 0)
            return;

        using var draw = UiDraw.BeginMethod();

        var duty = Math.Clamp(style.Duty, 0.01f, 1f);
        var inner = Math.Clamp(style.InnerRatio, 0f, 0.99f) * radius;
        var slot = MathF.Tau / style.Rays;
        var half = slot * duty * 0.5f;
        var softness = Math.Clamp(style.Softness, 0f, 1f);
        var faded = color with { W = 0f };

        // A soft side is built the way the glow is: layers that each carry part of the alpha, narrowing inwards so
        // they accumulate towards the middle of the ray and leave its edge at a fraction of full strength. An angular
        // fade cannot be a linear gradient, because the ray's sides converge, so stacking is what is left.
        var layers = softness > 0f ? Math.Clamp((int)MathF.Ceiling(softness * 8f), 2, 8) : 1;

        var tipped = inner <= 0.5f;

        // Each layer carries whatever is left to reach its step of the ramp, not an equal share, because layers
        // composite rather than add. An equal share lands short of the color asked for, and solving instead for one
        // flat alpha that composites to it breaks down at full opacity, where every layer comes out opaque and there
        // is no soft edge left at all.
        Span<float> layerAlphas = stackalloc float[layers];
        var covered = 0f;

        for (var layer = 0; layer < layers; layer++)
        {
            var reach = color.W * (layer + 1) / layers;
            layerAlphas[layer] = covered >= 1f ? 0f : (reach - covered) / (1f - covered);
            covered = reach;
        }

        Span<Vector2> wedge = stackalloc Vector2[4];

        for (var ray = 0; ray < style.Rays; ray++)
        {
            var angle = Radians(style.RotationTurns) + (slot * ray);
            var along = Direction(angle);

            for (var layer = 0; layer < layers; layer++)
            {
                var width = layers == 1 ? half : half * (1f - (softness * layer / (layers - 1)));
                var tint = color with { W = layerAlphas[layer] };
                var left = Direction(angle - width);
                var right = Direction(angle + width);

                int count;

                if (tipped)
                {
                    // The rays converge to a point, so both inner corners are the same point. Emitting both would
                    // leave a zero-length edge, which has no direction to build an antialiased join from.
                    wedge[0] = centre;
                    wedge[1] = centre + (right * radius);
                    wedge[2] = centre + (left * radius);
                    count = 3;
                }
                else
                {
                    wedge[0] = centre + (left * inner);
                    wedge[1] = centre + (right * inner);
                    wedge[2] = centre + (right * radius);
                    wedge[3] = centre + (left * radius);
                    count = 4;
                }

                if (style.Fade)
                    FillShaded(wedge[..count], centre + (along * inner), centre + (along * radius), tint, faded);
                else
                    Fill(wedge[..count], tint);
            }
        }
    }

    /// <summary>
    /// Draws a guilloche: the interlaced rosette engraved on banknotes and watch dials.
    /// </summary>
    /// <remarks>
    /// Drawn as a polyline rather than rendered into a cached texture, for the same reason as
    /// <see cref="Sunburst"/>: the curve is a few hundred points, and a curve stays a curve at any scale where a
    /// texture would have to be rebuilt.
    /// </remarks>
    /// <param name="centre">The centre of the rosette, in screen space.</param>
    /// <param name="radius">Its outer radius, in real pixels.</param>
    /// <param name="color">The line color.</param>
    /// <param name="style">The shape of the curve. When <see langword="null"/>, a single seven petal rosette.</param>
    public static void Guilloche(Vector2 centre, float radius, Vector4 color, GuillocheStyle? style = null)
    {
        style ??= DefaultGuillocheStyle;

        if (radius <= 0f || color.W <= 0f || style.Lobes < 2)
            return;

        var thickness = NoireUI.Scaled(style.Thickness);

        if (thickness <= 0f)
            return;

        using var draw = UiDraw.BeginMethod();

        var fixedSegments = style.Segments > 0 ? Math.Clamp(style.Segments, 16, MaxCurvePoints) : 0;

        var depth = Math.Clamp(style.Depth, 0f, 1f);
        var rings = Math.Max(1, style.Rings);

        // The outermost ring is the largest, so its count is an upper bound for every ring inside it.
        var widest = fixedSegments > 0 ? fixedSegments : GuillocheSegments(radius, style.Lobes);

        Span<Vector2> points = stackalloc Vector2[widest];

        for (var ring = 0; ring < rings; ring++)
        {
            var ringRadius = radius * (1f - (ring * style.RingSpacing));

            if (ringRadius <= 0f)
                break;

            var segments = fixedSegments > 0 ? fixedSegments : GuillocheSegments(ringRadius, style.Lobes);
            var path = points[..segments];

            var rotation = Radians(style.RotationTurns + (ring * style.RingRotationTurns));

            // Turned as a whole rather than by offsetting t, so a ring rotates instead of sliding along its own curve
            // and landing back where it started. Read once per ring: the angle does not vary along the curve, and
            // taking it per point spent two thirds of this loop's trigonometry recomputing one constant.
            var cos = MathF.Cos(rotation);
            var sin = MathF.Sin(rotation);

            // The hypotrochoid: a point offset from the centre of a small circle rolling inside a large one. The lobe
            // count is the ratio of the two radii, and the depth is how far out from that centre the point sits.
            var rolling = ringRadius / style.Lobes;
            var carrier = ringRadius - rolling;
            var offset = depth * rolling;
            var ratio = carrier / rolling;

            for (var step = 0; step < segments; step++)
            {
                var t = MathF.Tau * step / segments;

                var x = (carrier * MathF.Cos(t)) + (offset * MathF.Cos(ratio * t));
                var y = (carrier * MathF.Sin(t)) - (offset * MathF.Sin(ratio * t));

                path[step] = centre + new Vector2((x * cos) - (y * sin), (x * sin) + (y * cos));
            }

            Stroke(path, color, thickness);
        }
    }

    /// <summary>
    /// How long a curve's straight segments are allowed to be, in real pixels.
    /// </summary>
    /// <remarks>
    /// Short enough that a chord across a curve is not visible as a flat, long enough that a small rosette is not drawn
    /// with hundreds of points nobody can see. Lower it if a large pattern looks faceted.
    /// </remarks>
    private const float CurveSegmentPx = 3f;

    /// <summary>
    /// The fewest points a single lobe is ever drawn with, whatever the radius, so a small rosette still reads as
    /// having the petals it was asked for rather than as a polygon.
    /// </summary>
    private const int MinPointsPerLobe = 12;

    /// <summary>
    /// How many points a guilloche ring of a given radius is drawn with.
    /// </summary>
    /// <remarks>
    /// Taken from the radius rather than from the lobe count alone, which is what the count used to be. A count that
    /// ignores the radius spends the same hundreds of points on a rosette an inch across as on one filling the window:
    /// at a small radius those segments are a fraction of a pixel each, which costs a great deal and shows nothing.
    /// </remarks>
    /// <param name="radius">The ring's radius, in real pixels.</param>
    /// <param name="lobes">How many petals the rosette has.</param>
    /// <returns>The number of points to draw the ring with.</returns>
    internal static int GuillocheSegments(float radius, int lobes)
    {
        var byLength = (int)MathF.Ceiling(MathF.Tau * radius / CurveSegmentPx);
        var byLobes = lobes * MinPointsPerLobe;

        return Math.Clamp(Math.Max(byLength, byLobes), 32, MaxCurvePoints);
    }

    #endregion
}
