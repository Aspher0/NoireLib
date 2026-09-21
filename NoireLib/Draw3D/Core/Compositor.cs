using System;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

// Must match CompositeCB in Composite.hlsl (4112 bytes).
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CompositeCBData
{
    public Vector4 OpacityProtect; // x = layer opacity, y = ui mask enabled, z = rect count, w = difference gain
    public fixed float Rects[128 * 4];
    public fixed float Factors[128 * 4]; // x = UI visibility inside the rect (1 = UI on top)
}

// Must match OutlineCB in Outline.hlsl (16 bytes).
[StructLayout(LayoutKind.Sequential)]
internal struct OutlineCBData
{
    public Vector4 OutlineParams; // x = width px, yz = 1/viewport
}

// The blend writes RGB only. The target's alpha stays untouched.
internal sealed unsafe class Compositor : IDisposable
{
    // Both snapshots are bit-identical outside the UI. A gentler gain shows the layer through semi-transparent HUD panels.
    private const float UiDiffGain = 255f;

    private GpuBuffer? compositeCb;
    private GpuBuffer? outlineCb;

    // The target is bound before the scene SRV, never as input and output at once.
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

        ctx->IASetInputLayout(null);
        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        var cb = compositeCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var srvs = stackalloc ID3D11ShaderResourceView*[3] { layerSrv, uiBeforeSrv, uiAfterSrv };
        ctx->PSSetShaderResources(0, 3, srvs);
        // s0 point: the UI-mask difference reads exact texels. s1 linear: box-downsamples a supersampled layer.
        var samplers = stackalloc ID3D11SamplerState*[2] { cache.GetSampler(device, SamplerKey.PointClamp), cache.GetSampler(device, SamplerKey.LinearClamp) };
        ctx->PSSetSamplers(0, 2, samplers);

        ctx->Draw(3, 0);
    }

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

        ctx->IASetInputLayout(null);
        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        var cb = outlineCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var srvs = stackalloc ID3D11ShaderResourceView*[2] { maskSrv, visSrv }; // t0 = colour and coverage, t1 = worldVisible
        ctx->PSSetShaderResources(0, 2, srvs);
        var sampler = cache.GetSampler(device, SamplerKey.PointClamp);
        ctx->PSSetSamplers(0, 1, &sampler);

        ctx->Draw(3, 0);

        var nullSrvs = stackalloc ID3D11ShaderResourceView*[2];
        ctx->PSSetShaderResources(0, 2, nullSrvs);
    }

    public void Dispose()
    {
        compositeCb?.Dispose();
        compositeCb = null;
        outlineCb?.Dispose();
        outlineCb = null;
    }
}
