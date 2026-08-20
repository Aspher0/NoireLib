using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// The plot area, projected points and resolved colours handed to a <see cref="SparklineStyle.CustomDraw"/> hook.
/// </summary>
/// <remarks>
/// A <see langword="ref struct"/> because the points are a span over stack memory. The background and baseline are
/// drawn before the hook runs; the hook replaces only the trace (area, line and end mark).
/// </remarks>
public readonly ref struct UiSparklineDraw
{
    internal UiSparklineDraw(
        ImDrawListPtr drawList,
        UiRect plot,
        float min,
        float max,
        ReadOnlySpan<Vector2> points,
        Vector4 color,
        Vector4 fillColor,
        float thickness,
        bool markLast,
        float markRadius)
    {
        DrawList = drawList;
        Plot = plot;
        Min = min;
        Max = max;
        Points = points;
        Color = color;
        FillColor = fillColor;
        Thickness = thickness;
        MarkLast = markLast;
        MarkRadius = markRadius;
    }

    /// <summary>The draw list to paint into.</summary>
    public ImDrawListPtr DrawList { get; }

    /// <summary>The plot area, in screen pixels.</summary>
    public UiRect Plot { get; }

    /// <summary>The value at the bottom of the plot.</summary>
    public float Min { get; }

    /// <summary>The value at the top of the plot.</summary>
    public float Max { get; }

    /// <summary>The series projected into screen coordinates, oldest first. Always two or more points.</summary>
    public ReadOnlySpan<Vector2> Points { get; }

    /// <summary>The colour of the trace, already resolved through the style and the theme.</summary>
    public Vector4 Color { get; }

    /// <summary>The colour of the area under the trace, already resolved.</summary>
    public Vector4 FillColor { get; }

    /// <summary>The trace thickness, in real pixels.</summary>
    public float Thickness { get; }

    /// <summary>Whether the style asked for the last point to be marked.</summary>
    public bool MarkLast { get; }

    /// <summary>The radius of that mark, in real pixels.</summary>
    public float MarkRadius { get; }

    /// <summary>
    /// Draws the sparkline's own filled area under the trace. Nothing when <see cref="FillColor"/> is transparent.
    /// </summary>
    public void DrawArea() => NoireGauges.DrawTraceArea(Points, Plot, FillColor);

    /// <summary>
    /// Draws the sparkline's own line through the points.
    /// </summary>
    public void DrawLine() => NoireGauges.DrawTraceLine(Points, Color, Thickness);

    /// <summary>
    /// Draws the sparkline's own end-point mark. Nothing when <see cref="MarkLast"/> is off.
    /// </summary>
    public void DrawMark()
    {
        if (MarkLast)
            NoireGauges.DrawTraceMark(Points, MarkRadius, Color);
    }
}
