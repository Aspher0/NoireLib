using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// The per-pixel "game UI on top" source: a same-format copy of the backbuffer taken right before the
/// layer composites. At that moment the game's frame — world, post-processing AND native UI — is
/// complete, and the backbuffer's alpha channel holds the accumulated UI coverage (the game's UI
/// alpha-blends over an alpha-0 scene). The composite multiplies the layer by (1 − uiAlpha), so
/// nameplate letters, window shadows and chat transparency read on top at pixel granularity —
/// no rectangles, no hooks, no added latency.
/// </summary>
internal sealed unsafe class UiMaskSource : IDisposable
{
    private ComPtr<ID3D11Texture2D> texture;
    private ComPtr<ID3D11ShaderResourceView> srv;
    private uint width, height;
    private DXGI_FORMAT format;

    /// <summary>The copied-backbuffer SRV, or null when unavailable this frame.</summary>
    public ID3D11ShaderResourceView* Srv => srv.Get();

    /// <summary>The copy texture (health probe readback source).</summary>
    public ID3D11Texture2D* Texture => texture.Get();

    /// <summary>Copy format (health probe alpha decoding).</summary>
    public DXGI_FORMAT Format => format;

    /// <summary>Copy width/height in pixels.</summary>
    public uint Width => width;

    /// <inheritdoc cref="Width"/>
    public uint Height => height;

    /// <summary>
    /// Ensures the copy target matches the backbuffer and copies this frame's content into it.
    /// Returns false (mask off this frame) when anything is incompatible — never throws.
    /// </summary>
    public bool EnsureAndCopy(RenderDevice device, ID3D11DeviceContext* ctx, nint backbufferTexture)
    {
        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)backbufferTexture, out var source))
            return false;

        using (source)
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);

            if (texture.Get() == null || desc.Width != width || desc.Height != height || desc.Format != format)
            {
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

                if (device.Device->CreateTexture2D(&copyDesc, null, texture.GetAddressOf()) < 0)
                    return false;
                if (device.Device->CreateShaderResourceView((ID3D11Resource*)texture.Get(), null, srv.GetAddressOf()) < 0)
                {
                    Release();
                    return false;
                }

                width = desc.Width;
                height = desc.Height;
                format = desc.Format;
            }

            ctx->CopyResource((ID3D11Resource*)texture.Get(), (ID3D11Resource*)source.Get());
            return true;
        }
    }

    /// <summary>Releases GPU objects (recreated by the next EnsureAndCopy).</summary>
    public void Release()
    {
        srv.Dispose();
        srv = default;
        texture.Dispose();
        texture = default;
        width = height = 0;
        format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
    }

    /// <inheritdoc/>
    public void Dispose() => Release();
}

/// <summary>
/// Self-check for the backbuffer-alpha assumption. Known failure mode (documented by other overlay
/// libraries too): some upscaling paths fill the scene's alpha with 1.0, which would read as "UI
/// everywhere" and silently erase the whole layer. This samples a sparse screen grid every ~2 seconds
/// (stall-free: the staging copy is mapped one check-cycle later) and disables the per-pixel mask when
/// virtually every sample reads fully covered for several consecutive checks. Self-healing: masking
/// re-enables as soon as the alpha channel looks sane again.
/// </summary>
internal sealed unsafe class UiMaskHealth : IDisposable
{
    private const int GridX = 6, GridY = 4;
    private const int SampleCount = GridX * GridY;
    private const int CheckIntervalFrames = 120;
    private const float CoveredThreshold = 0.98f;
    private const int SuspiciousChecksToDisable = 3;

    private ComPtr<ID3D11Texture2D> staging;
    private DXGI_FORMAT stagingFormat;
    private long lastCheckFrame = long.MinValue;
    private bool copyPending;
    private int consecutiveSuspicious;
    private bool disabledLogged;

    /// <summary>False when the alpha channel was judged unusable (mask must not be applied).</summary>
    public bool AlphaUsable { get; private set; } = true;

    /// <summary>Human-readable state for stats/probe.</summary>
    public string Description { get; private set; } = "unchecked";

    /// <summary>Latest alpha samples (probe output), or null before the first completed check.</summary>
    public float[]? LastSamples { get; private set; }

