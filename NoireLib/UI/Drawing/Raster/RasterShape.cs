using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// One vector shape for <see cref="NoireRaster"/>: an outline, and how it is filled and stroked. Build one with
/// <see cref="Path"/> or <see cref="Ellipse"/>, then chain <see cref="Filled(Vector4)"/> and <see cref="Stroked(Vector4, float)"/>.
/// </summary>
/// <param name="Subpaths">The outline, in the shape's own units.</param>
public sealed record RasterShape(IReadOnlyList<SvgSubpath> Subpaths)
{
    /// <summary>What fills the outline under the non-zero rule, or null to leave it empty.</summary>
    public NoireGradient? Fill { get; init; }

    /// <summary>What strokes the outline, or null for no stroke.</summary>
    public NoireGradient? Stroke { get; init; }

    /// <summary>The stroke width, in the shape's own units.</summary>
    public float StrokeWidth { get; init; }

    /// <summary>How opaque the whole shape is, from 0 to 1.</summary>
    public float Opacity { get; init; } = 1f;

    /// <summary>A shape outlined by an SVG path.</summary>
    /// <param name="pathData">The path data, as written in the <c>d</c> attribute.</param>
    /// <param name="tolerance">How far, in the shape's own units, the flattened outline may stray from its curves.</param>
    /// <returns>The shape, neither filled nor stroked yet.</returns>
    public static RasterShape Path(string pathData, float tolerance = SvgPathHelper.DefaultTolerance)
        => new(SvgPathHelper.Flatten(pathData, tolerance));

    /// <summary>A closed ellipse.</summary>
    /// <param name="centre">The centre, in the shape's own units.</param>
    /// <param name="radius">The horizontal and vertical radii.</param>
    /// <param name="tolerance">How far, in the shape's own units, the flattened outline may stray from the true ellipse.</param>
    /// <returns>The shape, neither filled nor stroked yet.</returns>
    public static RasterShape Ellipse(Vector2 centre, Vector2 radius, float tolerance = SvgPathHelper.DefaultTolerance)
    {
        var largest = MathF.Max(MathF.Abs(radius.X), MathF.Abs(radius.Y));
        var step = tolerance >= largest ? MathF.PI * 0.5f : 2f * MathF.Acos(1f - (tolerance / largest));
        var count = Math.Clamp((int)MathF.Ceiling(MathF.PI * 2f / step), 8, 256);
        var points = new Vector2[count];

        for (var i = 0; i < count; i++)
        {
            var angle = MathF.PI * 2f * i / count;
            points[i] = centre + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y);
        }

        return new RasterShape([new SvgSubpath(points, true)]);
    }

    /// <summary>This shape filled with one color.</summary>
    /// <param name="color">The fill color, RGBA from 0 to 1.</param>
    /// <returns>A filled copy.</returns>
    public RasterShape Filled(Vector4 color) => this with { Fill = NoireGradient.Solid(color) };

    /// <summary>This shape filled with a gradient, laid over the shape's bounding box.</summary>
    /// <param name="gradient">The fill.</param>
    /// <returns>A filled copy.</returns>
    public RasterShape Filled(NoireGradient gradient) => this with { Fill = gradient };

    /// <summary>This shape stroked with one color.</summary>
    /// <param name="color">The stroke color, RGBA from 0 to 1.</param>
    /// <param name="width">The stroke width, in the shape's own units.</param>
    /// <returns>A stroked copy.</returns>
    public RasterShape Stroked(Vector4 color, float width) => this with { Stroke = NoireGradient.Solid(color), StrokeWidth = width };

    /// <summary>This shape stroked with a gradient, laid over the shape's bounding box.</summary>
    /// <param name="gradient">The stroke.</param>
    /// <param name="width">The stroke width, in the shape's own units.</param>
    /// <returns>A stroked copy.</returns>
    public RasterShape Stroked(NoireGradient gradient, float width) => this with { Stroke = gradient, StrokeWidth = width };
}
