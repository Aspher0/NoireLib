using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Effects that need more than one shape: keeping drawing inside a box, and light travelling along a line.
/// </summary>
public static partial class NoireShapes
{
    /// <summary>
    /// Runs a body with everything it draws clipped to a rectangle.
    /// </summary>
    /// <remarks>
    /// A painted background is drawn from its centre outwards and has no idea where the block holding it ends: a
    /// sunburst reaching the corners of a masthead reaches just as far past it, over whatever comes next. Clipping is
    /// what makes a painted panel a panel rather than a wash across the page.<br/>
    /// A scope rather than a parameter on every shape, for the same reason the gradient is: one call contains a whole
    /// composition, however many shapes it turns out to be made of.
    /// </remarks>
    /// <param name="min">The top left of the box to keep drawing inside, in screen space.</param>
    /// <param name="max">The bottom right.</param>
    /// <param name="body">The drawing to contain.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body"/> is <see langword="null"/>.</exception>
    public static void Clipped(Vector2 min, Vector2 max, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Clipped(min, max, body, static b => b());
    }

    /// <summary>
    /// Runs a body with everything it draws clipped to a rectangle.
    /// </summary>
    /// <remarks>
    /// A painted background is drawn from its centre outwards and has no idea where the block holding it ends: a
    /// sunburst reaching the corners of a masthead reaches just as far past it, over whatever comes next. Clipping is
    /// what makes a painted panel a panel rather than a wash across the page.
    /// </remarks>
    /// <typeparam name="TState">The type carried into the body.</typeparam>
    /// <param name="min">The top left of the box to keep drawing inside, in screen space.</param>
    /// <param name="max">The bottom right.</param>
    /// <param name="state">Passed to <paramref name="body"/>, so the body can stay a static lambda.</param>
    /// <param name="body">The drawing to contain.</param>
    public static void Clipped<TState>(Vector2 min, Vector2 max, TState state, Action<TState> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Intersected with what is already in force rather than replacing it, so a clipped block inside a scrolling
        // region cannot draw its way back out of the region.
        ImGui.PushClipRect(min, max, true);

        try
        {
            body(state);
        }
        finally
        {
            ImGui.PopClipRect();
        }
    }

    /// <summary>
    /// Draws a line with a bright band travelling along it.
    /// </summary>
    /// <remarks>
    /// The mark that makes a masthead rule read as lit rather than drawn. It cannot be a
    /// <see cref="Gradient(Vector2, Vector2, Vector4, Vector4, Action)"/>: that ramps between two colors across the
    /// whole span, and this is three stops with the bright one somewhere in the middle and moving. So the line is drawn
    /// as segments whose alpha is a function of how near each one is to the band.<br/>
    /// The band runs off both ends rather than bouncing, which is why <paramref name="phase"/> is taken over a range
    /// wider than the line: a highlight that reverses reads as a scanner, and one that wraps mid-line flickers.
    /// </remarks>
    /// <param name="from">Where the line starts, in screen space.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="color">The line's own color.</param>
    /// <param name="highlight">The color at the centre of the band.</param>
    /// <param name="phase">Where the band sits, from 0 (just before the start) to 1 (just past the end).</param>
    /// <param name="width">How much of the line the band covers, from 0 to 1.</param>
    /// <param name="thickness">How thick the line is, in real pixels.</param>
    public static void SweepLine(Vector2 from, Vector2 to, Vector4 color, Vector4 highlight, float phase, float width = 0.16f, float thickness = 1f)
    {
        var span = to - from;
        var length = span.Length();

        if (length <= 1f || thickness <= 0f)
            return;

        var band = Math.Clamp(width, 0.01f, 1f);
        var half = thickness * 0.5f;

        // Honoured here rather than left to the caller, the way the animation helpers do it. A travelling highlight is
        // motion whoever asked for it, and a reader who has turned motion off should not have to know which of the
        // things on screen happens to be drawn by a shape helper rather than by an animation one.
        if (NoireUI.ReducedMotion)
        {
            Rect(
                new Vector2(MathF.Min(from.X, to.X), MathF.Min(from.Y, to.Y) - half),
                new Vector2(MathF.Max(from.X, to.X), MathF.Max(from.Y, to.Y) + half),
                color);

            return;
        }

        // Taken over a range wider than the line so the band enters and leaves rather than appearing at one end.
        var centre = (phase * (1f + (band * 2f))) - band;

        // The line is one rectangle and the band is two, which is the whole of why this is not drawn as a run of
        // segments. Segments have to meet somewhere, and two translucent rectangles meeting composite twice over the
        // pixels they share: the seam comes out darker than either, so an animated line reads as a row of dents
        // travelling along it. Nothing here overlaps anything else of its own colour.
        var lineMin = new Vector2(MathF.Min(from.X, to.X), MathF.Min(from.Y, to.Y) - half);
        var lineMax = new Vector2(MathF.Max(from.X, to.X), MathF.Max(from.Y, to.Y) + half);

        Rect(lineMin, lineMax, color);

        var bandFrom = Math.Clamp(centre - band, 0f, 1f);
        var bandTo = Math.Clamp(centre + band, 0f, 1f);

        if (bandTo - bandFrom <= 0.0001f)
            return;

        var peak = Math.Clamp(centre, 0f, 1f);
        var lit = highlight;
        var clear = highlight with { W = 0f };

        // Antialiasing off for the band, and this is the whole reason the seam behaves. The two halves meet along one
        // edge; with antialiasing on, each contributes a partly transparent pixel there and the two composite into a
        // bright point sitting in the middle of the sweep. Nothing here is diagonal or curved, so there is no edge that
        // wanted smoothing in the first place.
        var antiAlias = AntiAlias;
        AntiAlias = false;

        try
        {
            // Ramped up to the middle of the band and back down, as two gradients meeting exactly at the peak. A single
            // gradient cannot do it: it has two stops and this needs three, with the bright one in the middle.
            DrawBandHalf(from, span, lineMin.Y, lineMax.Y, bandFrom, peak, clear, lit);
            DrawBandHalf(from, span, lineMin.Y, lineMax.Y, peak, bandTo, lit, clear);
        }
        finally
        {
            AntiAlias = antiAlias;
        }
    }

