using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The shapes ImGui lacks: angled gradients, notched and rounded plates, bevels, glows, framed ticks, arcs and
/// pattern fills. Coordinates are real screen pixels; defaults are logical and scaled.
/// </summary>
[NoireFacade]
public static partial class NoireShapes
{
    /// <summary>The most points <see cref="RectPath"/> can ever write.</summary>
    public const int MaxRectPathPoints = 128;

    private static ImDrawListPtr target = ImDrawListPtr.Null;

    /// <summary>Whether the shapes drawn here are antialiased.</summary>
    public static bool AntiAlias { get; set; } = true;

    private static ImDrawListFlags PushAntiAlias(ImDrawListPtr drawList)
    {
        var previous = drawList.Flags;
        const ImDrawListFlags wanted = ImDrawListFlags.AntiAliasedFill | ImDrawListFlags.AntiAliasedLines;

        drawList.Flags = AntiAlias ? previous | wanted : previous & ~wanted;

        return previous;
    }

    /// <summary>
    /// The draw list everything here paints into: the one <see cref="On(ImDrawListPtr, Action)"/> is redirecting to,
    /// and the current window's otherwise.
    /// </summary>
    public static ImDrawListPtr DrawList
    {
        get
        {
            if (!target.IsNull)
                return target;

            return UiDraw.Available ? UiContext.WindowDrawList : ImDrawListPtr.Null;
        }
    }

    #region Target

    /// <summary>Runs a block of drawing against another draw list. Nests, restoring the previous target even on a throw.</summary>
    /// <param name="drawList">The list to paint into.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="body"/> is null.</exception>
    public static void On(ImDrawListPtr drawList, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        On(drawList, body, static b => b());
    }

    /// <summary>Runs a block of drawing against another draw list. Nests, restoring the previous target even on a throw.</summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="drawList">The list to paint into.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to run.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="body"/> is null.</exception>
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

    /// <summary>Shades everything a block drew along a line. Color is replaced and <b>alpha is multiplied</b>. Nests.</summary>
    /// <param name="from">Where <paramref name="fromColor"/> is at full strength, in screen space.</param>
    /// <param name="to">Where <paramref name="toColor"/> is at full strength, in screen space.</param>
    /// <param name="fromColor">The color at <paramref name="from"/>.</param>
    /// <param name="toColor">The color at <paramref name="to"/>.</param>
    /// <param name="body">The drawing to shade.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="body"/> is null.</exception>
    public static void Gradient(Vector2 from, Vector2 to, Vector4 fromColor, Vector4 toColor, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Gradient(from, to, fromColor, toColor, body, static b => b());
    }

    /// <summary>Shades everything a block drew along a line. Color is replaced and <b>alpha is multiplied</b>. Nests.</summary>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="from">Where <paramref name="fromColor"/> is at full strength, in screen space.</param>
    /// <param name="to">Where <paramref name="toColor"/> is at full strength, in screen space.</param>
    /// <param name="fromColor">The color at <paramref name="from"/>.</param>
    /// <param name="toColor">The color at <paramref name="to"/>.</param>
    /// <param name="state">Passed to <paramref name="body"/>.</param>
    /// <param name="body">The drawing to shade.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="body"/> is null.</exception>
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

    /// <summary>Runs a block of drawing and shades it along one of a rectangle's own axes.</summary>
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

    /// <summary>Runs a block of drawing and shades it along one of a rectangle's own axes.</summary>
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

    private static (Vector2 From, Vector2 To) AxisPoints(Vector2 min, Vector2 max, GradientAxis axis) => axis switch
    {
        GradientAxis.Horizontal => (min, new Vector2(max.X, min.Y)),
        GradientAxis.Diagonal => (min, max),
        GradientAxis.Antidiagonal => (new Vector2(min.X, max.Y), new Vector2(max.X, min.Y)),
        _ => (min, new Vector2(min.X, max.Y)),
    };

