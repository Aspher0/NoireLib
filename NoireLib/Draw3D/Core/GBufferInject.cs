using NoireLib.Draw3D.Geometry;
using System;
using System.Collections.Generic;
using System.Numerics;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Draws meshes into the GAME's G-buffer, inside the game's own geometry pass, so the game's deferred lighting
/// pass lights them.<br/>
/// <b>What this buys.</b> Deferred lighting runs over pixels rather than objects: the lighting shader reads
/// albedo, normal and depth out of the G-buffer and cannot tell which object wrote any given pixel. Geometry
/// placed there is therefore lit by every lamp, the sun and the ambient term, receives shadow-map lookups, is
/// occluded by walls at pixel precision, and passes through the game's tonemapping and exposure - all of it
/// identical to the wall beside it by construction rather than by approximation.<br/>
/// <b>What it costs.</b> Everything that lives in Draw3D's own pass is unavailable here: outlines and rims,
/// transparency, ground decals, and drawing above everything. Deferred geometry is opaque. An object needing
/// any of those stays on the normal path.<br/>
/// <b>What it does not do.</b> Cast shadows. Shadow maps are rendered in an earlier depth-only pass that this
/// geometry is not part of, so an injected object is lit and shadowed correctly but casts nothing.
/// </summary>
/// <remarks>
/// This is the only part of Draw3D that draws inside the game's frame rather than into its own target, so a
/// failure here corrupts the game's rendering rather than Draw3D's. Two rules follow, and neither is optional:
/// every pipeline slot touched goes through <see cref="StateGuard"/>, and the render targets are never
/// re-bound - the callback runs with the game's own targets already bound and must leave them that way.
/// </remarks>
internal sealed unsafe class GBufferInject : IDisposable
{
    /// <summary>
    /// One mesh queued for injection this frame. Everything here is per mesh; what gets written into the
    /// channels the game authored is per frame and lives on <see cref="Draw3DGameLit"/>.
    /// </summary>
    /// <param name="Mesh">The geometry.</param>
    /// <param name="World">Its world transform.</param>
    /// <param name="Color">Albedo tint, multiplied into the vertex colour.</param>
    /// <param name="Textured">Whether the mesh samples a base texture into its albedo.</param>
    /// <param name="Srv">The base texture, when textured.</param>
    /// <param name="NormalSrv">The material's normal map, or 0. Supplies the relief the game's own normal buffer shows.</param>
    /// <param name="SpecularSrv">The material's specular map, or 0. Supplies rtv1's per-pixel material response.</param>
    /// <param name="NormalStrength">How strongly the normal map perturbs the surface normal.</param>
    internal readonly record struct Item(
        Mesh Mesh,
        Matrix4x4 World,
        Vector4 Color,
        bool Textured,
        nint Srv,
        nint NormalSrv,
        nint SpecularSrv,
        float NormalStrength);

    /// <summary>Depth-state variants, indexed by <see cref="DepthStateIndex"/>: depth write on or off, stencil stamp on or off.</summary>
    private const int DepthStateCount = 4;

    /// <summary>Blend-state variants: writing the five targets, or writing none of them.</summary>
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

    /// <summary>Which depth state a given combination of depth write and stencil stamp needs.</summary>
    private static int DepthStateIndex(bool writeDepth, bool writeStencil) => (writeDepth ? 1 : 0) | (writeStencil ? 2 : 0);

    /// <summary>How many meshes were injected on the last frame that ran, for diagnostics.</summary>
    public int LastInjectedCount { get; private set; }

    /// <summary>Whether anything is queued for this frame.</summary>
    public bool HasWork => queue.Count > 0;

    /// <summary>Queues a mesh for injection. Called from the normal render path, not the render thread.</summary>
    public void Enqueue(in Item item) => queue.Add(item);

    /// <summary>Drops anything queued but not drawn, so a frame that never reached the pass does not leak into the next.</summary>
    public void Clear() => queue.Clear();

    /// <summary>
    /// Draws the queued meshes into the currently bound G-buffer. Runs on the render thread, inside the game's
    /// geometry pass, with the game's targets already bound.
    /// </summary>
    /// <param name="device">The render device.</param>
    /// <param name="shaders">The shader library supplying the G-buffer pipeline.</param>
    /// <param name="viewProj">The game's own view-projection for this frame, already transposed for the shader.</param>
    /// <param name="options">What to write into the channels the game authored; read fresh so a live change applies next frame.</param>
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

        // Everything below this line runs against the game's pipeline. The restore in the finally is what keeps
        // a failure here from becoming the game's problem.
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

