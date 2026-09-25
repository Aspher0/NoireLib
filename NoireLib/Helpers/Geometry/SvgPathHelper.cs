using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Flattens the <c>d</c> attribute of an SVG path to point lists, every command supported. Curves stay within the
/// tolerance of the true curve.
/// </summary>
public static class SvgPathHelper
{
    /// <summary>The default flattening tolerance, in path units.</summary>
    public const float DefaultTolerance = 0.05f;

    private const int MaxSegments = 256;

    /// <summary>Flattens an SVG path.</summary>
    /// <param name="pathData">The path data, as written in the <c>d</c> attribute.</param>
    /// <param name="tolerance">How far, in path units, the flattened outline may stray from the true curves.</param>
    /// <returns>The subpaths, in the order the path draws them. A subpath with fewer than two points is left out.</returns>
    public static SvgSubpath[] Flatten(string pathData, float tolerance = DefaultTolerance)
    {
        var output = new List<SvgSubpath>();
        Flatten(pathData, output, tolerance);
        return output.ToArray();
    }

    /// <inheritdoc cref="Flatten(string, float)"/>
    /// <param name="pathData">The path data, as written in the <c>d</c> attribute.</param>
    /// <param name="output">Receives the subpaths, appended after what it already holds.</param>
    /// <param name="tolerance">How far, in path units, the flattened outline may stray from the true curves.</param>
    public static void Flatten(string pathData, List<SvgSubpath> output, float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(pathData);
        ArgumentNullException.ThrowIfNull(output);

        tolerance = MathF.Max(tolerance, 1e-4f);

        var current = new List<Vector2>();
        Span<Vector2> samples = stackalloc Vector2[MaxSegments + 1];
        var position = Vector2.Zero;
        var start = Vector2.Zero;
        var cubicControl = (Vector2?)null;
        var quadraticControl = (Vector2?)null;
        var index = 0;
        var command = 'M';

        void Flush(bool closed)
        {
            if (current.Count >= 2)
                output.Add(new SvgSubpath(current.ToArray(), closed));

            current.Clear();
        }

        // A drawing command right after a close starts a new subpath at the closed one's start.
        void Begin()
        {
            if (current.Count == 0)
                current.Add(position);
        }

        void AddSamples(ReadOnlySpan<Vector2> points)
        {
            foreach (var point in points)
                current.Add(point);
        }

        while (true)
        {
            SkipSeparators(pathData, ref index);

            if (index >= pathData.Length)
                break;

            var read = index;

            if (char.IsLetter(pathData[index]))
            {
                command = pathData[index++];

                if (command is 'Z' or 'z')
                {
                    Flush(true);
                    position = start;
                    cubicControl = quadraticControl = null;
                    continue;
                }
            }
            else if (!(char.IsDigit(pathData[index]) || pathData[index] is '-' or '+' or '.'))
            {
                break;
            }

            var relative = char.IsLower(command);
            var origin = relative ? position : Vector2.Zero;
            Vector2? nextCubic = null;
            Vector2? nextQuadratic = null;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    Flush(false);
                    position = origin + Pair(pathData, ref index);
                    start = position;
                    current.Add(position);
                    command = relative ? 'l' : 'L';
                    break;

                case 'L':
                    Begin();
                    position = origin + Pair(pathData, ref index);
                    current.Add(position);
                    break;

                case 'H':
                {
                    Begin();
                    var x = Number(pathData, ref index);
                    position = new Vector2(relative ? position.X + x : x, position.Y);
                    current.Add(position);
                    break;
                }

                case 'V':
                {
                    Begin();
                    var y = Number(pathData, ref index);
                    position = new Vector2(position.X, relative ? position.Y + y : y);
                    current.Add(position);
                    break;
                }

                case 'C':
                case 'S':
                {
                    Begin();
                    var control1 = char.ToUpperInvariant(command) == 'S'
                        ? cubicControl is { } previous ? (2f * position) - previous : position
                        : origin + Pair(pathData, ref index);
                    var control2 = origin + Pair(pathData, ref index);
                    var end = origin + Pair(pathData, ref index);
                    var count = Geometry2DHelper.SampleCubic(samples, position, control1, control2, end,
                        CubicSegments(position, control1, control2, end, tolerance), includeStart: false);
                    AddSamples(samples[..count]);
                    nextCubic = control2;
                    position = end;
                    break;
                }

                case 'Q':
                case 'T':
                {
                    Begin();
                    var control = char.ToUpperInvariant(command) == 'T'
                        ? quadraticControl is { } previous ? (2f * position) - previous : position
                        : origin + Pair(pathData, ref index);
                    var end = origin + Pair(pathData, ref index);
                    var count = Geometry2DHelper.SampleQuadratic(samples, position, control, end,
                        QuadraticSegments(position, control, end, tolerance), includeStart: false);
                    AddSamples(samples[..count]);
                    nextQuadratic = control;
                    position = end;
                    break;
                }

                case 'A':
                {
                    Begin();
                    var rx = Number(pathData, ref index);
                    var ry = Number(pathData, ref index);
                    var rotation = Number(pathData, ref index);
                    var large = Flag(pathData, ref index);
                    var sweep = Flag(pathData, ref index);
                    var end = origin + Pair(pathData, ref index);
                    Arc(current, position, end, rx, ry, rotation, large, sweep, tolerance);
                    position = end;
                    break;
                }

                default:
                    index = pathData.Length;
                    break;
            }

            cubicControl = nextCubic;
            quadraticControl = nextQuadratic;

            // A command whose arguments read as nothing would otherwise repeat forever.
            if (index == read)
                break;
        }

