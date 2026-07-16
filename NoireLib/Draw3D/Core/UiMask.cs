using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// The per-pixel "game UI on top" source for the over-everything composite, built by difference rather than by
/// reading a coverage channel: the game's present-composition buffer is copied once at the pre-UI injection point
/// (world image, no UI) and once at present time (the same buffer, now with the UI drawn into it). Any pixel whose
/// colour changed between the two is a pixel the native UI painted, so the difference IS the UI - letter-exact,
/// antialiased edges included, with no rectangles anywhere.
/// <br/>
/// This replaces an earlier design that masked by the backbuffer's alpha channel on the assumption it carried UI
/// coverage. FFXIV writes no such coverage, so that mask was inert in every frame it ever ran. Both snapshots here
/// are of the same texture, so they always agree on format and resolution and the difference needs no rescaling.
/// </summary>
internal sealed unsafe class UiDiffMask : IDisposable
{
    private ComPtr<ID3D11Texture2D> beforeTex, afterTex;
    private ComPtr<ID3D11ShaderResourceView> beforeSrv, afterSrv;
    private uint width, height;
    private DXGI_FORMAT format;
    private bool beforeCaptured;

    /// <summary>The pre-UI snapshot's SRV, or null when unavailable.</summary>
    public ID3D11ShaderResourceView* BeforeSrv => beforeSrv.Get();

    /// <summary>The post-UI snapshot's SRV, or null when unavailable.</summary>
    public ID3D11ShaderResourceView* AfterSrv => afterSrv.Get();

    /// <summary>The pre-UI snapshot texture (health probe readback source).</summary>
    public ID3D11Texture2D* BeforeTexture => beforeTex.Get();

    /// <inheritdoc cref="BeforeTexture"/>
    public ID3D11Texture2D* AfterTexture => afterTex.Get();

    /// <summary>Snapshot format (health probe decoding).</summary>
    public DXGI_FORMAT Format => format;

    /// <summary>Snapshot width/height in pixels.</summary>
    public uint Width => width;

    /// <inheritdoc cref="Width"/>
    public uint Height => height;

    /// <summary>
    /// Copies the present buffer as it stands before the native UI is drawn. Called from the render thread at the
    /// injection point. Returns false (no mask this frame) when anything is incompatible - never throws.
    /// </summary>
    public bool CaptureBefore(RenderDevice device, ID3D11DeviceContext* ctx, nint presentBufferResource)
    {
        beforeCaptured = false;
        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)presentBufferResource, out var source))
            return false;

        using (source)
        {
            if (!EnsureTargets(device, source.Get()))
                return false;

            ctx->CopyResource((ID3D11Resource*)beforeTex.Get(), (ID3D11Resource*)source.Get());
            beforeCaptured = true;
            return true;
        }
    }

    /// <summary>
    /// Copies the same present buffer at present time, with the native UI now drawn into it. Only meaningful when
    /// <see cref="CaptureBefore"/> succeeded on this same frame - without a "before" there is nothing to difference
    /// against, so this reports false and the layer composites unmasked.
    /// </summary>
    public bool CaptureAfter(RenderDevice device, ID3D11DeviceContext* ctx, nint presentBufferResource)
    {
        if (!beforeCaptured)
            return false;

        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)presentBufferResource, out var source))
            return false;

        using (source)
        {
            // A resize between the two snapshots would leave them describing different viewports; EnsureTargets
            // reallocates and drops the stale "before", so the frame goes unmasked rather than differencing garbage.
            if (!EnsureTargets(device, source.Get()) || !beforeCaptured)
                return false;

            ctx->CopyResource((ID3D11Resource*)afterTex.Get(), (ID3D11Resource*)source.Get());
            return true;
        }
    }

    /// <summary>Clears the per-frame "before" flag: a frame whose injection point never fired must not mask.</summary>
    public void EndFrame() => beforeCaptured = false;

    private bool EnsureTargets(RenderDevice device, ID3D11Texture2D* source)
    {
        D3D11_TEXTURE2D_DESC desc;
        source->GetDesc(&desc);

        if (beforeTex.Get() != null && desc.Width == width && desc.Height == height && desc.Format == format)
            return true;

        Release();

        var copyDesc = new D3D11_TEXTURE2D_DESC
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
        };

        if (device.Device->CreateTexture2D(&copyDesc, null, beforeTex.GetAddressOf()) < 0
            || device.Device->CreateTexture2D(&copyDesc, null, afterTex.GetAddressOf()) < 0
            || device.Device->CreateShaderResourceView((ID3D11Resource*)beforeTex.Get(), null, beforeSrv.GetAddressOf()) < 0
            || device.Device->CreateShaderResourceView((ID3D11Resource*)afterTex.Get(), null, afterSrv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        width = desc.Width;
        height = desc.Height;
        format = desc.Format;
        return true;
    }

    /// <summary>Releases GPU objects (recreated by the next capture).</summary>
    public void Release()
    {
        beforeSrv.Dispose();
        beforeSrv = default;
        afterSrv.Dispose();
        afterSrv = default;
        beforeTex.Dispose();
        beforeTex = default;
        afterTex.Dispose();
        afterTex = default;
        width = height = 0;
        format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        beforeCaptured = false;
    }

    /// <inheritdoc/>
    public void Dispose() => Release();
}

