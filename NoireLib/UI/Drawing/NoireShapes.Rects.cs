using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The rectangular shapes: fills, outlines, gradients, glows, the composed plate and the hairline frame.
/// </summary>
public static partial class NoireShapes
{
    private static readonly PlateStyle DefaultPlateStyle = new();
    private static readonly FrameStyle DefaultFrameStyle = new();

    #region Rectangles

    /// <summary>
    /// Fills a rectangle whose corners are cut.
    /// </summary>
    /// <remarks>
    /// Sizes here are real pixels, because they sit in the same arithmetic as the screen coordinates they are measured
    /// from. It is the values on a <see cref="PlateStyle"/> or a <see cref="FrameStyle"/> that are logical, and those
    /// are scaled before they reach this.
    /// </remarks>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="color">The fill color.</param>
    /// <param name="shape">How the corners are cut.</param>
    /// <param name="cornerSize">How deep the cut is, in real pixels.</param>
    /// <param name="corners">Which corners are cut.</param>
    public static void Rect(Vector2 min, Vector2 max, Vector4 color, CornerShape shape = CornerShape.Square, float cornerSize = 0f, RectCorners corners = RectCorners.All)
    {
        Span<Vector2> path = stackalloc Vector2[MaxRectPathPoints];
        var count = RectPath(path, min, max, shape, cornerSize, corners);

        if (count > 0)
            Fill(path[..count], color);
    }

    /// <summary>
    /// Outlines a rectangle whose corners are cut.
    /// </summary>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="color">The line color.</param>
    /// <param name="thickness">The line thickness, in real pixels.</param>
    /// <param name="shape">How the corners are cut.</param>
    /// <param name="cornerSize">How deep the cut is, in real pixels.</param>
    /// <param name="corners">Which corners are cut.</param>
    public static void RectOutline(Vector2 min, Vector2 max, Vector4 color, float thickness = 1f, CornerShape shape = CornerShape.Square, float cornerSize = 0f, RectCorners corners = RectCorners.All)
    {
        Span<Vector2> path = stackalloc Vector2[MaxRectPathPoints];
        var count = RectPath(path, min, max, shape, cornerSize, corners);

        if (count > 0)
            Stroke(path[..count], color, thickness);
    }

    /// <summary>
    /// Fills a rectangle with a gradient, corners and all, which is the thing ImGui's own multicolor rectangle cannot
    /// do.
    /// </summary>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="fromColor">The color at the start of the axis.</param>
    /// <param name="toColor">The color at the end of the axis.</param>
    /// <param name="axis">Which way the gradient runs.</param>
    /// <param name="shape">How the corners are cut.</param>
    /// <param name="cornerSize">How deep the cut is, in real pixels.</param>
    /// <param name="corners">Which corners are cut.</param>
    public static void GradientRect(Vector2 min, Vector2 max, Vector4 fromColor, Vector4 toColor, GradientAxis axis = GradientAxis.Vertical, CornerShape shape = CornerShape.Square, float cornerSize = 0f, RectCorners corners = RectCorners.All)
    {
        Span<Vector2> path = stackalloc Vector2[MaxRectPathPoints];
        var count = RectPath(path, min, max, shape, cornerSize, corners);

        if (count == 0)
            return;

        var (from, to) = AxisPoints(min, max, axis);
        FillShaded(path[..count], from, to, fromColor, toColor);
    }

    /// <summary>
    /// Fills a path and shades it in one step, for the shapes that build their own gradient rather than wrapping a
    /// caller's body.
    /// </summary>
    /// <remarks>
    /// Filled in white so the gradient lands exactly as given. See the alpha rule on
    /// <see cref="Gradient(Vector2, Vector2, Vector4, Vector4, Action)"/>.
    /// </remarks>
    private static void FillShaded(ReadOnlySpan<Vector2> path, Vector2 from, Vector2 to, Vector4 fromColor, Vector4 toColor)
    {
        var drawList = DrawList;

        if (drawList.IsNull)
            return;

        var start = drawList.VtxBuffer.Size;
        Fill(path, Vector4.One);
        Shade(drawList, start, drawList.VtxBuffer.Size, from, to, fromColor, toColor);
    }

    #endregion

    #region Glow

