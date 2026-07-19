using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Draws part of a rectangle's outline, for the widgets that show progress by tracing their own edge.
/// </summary>
internal static class UiOutline
{
    /// <summary>
    /// Traces the outline of a rectangle clockwise from its top left corner, stopping once
    /// <paramref name="fraction"/> of the perimeter has been drawn.
    /// </summary>
    /// <remarks>
    /// The path runs half a thickness inside the rectangle rather than along its edge. A line is drawn centred on its
    /// path, so an edge-aligned outline puts half of every side outside the rectangle: on a clipped surface the sides
    /// that fall on a clip boundary lose that half and the others keep it, leaving an outline that is visibly thinner
    /// on some sides than others, and on an unclipped one the whole outline bleeds over its neighbours instead.<br/>
    /// Each run that reaches a corner is extended by half a thickness so the corners close rather than leaving a notch
    /// the size of the line width.
    /// </remarks>
    /// <param name="drawList">The draw list to paint into.</param>
    /// <param name="min">The top left corner of the rectangle.</param>
    /// <param name="max">The bottom right corner of the rectangle.</param>
    /// <param name="color">The outline color, already packed.</param>
    /// <param name="thickness">The outline thickness in pixels.</param>
    /// <param name="fraction">How much of the perimeter to draw, from 0 to 1.</param>
    internal static void TraceClockwise(ImDrawListPtr drawList, Vector2 min, Vector2 max, uint color, float thickness, float fraction)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);

        if (fraction <= 0f || thickness <= 0f)
            return;

        // Snapped to whole pixels before anything else. A toast's slot lands on fractional coordinates (its height is
        // a measured value scaled by an animation), so the four sides would otherwise sit at four different sub-pixel
        // phases: antialiasing then spreads each one across a different pair of pixel rows and they come out visibly
        // different weights, the bottom and right reading thinner than the top. A hairline is the one thing that
        // cannot absorb a half-pixel offset.
        min = new Vector2(MathF.Round(min.X), MathF.Round(min.Y));
        max = new Vector2(MathF.Round(max.X), MathF.Round(max.Y));

        var inset = thickness * 0.5f;
        var width = max.X - min.X - thickness;
        var height = max.Y - min.Y - thickness;

        if (width <= 0f || height <= 0f)
            return;

        var topLeft = new Vector2(min.X + inset, min.Y + inset);
        var topRight = new Vector2(topLeft.X + width, topLeft.Y);
        var bottomRight = new Vector2(topRight.X, topLeft.Y + height);
        var bottomLeft = new Vector2(topLeft.X, bottomRight.Y);

        Span<Vector2> corners = [topLeft, topRight, bottomRight, bottomLeft];
        Span<float> lengths = [width, height, width, height];

        var remaining = (width + height) * 2f * fraction;

        for (var side = 0; side < 4 && remaining > 0f; side++)
        {
            var run = MathF.Min(remaining, lengths[side]);
            var from = corners[side];
            var along = Vector2.Normalize(corners[(side + 1) % 4] - from);

            var start = side > 0 ? from - (along * inset) : from;
            var end = from + (along * (run >= lengths[side] ? run + inset : run));

            drawList.AddLine(start, end, color, thickness);
            remaining -= run;
        }
    }
}