/// <summary>
/// Self-check for the difference mask, and the answer to "is UI protection actually doing anything". Samples a
/// sparse screen grid from both snapshots every ~2 seconds (stall-free: the staging copies are mapped one
/// check-cycle later) and reports what fraction of samples the UI changed.
/// <br/>
/// Two failure modes matter, and they are opposites. If virtually every sample differs, the two snapshots are not
/// the same image - the present buffer is being transformed on its way to the screen - and differencing them would
/// read as "UI everywhere" and erase the whole layer; the mask disables itself. If no sample ever differs across
/// many checks, the injection point is not landing where the UI is drawn and the mask is inert; that is reported
/// rather than disabled, because "no UI on screen right now" looks identical and is perfectly normal.
/// </summary>
internal sealed unsafe class UiDiffMaskHealth : IDisposable
{
    private const int GridX = 6, GridY = 4;
    private const int SampleCount = GridX * GridY;
    private const int CheckIntervalFrames = 120;
    // "The UI touched this sample": one 8-bit step. Untouched pixels are bit-identical between the snapshots, so
    // anything above zero is the UI; this only guards float-format rounding, and must not be raised into "faint UI
    // reads as none" - that is the mistake that let the layer bleed through semi-transparent HUD panels.
    private const float TouchedThreshold = 1f / 255f;

    // "This sample changed grossly": the bar for deciding the two snapshots are not the same image at all. Kept
    // deliberately coarse and separate from TouchedThreshold: the failure it guards against is the present buffer
    // being transformed (tonemapped, rescaled) after the injection point, which moves pixels far more than a HUD
    // panel does. Judging that at one 8-bit step would let any faint full-screen effect disable masking entirely.
    private const float TransformThreshold = 0.02f;
    private const int SuspiciousChecksToDisable = 3;

    private ComPtr<ID3D11Texture2D> beforeStaging, afterStaging;
    private DXGI_FORMAT stagingFormat;
    private long lastCheckFrame = long.MinValue;
    private bool copyPending;
    private int consecutiveSuspicious;
    private bool disabledLogged;

    /// <summary>False when the difference was judged unusable (mask must not be applied).</summary>
    public bool DiffUsable { get; private set; } = true;

    /// <summary>Human-readable state for stats/probe.</summary>
    public string Description { get; private set; } = "unchecked";

    /// <summary>Latest per-sample difference magnitudes (probe output), or null before the first completed check.</summary>
    public float[]? LastSamples { get; private set; }

    /// <summary>Runs one throttled check step: read the previous cycle's texels, then queue this frame's.</summary>
    public void Update(RenderDevice device, ID3D11DeviceContext* ctx, UiDiffMask mask, long frameId)
    {
        if (mask.BeforeTexture == null || mask.AfterTexture == null || mask.Width == 0 || mask.Height == 0)
            return;

        // Overflow-safe throttle: never subtract the long.MinValue "never checked" sentinel - frameId - long.MinValue
        // overflows negative and always reads as "throttled", which would wedge the self-check off forever.
        if (lastCheckFrame != long.MinValue && frameId - lastCheckFrame < CheckIntervalFrames)
            return;

        lastCheckFrame = frameId;

        if (beforeStaging.Get() != null && stagingFormat != mask.Format)
        {
            beforeStaging.Dispose();
            beforeStaging = default;
            afterStaging.Dispose();
            afterStaging = default;
            copyPending = false;
        }

        if (beforeStaging.Get() == null)
        {
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = SampleCount,
                Height = 1,
                MipLevels = 1,
                ArraySize = 1,
                Format = mask.Format,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            };
            if (device.Device->CreateTexture2D(&desc, null, beforeStaging.GetAddressOf()) < 0
                || device.Device->CreateTexture2D(&desc, null, afterStaging.GetAddressOf()) < 0)
            {
                beforeStaging.Dispose();
                beforeStaging = default;
                return;
            }

            stagingFormat = mask.Format;
            copyPending = false;
        }

        // 1. Read the texels queued on the previous check - long done by now, so DO_NOT_WAIT never stalls.
        if (copyPending)
            TryEvaluate(ctx);

        // 2. Queue this check's texels from both snapshots at identical points.
        var box = new D3D11_BOX { front = 0, back = 1 };
        for (var i = 0; i < SampleCount; i++)
        {
            var gx = i % GridX;
            var gy = i / GridX;
            var px = (uint)((mask.Width - 1) * (0.1f + 0.8f * gx / (GridX - 1)));
            var py = (uint)((mask.Height - 1) * (0.15f + 0.7f * gy / (GridY - 1)));
            box.left = px;
            box.right = px + 1;
            box.top = py;
            box.bottom = py + 1;
            ctx->CopySubresourceRegion((ID3D11Resource*)beforeStaging.Get(), 0, (uint)i, 0, 0, (ID3D11Resource*)mask.BeforeTexture, 0, &box);
            ctx->CopySubresourceRegion((ID3D11Resource*)afterStaging.Get(), 0, (uint)i, 0, 0, (ID3D11Resource*)mask.AfterTexture, 0, &box);
        }

