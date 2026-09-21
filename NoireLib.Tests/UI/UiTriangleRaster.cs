using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace NoireLib.Tests;

/// <summary>Rasterises a draw list's triangles like the GPU, with Gouraud colours and source-over blending.</summary>
internal sealed class UiTriangleRaster
{
    private readonly int width;
    private readonly int height;
    private readonly Vector2 origin;
    private readonly float[] pixels;

    internal UiTriangleRaster(Vector2 min, Vector2 max)
    {
        origin = min;
        width = (int)MathF.Ceiling(max.X - min.X);
        height = (int)MathF.Ceiling(max.Y - min.Y);
        pixels = new float[width * height * 3];
    }

    internal int Width => width;

    internal int Height => height;

    /// <summary>
    /// Blends every triangle in an index range over what is already there.
    /// </summary>
    internal void Add(ReadOnlySpan<ImDrawVert> vertices, ReadOnlySpan<ushort> indices, int vertexBase)
    {
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = vertices[indices[i] - vertexBase];
            var b = vertices[indices[i + 1] - vertexBase];
            var c = vertices[indices[i + 2] - vertexBase];

            Triangle(a, b, c);
        }
    }

    /// <summary>
    /// The painted image as bytes, the way a screenshot would hold it.
    /// </summary>
    internal byte[] ToBytes()
    {
        var bytes = new byte[pixels.Length];

        for (var i = 0; i < pixels.Length; i++)
            bytes[i] = (byte)Math.Clamp((int)MathF.Round(pixels[i] * 255f), 0, 255);

        return bytes;
    }

    private void Triangle(ImDrawVert a, ImDrawVert b, ImDrawVert c)
    {
        var pa = a.Pos - origin;
        var pb = b.Pos - origin;
        var pc = c.Pos - origin;

        var area = ((pb.X - pa.X) * (pc.Y - pa.Y)) - ((pb.Y - pa.Y) * (pc.X - pa.X));

        if (MathF.Abs(area) < 1e-9f)
            return;

        var left = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X))));
        var right = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))));
        var top = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y))));
        var bottom = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))));

        var ca = Unpack(a.Col);
        var cb = Unpack(b.Col);
        var cc = Unpack(c.Col);

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);

                var w0 = (((pb.X - point.X) * (pc.Y - point.Y)) - ((pb.Y - point.Y) * (pc.X - point.X))) / area;
                var w1 = (((pc.X - point.X) * (pa.Y - point.Y)) - ((pc.Y - point.Y) * (pa.X - point.X))) / area;
                var w2 = 1f - w0 - w1;

                if (w0 < 0f || w1 < 0f || w2 < 0f)
                    continue;

                var colour = (ca * w0) + (cb * w1) + (cc * w2);
                var alpha = colour.W;

                if (alpha <= 0f)
                    continue;

                var at = ((y * width) + x) * 3;

                pixels[at] = (colour.X * alpha) + (pixels[at] * (1f - alpha));
                pixels[at + 1] = (colour.Y * alpha) + (pixels[at + 1] * (1f - alpha));
                pixels[at + 2] = (colour.Z * alpha) + (pixels[at + 2] * (1f - alpha));
            }
        }
    }

    private static Vector4 Unpack(uint packed)
        => new(
            (packed & 0xFF) / 255f,
            ((packed >> 8) & 0xFF) / 255f,
            ((packed >> 16) & 0xFF) / 255f,
            ((packed >> 24) & 0xFF) / 255f);
}
