using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// Four of the five targets share B8G8R8A8_UNORM. The format cannot tell albedo from a normal or a mask.
internal static unsafe class GBufferProbe
{
    private readonly record struct ChannelStats(
        float[] Min,
        float[] Max,
        float[] Mean,
        int DistinctApprox,
        int NanCount,
        int InfCount,
        float DisplayScale);

    private static float[]? scratch;

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
            sb.AppendLine(Dump(device, targets[i], path));
        }

        return sb.ToString();
    }

    internal static string Dump(RenderDevice device, nint resource, string path)
    {
        if (resource == 0 || !ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var source))
            return "not a texture, skipped";

        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            return $"could not create the output folder: {ex.Message}";
        }

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
            try
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

                    WriteBmp(path, pixels, (int)desc.Width, (int)desc.Height, stats.DisplayScale, alphaOnly: false);
                    WriteBmp(AlphaPath(path), pixels, (int)desc.Width, (int)desc.Height, stats.Max[3] > 1.001f ? 1f / stats.Max[3] : 1f, alphaOnly: true);

                    var scaleNote = stats.DisplayScale < 1f ? $"  (image divided by {1f / stats.DisplayScale:F2} to be viewable)" : string.Empty;

                    var badNote = stats.NanCount > 0 || stats.InfCount > 0
                        ? $"\n    NOT FINITE: {stats.NanCount} pixel(s) NaN, {stats.InfCount} infinite - these survive any later multiply"
                        : string.Empty;

                    return $"{FormatShort(desc.Format)}, {desc.Width}x{desc.Height}{scaleNote}\n"
                         + $"    R {stats.Min[0]:F3}..{stats.Max[0]:F3} mean {stats.Mean[0]:F3}   "
                         + $"G {stats.Min[1]:F3}..{stats.Max[1]:F3} mean {stats.Mean[1]:F3}\n"
                         + $"    B {stats.Min[2]:F3}..{stats.Max[2]:F3} mean {stats.Mean[2]:F3}   "
                         + $"A {stats.Min[3]:F3}..{stats.Max[3]:F3} mean {stats.Mean[3]:F3}\n"
                         + $"    about {stats.DistinctApprox} distinct red values{(stats.DistinctApprox <= 8 ? " - few enough to be a mask or an id" : string.Empty)}"
                         + badNote;
                }
                finally
                {
                    ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
                }
            }
            finally
            {
                staging.Dispose();
            }
        }
    }

    // The game's stencil marks only exist between its geometry pass and its light volumes.
    internal static string DumpStencil(RenderDevice device, nint resource, string path)
    {
        if (resource == 0 || !ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var source))
            return "no depth-stencil";

        using (source)
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);

            var stride = desc.Format switch
            {
                DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT => 4,
                DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT => 8,
                _ => 0,
            };

            if (stride == 0)
                return $"{desc.Format} carries no stencil plane";

            var offset = stride - 1 == 3 ? 3 : 4; // R24G8: byte 3, R32G8X24: byte 4

            var stagingDesc = desc;
            stagingDesc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            stagingDesc.MiscFlags = 0;

            ComPtr<ID3D11Texture2D> staging = default;
            try
            {
                if (device.Device->CreateTexture2D(&stagingDesc, null, staging.GetAddressOf()) < 0)
                    return "stencil staging allocation failed";

                var ctx = device.Context;
                ctx->CopyResource((ID3D11Resource*)staging.Get(), (ID3D11Resource*)source.Get());

                D3D11_MAPPED_SUBRESOURCE mapped;
                if (ctx->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0)
                    return "stencil map failed";

                try
                {
                    var width = (int)desc.Width;
                    var height = (int)desc.Height;
                    var needed = width * height * 4;

                    if (scratch is null || scratch.Length < needed)
                        scratch = new float[needed];

                    var census = new int[256];
                    var row = (byte*)mapped.pData;

                    for (var y = 0; y < height; y++)
                    {
                        var src = row + (y * mapped.RowPitch);
                        for (var x = 0; x < width; x++)
                        {
                            var value = src[(x * stride) + offset];
                            census[value]++;

                            StoreGrey(scratch, ((y * width) + x) * 4, value / 255f);
                        }
                    }

                    WriteBmp(path, scratch, width, height, 1f, alphaOnly: false);

                    var sb = new StringBuilder();
                    sb.Append($"stencil {desc.Width}x{desc.Height}: ");
                    for (var v = 0; v < census.Length; v++)
                    {
                        if (census[v] > 0)
                            sb.Append($"0x{v:X2}={census[v]} ");
                    }

                    return sb.ToString();
                }
                finally
                {
                    ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
                }
            }
            finally
            {
                staging.Dispose();
            }
        }
    }

    internal static bool TrySampleAt(RenderDevice device, IReadOnlyList<nint> targets, int x, int y, int patch, List<Vector4> samples)
    {
        samples.Clear();
        if (targets.Count == 0)
            return false;

        foreach (var target in targets)
        {
            if (!TrySampleOne(device, target, x, y, patch, out var value))
                return false;

            samples.Add(value);
        }

        return true;
    }

    private static bool TrySampleOne(RenderDevice device, nint resource, int x, int y, int patch, out Vector4 value)
    {
        value = default;
        if (resource == 0 || !ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var source))
            return false;

        using (source)
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);

            var size = Math.Max(1, patch);
            var left = Math.Clamp(x - (size / 2), 0, (int)desc.Width - 1);
            var top = Math.Clamp(y - (size / 2), 0, (int)desc.Height - 1);
            var right = Math.Min(left + size, (int)desc.Width);
            var bottom = Math.Min(top + size, (int)desc.Height);
            if (right <= left || bottom <= top)
                return false;

            var stagingDesc = desc;
            stagingDesc.Width = (uint)(right - left);
            stagingDesc.Height = (uint)(bottom - top);
            stagingDesc.MipLevels = 1;
            stagingDesc.ArraySize = 1;
            stagingDesc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            stagingDesc.MiscFlags = 0;

            ComPtr<ID3D11Texture2D> staging = default;
            try
            {
                if (device.Device->CreateTexture2D(&stagingDesc, null, staging.GetAddressOf()) < 0)
                    return false;

                var box = new D3D11_BOX
                {
                    left = (uint)left,
                    top = (uint)top,
                    front = 0,
                    right = (uint)right,
                    bottom = (uint)bottom,
                    back = 1,
                };

                var ctx = device.Context;
                ctx->CopySubresourceRegion((ID3D11Resource*)staging.Get(), 0, 0, 0, 0, (ID3D11Resource*)source.Get(), 0, &box);

                D3D11_MAPPED_SUBRESOURCE mapped;
                if (ctx->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0)
                    return false;

                try
                {
                    var pixels = ReadPixels(mapped, stagingDesc, out var supported);
                    if (!supported)
                        return false;

                    var count = (int)(stagingDesc.Width * stagingDesc.Height);
                    var sum = new double[4];
                    for (var i = 0; i < count; i++)
                    {
                        for (var c = 0; c < 4; c++)
                        {
                            var v = pixels[(i * 4) + c];
                            if (!float.IsNaN(v) && !float.IsInfinity(v))
                                sum[c] += v;
                        }
                    }

                    value = new Vector4(
                        (float)(sum[0] / count),
                        (float)(sum[1] / count),
                        (float)(sum[2] / count),
                        (float)(sum[3] / count));
                    return true;
                }
                finally
                {
                    ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
                }
            }
            finally
            {
                staging.Dispose();
            }
        }
    }

    private static string AlphaPath(string path) => Path.ChangeExtension(path, null) + "_alpha.bmp";

    private static float[] ReadPixels(in D3D11_MAPPED_SUBRESOURCE mapped, in D3D11_TEXTURE2D_DESC desc, out bool supported)
    {
        var width = (int)desc.Width;
        var height = (int)desc.Height;
        var needed = width * height * 4;

        if (scratch is null || scratch.Length < needed)
            scratch = new float[needed];

        var pixels = scratch;
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

            case DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT:
                for (var y = 0; y < height; y++)
                {
                    var src = (Half*)(row + (y * mapped.RowPitch));
                    for (var x = 0; x < width; x++)
                        Store(pixels, ((y * width) + x) * 4, (float)src[(x * 2) + 0], (float)src[(x * 2) + 1], 0f);
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT:
                for (var y = 0; y < height; y++)
                {
                    var src = (float*)(row + (y * mapped.RowPitch));
                    for (var x = 0; x < width; x++)
                        StoreGrey(pixels, ((y * width) + x) * 4, src[x]);
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT:
                for (var y = 0; y < height; y++)
                {
                    var src = (Half*)(row + (y * mapped.RowPitch));
                    for (var x = 0; x < width; x++)
                        StoreGrey(pixels, ((y * width) + x) * 4, (float)src[x]);
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R8_UNORM:
                for (var y = 0; y < height; y++)
                {
                    var src = row + (y * mapped.RowPitch);
                    for (var x = 0; x < width; x++)
                        StoreGrey(pixels, ((y * width) + x) * 4, src[x] / 255f);
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM:
                for (var y = 0; y < height; y++)
                {
                    var src = row + (y * mapped.RowPitch);
                    for (var x = 0; x < width; x++)
                        Store(pixels, ((y * width) + x) * 4, src[(x * 2) + 0] / 255f, src[(x * 2) + 1] / 255f, 0f);
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT:
                for (var y = 0; y < height; y++)
                {
                    var src = (uint*)(row + (y * mapped.RowPitch));
                    for (var x = 0; x < width; x++)
                    {
                        var packed = src[x];
                        Store(
                            pixels,
                            ((y * width) + x) * 4,
                            UnpackSmallFloat(packed & 0x7FF, 6),
                            UnpackSmallFloat((packed >> 11) & 0x7FF, 6),
                            UnpackSmallFloat((packed >> 22) & 0x3FF, 5));
                    }
                }

                break;

            case DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM:
                for (var y = 0; y < height; y++)
                {
                    var src = (uint*)(row + (y * mapped.RowPitch));
                    for (var x = 0; x < width; x++)
                    {
                        var packed = src[x];
                        var o = ((y * width) + x) * 4;
                        pixels[o + 0] = (packed & 0x3FF) / 1023f;
                        pixels[o + 1] = ((packed >> 10) & 0x3FF) / 1023f;
                        pixels[o + 2] = ((packed >> 20) & 0x3FF) / 1023f;
                        pixels[o + 3] = ((packed >> 30) & 0x3) / 3f;
                    }
                }

                break;

            default:
                supported = false;
                break;
        }

        return pixels;
    }

    private static void Store(float[] pixels, int offset, float r, float g, float b)
    {
        pixels[offset + 0] = r;
        pixels[offset + 1] = g;
        pixels[offset + 2] = b;
        pixels[offset + 3] = 1f;
    }

    private static void StoreGrey(float[] pixels, int offset, float value) => Store(pixels, offset, value, value, value);

    // Five exponent bits, no sign bit, bias 15.
    private static float UnpackSmallFloat(uint bits, int mantissaBits)
    {
        var exponent = (int)(bits >> mantissaBits) & 0x1F;
        var mantissa = bits & ((1u << mantissaBits) - 1);
        var scale = 1f / (1 << mantissaBits);

        return exponent switch
        {
            0 => mantissa == 0 ? 0f : mantissa * scale * MathF.Pow(2f, -14f),
            0x1F => mantissa == 0 ? float.PositiveInfinity : float.NaN,
            _ => (1f + (mantissa * scale)) * MathF.Pow(2f, exponent - 15),
        };
    }

    private const int HistogramBuckets = 320;

    private const float DisplayPercentile = 0.995f;

    private static ChannelStats Measure(float[] pixels, int width, int height)
    {
        var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
        var max = new[] { float.MinValue, float.MinValue, float.MinValue, float.MinValue };
        var sum = new double[4];
        var finite = new int[4];
        var seen = new bool[256];
        var histogram = new int[HistogramBuckets];
        var nan = 0;
        var inf = 0;
        var histogramTotal = 0;

        var count = width * height;
        for (var i = 0; i < count; i++)
        {
            var pixelNan = false;
            var pixelInf = false;

            for (var c = 0; c < 4; c++)
            {
                var v = pixels[(i * 4) + c];

                if (float.IsNaN(v))
                {
                    if (c < 3)
                        pixelNan = true;
                    continue;
                }

                if (float.IsInfinity(v))
                {
                    if (c < 3)
                        pixelInf = true;
                    continue;
                }

                if (v < min[c]) min[c] = v;
                if (v > max[c]) max[c] = v;
                sum[c] += v;
                finite[c]++;

                if (c < 3 && v > 0f)
                {
                    histogram[BucketOf(v)]++;
                    histogramTotal++;
                }
            }

            if (pixelNan) nan++;
            if (pixelInf) inf++;

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
        {
            mean[c] = finite[c] > 0 ? (float)(sum[c] / finite[c]) : 0f;
            if (min[c] == float.MaxValue) min[c] = 0f;
            if (max[c] == float.MinValue) max[c] = 0f;
        }

        return new ChannelStats(min, max, mean, distinct, nan, inf, PercentileScale(histogram, histogramTotal));
    }

    private static int BucketOf(float value)
    {
        var bucket = (int)((MathF.Log2(value) + 20f) * (HistogramBuckets / 40f));
        return Math.Clamp(bucket, 0, HistogramBuckets - 1);
    }

    private static float PercentileScale(int[] histogram, int total)
    {
        if (total == 0)
            return 1f;

        var target = (int)(total * DisplayPercentile);
        var running = 0;

        for (var i = 0; i < histogram.Length; i++)
        {
            running += histogram[i];
            if (running < target)
                continue;

            var upper = MathF.Pow(2f, ((i + 1) * (40f / HistogramBuckets)) - 20f);
            return upper > 1.001f ? 1f / upper : 1f;
        }

        return 1f;
    }

    private static void WriteBmp(string path, float[] pixels, int width, int height, float scale, bool alphaOnly)
    {
        var rowBytes = ((width * 3) + 3) & ~3;   // padded to 4 bytes
        var imageBytes = rowBytes * height;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);

        w.Write((ushort)0x4D42);
        w.Write(54 + imageBytes);
        w.Write(0);
        w.Write(54);
        w.Write(40);
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
        for (var y = height - 1; y >= 0; y--)   // bottom-up
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

    private static byte ToByte(float v) => float.IsNaN(v) ? (byte)0 : (byte)(Math.Clamp(v, 0f, 1f) * 255f);

    private static string FormatShort(DXGI_FORMAT format) => format switch
    {
        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM => "B8G8R8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM => "R8G8B8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT => "R16G16B16A16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT => "R11G11B10_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT => "R16G16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM => "R8G8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM => "R10G10B10A2_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT => "R32_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT => "R16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R8_UNORM => "R8_UNORM",
        _ => format.ToString(),
    };
}