    /// <summary>Runs one throttled check step: read the previous copy's texels, then queue this frame's.</summary>
    public void Update(RenderDevice device, ID3D11DeviceContext* ctx, UiMaskSource source, long frameId)
    {
        if (source.Texture == null || source.Width == 0 || source.Height == 0)
            return;

        // Overflow-safe throttle: never subtract the long.MinValue "never checked" sentinel —
        // frameId - long.MinValue overflows negative and always reads as "throttled", which would
        // wedge the self-check off forever (Description stuck at "unchecked", auto-disable dead).
        if (lastCheckFrame != long.MinValue && frameId - lastCheckFrame < CheckIntervalFrames)
            return;

        lastCheckFrame = frameId;

        if (staging.Get() != null && stagingFormat != source.Format)
        {
            staging.Dispose();
            staging = default;
            copyPending = false;
        }

        if (staging.Get() == null)
        {
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = SampleCount,
                Height = 1,
                MipLevels = 1,
                ArraySize = 1,
                Format = source.Format,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            };
            if (device.Device->CreateTexture2D(&desc, null, staging.GetAddressOf()) < 0)
                return;

            stagingFormat = source.Format;
            copyPending = false;
        }

        // 1. Read the texels queued on the previous check — long done by now, so DO_NOT_WAIT never stalls.
        if (copyPending)
            TryEvaluate(ctx);

        // 2. Queue this check's texels.
        var box = new D3D11_BOX { front = 0, back = 1 };
        for (var i = 0; i < SampleCount; i++)
        {
            var gx = i % GridX;
            var gy = i / GridX;
            var px = (uint)((source.Width - 1) * (0.1f + 0.8f * gx / (GridX - 1)));
            var py = (uint)((source.Height - 1) * (0.15f + 0.7f * gy / (GridY - 1)));
            box.left = px;
            box.right = px + 1;
            box.top = py;
            box.bottom = py + 1;
            ctx->CopySubresourceRegion((ID3D11Resource*)staging.Get(), 0, (uint)i, 0, 0, (ID3D11Resource*)source.Texture, 0, &box);
        }

        copyPending = true;
    }

    private void TryEvaluate(ID3D11DeviceContext* ctx)
    {
        const uint DoNotWait = (uint)D3D11_MAP_FLAG.D3D11_MAP_FLAG_DO_NOT_WAIT;
        D3D11_MAPPED_SUBRESOURCE mapped;
        var hr = ctx->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, DoNotWait, &mapped);
        if (hr < 0)
            return; // still in flight (essentially never) — evaluate on the next cycle

        var samples = new float[SampleCount];
        try
        {
            for (var i = 0; i < SampleCount; i++)
                samples[i] = ReadAlpha(mapped, stagingFormat, i);
        }
        finally
        {
            ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
        }

        LastSamples = samples;

        var covered = 0;
        var readable = 0;
        foreach (var a in samples)
        {
            if (float.IsNaN(a))
                continue;
            readable++;
            if (a >= CoveredThreshold)
                covered++;
        }

        if (readable < SampleCount / 2)
        {
            Description = $"alpha unreadable for format {stagingFormat} — mask stays on, unverified";
            return;
        }

        var suspicious = covered >= readable - 1;
        consecutiveSuspicious = suspicious ? consecutiveSuspicious + 1 : 0;

        if (consecutiveSuspicious >= SuspiciousChecksToDisable)
        {
            if (AlphaUsable)
            {
                AlphaUsable = false;
                if (!disabledLogged)
                {
                    disabledLogged = true;
                    NoireLogger.LogError(
                        "Draw3D: the backbuffer alpha channel reads fully covered everywhere — per-pixel game-UI-on-top masking disabled " +
                        "(known cause: 3D resolution scaling / upscalers filling alpha). The layer now draws over the game UI. " +
                        "Run '/noire3d probe' and report the log if this looks wrong.", "Draw3D");
                }
            }

            Description = $"alpha unusable ({covered}/{readable} samples fully covered)";
        }
        else
        {
            if (!AlphaUsable)
            {
                AlphaUsable = true;
                NoireLogger.LogInfo("Draw3D: backbuffer alpha looks sane again — per-pixel UI masking re-enabled.", "Draw3D");
            }

            Description = $"ok ({covered}/{readable} samples UI-covered)";
        }
    }

    private static float ReadAlpha(in D3D11_MAPPED_SUBRESOURCE mapped, DXGI_FORMAT format, int index)
    {
        var p = (byte*)mapped.pData;
        switch (format)
        {
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
                return p[index * 4 + 3] / 255f;
            case DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM:
                return (*(uint*)(p + index * 4) >> 30) / 3f;
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT:
                return (float)*(Half*)(p + index * 8 + 6);
            default:
                return float.NaN;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        staging.Dispose();
        staging = default;
        copyPending = false;
    }
}
