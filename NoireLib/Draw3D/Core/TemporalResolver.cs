using System;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

// Must match ResolveCB in TemporalResolve.hlsl (176 bytes).
[StructLayout(LayoutKind.Sequential)]
internal struct ResolveCBData
{
    public Matrix4x4 InvViewProj;
    public Matrix4x4 PrevViewProj;
    public Vector4 Params;         // x = current weight, y = history valid, zw = 1 / target size
    public Vector4 GameDepthMap;   // game sample = x + y / w, z = near, w = 1 when valid
    public Vector4 GameDepthUv;    // xy = display uv to game depth uv scale, zw = jitter uv offset
}

// Accumulates the layer over the game's jitter cycle. History is dropped wherever the game's surface moved.
internal sealed unsafe class TemporalResolver : IDisposable
{
    private readonly RenderTarget[] colour =
    {
        new(DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT),
        new(DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT),
    };

    private readonly RenderTarget[] gameW =
    {
        new(DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT),
        new(DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT),
    };

    private GpuBuffer? cb;
    private int current;
    private bool historyValid;
    private Matrix4x4 prevViewProj;

    public void Reset() => historyValid = false;

    public ID3D11ShaderResourceView* Resolve(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        ShaderPipeline pipeline,
        StateCache cache,
        ID3D11ShaderResourceView* layerSrv,
        ID3D11ShaderResourceView* layerDepthSrv,
        ID3D11ShaderResourceView* gameDepthSrv,
        Vector4 gameDepthMap,
        Vector4 gameDepthUv,
        uint width,
        uint height,
        in Matrix4x4 viewProj,
        in Matrix4x4 invViewProj,
        float currentWeight)
    {
        if (layerSrv == null || layerDepthSrv == null)
            return null;

        var output = colour[current ^ 1];
        var history = colour[current];
        var outputW = gameW[current ^ 1];
        var historyW = gameW[current];
        var resized = history.Width != width || history.Height != height;
        if (!output.EnsureSize(device, width, height) || !history.EnsureSize(device, width, height)
            || !outputW.EnsureSize(device, width, height) || !historyW.EnsureSize(device, width, height))
        {
            historyValid = false;
            return null;
        }

        if (resized)
            historyValid = false;

        cb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(ResolveCBData));
        var data = new ResolveCBData
        {
            InvViewProj = Matrix4x4.Transpose(invViewProj),
            PrevViewProj = Matrix4x4.Transpose(prevViewProj),
            Params = new Vector4(Math.Clamp(currentWeight, 0.01f, 1f), historyValid ? 1f : 0f, 1f / width, 1f / height),
            GameDepthMap = gameDepthSrv != null ? gameDepthMap : Vector4.Zero,
            GameDepthUv = gameDepthUv,
        };
        cb.UpdateConstant(ctx, in data);

        // The null depth-stencil releases the layer's depth for reading.
        var rtvs = stackalloc ID3D11RenderTargetView*[2] { output.Rtv, outputW.Rtv };
        ctx->OMSetRenderTargets(2, rtvs, null);
        var viewport = new D3D11_VIEWPORT { Width = width, Height = height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)width, bottom = (int)height };
        ctx->RSSetScissorRects(1, &scissor);

        var blendFactor = stackalloc float[4];
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.Opaque), blendFactor, 0xFFFFFFFF);
        ctx->OMSetDepthStencilState(cache.GetDepth(device, DepthKey.Disabled), 0);
        ctx->RSSetState(cache.GetRaster(device, RasterKey.TwoSided));

        ctx->IASetInputLayout(null);
        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        var buffer = cb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &buffer);
        ctx->PSSetConstantBuffers(0, 1, &buffer);
        var srvs = stackalloc ID3D11ShaderResourceView*[5] { layerSrv, history.Srv, layerDepthSrv, gameDepthSrv, historyW.Srv };
        ctx->PSSetShaderResources(0, 5, srvs);
        var sampler = cache.GetSampler(device, SamplerKey.LinearClamp);
        ctx->PSSetSamplers(0, 1, &sampler);

        ctx->Draw(3, 0);

        var nullSrvs = stackalloc ID3D11ShaderResourceView*[5];
        ctx->PSSetShaderResources(0, 5, nullSrvs);

        prevViewProj = viewProj;
        historyValid = true;
        current ^= 1;
        return output.Srv;
    }

    public void Dispose()
    {
        foreach (var target in colour)
            target.Dispose();
        foreach (var target in gameW)
            target.Dispose();

        cb?.Dispose();
        cb = null;
        historyValid = false;
    }
}
