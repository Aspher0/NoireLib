using NoireLib.Helpers;
using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// The game writes no UI coverage alpha. The present buffer is copied before and after the native UI, and every changed pixel is UI.
internal sealed unsafe class UiDiffMask : IDisposable
{
    private ComPtr<ID3D11Texture2D> beforeTex, afterTex;
    private ComPtr<ID3D11ShaderResourceView> beforeSrv, afterSrv;
    private uint width, height;
    private DXGI_FORMAT format;
    private bool beforeCaptured;

    public ID3D11ShaderResourceView* BeforeSrv => beforeSrv.Get();

    public ID3D11ShaderResourceView* AfterSrv => afterSrv.Get();

    public ID3D11Texture2D* BeforeTexture => beforeTex.Get();

    public ID3D11Texture2D* AfterTexture => afterTex.Get();

    public DXGI_FORMAT Format => format;

    public uint Width => width;

    public uint Height => height;

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

    public bool CaptureAfter(RenderDevice device, ID3D11DeviceContext* ctx, nint presentBufferResource)
    {
        if (!beforeCaptured)
            return false;

        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)presentBufferResource, out var source))
            return false;

        using (source)
        {
            if (!EnsureTargets(device, source.Get()) || !beforeCaptured)
                return false;

            ctx->CopyResource((ID3D11Resource*)afterTex.Get(), (ID3D11Resource*)source.Get());
            return true;
        }
    }

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

    public void Dispose() => Release();
}

// If nearly every sample differs, the present buffer is transformed after the injection point and the mask disables itself.
internal sealed unsafe class UiDiffMaskHealth : IDisposable
{
    private const int GridX = 6, GridY = 4;
    private const int SampleCount = GridX * GridY;
    private const int CheckIntervalFrames = 120;
    // Raising it would read semi-transparent HUD panels as untouched.
    private const float TouchedThreshold = 1f / 255f;

    // A one-step bar would let any faint full-screen effect disable masking.
    private const float TransformThreshold = 0.02f;
    private const int SuspiciousChecksToDisable = 3;

    private ComPtr<ID3D11Texture2D> beforeStaging, afterStaging;
    private DXGI_FORMAT stagingFormat;
    private long lastCheckFrame = FrameThrottler.Never;
    private bool copyPending;
    private int consecutiveSuspicious;
    private bool disabledLogged;

    public bool DiffUsable { get; private set; } = true;

    public string Description { get; private set; } = "unchecked";

    public float[]? LastSamples { get; private set; }

    public void Update(RenderDevice device, ID3D11DeviceContext* ctx, UiDiffMask mask, long frameId)
    {
        if (mask.BeforeTexture == null || mask.AfterTexture == null || mask.Width == 0 || mask.Height == 0)
            return;

        if (!FrameThrottler.TryPass(frameId, ref lastCheckFrame, CheckIntervalFrames))
            return;

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

        // The texels queued on the previous check are done. DO_NOT_WAIT never stalls.
        if (copyPending)
            TryEvaluate(ctx);

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
            return;

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
                        "Draw3D: the pre-UI and post-UI snapshots of the present buffer differ everywhere. The present buffer is " +
                        "transformed after the injection point. Keeping the UI on top is disabled. Run '/noire3d uimask' and report the log.", "Draw3D");
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

    public void Dispose()
    {
        beforeStaging.Dispose();
        beforeStaging = default;
        afterStaging.Dispose();
        afterStaging = default;
        copyPending = false;
    }
}