    /// <summary>
    /// Paints a soft halo around a rectangle, from a stack of expanding fills that fade as they grow.
    /// </summary>
    /// <remarks>
    /// A glow or a drop shadow depending only on the color: a dark one reads as a shadow, a tinted one as a glow.
    /// Nothing is drawn inside the rectangle itself, so whatever is painted over it covers the brightest part.
    /// </remarks>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="color">The glow color at full strength.</param>
    /// <param name="spread">How far it reaches beyond the rectangle, in real pixels.</param>
    /// <param name="shape">How the corners are cut.</param>
    /// <param name="cornerSize">How deep the cut is, in real pixels.</param>
    /// <param name="corners">Which corners are cut.</param>
    public static void Glow(Vector2 min, Vector2 max, Vector4 color, float spread, CornerShape shape = CornerShape.Square, float cornerSize = 0f, RectCorners corners = RectCorners.All)
    {
        if (spread <= 0f || color.W <= 0f)
            return;

        // A layer roughly every two pixels reads as smooth without spending a draw call per pixel on a wide glow.
        var layers = Math.Clamp((int)MathF.Ceiling(spread * 0.5f), 3, 12);

        Span<Vector2> path = stackalloc Vector2[MaxRectPathPoints];

        // Painted outwards in, so the layers accumulate towards the shape rather than away from it. Each one carries a
        // fraction of the total alpha, which is what makes the falloff smooth instead of a stack of visible rings.
        for (var layer = layers; layer >= 1; layer--)
        {
            var distance = (float)layer / layers;
            var grow = spread * distance;
            var alpha = color.W * (1f - distance) * 2f / layers;

            if (alpha <= 0f)
                continue;

            var count = RectPath(path, min - new Vector2(grow), max + new Vector2(grow), shape, cornerSize + grow, corners);

            if (count > 0)
                Fill(path[..count], color with { W = alpha });
        }
    }

    #endregion

    #region Plate

    /// <summary>
    /// Draws a plate: a filled, optionally gradient, optionally beveled surface with its own border and glow. The
    /// building block a bespoke panel, card, masthead or button face is made of.
    /// </summary>
    /// <remarks>
    /// Every part is optional and off by default except the fill and the theme's own border, so a plate with no style
    /// is a surface that matches the interface around it, and a plate with one is whatever the design calls for.
    /// </remarks>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="style">How to paint it. When <see langword="null"/>, everything resolves through the theme.</param>
    public static void Plate(Vector2 min, Vector2 max, PlateStyle? style = null)
    {
        style ??= DefaultPlateStyle;

        var cornerSize = style.ResolveCornerSize();

        Span<Vector2> path = stackalloc Vector2[MaxRectPathPoints];
        var count = RectPath(path, min, max, style.CornerShape, cornerSize, style.Corners);

        if (count == 0)
            return;

        var shape = path[..count];

        if (style.GlowSpread > 0f)
            Glow(min, max, style.ResolveGlowColor(), style.ScaledGlowSpread, style.CornerShape, cornerSize, style.Corners);

        var fill = style.ResolveFill();

        if (style.FillTo is { } fillTo)
        {
            var (from, to) = AxisPoints(min, max, style.FillAxis);
            FillShaded(shape, from, to, fill, fillTo);
        }
        else
        {
            Fill(shape, fill);
        }

        if (style.BevelSize > 0f)
            Bevel(shape, style.ResolveBevelLight(), style.ResolveBevelShadow(), style.ScaledBevelSize, style.BevelDirection);

        var borderSize = style.ResolveBorderSize();

        if (borderSize > 0f)
            Stroke(shape, style.ResolveBorderColor(), borderSize);
    }

    #endregion

    #region Frame