            // The viewport is deliberately left as the game set it: this draws at the game's resolution into
            // the game's targets, and overriding it would put the geometry in the wrong pixels.
            ctx->RSSetState(rasterState);

            // The game marks object categories in the stencil plane. World geometry measures 0x00, which is
            // also what an unwritten pixel holds, so the stamp is off unless a caller asks for a category.
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

    /// <summary>Draws one queued mesh. Returns whether it was drawn.</summary>
    private bool Draw(ID3D11DeviceContext* ctx, ShaderLibrary shaders, RenderDevice device, in Item item, Draw3DGameLit options)
    {
        if (item.Mesh is not { IndexCount: > 0 } mesh || mesh.Vb == null || mesh.Ib == null)
            return false;

        var hasMaps = item.NormalSrv != 0 && item.SpecularSrv != 0;
        if (shaders.GetGameGBuffer(device, item.Textured, hasMaps) is not { } pipeline)
            return false;

        // The slot meanings here are GameGBuffer.hlsl's, not the scene shaders' - it aliases the shared
        // ObjectCB rather than carrying a second layout, the same way the decal shader does.
        var obj = default(ObjectCBData);
        obj.World = Matrix4x4.Transpose(item.World);
        obj.BaseColor = item.Color;
        obj.Params0 = new Vector4(options.MaterialParams, options.MaterialOverride);
        obj.Params1 = options.Misc;
        obj.Params2 = new Vector4(item.NormalStrength, options.ShadingModelId / 255f, 0f, 0f);
        obj.OutlineColor = options.AlbedoOverride;
        objectCb!.UpdateConstant(ctx, obj);

        ctx->IASetInputLayout(pipeline.Layout);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        if (item.Textured && item.Srv != 0)
        {
            var srv = (ID3D11ShaderResourceView*)item.Srv;
            ctx->PSSetShaderResources(1, 1, &srv);   // t1 = BaseTex, matching Common.hlsli

            // s1, not s0. BaseSamp is declared at register(s1); binding s0 left the shader sampling through
            // whatever sampler the game happened to have bound, which is not a defined state to rely on.
            var samp = sampler;
            ctx->PSSetSamplers(1, 1, &samp);
        }

        // The material's normal and specular maps carry every surface detail the game's own G-buffer shows -
        // the carvings in the normal buffer and the rings in the material buffer. Without them the object is
        // written as a flat surface with a constant material response.
        if (item.NormalSrv != 0 && item.SpecularSrv != 0)
        {
            var aux0 = (ID3D11ShaderResourceView*)item.NormalSrv;
            var aux1 = (ID3D11ShaderResourceView*)item.SpecularSrv;
            ctx->PSSetShaderResources(4, 1, &aux0);  // t4 = AuxTex0, the normal map
            ctx->PSSetShaderResources(5, 1, &aux1);  // t5 = AuxTex1, the specular map
        }

        var vb = mesh.Vb;
        var stride = (uint)sizeof(Vertex3D);
        var offset = 0u;
        ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
        ctx->IASetIndexBuffer(mesh.Ib, mesh.IndexFormat, 0);
        ctx->DrawIndexed((uint)mesh.IndexCount, 0, 0);
        return true;
    }

    /// <summary>Creates the constant buffers and pipeline states once. Returns whether they are usable.</summary>
    private bool EnsureResources(RenderDevice device)
    {
        if (statesReady)
            return true;

        frameCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(FrameCBData));
        objectCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(ObjectCBData));

        if (frameCb == null || objectCb == null)
            return false;

        // The depth test always runs, so walls occlude the object under every variant below. What varies is
        // whether the object writes depth back - which is what makes it occlude the world, and what makes its
        // surface exist for every later pass that reads depth.
        //
        // GREATER_EQUAL, not LESS: FFXIV renders reversed-Z infinite-far, so near maps to 1 and far to 0 and
        // the nearer surface is the one with the LARGER depth value (see DepthCalibration). Getting this
        // backwards would not hide the object - it would draw it only where the world already occludes it,
        // which reads as an object visible exclusively through walls.
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

            // Pass unconditionally and replace, so every pixel the geometry covers ends up carrying the
            // reference value rather than combining with what was there. The reference is supplied per draw,
            // so one state serves any value.
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

        // Opaque into all five targets. Deferred geometry cannot blend: each target holds a different quantity,
        // and blending a normal against the wall behind it produces a direction that describes neither.
        // The write-mask-zero variant keeps the draw and its depth behaviour while writing no target at all,
        // which is how an artefact caused by describing the surface is told apart from one caused by occupying
        // the pixels.
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

    /// <summary>Releases the pipeline states and constant buffers.</summary>
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
