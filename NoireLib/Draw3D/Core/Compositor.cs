using System;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

/// <summary>Composite constants - must match CompositeCB in Composite.hlsl exactly (4112 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CompositeCBData
{
    public Vector4 OpacityProtect; // x = layer opacity, y = ui mask enabled, z = rect count, w = difference gain
    public fixed float Rects[128 * 4];
    public fixed float Factors[128 * 4]; // x of each float4 = UI visibility inside the rect (1 = UI on top)
}

/// <summary>Outline composite constants - must match OutlineCB in Outline.hlsl exactly (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OutlineCBData
{
    public Vector4 OutlineParams; // x = width px, yz = 1/viewport, w unused
}

/// <summary>
/// Blits the premultiplied scene layer onto the target with one fullscreen triangle - Law 11 at the
/// pixel level: the entire visible output of Draw3D reaches the screen without a single ImGui call.<br/>
/// On the over-everything path it also applies per-pixel game-UI-on-top masking (the difference between the
/// pre-UI and post-UI present-buffer snapshots) and the nameplate policy rects in the same pass. The blend
/// writes RGB only, leaving the target's alpha channel untouched.
/// </summary>
internal sealed unsafe class Compositor : IDisposable
{
    /// <summary>
    /// Scales the pre/post-UI colour difference into mask coverage, steeply enough that any pixel the UI touched at
    /// all masks fully: one 8-bit step of change saturates.
    /// <br/>
    /// It can be this aggressive because the difference is not a noisy measurement. Both snapshots are copies of the
    /// same texture taken either side of the game's UI pass, so every pixel the UI did not draw on is bit-identical
    /// between them - there is no noise floor to stay above, and any difference whatsoever is the UI. A gentler gain
    /// only under-reports: a semi-transparent HUD panel over dark scenery shifts the image by a few percent, which a
    /// proportional mask would read as "partly UI" and let the layer bleed through at half strength.
    /// </summary>
    private const float UiDiffGain = 255f;

    private GpuBuffer? compositeCb;
    private GpuBuffer? outlineCb;

    /// <summary>
    /// Composites the scene layer onto the target. Bind order matters: the target RTV is set
    /// <i>before</i> the scene SRV so the runtime never sees the scene texture bound on both ends.<br/>
    /// Pass null snapshots to composite unmasked (the under-UI path, where the game paints over the layer itself).
    /// </summary>
    public void Blit(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        ShaderPipeline pipeline,
        StateCache cache,
        ID3D11ShaderResourceView* layerSrv,
        ID3D11ShaderResourceView* uiBeforeSrv,
        ID3D11ShaderResourceView* uiAfterSrv,
        ID3D11RenderTargetView* targetRtv,
        uint width,
        uint height,
        float layerOpacity,
        Vector4[] protectRects,
        float[] protectFactors,
        int protectRectCount)
    {
        compositeCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(CompositeCBData));

        var masked = uiBeforeSrv != null && uiAfterSrv != null;
        var data = new CompositeCBData
        {
            OpacityProtect = new Vector4(layerOpacity, masked ? 1f : 0f, masked ? protectRectCount : 0, UiDiffGain),
        };
        for (var i = 0; i < protectRectCount && i < 128 && masked; i++)
        {
            data.Rects[i * 4 + 0] = protectRects[i].X;
            data.Rects[i * 4 + 1] = protectRects[i].Y;
            data.Rects[i * 4 + 2] = protectRects[i].Z;
            data.Rects[i * 4 + 3] = protectRects[i].W;
            data.Factors[i * 4] = protectFactors[i];
        }

        compositeCb.UpdateConstant(ctx, in data);

        ctx->OMSetRenderTargets(1, &targetRtv, null);

        var viewport = new D3D11_VIEWPORT { Width = width, Height = height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)width, bottom = (int)height };
        ctx->RSSetScissorRects(1, &scissor);

        var blendFactor = stackalloc float[4];
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.CompositeRgb), blendFactor, 0xFFFFFFFF);
        ctx->OMSetDepthStencilState(cache.GetDepth(device, DepthKey.Disabled), 0);
        ctx->RSSetState(cache.GetRaster(device, RasterKey.TwoSided));

        ctx->IASetInputLayout(null); // SV_VertexID triangle - no vertex buffer
        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        var cb = compositeCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var srvs = stackalloc ID3D11ShaderResourceView*[3] { layerSrv, uiBeforeSrv, uiAfterSrv };
        ctx->PSSetShaderResources(0, 3, srvs);
        // s0 point (the UI-mask difference must read exact texels), s1 linear (box-downsamples a supersampled layer).
        var samplers = stackalloc ID3D11SamplerState*[2] { cache.GetSampler(device, SamplerKey.PointClamp), cache.GetSampler(device, SamplerKey.LinearClamp) };
        ctx->PSSetSamplers(0, 2, samplers);

        ctx->Draw(3, 0);
    }

    /// <summary>
    /// Dilates the outline coverage mask into a real silhouette rim and blends it (premultiplied) onto the scene
    /// layer. Binds the target RTV before the mask SRV so the runtime never sees the mask on both ends.
    /// </summary>
    public void BlitOutline(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        ShaderPipeline pipeline,
        StateCache cache,
        ID3D11ShaderResourceView* maskSrv,
        ID3D11ShaderResourceView* visSrv,
        ID3D11RenderTargetView* targetRtv,
        uint width,
        uint height,
        float outlineWidthPx)
    {
        outlineCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(OutlineCBData));

        var data = new OutlineCBData
        {
            OutlineParams = new Vector4(outlineWidthPx, 1f / width, 1f / height, 0f),
        };
        outlineCb.UpdateConstant(ctx, in data);

        ctx->OMSetRenderTargets(1, &targetRtv, null);

        var viewport = new D3D11_VIEWPORT { Width = width, Height = height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)width, bottom = (int)height };
        ctx->RSSetScissorRects(1, &scissor);

        var blendFactor = stackalloc float[4];
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.Premultiplied), blendFactor, 0xFFFFFFFF);
        ctx->OMSetDepthStencilState(cache.GetDepth(device, DepthKey.Disabled), 0);
        ctx->RSSetState(cache.GetRaster(device, RasterKey.TwoSided));

        ctx->IASetInputLayout(null); // SV_VertexID triangle - no vertex buffer
        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        var cb = outlineCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var srvs = stackalloc ID3D11ShaderResourceView*[2] { maskSrv, visSrv }; // t0 = colour+coverage, t1 = worldVisible
        ctx->PSSetShaderResources(0, 2, srvs);
        var sampler = cache.GetSampler(device, SamplerKey.PointClamp);
        ctx->PSSetSamplers(0, 1, &sampler);

        ctx->Draw(3, 0);

        // Unbind the mask SRVs so they can serve as RTVs again next frame with no read+write hazard.
        var nullSrvs = stackalloc ID3D11ShaderResourceView*[2];
        ctx->PSSetShaderResources(0, 2, nullSrvs);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        compositeCb?.Dispose();
        compositeCb = null;
        outlineCb?.Dispose();
        outlineCb = null;
    }
}