    /// <summary>
    /// Draws a hairline frame: one line, optionally two, with optional brackets set inside the corners.
    /// </summary>
    /// <remarks>
    /// The brackets are the reason this is not <c>AddRect</c>. A frame with a short tick inside each corner reads as
    /// drawn rather than as a border, and it is the single cheapest thing that makes a panel look composed.
    /// </remarks>
    /// <param name="min">The top left corner, in screen space.</param>
    /// <param name="max">The bottom right corner, in screen space.</param>
    /// <param name="style">How to draw it. When <see langword="null"/>, a single hairline in the theme's border color.</param>
    public static void Frame(Vector2 min, Vector2 max, FrameStyle? style = null)
    {
        style ??= DefaultFrameStyle;

        var inset = style.ScaledInset;
        var outerMin = min + new Vector2(inset);
        var outerMax = max - new Vector2(inset);

        var thickness = style.ScaledThickness;
        var cornerSize = style.ResolveCornerSize();
        var color = style.ResolveColor();

        Span<Vector2> path = stackalloc Vector2[MaxRectPathPoints];
        var count = RectPath(path, outerMin, outerMax, style.CornerShape, cornerSize, style.Corners);

        if (count == 0)
            return;

        Stroke(path[..count], color, thickness);

        var gap = style.DoubleGap > 0f ? style.ScaledDoubleGap + thickness : 0f;

        if (gap > 0f)
        {
            count = RectPath(path, outerMin + new Vector2(gap), outerMax - new Vector2(gap), style.CornerShape, MathF.Max(0f, cornerSize - gap), style.Corners);

            if (count > 0)
                Stroke(path[..count], color, thickness);
        }

        if (style.TickLength > 0f)
            CornerTicks(outerMin, outerMax, gap, style);
    }

    /// <summary>
    /// Draws the inner bracket at each corner the style asks for.
    /// </summary>
    /// <param name="min">The top left of the outer frame line.</param>
    /// <param name="max">The bottom right of the outer frame line.</param>
    /// <param name="gap">How far in the innermost frame line already sits.</param>
    /// <param name="style">The frame style.</param>
    private static void CornerTicks(Vector2 min, Vector2 max, float gap, FrameStyle style)
    {
        var drawList = DrawList;

        if (drawList.IsNull)
            return;

        var length = style.ScaledTickLength;
        var inset = style.ScaledTickInset + gap;
        var thickness = style.ResolveTickThickness();
        var color = style.ResolveTickColor();

        var topLeft = min + new Vector2(inset);
        var bottomRight = max - new Vector2(inset);

        // Two brackets that would meet or cross read as a smaller frame rather than as corner ticks.
        if (bottomRight.X - topLeft.X < length * 2f || bottomRight.Y - topLeft.Y < length * 2f)
            return;

        Span<Vector2> origins = [topLeft, new Vector2(bottomRight.X, topLeft.Y), bottomRight, new Vector2(topLeft.X, bottomRight.Y)];
        Span<Vector2> along = [new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(-1f, 0f), new Vector2(1f, 0f)];
        Span<Vector2> down = [new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -1f), new Vector2(0f, -1f)];
        Span<RectCorners> flags = [RectCorners.TopLeft, RectCorners.TopRight, RectCorners.BottomRight, RectCorners.BottomLeft];

        for (var corner = 0; corner < 4; corner++)
        {
            if ((style.TickCorners & flags[corner]) == 0)
                continue;

            Line(drawList, origins[corner], origins[corner] + (along[corner] * length), color, thickness);
            Line(drawList, origins[corner], origins[corner] + (down[corner] * length), color, thickness);
        }
    }

    #endregion

    #region Separators

    /// <summary>
    /// Draws a rule that fades out at both ends, for a divider that stops rather than being cut off.
    /// </summary>
    /// <param name="from">Where the rule starts, in screen space.</param>
    /// <param name="to">Where it ends, in screen space.</param>
    /// <param name="color">The color at its centre.</param>
    /// <param name="thickness">The line thickness, in real pixels.</param>
    public static void FadedLine(Vector2 from, Vector2 to, Vector4 color, float thickness = 1f)
    {
        var drawList = DrawList;

        if (drawList.IsNull || thickness <= 0f || color.W <= 0f)
            return;

        var transparent = color with { W = 0f };
        var centre = (from + to) * 0.5f;
        var packed = ColorHelper.Vector4ToUint(Vector4.One);

        // Two runs rather than one, because a gradient is a single ramp and this needs to come back down again.
        var start = drawList.VtxBuffer.Size;
        drawList.AddLine(from, centre, packed, thickness);
        Shade(drawList, start, drawList.VtxBuffer.Size, from, centre, transparent, color);

        start = drawList.VtxBuffer.Size;
        drawList.AddLine(centre, to, packed, thickness);
        Shade(drawList, start, drawList.VtxBuffer.Size, centre, to, color, transparent);
    }

    #endregion
}