    // Vectorised: a replayed mesh has thousands of indices.
    internal static unsafe void RebaseIndices(ushort* from, ushort* to, int count, uint baseVertex)
    {
        var i = 0;

        if (System.Numerics.Vector.IsHardwareAccelerated && count >= System.Numerics.Vector<ushort>.Count)
        {
            var width = System.Numerics.Vector<ushort>.Count;
            var offset = new System.Numerics.Vector<ushort>((ushort)baseVertex);

            for (; i <= count - width; i += width)
            {
                var chunk = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<System.Numerics.Vector<ushort>>(from + i);
                System.Runtime.CompilerServices.Unsafe.WriteUnaligned(to + i, chunk + offset);
            }
        }

        for (; i < count; i++)
            to[i] = (ushort)(from[i] + baseVertex);
    }

    internal static unsafe void AdvancePrimWrite(ImDrawListPtr drawList, int vertexCount, int indexCount)
    {
        var native = drawList.Handle;

        native->VtxCurrentIdx += (uint)vertexCount;
        native->VtxWritePtr += vertexCount;
        native->IdxWritePtr += indexCount;
    }

    // Full strength at innerRadius, gone at outerRadius.
    private static void ShadeRadial(ImDrawListPtr drawList, int start, int end, Vector2 centre, float innerRadius, float outerRadius)
    {
        if (end <= start)
            return;

        var span = outerRadius - innerRadius;

        if (span < 0.0001f)
            return;

        var vertices = drawList.VtxBuffer.AsSpan();

        if (end > vertices.Length)
            return;

        var inverseSpan = 1f / span;

        for (var i = start; i < end; i++)
        {
            ref var vertex = ref vertices[i];

            var position = Math.Clamp((Vector2.Distance(vertex.Pos, centre) - innerRadius) * inverseSpan, 0f, 1f);

            // Only the alpha byte is scaled: unpacking four channels per vertex costs for the same answer.
            var packed = vertex.Col;
            var faded = (uint)((((packed >> 24) & 0xFFu) * (1f - position)) + 0.5f);

            vertex.Col = (packed & 0x00FFFFFFu) | (faded << 24);
        }
    }

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

