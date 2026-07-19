using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Reads back the game's G-buffer targets so what each one carries can be seen rather than assumed.<br/>
/// <b>Why a readback and not a reading of the formats.</b> A format constrains what a channel could hold and
/// says nothing about what it does hold: four of the five targets here are the same
/// <c>B8G8R8A8_UNORM</c>, so format alone cannot tell albedo from a normal from a mask. Guessing from shape has
/// already produced two wrong readings of a texture on this project. Pixels settle it - a normal buffer is
/// visibly pastel and centred near the middle of its range, an albedo looks like the room, and a mask is a
/// handful of flat values.
/// </summary>
internal static unsafe class GBufferProbe
{
    /// <summary>Per-channel statistics of one target, which is what distinguishes the kinds apart.</summary>
    /// <param name="Min">Lowest value seen in each channel.</param>
    /// <param name="Max">Highest value seen in each channel.</param>
    /// <param name="Mean">Average of each channel.</param>
    /// <param name="DistinctApprox">Roughly how many distinct values the red channel takes, quantised to 8 bits.</param>
    private readonly record struct ChannelStats(float[] Min, float[] Max, float[] Mean, int DistinctApprox);

    /// <summary>
    /// Copies each target, measures it, and writes it out as a viewable image.
    /// </summary>
    /// <param name="device">The render device whose context performs the copy.</param>
    /// <param name="targets">The G-buffer resources, in bind order.</param>
    /// <param name="folder">Where to write the images.</param>
    public static string Describe(RenderDevice device, IReadOnlyList<nint> targets, string folder)
    {
        var sb = new StringBuilder();

        if (targets.Count == 0)
        {
            sb.AppendLine("No G-buffer targets known. Run /noire3d rtlog first - this reads the target set that capture identified.");
            return sb.ToString();
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Could not create {folder}: {ex.Message}");
            return sb.ToString();
        }

        sb.AppendLine($"G-buffer readback of {targets.Count} target(s). Images written to {folder}");
        sb.AppendLine("A normal buffer sits near the middle of its range in every channel; an albedo varies widely; a mask has few distinct values.");

        for (var i = 0; i < targets.Count; i++)
        {
            sb.AppendLine();
            sb.Append($"rtv{i} 0x{targets[i]:X}: ");

            var path = Path.Combine(folder, $"gbuffer_rtv{i}.bmp");
            sb.AppendLine(ReadOne(device, targets[i], path));
        }

        return sb.ToString();
    }

    /// <summary>Copies one target to staging, measures it, and writes a BMP of it.</summary>
    private static string ReadOne(RenderDevice device, nint resource, string path)
    {
        if (resource == 0 || !ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var source))
            return "not a texture, skipped";

