using System;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

/// <summary>Composite constants - must match CompositeCB in Composite.hlsl exactly (4112 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CompositeCBData
{
    public Vector4 OpacityProtect; // x = layer opacity, y = ui mask enabled, z = rect count
    public fixed float Rects[128 * 4];
    public fixed float Factors[128 * 4]; // x of each float4 = UI visibility inside the rect (1 = UI on top)
}

/// <summary>
/// Blits the premultiplied scene layer onto the backbuffer with one fullscreen triangle - Law 11 at
/// the pixel level: the entire visible output of Draw3D reaches the screen without a single ImGui call.<br/>
/// Applies per-pixel game-UI-on-top masking (backbuffer alpha) and the nameplate policy rects in the
/// same pass. The blend writes RGB only - the backbuffer's alpha channel is the mask source and is
/// never polluted.
/// </summary>
internal sealed unsafe class Compositor : IDisposable
{
    private GpuBuffer? compositeCb;

    /// <summary>
    /// Composites the scene layer onto the target. Bind order matters: the backbuffer RTV is set
    /// <i>before</i> the scene SRV so the runtime never sees the scene texture bound on both ends.
    /// </summary>
    public void Blit(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        ShaderPipeline pipeline,
        StateCache cache,
        ID3D11ShaderResourceView* layerSrv,
        ID3D11ShaderResourceView* uiMaskSrv,
        ID3D11RenderTargetView* targetRtv,
        uint width,
        uint height,
        float layerOpacity,
        Vector4[] protectRects,
        float[] protectFactors,
        int protectRectCount)
    {
        compositeCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(CompositeCBData));

        var data = new CompositeCBData
        {
            OpacityProtect = new Vector4(layerOpacity, uiMaskSrv != null ? 1f : 0f, protectRectCount, 0f),
        };
        for (var i = 0; i < protectRectCount && i < 128; i++)
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
        var srvs = stackalloc ID3D11ShaderResourceView*[2] { layerSrv, uiMaskSrv };
        ctx->PSSetShaderResources(0, 2, srvs);
        var sampler = cache.GetSampler(device, SamplerKey.PointClamp);
        ctx->PSSetSamplers(0, 1, &sampler);

        ctx->Draw(3, 0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        compositeCb?.Dispose();
        compositeCb = null;
    }
}
