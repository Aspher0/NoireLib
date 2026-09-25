using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>Span overloads of the draw list calls the ImGui binding only takes a pointer for.</summary>
public static class ImDrawListExtensions
{
    /// <summary>Strokes a path.</summary>
    /// <param name="drawList">The list to draw into.</param>
    /// <param name="points">The path, in order.</param>
    /// <param name="color">The packed line color.</param>
    /// <param name="thickness">The line thickness, in real pixels.</param>
    /// <param name="closed">Whether the last point joins back to the first.</param>
    public static unsafe void AddPolyline(this ImDrawListPtr drawList, ReadOnlySpan<Vector2> points, uint color, float thickness = 1f, bool closed = false)
    {
        if (drawList.IsNull || points.Length < 2)
            return;

        fixed (Vector2* first = points)
            drawList.AddPolyline(first, points.Length, color, closed ? ImDrawFlags.Closed : ImDrawFlags.None, thickness);
    }
}