        using (source)
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);

            var stagingDesc = desc;
            stagingDesc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            stagingDesc.MiscFlags = 0;

            ComPtr<ID3D11Texture2D> staging = default;
            using (staging)
            {
                if (device.Device->CreateTexture2D(&stagingDesc, null, staging.GetAddressOf()) < 0)
                    return "staging allocation failed";

                var ctx = device.Context;
                ctx->CopyResource((ID3D11Resource*)staging.Get(), (ID3D11Resource*)source.Get());

                D3D11_MAPPED_SUBRESOURCE mapped;
                if (ctx->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0)
                    return "map failed";

                try
                {
                    var pixels = ReadPixels(mapped, desc, out var supported);
                    if (!supported)
                        return $"{desc.Format}, {desc.Width}x{desc.Height} - format not decoded";

                    var stats = Measure(pixels, (int)desc.Width, (int)desc.Height);

                    // A half-float target carries values far above 1, and clamping them to write an image
                    // destroys exactly the information the image was for: everything bright collapses to one
                    // flat value and the channel reads as though it held two.
                    var peak = MathF.Max(stats.Max[0], MathF.Max(stats.Max[1], stats.Max[2]));
                    var scale = peak > 1.001f ? 1f / peak : 1f;

                    WriteBmp(path, pixels, (int)desc.Width, (int)desc.Height, scale, alphaOnly: false);
                    WriteBmp(AlphaPath(path), pixels, (int)desc.Width, (int)desc.Height, stats.Max[3] > 1.001f ? 1f / stats.Max[3] : 1f, alphaOnly: true);

                    var scaleNote = scale < 1f ? $"  (image divided by {peak:F2} to be viewable)" : string.Empty;

                    return $"{FormatShort(desc.Format)}, {desc.Width}x{desc.Height}{scaleNote}\n"
                         + $"    R {stats.Min[0]:F3}..{stats.Max[0]:F3} mean {stats.Mean[0]:F3}   "
                         + $"G {stats.Min[1]:F3}..{stats.Max[1]:F3} mean {stats.Mean[1]:F3}\n"
                         + $"    B {stats.Min[2]:F3}..{stats.Max[2]:F3} mean {stats.Mean[2]:F3}   "
                         + $"A {stats.Min[3]:F3}..{stats.Max[3]:F3} mean {stats.Mean[3]:F3}\n"
                         + $"    about {stats.DistinctApprox} distinct red values{(stats.DistinctApprox <= 8 ? " - few enough to be a mask or an id" : string.Empty)}";
                }
                finally
                {
                    ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
                }
            }
        }
    }

    /// <summary>The companion path for a target's alpha channel, which carries its own quantity and needs its own image.</summary>
    private static string AlphaPath(string path) => Path.ChangeExtension(path, null) + "_alpha.bmp";

    /// <summary>Decodes a mapped target into RGBA floats. Half-float targets keep their range, so values above 1 survive.</summary>
    private static float[] ReadPixels(in D3D11_MAPPED_SUBRESOURCE mapped, in D3D11_TEXTURE2D_DESC desc, out bool supported)
    {
        var width = (int)desc.Width;
        var height = (int)desc.Height;
        var pixels = new float[width * height * 4];
        supported = true;

        var row = (byte*)mapped.pData;

        switch (desc.Format)
        {
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
                for (var y = 0; y < height; y++)
                {
                    var src = row + (y * mapped.RowPitch);
                    for (var x = 0; x < width; x++)
                    {
                        var o = ((y * width) + x) * 4;
                        pixels[o + 0] = src[(x * 4) + 2] / 255f;   // B8G8R8A8 stores blue first
                        pixels[o + 1] = src[(x * 4) + 1] / 255f;
                        pixels[o + 2] = src[(x * 4) + 0] / 255f;
                        pixels[o + 3] = src[(x * 4) + 3] / 255f;
                    }
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
                for (var y = 0; y < height; y++)
                {
                    var src = row + (y * mapped.RowPitch);
                    for (var x = 0; x < width; x++)
                    {
                        var o = ((y * width) + x) * 4;
                        for (var c = 0; c < 4; c++)
                            pixels[o + c] = src[(x * 4) + c] / 255f;
                    }
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT:
                for (var y = 0; y < height; y++)
                {
                    var src = (Half*)(row + (y * mapped.RowPitch));
                    for (var x = 0; x < width; x++)
                    {
                        var o = ((y * width) + x) * 4;
                        for (var c = 0; c < 4; c++)
                            pixels[o + c] = (float)src[(x * 4) + c];
                    }
                }

                break;

            default:
                supported = false;
                break;
        }

        return pixels;
    }

    /// <summary>Measures the channels, including how many distinct values red takes.</summary>
    private static ChannelStats Measure(float[] pixels, int width, int height)
    {
        var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
        var max = new[] { float.MinValue, float.MinValue, float.MinValue, float.MinValue };
        var sum = new double[4];
        var seen = new bool[256];

        var count = width * height;
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < 4; c++)
            {
                var v = pixels[(i * 4) + c];
                if (float.IsNaN(v))
                    continue;

                if (v < min[c]) min[c] = v;
                if (v > max[c]) max[c] = v;
                sum[c] += v;
            }

            // Quantised so a smoothly varying channel reads as many values and a flag channel as few.
            var r = pixels[i * 4];
            if (r is >= 0f and <= 1f)
                seen[(int)(r * 255f)] = true;
        }

        var distinct = 0;
        foreach (var s in seen)
        {
            if (s)
                distinct++;
        }

        var mean = new float[4];
        for (var c = 0; c < 4; c++)
            mean[c] = (float)(sum[c] / count);

        return new ChannelStats(min, max, mean, distinct);
    }

    /// <summary>
    /// Writes a 24-bit BMP, which every image viewer opens and which needs no encoder.
    /// </summary>
    /// <param name="path">File to write.</param>
    /// <param name="pixels">RGBA float pixels.</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="scale">
    /// Divisor applied before clamping, so a target holding values above 1 stays legible. Without it a
    /// half-float buffer writes out as a flat saturated block and reads as though it held two values.
    /// </param>
    /// <param name="alphaOnly">
    /// Write the alpha channel as grey instead of the colour channels. Alpha regularly carries its own
    /// unrelated quantity - roughness, an id, a mask - and a colour-only image simply discards it.
    /// </param>
    private static void WriteBmp(string path, float[] pixels, int width, int height, float scale, bool alphaOnly)
    {
        var rowBytes = ((width * 3) + 3) & ~3;   // BMP rows are padded to 4 bytes
        var imageBytes = rowBytes * height;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);

        w.Write((ushort)0x4D42);            // "BM"
        w.Write(54 + imageBytes);
        w.Write(0);
        w.Write(54);
        w.Write(40);                        // BITMAPINFOHEADER
        w.Write(width);
        w.Write(height);
        w.Write((ushort)1);
        w.Write((ushort)24);
        w.Write(0);
        w.Write(imageBytes);
        w.Write(2835);
        w.Write(2835);
        w.Write(0);
        w.Write(0);

        var row = new byte[rowBytes];
        for (var y = height - 1; y >= 0; y--)   // BMP rows run bottom-up
        {
            Array.Clear(row);
            for (var x = 0; x < width; x++)
            {
                var o = ((y * width) + x) * 4;

                if (alphaOnly)
                {
                    var a = ToByte(pixels[o + 3] * scale);
                    row[(x * 3) + 0] = a;
                    row[(x * 3) + 1] = a;
                    row[(x * 3) + 2] = a;
                    continue;
                }

                row[(x * 3) + 0] = ToByte(pixels[o + 2] * scale);
                row[(x * 3) + 1] = ToByte(pixels[o + 1] * scale);
                row[(x * 3) + 2] = ToByte(pixels[o + 0] * scale);
            }

            w.Write(row);
        }
    }

    /// <summary>Clamps a channel to a displayable byte.</summary>
    private static byte ToByte(float v) => float.IsNaN(v) ? (byte)0 : (byte)(Math.Clamp(v, 0f, 1f) * 255f);

    /// <summary>Short format name for the report.</summary>
    private static string FormatShort(DXGI_FORMAT format) => format switch
    {
        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM => "B8G8R8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM => "R8G8B8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT => "R16G16B16A16_FLOAT",
        _ => format.ToString(),
    };
}
