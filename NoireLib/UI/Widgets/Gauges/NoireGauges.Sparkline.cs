using Dalamud.Bindings.ImGui;
using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>The sparkline: a series drawn small enough to sit in a line of text.</summary>
public static partial class NoireGauges
{
    private static readonly SparklineStyle DefaultSparklineStyle = new();

    /// <summary>Draws a sparkline over a series of values, oldest first.</summary>
    /// <param name="values">The series, oldest first.</param>
    /// <param name="style">How to draw it, or <see langword="null"/> for the default sparkline.</param>
    public static void Sparkline(ReadOnlySpan<float> values, SparklineStyle? style = null)
    {
        style ??= DefaultSparklineStyle;

        using var draw = UiDraw.Begin();

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
            DrawTrace(draw.List, values, style, plot, min, max, color);

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawTrace(
        ImDrawListPtr list,
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

        if (style.CustomDraw != null)
        {
            if (!list.IsNull)
            {
                var args = new UiSparklineDraw(
                    list, plot, min, max, line, color, fill, style.ScaledThickness, style.MarkLast,
                    MathF.Max(style.ScaledMarkSize, 1f));
                UiHook.Invoke(style.CustomDraw, args, nameof(Sparkline), CallbackFault);
            }

            return;
        }

        DrawTraceArea(line, plot, fill);
        DrawTraceLine(line, color, style.ScaledThickness);

        if (style.MarkLast)
            DrawTraceMark(line, MathF.Max(style.ScaledMarkSize, 1f), color);
    }

    internal static void DrawTraceArea(ReadOnlySpan<Vector2> line, UiRect plot, Vector4 fill)
    {
        if (fill.W <= 0f || line.Length < 2)
            return;

        NoireShapes.FillUnder(line, plot.Bottom, fill);
    }

    internal static void DrawTraceLine(ReadOnlySpan<Vector2> line, Vector4 color, float thickness)
    {
        if (line.Length < 2)
            return;

        NoireShapes.Stroke(line, color, thickness, closed: false);
    }

    internal static void DrawTraceMark(ReadOnlySpan<Vector2> line, float radius, Vector4 color)
    {
        if (line.Length == 0)
            return;

        var last = line[^1];
        NoireShapes.Rect(last - new Vector2(radius), last + new Vector2(radius), color, CornerShape.Rounded, radius);
    }

    // The most points a sparkline is drawn from, past which segments fall below a pixel.
    private const int MaxSparklinePoints = 512;

    /// <summary>The vertical range a sparkline is plotted against.</summary>
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

    private static float PlotY(float value, float min, float max, UiRect plot)
    {
        var normalized = Math.Clamp((value - min) / (max - min), 0f, 1f);
        return plot.Bottom - (normalized * plot.Size.Y);
    }
}
