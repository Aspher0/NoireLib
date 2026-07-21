using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The shapes a bespoke interface is built out of, and that ImGui's draw list does not have: gradients at any angle,
/// notched and rounded plates, beveled edges, glows, hairline frames with corner ticks, arcs, and pattern fills.
/// </summary>
/// <remarks>
/// Everything here paints into an ImGui draw list, and everything composes: the shapes are drawn by the same
/// <see cref="Fill"/>, <see cref="Stroke"/> and <see cref="Bevel"/> that are public, over paths the public
/// <see cref="RectPath"/> generates. A shape NoireUI does not ship is your own path handed to the same three calls.<br/>
/// Coordinates are screen space, in real pixels, because that is what a draw list takes and what
/// <c>ImGui.GetCursorScreenPos</c> and <c>GetItemRectMin</c> give you. The numbers NoireUI ships a default for
/// (a bevel depth, a tick length, a glow spread) are logical and scaled for you. See <see cref="NoireUI.Scale"/>.<br/>
/// This has nothing to do with the Draw3D renderer, which paints the game world through D3D11. The two share no code
/// and no concepts.
/// </remarks>
/// <example>
/// <code>
/// var min = ImGui.GetCursorScreenPos();
/// var max = min + new Vector2(320f, 90f) * NoireUI.Scale;
///
/// NoireShapes.Plate(min, max, new PlateStyle { CornerShape = CornerShape.Notched, CornerSize = 12f, BevelSize = 2f });
/// NoireShapes.Frame(min, max, new FrameStyle { TickLength = 14f });
/// </code>
/// </example>
[NoireFacade]
public static partial class NoireShapes
{
    /// <summary>
    /// The most points <see cref="RectPath"/> can ever write, so a caller can size a buffer once and stop thinking
    /// about it.
    /// </summary>
    public const int MaxRectPathPoints = 128;

    private static ImDrawListPtr target = ImDrawListPtr.Null;

    /// <summary>
    /// Whether the shapes drawn here are antialiased. On by default.
    /// </summary>
    /// <remarks>
    /// Antialiasing is a draw list flag rather than a per-call argument, so it is whatever the host last left it as.
    /// NoireUI sets it around its own drawing instead of inheriting it, because the difference between a shape that is
    /// smooth and one that is visibly stepped should not depend on a setting somewhere else in the process.<br/>
    /// Turn it off for the shapes here without touching anything else, if a plugin has deliberately traded
    /// antialiasing for fill rate.
    /// </remarks>
    public static bool AntiAlias { get; set; } = true;

    /// <summary>
    /// Forces a draw list's antialiasing to match <see cref="AntiAlias"/>, returning what it was so the caller can put
    /// it back.
    /// </summary>
    private static ImDrawListFlags PushAntiAlias(ImDrawListPtr drawList)
    {
        var previous = drawList.Flags;
        const ImDrawListFlags wanted = ImDrawListFlags.AntiAliasedFill | ImDrawListFlags.AntiAliasedLines;

        drawList.Flags = AntiAlias ? previous | wanted : previous & ~wanted;

        return previous;
    }

    /// <summary>
    /// The draw list everything here paints into: the one <see cref="On(ImDrawListPtr, Action)"/> is currently
    /// redirecting to, and the current window's otherwise.
    /// </summary>
    /// <remarks>
    /// Public so a block of drawing can mix these shapes with raw <c>ImDrawListPtr</c> calls and be sure both land in
    /// the same place, including inside an <see cref="On(ImDrawListPtr, Action)"/> scope where the current window's
    /// list is not the answer.
    /// </remarks>
    public static ImDrawListPtr DrawList
    {
        get
        {
            if (!target.IsNull)
                return target;

            return UiDraw.Available ? ImGui.GetWindowDrawList() : ImDrawListPtr.Null;
        }
    }

    #region Target

    /// <summary>
    /// Runs a block of drawing against a different draw list: the background or foreground list, or one belonging to
    /// another window.
    /// </summary>
    /// <remarks>
    /// Nests, and restores the previous target on the way out even if the body throws.
    /// </remarks>
    /// <param name="drawList">The list to paint into.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// // Behind every window, across the whole screen.
    /// NoireShapes.On(ImGui.GetBackgroundDrawList(), () => NoireShapes.Sunburst(centre, 400f, glow));
    /// </code>
    /// </example>
    public static void On(ImDrawListPtr drawList, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        On(drawList, body, static b => b());
    }

