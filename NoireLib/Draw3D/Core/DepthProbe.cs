using System;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// D3D forbids a sub-region copy of a depth-stencil texture, and a per-frame create-copy-map-destroy crashes the device.
// One staging copy is kept and read one cycle late. Render thread only.
internal sealed unsafe class DepthProbe : IDisposable
{
    private ComPtr<ID3D11Texture2D> staging;
    private uint width, height;
    private DXGI_FORMAT format;
    private bool copyPending;
    private GameRenderSources.DepthTextureInfo pendingInfo;
    private Vector2 pendingDisplaySize;

    public bool TrySample(RenderDevice device, in GameRenderSources.DepthTextureInfo info, Vector2 screenPx, Vector2 displaySize, out float sample)
    {
        sample = float.NaN;
        var got = false;

        // Last cycle's copy is finished. DO_NOT_WAIT never stalls.
        if (copyPending && staging.Get() != null)
        {
            got = TryReadPending(device, screenPx, out sample);
            copyPending = false;
        }

        if (!EnsureStaging(device, in info) || !ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)info.Texture, out var source))
            return got;

        using (source)
        {
            device.Context->CopyResource((ID3D11Resource*)staging.Get(), (ID3D11Resource*)source.Get());
            copyPending = true;
            pendingInfo = info;
            pendingDisplaySize = displaySize;
        }

        return got;
    }

    private bool TryReadPending(RenderDevice device, Vector2 screenPx, out float sample)
    {
        sample = float.NaN;
        var ds = pendingDisplaySize;
        if (ds.X <= 0f || ds.Y <= 0f)
            return false;

        const uint doNotWait = (uint)D3D11_MAP_FLAG.D3D11_MAP_FLAG_DO_NOT_WAIT;
        var ctx = device.Context;
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (ctx->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, doNotWait, &mapped) < 0)
            return false;

        try
        {
            // Mirrors DepthReadback.
            var px = Math.Clamp((int)(screenPx.X / ds.X * pendingInfo.ActualWidth), 0, (int)pendingInfo.AllocatedWidth - 1);
            var py = Math.Clamp((int)(screenPx.Y / ds.Y * pendingInfo.ActualHeight), 0, (int)pendingInfo.AllocatedHeight - 1);
            var value = DepthReadback.ReadDepthTexel(mapped, format, px, py);
            if (value is { } v && !float.IsNaN(v))
            {
                sample = v;
                return true;
            }

            return false;
        }
        finally
        {
            ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
        }
    }

    private bool EnsureStaging(RenderDevice device, in GameRenderSources.DepthTextureInfo info)
    {
        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)info.Texture, out var source))
            return false;

        using (source)
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);

            if (staging.Get() != null && desc.Width == width && desc.Height == height && desc.Format == format)
                return true;

            Release();

            var stagingDesc = desc;
            stagingDesc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
            stagingDesc.MiscFlags = 0;

            if (device.Device->CreateTexture2D(&stagingDesc, null, staging.GetAddressOf()) < 0)
            {
                staging = default;
                return false;
            }

            width = desc.Width;
            height = desc.Height;
            format = desc.Format;
            return true;
        }
    }

    public void Release()
    {
        staging.Dispose();
        staging = default;
        width = height = 0;
        format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        copyPending = false;
    }

    public void Dispose() => Release();
}
