using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

// A convex fill cut into small cells: a gradient shows across it, with the half-pixel antialiased edge of a plain fill.
internal static class EffectTessellation
{
    private const int MaxCellsPerAxis = 48;
    private const int MaxPoints = 512;

    internal static void FillConvex(ImDrawListPtr drawList, ReadOnlySpan<Vector2> points, uint color, float cellSize, bool antiAlias)
    {
        var count = points.Length;

        if (count < 3 || count > MaxPoints)
            return;

        Span<Vector2> inner = stackalloc Vector2[count];
        Span<Vector2> normals = stackalloc Vector2[count];

        OutwardNormals(points, normals);

        for (var i = 0; i < count; i++)
            inner[i] = antiAlias ? points[i] - (normals[i] * 0.5f) : points[i];

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var point in inner)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        var size = max - min;

        if (size.X <= 0f || size.Y <= 0f)
            return;

        var columns = Math.Clamp((int)MathF.Ceiling(size.X / cellSize), 1, MaxCellsPerAxis);
        var rows = Math.Clamp((int)MathF.Ceiling(size.Y / cellSize), 1, MaxCellsPerAxis);
        var cell = size / new Vector2(columns, rows);

        Span<Vector2> a = stackalloc Vector2[count + 8];
        Span<Vector2> b = stackalloc Vector2[count + 8];

        var white = ImGui.GetFontTexUvWhitePixel();

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cellMin = min + (cell * new Vector2(column, row));
                var cellMax = column == columns - 1 && row == rows - 1 ? max : cellMin + cell;

                if (column == columns - 1)
                    cellMax.X = max.X;

                if (row == rows - 1)
                    cellMax.Y = max.Y;

                inner.CopyTo(a);
                var clipped = count;

                clipped = Clip(a, clipped, b, 0, cellMin.X, true);
                clipped = Clip(b, clipped, a, 0, cellMax.X, false);
                clipped = Clip(a, clipped, b, 1, cellMin.Y, true);
                clipped = Clip(b, clipped, a, 1, cellMax.Y, false);

                if (clipped >= 3)
                    WriteFan(drawList, a[..clipped], white, color);
            }
        }

        if (antiAlias)
            WriteFringe(drawList, inner, normals, white, color);
    }

    // Keeps the part of a polygon on one side of an axis-aligned line: at or above 'value' when 'keepAbove', else at or below.
    private static int Clip(ReadOnlySpan<Vector2> input, int count, Span<Vector2> output, int axis, float value, bool keepAbove)
    {
        var written = 0;

        for (var i = 0; i < count; i++)
        {
            var current = input[i];
            var next = input[(i + 1) % count];
            var currentIn = Inside(current, axis, value, keepAbove);
            var nextIn = Inside(next, axis, value, keepAbove);

            if (currentIn)
                output[written++] = current;

            if (currentIn != nextIn)
            {
                var from = axis == 0 ? current.X : current.Y;
                var to = axis == 0 ? next.X : next.Y;
                var f = (value - from) / (to - from);
                output[written++] = Vector2.Lerp(current, next, f);
            }
        }

        return written;
    }

    private static bool Inside(Vector2 point, int axis, float value, bool keepAbove)
    {
        var coordinate = axis == 0 ? point.X : point.Y;
        return keepAbove ? coordinate >= value : coordinate <= value;
    }

    private static void WriteFan(ImDrawListPtr drawList, ReadOnlySpan<Vector2> polygon, Vector2 white, uint color)
    {
        var vertexCount = polygon.Length;
        var indexCount = (vertexCount - 2) * 3;

        drawList.PrimReserve(indexCount, vertexCount);

        // Read after the reservation, never before: reserving can roll the index offset over.
        var baseVertex = drawList.VtxCurrentIdx;
        var vertices = drawList.VtxBuffer.AsSpan()[^vertexCount..];
        var indices = drawList.IdxBuffer.AsSpan()[^indexCount..];

        for (var i = 0; i < vertexCount; i++)
            vertices[i] = new ImDrawVert { Pos = polygon[i], Uv = white, Col = color };

        for (var i = 0; i < vertexCount - 2; i++)
        {
            indices[i * 3] = (ushort)baseVertex;
            indices[(i * 3) + 1] = (ushort)(baseVertex + i + 1);
            indices[(i * 3) + 2] = (ushort)(baseVertex + i + 2);
        }

        NoireShapes.AdvancePrimWrite(drawList, vertexCount, indexCount);
    }

    // A one pixel band from the inner outline out to the true edge and beyond, fading to nothing: ImGui's antialiasing.
    private static void WriteFringe(ImDrawListPtr drawList, ReadOnlySpan<Vector2> inner, ReadOnlySpan<Vector2> normals, Vector2 white, uint color)
    {
        var count = inner.Length;
        var vertexCount = count * 2;
        var indexCount = count * 6;
        var transparent = color & 0x00FFFFFFu;

        drawList.PrimReserve(indexCount, vertexCount);

        var baseVertex = drawList.VtxCurrentIdx;
        var vertices = drawList.VtxBuffer.AsSpan()[^vertexCount..];
        var indices = drawList.IdxBuffer.AsSpan()[^indexCount..];

        for (var i = 0; i < count; i++)
        {
            vertices[i * 2] = new ImDrawVert { Pos = inner[i], Uv = white, Col = color };
            vertices[(i * 2) + 1] = new ImDrawVert { Pos = inner[i] + normals[i], Uv = white, Col = transparent };
        }

        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            var first = (ushort)(baseVertex + (i * 2));
            var second = (ushort)(baseVertex + (next * 2));

            indices[i * 6] = first;
            indices[(i * 6) + 1] = (ushort)(first + 1);
            indices[(i * 6) + 2] = (ushort)(second + 1);
            indices[(i * 6) + 3] = first;
            indices[(i * 6) + 4] = (ushort)(second + 1);
            indices[(i * 6) + 5] = second;
        }

        NoireShapes.AdvancePrimWrite(drawList, vertexCount, indexCount);
    }

    // The normal at every corner, pointing away from the shape and long enough that moving a corner along it moves both
    // of its edges by one unit.
    private static void OutwardNormals(ReadOnlySpan<Vector2> points, Span<Vector2> normals)
    {
        var count = points.Length;
        var centroid = Vector2.Zero;

        foreach (var point in points)
            centroid += point;

        centroid /= count;

        for (var i = 0; i < count; i++)
        {
            var previous = EdgeNormal(points[(i + count - 1) % count], points[i], centroid);
            var next = EdgeNormal(points[i], points[(i + 1) % count], centroid);
            var average = (previous + next) * 0.5f;
            var lengthSquared = average.LengthSquared();

            normals[i] = lengthSquared < 0.000001f ? next : average / MathF.Max(lengthSquared, 0.25f);
        }
    }

    private static Vector2 EdgeNormal(Vector2 from, Vector2 to, Vector2 centroid)
    {
        var edge = to - from;
        var length = edge.Length();

        if (length < 0.000001f)
            return Vector2.Zero;

        var normal = new Vector2(edge.Y, -edge.X) / length;
        return Vector2.Dot(normal, ((from + to) * 0.5f) - centroid) < 0f ? -normal : normal;
    }
}