    /// <summary>
    /// Runs a block of drawing against a different draw list: the background or foreground list, or one belonging to
    /// another window.
    /// </summary>
    /// <remarks>
    /// Nests, and restores the previous target on the way out even if the body throws.
    /// </remarks>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="drawList">The list to paint into.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void On<TState>(ImDrawListPtr drawList, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var previous = target;
        target = drawList;

        try
        {
            UiScope.Run(nameof(NoireShapes), state, body);
        }
        finally
        {
            target = previous;
        }
    }

    #endregion

    #region Gradient

    /// <summary>
    /// Runs a block of drawing and shades everything it drew along a line, which is how any shape at all becomes a
    /// gradient: a rounded plate, a notched one, an arc, a run of text.
    /// </summary>
    /// <remarks>
    /// ImGui's own gradient is a single axis-aligned rectangle with no rounding, which is why this is a scope over
    /// arbitrary drawing rather than one more shape.<br/>
    /// Color is replaced and <b>alpha is multiplied</b> into whatever was drawn. That is deliberate: ImGui carries its
    /// antialiasing in the alpha of the outer vertices, so replacing alpha outright would give every shaded shape hard,
    /// jagged edges. The practical consequence is that a body drawn in white takes the gradient exactly, a body drawn
    /// in a color is tinted by it, and a gradient that fades to zero alpha fades the shape out.<br/>
    /// Nests. An inner gradient shades only what it drew, and the outer one then shades that again.
    /// </remarks>
    /// <param name="from">Where <paramref name="fromColor"/> is at full strength, in screen space.</param>
    /// <param name="to">Where <paramref name="toColor"/> is at full strength, in screen space.</param>
    /// <param name="fromColor">The color at <paramref name="from"/>.</param>
    /// <param name="toColor">The color at <paramref name="to"/>.</param>
    /// <param name="body">The drawing to shade.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// // A notched plate that fades out towards its bottom edge.
    /// NoireShapes.Gradient(min, new Vector2(min.X, max.Y), Vector4.One, new Vector4(1f, 1f, 1f, 0f), () =>
    ///     NoireShapes.Rect(min, max, accent, CornerShape.Notched, 12f));
    /// </code>
    /// </example>
    public static void Gradient(Vector2 from, Vector2 to, Vector4 fromColor, Vector4 toColor, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Gradient(from, to, fromColor, toColor, body, static b => b());
    }

    /// <summary>
    /// Runs a block of drawing and shades everything it drew along a line, which is how any shape at all becomes a
    /// gradient: a rounded plate, a notched one, an arc, a run of text.
    /// </summary>
    /// <remarks>
    /// Color is replaced and <b>alpha is multiplied</b> into whatever was drawn, so a body drawn in white takes the
    /// gradient exactly, a body drawn in a color is tinted by it, and a gradient fading to zero alpha fades the shape
    /// out. Alpha is multiplied rather than replaced because ImGui carries its antialiasing there.<br/>
    /// Nests. An inner gradient shades only what it drew, and the outer one then shades that again.
    /// </remarks>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="from">Where <paramref name="fromColor"/> is at full strength, in screen space.</param>
    /// <param name="to">Where <paramref name="toColor"/> is at full strength, in screen space.</param>
    /// <param name="fromColor">The color at <paramref name="from"/>.</param>
    /// <param name="toColor">The color at <paramref name="to"/>.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to shade.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Gradient<TState>(Vector2 from, Vector2 to, Vector4 fromColor, Vector4 toColor, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull)
        {
            UiScope.Run(nameof(NoireShapes), state, body);
            return;
        }

        var start = drawList.VtxBuffer.Size;

        UiScope.Run(nameof(NoireShapes), state, body);

        Shade(drawList, start, drawList.VtxBuffer.Size, from, to, fromColor, toColor);
    }

    /// <summary>
    /// Runs a block of drawing and shades it across a rectangle, for the ordinary case where the gradient runs along
    /// one of the rectangle's own axes.
    /// </summary>
    /// <param name="min">The top left corner the axis is measured across.</param>
    /// <param name="max">The bottom right corner the axis is measured across.</param>
    /// <param name="axis">Which way the gradient runs.</param>
    /// <param name="fromColor">The color at the start of the axis.</param>
    /// <param name="toColor">The color at the end of the axis.</param>
    /// <param name="body">The drawing to shade.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Gradient(Vector2 min, Vector2 max, GradientAxis axis, Vector4 fromColor, Vector4 toColor, Action body)
    {
        var (from, to) = AxisPoints(min, max, axis);
        Gradient(from, to, fromColor, toColor, body);
    }

    /// <summary>
    /// Runs a block of drawing and shades it across a rectangle, for the ordinary case where the gradient runs along
    /// one of the rectangle's own axes.
    /// </summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="min">The top left corner the axis is measured across.</param>
    /// <param name="max">The bottom right corner the axis is measured across.</param>
    /// <param name="axis">Which way the gradient runs.</param>
    /// <param name="fromColor">The color at the start of the axis.</param>
    /// <param name="toColor">The color at the end of the axis.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to shade.</param>
    public static void Gradient<TState>(Vector2 min, Vector2 max, GradientAxis axis, Vector4 fromColor, Vector4 toColor, TState state, Action<TState> body)
    {
        var (from, to) = AxisPoints(min, max, axis);
        Gradient(from, to, fromColor, toColor, state, body);
    }

    /// <summary>
    /// The two points a named axis runs between across a rectangle.
    /// </summary>
    private static (Vector2 From, Vector2 To) AxisPoints(Vector2 min, Vector2 max, GradientAxis axis) => axis switch
    {
        GradientAxis.Horizontal => (min, new Vector2(max.X, min.Y)),
        GradientAxis.Diagonal => (min, max),
        GradientAxis.Antidiagonal => (new Vector2(min.X, max.Y), new Vector2(max.X, min.Y)),
        _ => (min, new Vector2(min.X, max.Y)),
    };

    /// <summary>
    /// Recolors a run of vertices by where each one falls along a line.
    /// </summary>
    private static void Shade(ImDrawListPtr drawList, int start, int end, Vector2 from, Vector2 to, Vector4 fromColor, Vector4 toColor)
    {
        if (end <= start)
            return;

        var axis = to - from;
        var lengthSquared = axis.LengthSquared();

        if (lengthSquared < 0.0001f)
            return;

        var vertices = drawList.VtxBuffer.AsSpan();

        if (end > vertices.Length)
            return;

        var inverseLength = 1f / lengthSquared;

        for (var i = start; i < end; i++)
        {
            ref var vertex = ref vertices[i];

            var position = Math.Clamp(Vector2.Dot(vertex.Pos - from, axis) * inverseLength, 0f, 1f);
            var tint = Vector4.Lerp(fromColor, toColor, position);

            // Unpacked through ImGui's own converter rather than by shifting bytes, so the packed layout stays ImGui's
            // business. The existing alpha is what carries the antialiased edge, so it is multiplied, not replaced.
            var existing = ImGui.ColorConvertU32ToFloat4(vertex.Col);
            vertex.Col = ColorHelper.Vector4ToUint(tint with { W = existing.W * tint.W });
        }
    }

    #endregion

    #region Paths

    /// <summary>
    /// Writes the outline of a rectangle whose corners are cut, walking clockwise from the top left.
    /// </summary>
    /// <remarks>
    /// The primitive every rectangular shape here is drawn from, public so a shape NoireUI does not ship is still one
    /// call away: generate the path, adjust it, and hand it to <see cref="Fill"/>, <see cref="Stroke"/> or
    /// <see cref="Bevel"/>.<br/>
    /// A cut deeper than half the shortest side would meet the cut opposite it, so it is clamped there.
    /// </remarks>
    /// <param name="points">Receives the path. At least <see cref="MaxRectPathPoints"/> long is always enough.</param>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="shape">How the corners are cut.</param>
    /// <param name="cornerSize">How deep the cut is, in real pixels.</param>
    /// <param name="corners">Which corners are cut. The rest stay square.</param>
    /// <returns>How many points were written, or zero when the rectangle is empty or the buffer is too small.</returns>
    public static int RectPath(Span<Vector2> points, Vector2 min, Vector2 max, CornerShape shape, float cornerSize, RectCorners corners = RectCorners.All)
    {
        var width = max.X - min.X;
        var height = max.Y - min.Y;

        if (width <= 0f || height <= 0f)
            return 0;

        var size = MathF.Min(MathF.Max(cornerSize, 0f), MathF.Min(width, height) * 0.5f);

        if (shape == CornerShape.Square || size <= 0f || corners == RectCorners.None)
        {
            if (points.Length < 4)
                return 0;

            points[0] = min;
            points[1] = new Vector2(max.X, min.Y);
            points[2] = max;
            points[3] = new Vector2(min.X, max.Y);
            return 4;
        }

        Span<Vector2> squareCorners = [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
        Span<Vector2> arcCentres =
        [
            new Vector2(min.X + size, min.Y + size),
            new Vector2(max.X - size, min.Y + size),
            new Vector2(max.X - size, max.Y - size),
            new Vector2(min.X + size, max.Y - size),
        ];

        Span<RectCorners> flags = [RectCorners.TopLeft, RectCorners.TopRight, RectCorners.BottomRight, RectCorners.BottomLeft];

        // Enough to keep an arc smooth without spending points on a corner nobody can see the facets of.
        var segments = Math.Clamp((int)MathF.Ceiling(size * 0.4f) + 2, 3, 16);
        var count = 0;

        for (var corner = 0; corner < 4; corner++)
        {
            if ((corners & flags[corner]) == 0)
            {
                if (count >= points.Length)
                    return 0;

                points[count++] = squareCorners[corner];
                continue;
            }

            // Every corner sweeps a quarter turn, and corner zero starts pointing left, so the whole family is one
            // rotation apart. Walking clockwise on screen means increasing angle, because y grows downwards.
            var start = MathF.PI + (corner * MathF.PI * 0.5f);
            var centre = arcCentres[corner];

            if (shape == CornerShape.Notched)
            {
                if (count + 2 > points.Length)
                    return 0;

                points[count++] = centre + Direction(start) * size;
                points[count++] = centre + Direction(start + (MathF.PI * 0.5f)) * size;
                continue;
            }

            if (count + segments + 1 > points.Length)
                return 0;

            for (var step = 0; step <= segments; step++)
                points[count++] = centre + Direction(start + (MathF.PI * 0.5f * step / segments)) * size;
        }

        return Compact(points, count);
    }

    /// <summary>
    /// Removes points that repeat the one before them, and the last point when it repeats the first.
    /// </summary>
    /// <remarks>
    /// A cut deep enough to reach half a side collapses the centres of the two arcs meeting there onto one point, so
    /// those arcs share an endpoint. A pill does that on <b>both</b> sides: once mid-path where the right-hand arcs
    /// meet, and once at the wrap where the left-hand ones do. Every duplicate has to go, not only the wrap, because
    /// each one is a zero-length edge with no direction to build a join from, and a join built from nothing renders as
    /// a spike straight across the shape.<br/>
    /// Applied to every path rather than only to the pill case, since the same collapse happens to a fully notched
    /// rectangle and to any degenerate rectangle thin enough for two corners to meet.
    /// </remarks>
    /// <param name="points">The path to compact, in place.</param>
    /// <param name="count">How many points it holds.</param>
    /// <returns>How many points remain.</returns>
    private static int Compact(Span<Vector2> points, int count)
    {
        if (count < 2)
            return count;

        var write = 1;

        for (var read = 1; read < count; read++)
        {
            if (Vector2.DistanceSquared(points[write - 1], points[read]) < 0.0001f)
                continue;

            points[write++] = points[read];
        }

        if (write > 1 && Vector2.DistanceSquared(points[0], points[write - 1]) < 0.0001f)
            write--;

        return write;
    }

    /// <summary>
    /// Fills a convex path.
    /// </summary>
    /// <remarks>
    /// Convex is a real requirement, not a hint: a path that turns back on itself renders as overlapping fans rather
    /// than as the shape you drew. Every path <see cref="RectPath"/> produces is convex. A concave shape is drawn as
    /// two or more convex pieces.
    /// </remarks>
    /// <param name="points">The path, in order.</param>
    /// <param name="color">The fill color.</param>
    public static unsafe void Fill(ReadOnlySpan<Vector2> points, Vector4 color)
    {
        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull || points.Length < 3 || color.W <= 0f)
            return;

        var flags = PushAntiAlias(drawList);

        fixed (Vector2* first = points)
            drawList.AddConvexPolyFilled(first, points.Length, ColorHelper.Vector4ToUint(color));

        drawList.Flags = flags;
    }

    /// <summary>
    /// Strokes a path.
    /// </summary>
    /// <param name="points">The path, in order.</param>
    /// <param name="color">The line color.</param>
    /// <param name="thickness">The line thickness, in real pixels.</param>
    /// <param name="closed">Whether the last point joins back to the first.</param>
    public static unsafe void Stroke(ReadOnlySpan<Vector2> points, Vector4 color, float thickness = 1f, bool closed = true)
    {
        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull || points.Length < 2 || thickness <= 0f || color.W <= 0f)
            return;

        var flags = PushAntiAlias(drawList);

        fixed (Vector2* first = points)
            drawList.AddPolyline(first, points.Length, ColorHelper.Vector4ToUint(color), closed ? ImDrawFlags.Closed : ImDrawFlags.None, thickness);

        drawList.Flags = flags;
    }

    /// <summary>
    /// Strokes a closed path with a light source, so the edges facing the light are lit and the ones facing away fall
    /// into shadow. What makes a flat fill read as a raised plate.
    /// </summary>
    /// <remarks>
    /// Works on any closed path, which is the point: a notched plate bevels its diagonal cuts, and a rounded one turns
    /// smoothly from light to shadow around each corner, both from the same call.<br/>
    /// The path is expected to wind clockwise, which is what <see cref="RectPath"/> produces.
    /// </remarks>
    /// <param name="points">The closed path, in clockwise order.</param>
    /// <param name="light">The color of the edges facing the light.</param>
    /// <param name="shadow">The color of the edges facing away from it.</param>
    /// <param name="thickness">The bevel thickness, in real pixels.</param>
    /// <param name="direction">
    /// Where the light comes from. Defaults to above and to the left, which is where interfaces have lit things from
    /// since interfaces had raised things in them.
    /// </param>
    public static void Bevel(ReadOnlySpan<Vector2> points, Vector4 light, Vector4 shadow, float thickness = 1f, Vector2 direction = default)
    {
        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull || points.Length < 3 || thickness <= 0f)
            return;

        var toLight = direction == default ? new Vector2(-0.7071f, -0.7071f) : Vector2.Normalize(direction);

        // Half a thickness inside the path. An edge-aligned bevel puts half its width outside the shape, which reads as
        // a halo on the lit side and as a smear on the shaded one.
        var inset = thickness * 0.5f;

        for (var i = 0; i < points.Length; i++)
        {
            var from = points[i];
            var to = points[(i + 1) % points.Length];
            var along = to - from;

            if (along.LengthSquared() < 0.0001f)
                continue;

            along = Vector2.Normalize(along);

            // Clockwise winding with y downwards puts the outside of the shape to the left of the direction of travel.
            var outward = new Vector2(along.Y, -along.X);
            var facing = (Vector2.Dot(outward, toLight) + 1f) * 0.5f;

            var offset = outward * -inset;
            Line(drawList, from + offset, to + offset, Vector4.Lerp(shadow, light, facing), thickness);
        }
    }

    private static void Line(ImDrawListPtr drawList, Vector2 from, Vector2 to, Vector4 color, float thickness)
    {
        if (color.W > 0f)
            drawList.AddLine(from, to, ColorHelper.Vector4ToUint(color), thickness);
    }

    /// <summary>
    /// The unit vector at an angle, in the draw list's coordinate space.
    /// </summary>
    private static Vector2 Direction(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    #endregion
}