        Flush(false);
    }

    // A cubic's second derivative is at most six times its largest second difference, and n even segments stray from a
    // curve by at most that bound over 8n squared.
    private static int CubicSegments(Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1, float tolerance)
    {
        var bend = MathF.Max((p0 - (2f * c1) + c2).Length(), (c1 - (2f * c2) + p1).Length());
        return Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(0.75f * bend / tolerance)), 1, MaxSegments);
    }

    // A quadratic's second derivative is twice its second difference.
    private static int QuadraticSegments(Vector2 p0, Vector2 c, Vector2 p1, float tolerance)
    {
        var bend = (p0 - (2f * c) + p1).Length();
        return Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(0.25f * bend / tolerance)), 1, MaxSegments);
    }

    // Converts the endpoint form an SVG arc is written in to its centre, radii and angles.
    private static void Arc(List<Vector2> output, Vector2 from, Vector2 to, float rx, float ry, float rotationDegrees, bool large, bool sweep,
        float tolerance)
    {
        if (rx == 0f || ry == 0f || from == to)
        {
            output.Add(to);
            return;
        }

        rx = MathF.Abs(rx);
        ry = MathF.Abs(ry);

        var phi = rotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(phi);
        var sin = MathF.Sin(phi);
        var half = (from - to) * 0.5f;
        var x1 = (cos * half.X) + (sin * half.Y);
        var y1 = (-sin * half.X) + (cos * half.Y);
        var lambda = (x1 * x1 / (rx * rx)) + (y1 * y1 / (ry * ry));

        if (lambda > 1f)
        {
            var root = MathF.Sqrt(lambda);
            rx *= root;
            ry *= root;
        }

        var numerator = (rx * rx * ry * ry) - (rx * rx * y1 * y1) - (ry * ry * x1 * x1);
        var denominator = (rx * rx * y1 * y1) + (ry * ry * x1 * x1);
        var factor = MathF.Sqrt(MathF.Max(0f, numerator / denominator)) * (large == sweep ? -1f : 1f);
        var cxp = factor * (rx * y1 / ry);
        var cyp = factor * (-ry * x1 / rx);
        var mid = (from + to) * 0.5f;
        var centre = new Vector2((cos * cxp) - (sin * cyp) + mid.X, (sin * cxp) + (cos * cyp) + mid.Y);
        var startAngle = Angle(1f, 0f, (x1 - cxp) / rx, (y1 - cyp) / ry);
        var delta = Angle((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);

        if (!sweep && delta > 0f)
            delta -= MathF.PI * 2f;
        else if (sweep && delta < 0f)
            delta += MathF.PI * 2f;

        // The widest step whose chord stays within the tolerance of the larger radius.
        var radius = MathF.Max(rx, ry);
        var step = tolerance >= radius ? MathF.PI * 0.5f : 2f * MathF.Acos(1f - (tolerance / radius));
        var steps = Math.Clamp((int)MathF.Ceiling(MathF.Abs(delta) / step), 1, MaxSegments);

        for (var i = 1; i <= steps; i++)
        {
            var angle = startAngle + (delta * i / steps);
            var x = rx * MathF.Cos(angle);
            var y = ry * MathF.Sin(angle);
            output.Add(new Vector2((cos * x) - (sin * y) + centre.X, (sin * x) + (cos * y) + centre.Y));
        }
    }

    private static float Angle(float ux, float uy, float vx, float vy)
    {
        var dot = (ux * vx) + (uy * vy);
        var length = MathF.Sqrt((ux * ux) + (uy * uy)) * MathF.Sqrt((vx * vx) + (vy * vy));
        var angle = MathF.Acos(Math.Clamp(dot / length, -1f, 1f));
        return (ux * vy) - (uy * vx) < 0f ? -angle : angle;
    }

    private static void SkipSeparators(string d, ref int index)
    {
        while (index < d.Length && (d[index] is ' ' or ',' or '\n' or '\r' or '\t'))
            index++;
    }

    private static Vector2 Pair(string d, ref int index)
    {
        var x = Number(d, ref index);
        var y = Number(d, ref index);
        return new Vector2(x, y);
    }

    private static bool Flag(string d, ref int index)
    {
        SkipSeparators(d, ref index);
        var value = index < d.Length && d[index] == '1';
        index++;
        return value;
    }

    private static float Number(string d, ref int index)
    {
        SkipSeparators(d, ref index);
        var begin = index;

        if (index < d.Length && d[index] is '-' or '+')
            index++;

        var dot = false;

        while (index < d.Length)
        {
            var c = d[index];

            if (char.IsDigit(c))
            {
                index++;
                continue;
            }

            if (c == '.' && !dot)
            {
                dot = true;
                index++;
                continue;
            }

            if (c is 'e' or 'E' && index + 1 < d.Length)
            {
                index++;

                if (d[index] is '-' or '+')
                    index++;

                continue;
            }

            break;
        }

        return float.TryParse(d.AsSpan(begin, index - begin), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
    }
}
