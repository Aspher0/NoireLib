using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
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
    /// Drawn as geometry rather than rendered into a cached texture, so it stays sharp at every scale and takes no
    /// texture memory. The ray directions are worked out once for a given burst and reused: they are unit vectors,
    /// scaled by the radius and turned by the rotation on the way out, so one set serves a burst of any size at any
    /// angle.
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

        // A stated distance wins over the ratio: it exists for lining the hole up with something drawn at a fixed
        // radius, and scales with the UI rather than with the burst.
        var inner = style.InnerSize is { } innerSize
            ? Math.Clamp(NoireUI.Scaled(innerSize), 0f, 0.99f * radius)
            : Math.Clamp(style.InnerRatio, 0f, 0.99f) * radius;
        var slot = MathF.Tau / style.Rays;
        var half = slot * duty * 0.5f;
        var softness = Math.Clamp(style.Softness, 0f, 1f);

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

        // The rays are filled first and the fade is applied to all of them at once afterwards. Shading each ray as it
        // was filled meant entering the shading pass once per ray per layer, a hundred and eighty times for the burst
        // this window draws, and that call was three quarters of what a faded sunburst cost. The fade is the same
        // radial ramp for every ray, so it is one pass over everything the burst just produced.
        var drawList = draw.List;
        var shading = style.Fade && !drawList.IsNull;
        var firstVertex = shading ? drawList.VtxBuffer.Size : 0;

        // The ray directions come back unrotated, so the burst's own rotation is one pair of trigonometric calls for
        // the whole shape rather than three for every ray of every layer.
        var directions = ResolveSunburstDirections(style.Rays, half, softness, layers);
        var stride = SunburstStride(layers);
        var burst = Radians(style.RotationTurns);
        var burstCos = MathF.Cos(burst);
        var burstSin = MathF.Sin(burst);

        // A fading burst is written as raw geometry with the fade in the vertex colors, one reservation for the whole
        // shape. The general path below is a filled polygon per ray per layer, each an own call into ImGui, plus the
        // shading pass over every vertex produced: for the shipped default of sixty soft rays that was a hundred and
        // eighty calls a frame, and it was almost all of what a burst cost.
        // The results match: the fade is linear between the radii, which is exactly what interpolating full-alpha
        // inner vertices towards zero-alpha rim vertices draws. The rim carries no alpha, so the antialiased fringe
        // the polygon path would add there has nothing to smooth; the ray sides are softened by the layers, as they
        // are on the general path; and an inner edge, when the rays start off the centre, sits at the burst's faint
        // working alpha where the missing single pixel of fringe does not read.
        if (shading && style.Rays * layers * 4 <= 60000)
        {
            FillFadedBurst(drawList, centre, inner, tipped, radius, color, style.Rays, layers, layerAlphas, directions, stride, burstCos, burstSin);
            return;
        }

        for (var ray = 0; ray < style.Rays; ray++)
        {
            var origin = ray * stride;
            var along = Turn(directions[origin], burstCos, burstSin);

            for (var layer = 0; layer < layers; layer++)
            {
                var tint = color with { W = layerAlphas[layer] };
                var left = Turn(directions[origin + 1 + (layer * 2)], burstCos, burstSin);
                var right = Turn(directions[origin + 2 + (layer * 2)], burstCos, burstSin);

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

                // The layer's own alpha goes into the fill rather than into the fade, which is what lets one fade serve
                // every layer: each layer differs by its tint, and they all share the same ramp from centre to rim.
                Fill(wedge[..count], tint);
            }
        }

        // Scales what the fills already laid down rather than replacing it, so each layer keeps the alpha that makes it
        // a layer and the rays keep their colour.
        if (shading)
            ShadeRadial(drawList, firstVertex, drawList.VtxBuffer.Size, centre, inner, radius);
    }

    /// <summary>
    /// Writes a fading sunburst as one batch of raw geometry, with the radial fade carried in the vertex colors
    /// instead of applied in a pass afterwards.
    /// </summary>
    /// <remarks>
    /// A ray converging at the centre is a triangle; a ray starting off the centre is a quad whose inner corners
    /// carry the full layer alpha, which is where the radial ramp holds it at full strength.<br/>
    /// The vertex and index spans are the region the reservation just appended, so nothing here reaches past what it
    /// asked for. The base vertex id is read after the reservation, which is what keeps the indices right when the
    /// reservation itself rolled the buffer over to a new draw command.
    /// </remarks>
    /// <param name="drawList">The list to write into.</param>
    /// <param name="centre">Where the rays converge, in screen space.</param>
    /// <param name="inner">The radius the rays start at, in real pixels.</param>
    /// <param name="tipped">Whether the rays converge to the centre point.</param>
    /// <param name="radius">How far they reach, in real pixels.</param>
    /// <param name="color">The ray color at the centre.</param>
    /// <param name="rays">How many rays radiate from the centre.</param>
    /// <param name="layers">How many layers build one soft ray.</param>
    /// <param name="layerAlphas">The alpha each layer carries.</param>
    /// <param name="directions">The unit ray directions, as <see cref="ResolveSunburstDirections"/> lays them out.</param>
    /// <param name="stride">How many directions one ray occupies.</param>
    /// <param name="burstCos">The cosine of the burst's rotation.</param>
    /// <param name="burstSin">The sine of the burst's rotation.</param>
    private static void FillFadedBurst(ImDrawListPtr drawList, Vector2 centre, float inner, bool tipped, float radius, Vector4 color, int rays, int layers, ReadOnlySpan<float> layerAlphas, Vector2[] directions, int stride, float burstCos, float burstSin)
    {
        var wedges = rays * layers;
        var vertexCount = wedges * (tipped ? 3 : 4);
        var indexCount = wedges * (tipped ? 3 : 6);

        drawList.PrimReserve(indexCount, vertexCount);

        var baseVertex = drawList.VtxCurrentIdx;
        var vertices = drawList.VtxBuffer.AsSpan()[^vertexCount..];
        var indices = drawList.IdxBuffer.AsSpan()[^indexCount..];
        var white = ImGui.GetFontTexUvWhitePixel();

        // One packed color per layer for the inner corners and one shared rim color, rather than a conversion per wedge.
        Span<uint> innerColors = stackalloc uint[layers];

        for (var layer = 0; layer < layers; layer++)
            innerColors[layer] = ImGui.GetColorU32(color with { W = layerAlphas[layer] });

        var rimColor = ImGui.GetColorU32(color with { W = 0f });
        var vertex = 0;
        var index = 0;

        for (var ray = 0; ray < rays; ray++)
        {
            var origin = ray * stride;

            for (var layer = 0; layer < layers; layer++)
            {
                var left = Turn(directions[origin + 1 + (layer * 2)], burstCos, burstSin);
                var right = Turn(directions[origin + 2 + (layer * 2)], burstCos, burstSin);

                if (tipped)
                {
                    vertices[vertex] = new ImDrawVert { Pos = centre, Uv = white, Col = innerColors[layer] };
                    vertices[vertex + 1] = new ImDrawVert { Pos = centre + (right * radius), Uv = white, Col = rimColor };
                    vertices[vertex + 2] = new ImDrawVert { Pos = centre + (left * radius), Uv = white, Col = rimColor };

                    indices[index] = (ushort)(baseVertex + vertex);
                    indices[index + 1] = (ushort)(baseVertex + vertex + 1);
                    indices[index + 2] = (ushort)(baseVertex + vertex + 2);

                    vertex += 3;
                    index += 3;
                    continue;
                }

                vertices[vertex] = new ImDrawVert { Pos = centre + (left * inner), Uv = white, Col = innerColors[layer] };
                vertices[vertex + 1] = new ImDrawVert { Pos = centre + (right * inner), Uv = white, Col = innerColors[layer] };
                vertices[vertex + 2] = new ImDrawVert { Pos = centre + (right * radius), Uv = white, Col = rimColor };
                vertices[vertex + 3] = new ImDrawVert { Pos = centre + (left * radius), Uv = white, Col = rimColor };

                indices[index] = (ushort)(baseVertex + vertex);
                indices[index + 1] = (ushort)(baseVertex + vertex + 1);
                indices[index + 2] = (ushort)(baseVertex + vertex + 2);
                indices[index + 3] = (ushort)(baseVertex + vertex);
                indices[index + 4] = (ushort)(baseVertex + vertex + 2);
                indices[index + 5] = (ushort)(baseVertex + vertex + 3);

                vertex += 4;
                index += 6;
            }
        }

        AdvancePrimWrite(drawList, vertexCount, indexCount);
    }

    /// <summary>
    /// Draws a guilloche: the interlaced rosette engraved on banknotes and watch dials.
    /// </summary>
    /// <remarks>
    /// Drawn as a polyline rather than rendered into a cached texture, for the same reason as <see cref="Sunburst"/>:
    /// a curve stays a curve at any scale where a texture would have to be rebuilt. The curve itself is worked out
    /// once at radius one and then scaled, turned and placed on the way out, so a rosette that sits there, animates its
    /// size or rotates is tessellated once rather than on every frame.
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

            // The curve comes back at radius one and unrotated, so what is left per point is a multiply, a rotation and
            // a translation. The trigonometry that used to be here, four calls for every one of a few hundred points on
            // every ring on every frame, happens once for a given rosette and never again.
            var unit = ResolveGuillochePath(style.Lobes, depth, segments);

            for (var step = 0; step < segments; step++)
            {
                var x = unit[step].X * ringRadius;
                var y = unit[step].Y * ringRadius;

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

    /// <summary>
    /// What a sunburst's ray directions are decided by, and nothing else.
    /// </summary>
    /// <remarks>
    /// Neither the radius nor the rotation is here. The rays are unit directions scaled on the way out, and the burst
    /// is turned as a whole on the way out too, so one entry serves a sunburst of any size at any angle. That is what
    /// lets a rotating burst, which is the reason one is usually drawn, hit the cache on every frame instead of missing
    /// on all of them.
    /// </remarks>
    /// <param name="Rays">How many rays radiate from the centre.</param>
    /// <param name="HalfWidth">Half the angular width of a ray, in radians.</param>
    /// <param name="Softness">How much the layers narrow inwards.</param>
    /// <param name="Layers">How many layers build one soft ray.</param>
    private readonly record struct SunburstKey(int Rays, float HalfWidth, float Softness, int Layers);

    /// <summary>
    /// Sunburst ray directions already worked out, so the same burst is not re-tessellated every frame.
    /// </summary>
    private static readonly HotPathCache<SunburstKey, Vector2[]> SunburstCache = new();

    /// <summary>
    /// How many directions one ray needs: the one down its middle, and an edge pair for each layer.
    /// </summary>
    private static int SunburstStride(int layers) => 1 + (layers * 2);

    /// <summary>
    /// Turns a unit direction by an angle already reduced to its cosine and sine.
    /// </summary>
    private static Vector2 Turn(Vector2 direction, float cos, float sin)
        => new((direction.X * cos) - (direction.Y * sin), (direction.X * sin) + (direction.Y * cos));

    /// <summary>
    /// Writes the unrotated unit directions for every ray of a sunburst: down the middle of each ray, then the two
    /// edges of each of its layers.
    /// </summary>
    /// <remarks>
    /// Pure, so the layout can be asserted on without an ImGui context.
    /// </remarks>
    /// <param name="directions">Receives the directions. Must be at least rays by stride long.</param>
    /// <param name="rays">How many rays radiate from the centre.</param>
    /// <param name="halfWidth">Half the angular width of a ray, in radians.</param>
    /// <param name="softness">How much the layers narrow inwards.</param>
    /// <param name="layers">How many layers build one soft ray.</param>
    /// <returns>How many directions were written.</returns>
    internal static int SunburstDirections(Span<Vector2> directions, int rays, float halfWidth, float softness, int layers)
    {
        var slot = MathF.Tau / rays;
        var stride = SunburstStride(layers);

        for (var ray = 0; ray < rays; ray++)
        {
            var angle = slot * ray;
            var origin = ray * stride;

            directions[origin] = Direction(angle);

            for (var layer = 0; layer < layers; layer++)
            {
                var width = layers == 1 ? halfWidth : halfWidth * (1f - (softness * layer / (layers - 1)));

                directions[origin + 1 + (layer * 2)] = Direction(angle - width);
                directions[origin + 2 + (layer * 2)] = Direction(angle + width);
            }
        }

        return rays * stride;
    }

    /// <summary>
    /// The unit ray directions for a burst, from the cache when they are already there and computed into it when not.
    /// </summary>
    private static Vector2[] ResolveSunburstDirections(int rays, float halfWidth, float softness, int layers)
    {
        var key = new SunburstKey(rays, halfWidth, softness, layers);

        if (SunburstCache.TryGet(key, out var cached))
            return cached;

        var directions = new Vector2[rays * SunburstStride(layers)];
        SunburstDirections(directions, rays, halfWidth, softness, layers);
        SunburstCache.Set(key, directions);

        return directions;
    }

    /// <summary>
    /// What a guilloche ring's shape is decided by, and nothing else.
    /// </summary>
    /// <remarks>
    /// The radius is deliberately absent. A hypotrochoid is exactly proportional to it, so one curve serves every size
    /// of the same rosette and an animated radius keeps hitting the same entry instead of filling the cache with a
    /// curve per frame. Rotation is absent for the same reason: it is applied on the way out.
    /// </remarks>
    /// <param name="Lobes">How many petals.</param>
    /// <param name="Depth">How far the tracing point sits from the rolling circle's centre.</param>
    /// <param name="Segments">How many points the ring is drawn with.</param>
    private readonly record struct GuillocheKey(int Lobes, float Depth, int Segments);

    /// <summary>
    /// Guilloche rings already worked out, so the same rosette is not re-tessellated every frame.
    /// </summary>
    /// <remarks>
    /// A rosette is the most expensive thing this file draws: hundreds of points, four trigonometric calls each, on
    /// every frame it is on screen. The curve itself never changes while the ornament sits there, so it is computed
    /// once and the points are resubmitted.<br/>
    /// Not a texture, and it could not be: ImGui builds vertex buffers the host renders at the end of the frame and
    /// there is no render-to-texture to capture them with. Caching the geometry instead also survives tinting and
    /// rotation with no invalidation at all, which a cache of pixels would not.
    /// </remarks>
    private static readonly HotPathCache<GuillocheKey, Vector2[]> GuillocheCache = new();

    /// <summary>
    /// Writes one guilloche ring of radius one, centred on the origin and unrotated.
    /// </summary>
    /// <remarks>
    /// The hypotrochoid: a point offset from the centre of a small circle rolling inside a large one. The lobe count is
    /// the ratio of the two radii, and the depth is how far out from that centre the point sits.<br/>
    /// Pure, and kept that way so the curve can be asserted on without an ImGui context, the way the arc and diamond
    /// paths already are.
    /// </remarks>
    /// <param name="points">Receives the curve. Must be at least <paramref name="segments"/> long.</param>
    /// <param name="lobes">How many petals the rosette has.</param>
    /// <param name="depth">How pronounced the petals are, from 0 to 1.</param>
    /// <param name="segments">How many points to write.</param>
    /// <returns>How many points were written.</returns>
    internal static int GuillochePath(Span<Vector2> points, int lobes, float depth, int segments)
    {
        var rolling = 1f / lobes;
        var carrier = 1f - rolling;
        var offset = depth * rolling;
        var ratio = carrier / rolling;

        for (var step = 0; step < segments; step++)
        {
            var t = MathF.Tau * step / segments;

            points[step] = new Vector2(
                (carrier * MathF.Cos(t)) + (offset * MathF.Cos(ratio * t)),
                (carrier * MathF.Sin(t)) - (offset * MathF.Sin(ratio * t)));
        }

        return segments;
    }

    /// <summary>
    /// The unit ring for a shape, from the cache when it is already there and computed into it when it is not.
    /// </summary>
    private static Vector2[] ResolveGuillochePath(int lobes, float depth, int segments)
    {
        var key = new GuillocheKey(lobes, depth, segments);

        if (GuillocheCache.TryGet(key, out var cached))
            return cached;

        var path = new Vector2[segments];
        GuillochePath(path, lobes, depth, segments);
        GuillocheCache.Set(key, path);

        return path;
    }

    #endregion
}
