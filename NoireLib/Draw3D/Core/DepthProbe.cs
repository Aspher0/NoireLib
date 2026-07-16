using System;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Throttled, allocation-free CPU readback of a single depth texel, for the obstacle-occlusion hover test. A depth
/// resource can only be copied whole (D3D forbids a sub-region copy of a depth-stencil texture), so the naive
/// "make a staging texture, copy, map, read one texel, destroy" would both churn GPU memory and stall the pipeline
/// on every hover frame: that is what froze and then crashed the device when occlusion was on. This keeps <b>one</b>
/// staging copy of the depth texture, recreated only when its size or format changes, and reads it one cycle late
/// with a non-blocking map (the same deferred pattern the UI-mask health check uses), so a probe never allocates and
/// never waits on the GPU. Render-thread only (uses the immediate context); released with the renderer.
/// </summary>
internal sealed unsafe class DepthProbe : IDisposable
{
    private ComPtr<ID3D11Texture2D> staging;
    private uint width, height;
    private DXGI_FORMAT format;
    private bool copyPending;
    private GameRenderSources.DepthTextureInfo pendingInfo;
    private Vector2 pendingDisplaySize;

    /// <summary>
    /// Returns the depth texel under <paramref name="screenPx"/> from the previous cycle's whole-texture copy
    /// (non-blocking), then queues a fresh copy for next time. False until a copy has completed, on a resource
    /// mismatch, or when the texel is unreadable/open-sky. The sample is the surface's NDC z.
    /// </summary>
    public bool TrySample(RenderDevice device, in GameRenderSources.DepthTextureInfo info, Vector2 screenPx, Vector2 displaySize, out float sample)
    {
        sample = float.NaN;
        var got = false;

        // 1. Read the copy queued last cycle. At the callers' throttle it is long finished, so DO_NOT_WAIT never stalls.
        if (copyPending && staging.Get() != null)
        {
            got = TryReadPending(device, screenPx, out sample);
            copyPending = false;
        }

        // 2. Match the staging copy to the live depth texture (recreate only on change), then queue this frame's copy.
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
            return false; // still in flight (essentially never at the callers' throttle): read on the next cycle

        try
        {
            // displayUv * ActualSize = the texel the shader's scaled sample lands on (mirrors DepthReadback).
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

    /// <summary>Releases the staging texture (recreated on the next sample). Drops any pending copy.</summary>
    public void Release()
    {
        staging.Dispose();
        staging = default;
        width = height = 0;
        format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        copyPending = false;
    }

    /// <inheritdoc/>
    public void Dispose() => Release();
}
