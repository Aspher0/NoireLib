using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>Renders vector shapes to pixels on the CPU with supersampled edges. Pure: any thread.</summary>
public static class NoireRaster
{
    /// <summary>The default supersampling grid: each pixel is sampled this many times across and down.</summary>
    public const int DefaultSamples = 4;

    /// <summary>Renders shapes into a new RGBA image, painting them in order.</summary>
    /// <param name="shapes">The shapes, back to front.</param>
    /// <param name="width">The image width, in pixels.</param>
    /// <param name="height">The image height, in pixels.</param>
    /// <param name="scale">Pixels per shape unit.</param>
    /// <param name="offset">Where the shapes' origin lands, in pixels.</param>
    /// <param name="samples">The supersampling grid, from 1 (aliased) up.</param>
    /// <returns>The pixels, row by row, four bytes each in red, green, blue, alpha order, not premultiplied.</returns>
    public static byte[] Render(IReadOnlyList<RasterShape> shapes, int width, int height, float scale, Vector2 offset,
        int samples = DefaultSamples)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        samples = Math.Max(1, samples);

        var accumulated = new float[width * height * 4];

        foreach (var shape in shapes)
            Composite(accumulated, width, height, shape, scale, offset, samples);

        var bytes = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            var alpha = accumulated[(i * 4) + 3];

            if (alpha <= 0f)
                continue;

            bytes[i * 4] = ToByte(accumulated[i * 4] / alpha);
            bytes[(i * 4) + 1] = ToByte(accumulated[(i * 4) + 1] / alpha);
            bytes[(i * 4) + 2] = ToByte(accumulated[(i * 4) + 2] / alpha);
            bytes[(i * 4) + 3] = ToByte(alpha);
        }

        return bytes;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((value * 255f) + 0.5f, 0f, 255f);

    private static void Composite(float[] accumulated, int width, int height, RasterShape shape, float scale, Vector2 offset, int samples)
    {
        var subpaths = new Vector2[shape.Subpaths.Count][];
        var closed = new bool[subpaths.Length];
        var boundsMin = new Vector2(float.MaxValue);
        var boundsMax = new Vector2(float.MinValue);

        for (var s = 0; s < subpaths.Length; s++)
        {
            var source = shape.Subpaths[s].Points;
            var placed = new Vector2[source.Length];

            for (var i = 0; i < source.Length; i++)
            {
                placed[i] = (source[i] * scale) + offset;
                boundsMin = Vector2.Min(boundsMin, source[i]);
                boundsMax = Vector2.Max(boundsMax, source[i]);
            }

            subpaths[s] = placed;
            closed[s] = shape.Subpaths[s].Closed;
        }

        if (boundsMin.X > boundsMax.X)
            return;

        var fill = shape.Fill;
        var stroke = shape.StrokeWidth > 0f ? shape.Stroke : null;
        var halfStroke = stroke != null ? shape.StrokeWidth * scale * 0.5f : 0f;
        var reachSquared = halfStroke * halfStroke;
        var pixelMin = (boundsMin * scale) + offset - new Vector2(halfStroke + 1f);
        var pixelMax = (boundsMax * scale) + offset + new Vector2(halfStroke + 1f);
        var x0 = Math.Max(0, (int)MathF.Floor(pixelMin.X));
        var y0 = Math.Max(0, (int)MathF.Floor(pixelMin.Y));
        var x1 = Math.Min(width - 1, (int)MathF.Ceiling(pixelMax.X));
        var y1 = Math.Min(height - 1, (int)MathF.Ceiling(pixelMax.Y));
        var total = (float)(samples * samples);

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var fillHits = 0;
                var strokeHits = 0;

                for (var sy = 0; sy < samples; sy++)
                {
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var p = new Vector2(x + ((sx + 0.5f) / samples), y + ((sy + 0.5f) / samples));

                        if (fill != null && Winding(subpaths, p) != 0)
                            fillHits++;

                        if (stroke != null && NearStroke(subpaths, closed, p, reachSquared))
                            strokeHits++;
                    }
                }

                if (fillHits == 0 && strokeHits == 0)
                    continue;

                var local = (new Vector2(x + 0.5f, y + 0.5f) - offset) / scale;
                var index = ((y * width) + x) * 4;

                if (fillHits > 0)
                    Over(accumulated, index, fill!.ColorAt(local, boundsMin, boundsMax, 0f), fillHits / total * shape.Opacity);

                if (strokeHits > 0)
                    Over(accumulated, index, stroke!.ColorAt(local, boundsMin, boundsMax, 0f), strokeHits / total * shape.Opacity);
            }
        }
    }

    // Source over, accumulated premultiplied.
    private static void Over(float[] accumulated, int index, Vector4 color, float coverage)
    {
        var alpha = color.W * coverage;

        if (alpha <= 0f)
            return;

        var keep = 1f - alpha;
        accumulated[index] = (color.X * alpha) + (accumulated[index] * keep);
        accumulated[index + 1] = (color.Y * alpha) + (accumulated[index + 1] * keep);
        accumulated[index + 2] = (color.Z * alpha) + (accumulated[index + 2] * keep);
        accumulated[index + 3] = alpha + (accumulated[index + 3] * keep);
    }

    // Summed over every subpath: a hole wound the other way cancels its outline.
    private static int Winding(Vector2[][] subpaths, Vector2 p)
    {
        var winding = 0;

        foreach (var subpath in subpaths)
            winding += Geometry2DHelper.WindingNumber(subpath, p);

        return winding;
    }

    private static bool NearStroke(Vector2[][] subpaths, bool[] closed, Vector2 p, float reachSquared)
    {
        for (var s = 0; s < subpaths.Length; s++)
        {
            var subpath = subpaths[s];
            var count = subpath.Length;
            var segments = closed[s] ? count : count - 1;

            if (count == 1 && Vector2.DistanceSquared(subpath[0], p) <= reachSquared)
                return true;

            for (var i = 0; i < segments; i++)
            {
                if (Geometry2DHelper.PointToSegmentDistanceSquared(p, subpath[i], subpath[i + 1 == count ? 0 : i + 1]) <= reachSquared)
                    return true;
            }
        }

        return false;
    }
}
