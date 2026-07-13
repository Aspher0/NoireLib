using System;
using System.Collections.Generic;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// CPU readback of individual depth-texture texels (calibration + probe ground truth).
/// Depth resources must be copied whole — a full staging copy per readback, so callers throttle.
/// </summary>
internal static unsafe class DepthReadback
{
    /// <summary>
    /// Copies a depth texture to staging and reads it back at the given display positions.
    /// Returns null when the copy/map fails; individual unreadable texels come back as NaN.
    /// </summary>
    public static float[]? TryReadAtPoints(RenderDevice device, in GameRenderSources.DepthTextureInfo info, IReadOnlyList<Vector2> screens, Vector2 displaySize, out string description)
    {
        description = "unavailable";
        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)info.Texture, out var source))
            return null;

        using (source)
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);
            description = $"{desc.Format}, {info.ActualWidth}x{info.ActualHeight} (alloc {info.AllocatedWidth}x{info.AllocatedHeight})";

            var stagingDesc = desc;
            stagingDesc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            stagingDesc.MiscFlags = 0;

            ComPtr<ID3D11Texture2D> staging = default;
            using (staging)
            {
                if (device.Device->CreateTexture2D(&stagingDesc, null, staging.GetAddressOf()) < 0)
                    return null;

                var ctx = device.Context;
                ctx->CopyResource((ID3D11Resource*)staging.Get(), (ID3D11Resource*)source.Get());

                D3D11_MAPPED_SUBRESOURCE mapped;
                if (ctx->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0)
                    return null;

                try
                {
                    var values = new float[screens.Count];
                    for (var i = 0; i < screens.Count; i++)
                    {
                        // displayUv × ActualSize = the texel the shader's scaled sample lands on.
                        var px = Math.Clamp((int)(screens[i].X / displaySize.X * info.ActualWidth), 0, (int)info.AllocatedWidth - 1);
                        var py = Math.Clamp((int)(screens[i].Y / displaySize.Y * info.ActualHeight), 0, (int)info.AllocatedHeight - 1);
                        values[i] = ReadDepthTexel(mapped, desc.Format, px, py) ?? float.NaN;
                    }

                    return values;
                }
                finally
                {
                    ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
                }
            }
        }
    }

    private static float? ReadDepthTexel(in D3D11_MAPPED_SUBRESOURCE mapped, DXGI_FORMAT format, int x, int y)
    {
        var row = (byte*)mapped.pData + (nint)y * (nint)mapped.RowPitch;
        switch (format)
        {
            case DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS:
            case DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R24_UNORM_X8_TYPELESS:
                var raw = *(uint*)(row + x * 4L);
                return (raw & 0x00FFFFFF) / 16777215f;
            case DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS:
            case DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT:
                return *(float*)(row + x * 4L);
            case DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS:
            case DXGI_FORMAT.DXGI_FORMAT_D16_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R16_UNORM:
                return *(ushort*)(row + x * 2L) / 65535f;
            case DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS:
            case DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS:
                return *(float*)(row + x * 8L);
            default:
                return null;
        }
    }
}