        copyPending = true;
    }

    private void TryEvaluate(ID3D11DeviceContext* ctx)
    {
        const uint DoNotWait = (uint)D3D11_MAP_FLAG.D3D11_MAP_FLAG_DO_NOT_WAIT;
        D3D11_MAPPED_SUBRESOURCE mappedBefore, mappedAfter;
        if (ctx->Map((ID3D11Resource*)beforeStaging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, DoNotWait, &mappedBefore) < 0)
            return; // still in flight (essentially never) - evaluate on the next cycle

        if (ctx->Map((ID3D11Resource*)afterStaging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, DoNotWait, &mappedAfter) < 0)
        {
            ctx->Unmap((ID3D11Resource*)beforeStaging.Get(), 0);
            return;
        }

        var samples = new float[SampleCount];
        try
        {
            for (var i = 0; i < SampleCount; i++)
                samples[i] = Difference(mappedBefore, mappedAfter, stagingFormat, i);
        }
        finally
        {
            ctx->Unmap((ID3D11Resource*)afterStaging.Get(), 0);
            ctx->Unmap((ID3D11Resource*)beforeStaging.Get(), 0);
        }

        LastSamples = samples;

        var changed = 0;
        var transformed = 0;
        var readable = 0;
        foreach (var d in samples)
        {
            if (float.IsNaN(d))
                continue;
            readable++;
            if (d >= TouchedThreshold)
                changed++;
            if (d >= TransformThreshold)
                transformed++;
        }

        if (readable < SampleCount / 2)
        {
            Description = $"unreadable for format {stagingFormat} - mask stays on, unverified";
            return;
        }

        // Every sample moving grossly means the two snapshots are not the same image, not that the UI covers the
        // screen: a HUD dense enough to cover the whole sample grid is possible, but it would have to do so for
        // several seconds running, and it still would not shift every one of them this far.
        var suspicious = transformed >= readable - 1;
        consecutiveSuspicious = suspicious ? consecutiveSuspicious + 1 : 0;

        if (consecutiveSuspicious >= SuspiciousChecksToDisable)
        {
            if (DiffUsable)
            {
                DiffUsable = false;
                if (!disabledLogged)
                {
                    disabledLogged = true;
                    NoireLogger.LogError(
                        "Draw3D: the pre-UI and post-UI snapshots of the present buffer differ everywhere, so their difference cannot " +
                        "identify the game UI - keeping the UI on top is disabled and the layer now draws over it. This means the present " +
                        "buffer is being transformed after the injection point. Run '/noire3d uimask' and report the log.", "Draw3D");
                }
            }

            Description = $"unusable ({transformed}/{readable} samples changed grossly - the snapshots are not the same image)";
        }
        else
        {
            if (!DiffUsable)
            {
                DiffUsable = true;
                NoireLogger.LogInfo("Draw3D: present-buffer snapshots look comparable again - UI-on-top masking re-enabled.", "Draw3D");
            }

            Description = changed == 0
                ? "ok (0 samples UI-covered - normal with no HUD under the sample grid)"
                : $"ok ({changed}/{readable} samples UI-covered)";
        }
    }

    /// <summary>Largest per-channel colour difference between the two snapshots at one sample, or NaN for an undecodable format.</summary>
    private static float Difference(in D3D11_MAPPED_SUBRESOURCE before, in D3D11_MAPPED_SUBRESOURCE after, DXGI_FORMAT format, int index)
    {
        var b = (byte*)before.pData;
        var a = (byte*)after.pData;
        switch (format)
        {
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
            {
                var d = 0f;
                for (var c = 0; c < 3; c++)
                    d = MathF.Max(d, MathF.Abs(b[index * 4 + c] - a[index * 4 + c]) / 255f);
                return d;
            }
            case DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM:
            {
                var pb = *(uint*)(b + index * 4);
                var pa = *(uint*)(a + index * 4);
                var d = 0f;
                for (var c = 0; c < 3; c++)
                {
                    var shift = c * 10;
                    var vb = (pb >> shift) & 0x3FF;
                    var va = (pa >> shift) & 0x3FF;
                    d = MathF.Max(d, MathF.Abs((float)vb - va) / 1023f);
                }
                return d;
            }
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT:
            {
                var d = 0f;
                for (var c = 0; c < 3; c++)
                {
                    var vb = (float)*(Half*)(b + index * 8 + c * 2);
                    var va = (float)*(Half*)(a + index * 8 + c * 2);
                    d = MathF.Max(d, MathF.Abs(vb - va));
                }
                return d;
            }
            default:
                return float.NaN;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        beforeStaging.Dispose();
        beforeStaging = default;
        afterStaging.Dispose();
        afterStaging = default;
        copyPending = false;
    }
}
