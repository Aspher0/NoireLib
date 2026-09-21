using NoireLib.Draw3D.Geometry;
using System;
using System.Collections.Generic;
using System.Numerics;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

// Draws into the game's G-buffer inside its geometry pass. The game's deferred lighting treats the meshes as world geometry.
internal sealed unsafe class GBufferInject : IDisposable
{
    internal readonly record struct Item(
        Mesh Mesh,
        Matrix4x4 World,
        Vector4 Color,
        bool Textured,
        nint Srv,
        nint NormalSrv,
        nint SpecularSrv,
        float NormalStrength,
        Vector4 DyeColorStrength,
        float DyeReference);

    private const int DepthStateCount = 4;

    private const int BlendStateCount = 2;

    private readonly List<Item> queue = new(16);
    private readonly StateGuard guard = new();

    private GpuBuffer? frameCb;
    private GpuBuffer? objectCb;
    private readonly ID3D11DepthStencilState*[] depthStates = new ID3D11DepthStencilState*[DepthStateCount];
    private readonly ID3D11BlendState*[] blendStates = new ID3D11BlendState*[BlendStateCount];
    private ID3D11RasterizerState* rasterState;
    private ID3D11SamplerState* sampler;
    private bool statesReady;

    private static int DepthStateIndex(bool writeDepth, bool writeStencil) => (writeDepth ? 1 : 0) | (writeStencil ? 2 : 0);

    public int LastInjectedCount { get; private set; }

    public bool HasWork => queue.Count > 0;

    public void Enqueue(in Item item) => queue.Add(item);

    public void Clear() => queue.Clear();

    // viewProj is the game's own, already transposed.
    public void Execute(RenderDevice device, ShaderLibrary shaders, in Matrix4x4 viewProj, Draw3DGameLit options)
    {
        if (queue.Count == 0)
            return;

        var ctx = device.Context;

        if (!EnsureResources(device))
        {
            queue.Clear();
            return;
        }

        guard.Capture(ctx);
        try
        {
            var frame = default(FrameCBData);
            frame.ViewProj = viewProj;
            frameCb!.UpdateConstant(ctx, frame);

            var cb = frameCb.Buffer;
            ctx->VSSetConstantBuffers(0, 1, &cb);
            ctx->PSSetConstantBuffers(0, 1, &cb);

            var ocb = objectCb!.Buffer;
            ctx->VSSetConstantBuffers(1, 1, &ocb);
            ctx->PSSetConstantBuffers(1, 1, &ocb);

            ctx->RSSetState(rasterState);

            // World geometry's stencil category is 0x00, the same as an unwritten pixel.
            var stencil = options.Stencil;
            ctx->OMSetDepthStencilState(depthStates[DepthStateIndex(options.WriteDepth, stencil != 0)], stencil);

            var blendFactor = stackalloc float[4] { 0f, 0f, 0f, 0f };
            ctx->OMSetBlendState(blendStates[options.WriteColor ? 1 : 0], blendFactor, 0xFFFFFFFF);
            ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

            var drawn = 0;
            foreach (var item in queue)
            {
                if (Draw(ctx, shaders, device, item, options))
                    drawn++;
            }

            LastInjectedCount = drawn;
        }
        finally
        {
            guard.Restore(ctx);
            queue.Clear();
        }
    }

    private bool Draw(ID3D11DeviceContext* ctx, ShaderLibrary shaders, RenderDevice device, in Item item, Draw3DGameLit options)
    {
        if (item.Mesh is not { IndexCount: > 0 } mesh || mesh.Vb == null || mesh.Ib == null)
            return false;

        var hasMaps = item.NormalSrv != 0 && item.SpecularSrv != 0;
        if (shaders.GetGameGBuffer(device, item.Textured, hasMaps) is not { } pipeline)
            return false;

        // Slot meanings are GameGBuffer.hlsl's.
        var obj = default(ObjectCBData);
        obj.World = Matrix4x4.Transpose(item.World);
        obj.BaseColor = item.Color;
        obj.Params0 = new Vector4(options.MaterialParams, options.MaterialOverride);
        obj.Params1 = options.Misc;
        obj.Params2 = new Vector4(item.NormalStrength, options.ShadingModelId / 255f, item.DyeReference, options.MaterialCeiling);
        obj.Params3 = item.DyeColorStrength;
        obj.OutlineColor = options.AlbedoOverride;
        objectCb!.UpdateConstant(ctx, obj);

        ctx->IASetInputLayout(pipeline.Layout);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        if (item.Textured && item.Srv != 0)
        {
            var srv = (ID3D11ShaderResourceView*)item.Srv;
            ctx->PSSetShaderResources(1, 1, &srv);

            // BaseSamp is register(s1).
            var samp = sampler;
            ctx->PSSetSamplers(1, 1, &samp);
        }

        if (item.NormalSrv != 0 && item.SpecularSrv != 0)
        {
            var aux0 = (ID3D11ShaderResourceView*)item.NormalSrv;
            var aux1 = (ID3D11ShaderResourceView*)item.SpecularSrv;
            ctx->PSSetShaderResources(4, 1, &aux0);  // t4 = normal map
            ctx->PSSetShaderResources(5, 1, &aux1);  // t5 = specular map
        }

        var vb = mesh.Vb;
        var stride = (uint)sizeof(Vertex3D);
        var offset = 0u;
        ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
        ctx->IASetIndexBuffer(mesh.Ib, mesh.IndexFormat, 0);
        ctx->DrawIndexed((uint)mesh.IndexCount, 0, 0);
        return true;
    }