    /// <summary>
    /// Draws one side of a sweep's band, ramping between two alphas along the line.
    /// </summary>
    private static void DrawBandHalf(Vector2 from, Vector2 span, float top, float bottom, float startAt, float endAt, Vector4 startColor, Vector4 endColor)
    {
        if (endAt - startAt <= 0.0001f)
            return;

        var start = from + (span * startAt);
        var end = from + (span * endAt);

        // Snapped, so the two halves share an exact pixel boundary. A fractional one leaves them overlapping a column
        // by a fraction, which composites twice and shows as a mark travelling along the line.
        var min = new Vector2(MathF.Round(MathF.Min(start.X, end.X)), top);
        var max = new Vector2(MathF.Round(MathF.Max(start.X, end.X)), bottom);

        if (max.X - min.X < 1f)
            return;

        Gradient(min, max, GradientAxis.Horizontal, startColor, endColor, (min, max), static s => Rect(s.min, s.max, Vector4.One));
    }

    /// <summary>
    /// Draws a square stood on its corner, the mark a deco interface is built from.
    /// </summary>
    /// <remarks>
    /// Shipped because it was being written out by hand at every call site, and a hand-written one is four points that
    /// have to be in clockwise order for <see cref="Fill"/> and <see cref="GlowPath"/> to behave.
    /// </remarks>
    /// <param name="centre">The middle of the diamond, in screen space.</param>
    /// <param name="radius">How far each point sits from the middle, in real pixels.</param>
    /// <param name="points">Receives the four corners, clockwise from the top.</param>
    /// <returns>How many points were written, which is always four.</returns>
    public static int DiamondPath(Vector2 centre, float radius, Span<Vector2> points)
    {
        if (points.Length < 4)
            return 0;

        points[0] = new Vector2(centre.X, centre.Y - radius);
        points[1] = new Vector2(centre.X + radius, centre.Y);
        points[2] = new Vector2(centre.X, centre.Y + radius);
        points[3] = new Vector2(centre.X - radius, centre.Y);

        return 4;
    }

    /// <summary>
    /// Fills a diamond, optionally lit.
    /// </summary>
    /// <param name="centre">The middle of the diamond, in screen space.</param>
    /// <param name="radius">How far each point sits from the middle, in real pixels.</param>
    /// <param name="color">The fill color.</param>
    /// <param name="glow">The color of the halo around it. When <see langword="null"/>, there is none.</param>
    /// <param name="glowSpread">How far the halo reaches, in real pixels.</param>
    public static void Diamond(Vector2 centre, float radius, Vector4 color, Vector4? glow = null, float glowSpread = 6f)
    {
        Span<Vector2> path = stackalloc Vector2[4];
        var count = DiamondPath(centre, radius, path);

        if (count == 0)
            return;

        if (glow is { } halo)
            GlowPath(path[..count], halo, glowSpread);

        Fill(path[..count], color);
    }

    /// <summary>
    /// Outlines a diamond.
    /// </summary>
    /// <param name="centre">The middle of the diamond, in screen space.</param>
    /// <param name="radius">How far each point sits from the middle, in real pixels.</param>
    /// <param name="color">The line color.</param>
    /// <param name="thickness">How thick the line is, in real pixels.</param>
    public static void DiamondOutline(Vector2 centre, float radius, Vector4 color, float thickness = 1f)
    {
        Span<Vector2> path = stackalloc Vector2[4];
        var count = DiamondPath(centre, radius, path);

        if (count > 0)
            Stroke(path[..count], color, thickness, closed: true);
    }

    /// <summary>
    /// Draws a line that fades out at one or both ends, for a divider that stops rather than being cut off.
    /// </summary>
    /// <param name="from">The end that fades, in screen space.</param>
    /// <param name="to">The end at full strength.</param>
    /// <param name="color">The line color at full strength.</param>
    /// <param name="thickness">How thick the line is, in real pixels.</param>
    public static void FadeIn(Vector2 from, Vector2 to, Vector4 color, float thickness = 1f)
        => Gradient(from, to, ColorHelper.ScaleAlpha(color, 0f), color, (from, to, thickness), static s =>
            Rect(
                new Vector2(MathF.Min(s.from.X, s.to.X), MathF.Min(s.from.Y, s.to.Y) - (s.thickness * 0.5f)),
                new Vector2(MathF.Max(s.from.X, s.to.X), MathF.Max(s.from.Y, s.to.Y) + (s.thickness * 0.5f)),
                Vector4.One));
}