            // Only the existing alpha is read: it carries the antialiased edge and is multiplied, not replaced.
            var existing = ColorHelper.UintAlpha(vertex.Col);
            vertex.Col = ColorHelper.Vector4ToUint(tint with { W = existing * tint.W });
        }
    }

    #endregion

    #region Paths

    /// <summary>Writes a rectangle outline with cut corners, clockwise from the top left. A cut is clamped to half the shortest side.</summary>
    /// <param name="points">Receives the path. <see cref="MaxRectPathPoints"/> is always enough.</param>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="shape">How the corners are cut.</param>
    /// <param name="cornerSize">How deep the cut is, in real pixels.</param>
    /// <param name="corners">Which corners are cut.</param>
    /// <returns>How many points were written. Zero for an empty rectangle or a buffer too small.</returns>
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

            // Corner zero starts pointing left. Clockwise on screen is increasing angle: y grows downwards.
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

    /// <summary>Fills a convex path. Draw a concave shape as convex pieces.</summary>
    /// <param name="points">The path, in order.</param>
    /// <param name="color">The fill color.</param>
    public static unsafe void Fill(ReadOnlySpan<Vector2> points, Vector4 color)
    {
        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull || points.Length < 3 || color.W <= 0f)
            return;

        // A plain fill has vertices only at its outline: an open gradient would only show at the corners.
        if (NoireEffects.WantsTessellation(drawList, out var cellSize))
        {
            EffectTessellation.FillConvex(drawList, points, ColorHelper.Vector4ToUint(color), cellSize, AntiAlias);
            return;
        }

        var flags = PushAntiAlias(drawList);

        fixed (Vector2* first = points)
            drawList.AddConvexPolyFilled(first, points.Length, ColorHelper.Vector4ToUint(color));

        drawList.Flags = flags;
    }

    /// <summary>Fills between a left-to-right path and a horizontal line, for the area under a trace.</summary>
    /// <param name="points">The upper edge, in order.</param>
    /// <param name="baselineY">The screen y the area closes to.</param>
    /// <param name="color">The fill color.</param>
    public static unsafe void FillUnder(ReadOnlySpan<Vector2> points, float baselineY, Vector4 color)
    {
        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull || points.Length < 2 || color.W <= 0f)
            return;

        var vertexCount = points.Length * 2;
        var indexCount = (points.Length - 1) * 6;

        // The 16-bit index buffer limit. A trace this long has samples well under a pixel apart.
        if (vertexCount > ushort.MaxValue)
            return;

        drawList.PrimReserve(indexCount, vertexCount);

        // Read after the reservation, never before: reserving can roll the index offset over.
        var baseVertex = drawList.VtxCurrentIdx;
        var vertices = drawList.VtxBuffer.AsSpan()[^vertexCount..];
        var indices = drawList.IdxBuffer.AsSpan()[^indexCount..];

        var white = ImGui.GetFontTexUvWhitePixel();
        var packed = ColorHelper.Vector4ToUint(color);

        for (var point = 0; point < points.Length; point++)
        {
            var top = point * 2;

            vertices[top] = new ImDrawVert { Pos = points[point], Uv = white, Col = packed };
            vertices[top + 1] = new ImDrawVert { Pos = new Vector2(points[point].X, baselineY), Uv = white, Col = packed };
        }

        WriteBandIndices(indices, points.Length, (ushort)baseVertex);

        AdvancePrimWrite(drawList, vertexCount, indexCount);
    }

    // Vertices alternate upper, lower. The span holds 6 * (points - 1) indices.
    internal static int WriteBandIndices(Span<ushort> indices, int points, ushort baseVertex)
    {
        if (points < 2)
            return 0;

        var written = 0;

        for (var segment = 0; segment < points - 1; segment++)
        {
            var corner = (ushort)(baseVertex + (segment * 2));

            indices[written] = corner;
            indices[written + 1] = (ushort)(corner + 1);
            indices[written + 2] = (ushort)(corner + 2);
            indices[written + 3] = (ushort)(corner + 1);
            indices[written + 4] = (ushort)(corner + 3);
            indices[written + 5] = (ushort)(corner + 2);

            written += 6;
        }

        return written;
    }

    /// <summary>Strokes a path.</summary>
    /// <param name="points">The path, in order.</param>
    /// <param name="color">The line color.</param>
    /// <param name="thickness">The line thickness, in real pixels.</param>
    /// <param name="closed">Whether the last point joins back to the first.</param>
    public static void Stroke(ReadOnlySpan<Vector2> points, Vector4 color, float thickness = 1f, bool closed = true)
    {
        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull || points.Length < 2 || thickness <= 0f || color.W <= 0f)
            return;

        var flags = PushAntiAlias(drawList);
        drawList.AddPolyline(points, ColorHelper.Vector4ToUint(color), thickness, closed);
        drawList.Flags = flags;
    }

    /// <summary>
    /// Strokes a closed path with a light source: edges facing the light are lit, edges facing away fall into shadow.
    /// </summary>
    /// <param name="points">The closed path, in clockwise order.</param>
    /// <param name="light">The color of the edges facing the light.</param>
    /// <param name="shadow">The color of the edges facing away from it.</param>
    /// <param name="thickness">The bevel thickness, in real pixels.</param>
    /// <param name="direction">Where the light comes from. Defaults to above and to the left.</param>
    public static void Bevel(ReadOnlySpan<Vector2> points, Vector4 light, Vector4 shadow, float thickness = 1f, Vector2 direction = default)
    {
        using var draw = UiDraw.BeginMethod();
        var drawList = draw.List;

        if (drawList.IsNull || points.Length < 3 || thickness <= 0f)
            return;

        var toLight = direction == default ? new Vector2(-0.7071f, -0.7071f) : Vector2.Normalize(direction);

        // Inset half a thickness: an edge-aligned bevel halos on the lit side and smears on the shaded one.
        var inset = thickness * 0.5f;

        for (var i = 0; i < points.Length; i++)
        {
            var from = points[i];
            var to = points[(i + 1) % points.Length];
            var along = to - from;

            if (along.LengthSquared() < 0.0001f)
                continue;

            along = Vector2.Normalize(along);

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

    private static Vector2 Direction(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    #endregion
}
