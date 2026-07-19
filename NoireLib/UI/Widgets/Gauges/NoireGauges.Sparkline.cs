using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The sparkline: a series drawn small enough to sit in a line of text, where the shape of the last minute is the
/// whole point and the individual numbers are not.
/// </summary>
public static partial class NoireGauges
{
    private static readonly SparklineStyle DefaultSparklineStyle = new();

    /// <summary>
    /// Draws a sparkline over a series of values, oldest first.
    /// </summary>
    /// <remarks>
    /// The vertical range comes from the data unless <see cref="SparklineStyle.Min"/> and
    /// <see cref="SparklineStyle.Max"/> pin it. Pinning is what makes two sparklines comparable: left to itself, every
    /// trace fills its own box, and a flat line and a violent one come out looking the same.
    /// </remarks>
    /// <param name="values">The series, oldest first. Fewer than two values draws nothing but still reserves the space.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default sparkline.</param>
    public static void Sparkline(ReadOnlySpan<float> values, SparklineStyle? style = null)
    {
        style ??= DefaultSparklineStyle;

        var width = style.Width > 0f ? style.ScaledWidth : NoireLayout.ContentWidth();
        var height = MathF.Max(style.ScaledHeight, 1f);

        if (width <= 0f)
            return;

        var origin = ImGui.GetCursorScreenPos();
        var plot = new UiRect(origin, new Vector2(width, height));

        if (style.Background is { } background)
            NoireShapes.Rect(plot.Position, plot.Max, background, CornerShape.Rounded, NoireUI.Scaled(2f));

        var (min, max) = SparklineBounds(values, style.Min, style.Max);
        var color = style.Color ?? NoireTheme.Current.Resolve(ThemeColor.Accent);

        if (style.Baseline is { } baseline)
        {
            var y = MathF.Round(PlotY(baseline, min, max, plot));
            NoireShapes.Rect(
                new Vector2(plot.Left, y), new Vector2(plot.Right, y + 1f), style.ResolveBaselineColor());
        }

        if (values.Length >= 2)
            DrawTrace(values, style, plot, min, max, color);

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>
    /// Draws the filled area and the line over it.
    /// </summary>
    /// <param name="values">The series, oldest first.</param>
    /// <param name="style">The sparkline style.</param>
    /// <param name="plot">The area to draw into, in screen pixels.</param>
    /// <param name="min">The value at the bottom of the plot.</param>
    /// <param name="max">The value at the top of the plot.</param>
    /// <param name="color">The colour of the trace.</param>
    private static void DrawTrace(
        ReadOnlySpan<float> values,
        SparklineStyle style,
        UiRect plot,
        float min,
        float max,
        Vector4 color)
    {
        var count = Math.Min(values.Length, MaxSparklinePoints);
        var first = values.Length - count;
        var step = plot.Size.X / (count - 1);

        Span<Vector2> line = count <= 256 ? stackalloc Vector2[count] : new Vector2[count];

        for (var i = 0; i < count; i++)
        {
            line[i] = new Vector2(
                plot.Left + (i * step),
                PlotY(values[first + i], min, max, plot));
        }

        var fill = style.FillColor ?? ColorHelper.ScaleAlpha(color, 0.18f);

        if (fill.W > 0f)
        {
            // The filled shape is the trace closed along the bottom edge, so it is the line's own points plus the two
            // corners under its ends rather than a second pass over the data.
            Span<Vector2> area = count + 2 <= 258 ? stackalloc Vector2[count + 2] : new Vector2[count + 2];

            line.CopyTo(area);
            area[count] = new Vector2(line[count - 1].X, plot.Bottom);
            area[count + 1] = new Vector2(line[0].X, plot.Bottom);

            NoireShapes.Fill(area, fill);
        }

        NoireShapes.Stroke(line, color, style.ScaledThickness, closed: false);

        if (style.MarkLast)
        {
            var last = line[count - 1];
            var radius = MathF.Max(style.ScaledMarkSize, 1f);
            NoireShapes.Rect(last - new Vector2(radius), last + new Vector2(radius), color, CornerShape.Rounded, radius);
        }
    }

    /// <summary>
    /// The most points a sparkline is drawn from. Past this the segments are shorter than a pixel on any plausible
    /// width, and the extra work buys nothing.
    /// </summary>
    private const int MaxSparklinePoints = 512;

    /// <summary>
    /// The vertical range a sparkline is plotted against.
    /// </summary>
    /// <remarks>
    /// A flat series has no range of its own, and dividing by it would put the trace at infinity. It is drawn through
    /// the middle instead, which is the honest picture of a value that has not moved.
    /// </remarks>
    /// <param name="values">The series.</param>
    /// <param name="explicitMin">A pinned lower bound, or <see langword="null"/> to take it from the data.</param>
    /// <param name="explicitMax">A pinned upper bound, or <see langword="null"/> to take it from the data.</param>
    /// <returns>The bounds to plot against, always with the maximum above the minimum.</returns>
    public static (float Min, float Max) SparklineBounds(
        ReadOnlySpan<float> values,
        float? explicitMin,
        float? explicitMax)
    {
        var min = explicitMin ?? float.MaxValue;
        var max = explicitMax ?? float.MinValue;

        if (explicitMin == null || explicitMax == null)
        {
            foreach (var value in values)
            {
                if (explicitMin == null && value < min)
                    min = value;

                if (explicitMax == null && value > max)
                    max = value;
            }
        }

        if (values.Length == 0 && (explicitMin == null || explicitMax == null))
        {
            min = explicitMin ?? 0f;
            max = explicitMax ?? 1f;
        }

        if (max <= min)
        {
            var centre = min;
            min = centre - 0.5f;
            max = centre + 0.5f;
        }

        return (min, max);
    }

    /// <summary>
    /// Where a value sits vertically in the plot, with the maximum at the top.
    /// </summary>
    /// <param name="value">The value to place.</param>
    /// <param name="min">The value at the bottom.</param>
    /// <param name="max">The value at the top.</param>
    /// <param name="plot">The area being drawn into.</param>
    /// <returns>The screen y coordinate.</returns>
    private static float PlotY(float value, float min, float max, UiRect plot)
    {
        var normalized = Math.Clamp((value - min) / (max - min), 0f, 1f);
        return plot.Bottom - (normalized * plot.Size.Y);
    }
}