    private bool EnsureResources(RenderDevice device)
    {
        if (statesReady)
            return true;

        frameCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(FrameCBData));
        objectCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(ObjectCBData));

        if (frameCb == null || objectCb == null)
            return false;

        // GREATER_EQUAL: FFXIV renders reversed-Z.
        for (var i = 0; i < DepthStateCount; i++)
        {
            var writeDepth = (i & 1) != 0;
            var writeStencil = (i & 2) != 0;

            var depthDesc = default(D3D11_DEPTH_STENCIL_DESC);
            depthDesc.DepthEnable = 1;
            depthDesc.DepthWriteMask = writeDepth
                ? D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ALL
                : D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ZERO;
            depthDesc.DepthFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_GREATER_EQUAL;
            depthDesc.StencilEnable = (byte)(writeStencil ? 1 : 0);

            depthDesc.StencilReadMask = 0xFF;
            depthDesc.StencilWriteMask = 0xFF;
            depthDesc.FrontFace.StencilFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_ALWAYS;
            depthDesc.FrontFace.StencilPassOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_REPLACE;
            depthDesc.FrontFace.StencilFailOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP;
            depthDesc.FrontFace.StencilDepthFailOp = D3D11_STENCIL_OP.D3D11_STENCIL_OP_KEEP;
            depthDesc.BackFace = depthDesc.FrontFace;

            fixed (ID3D11DepthStencilState** p = &depthStates[i])
            {
                if (device.Device->CreateDepthStencilState(&depthDesc, p) < 0)
                    return false;
            }
        }

        // Each G-buffer target holds a different quantity. Blending would corrupt normals.
        for (var i = 0; i < BlendStateCount; i++)
        {
            var blendDesc = default(D3D11_BLEND_DESC);
            blendDesc.IndependentBlendEnable = 0;
            blendDesc.RenderTarget[0].BlendEnable = 0;
            blendDesc.RenderTarget[0].RenderTargetWriteMask =
                (byte)(i != 0 ? D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL : 0);

            fixed (ID3D11BlendState** p = &blendStates[i])
            {
                if (device.Device->CreateBlendState(&blendDesc, p) < 0)
                    return false;
            }
        }

        var rasterDesc = default(D3D11_RASTERIZER_DESC);
        rasterDesc.FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID;
        rasterDesc.CullMode = D3D11_CULL_MODE.D3D11_CULL_BACK;
        rasterDesc.FrontCounterClockwise = 0;
        rasterDesc.DepthClipEnable = 1;

        fixed (ID3D11RasterizerState** p = &rasterState)
        {
            if (device.Device->CreateRasterizerState(&rasterDesc, p) < 0)
                return false;
        }

        var sampDesc = default(D3D11_SAMPLER_DESC);
        sampDesc.Filter = D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP;
        sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP;
        sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP;
        sampDesc.MaxLOD = float.MaxValue;

        fixed (ID3D11SamplerState** p = &sampler)
        {
            if (device.Device->CreateSamplerState(&sampDesc, p) < 0)
                return false;
        }

        statesReady = true;
        return true;
    }

    public void Dispose()
    {
        queue.Clear();
        statesReady = false;

        frameCb?.Dispose();
        frameCb = null;
        objectCb?.Dispose();
        objectCb = null;

        for (var i = 0; i < depthStates.Length; i++)
        {
            if (depthStates[i] != null) { depthStates[i]->Release(); depthStates[i] = null; }
        }

        for (var i = 0; i < blendStates.Length; i++)
        {
            if (blendStates[i] != null) { blendStates[i]->Release(); blendStates[i] = null; }
        }

        if (rasterState != null) { rasterState->Release(); rasterState = null; }
        if (sampler != null) { sampler->Release(); sampler = null; }
    }
}
