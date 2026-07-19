using System.Diagnostics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Saves and restores exactly the pipeline slots Draw3D touches. The slot list below is the
/// exhaustive contract: touching a new slot anywhere in the renderer without adding it here is a bug.<br/>
/// Rules encoded: every XXGet AddRefs (each gets one Release); null is a value (restored, never skipped);
/// viewport/scissor counts are captured and restored exactly.
/// </summary>
internal sealed unsafe class StateGuard
{
    private const int ViewportSlotCount = 16; // D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE

    // IA
    private ID3D11InputLayout* inputLayout;
    private D3D_PRIMITIVE_TOPOLOGY topology;
    private ID3D11Buffer* vb0, vb1;
    private uint vb0Stride, vb0Offset, vb1Stride, vb1Offset;
    private ID3D11Buffer* indexBuffer;
    private DXGI_FORMAT indexFormat;
    private uint indexOffset;

    // VS
    private ID3D11VertexShader* vs;
    private ID3D11Buffer* vsCb0, vsCb1;

    // PS
    // Six SRV slots, not two: Common.hlsli declares t0..t5 (scene depth, base texture, world height, scene
    // stencil, and the two auxiliary material maps), and the G-buffer injection binds t4 and t5 inside the
    // GAME's own geometry pass. A slot left bound there is not Draw3D's problem to survive - it is whatever the
    // game draws next reading our texture.
    private const int PsSrvSlotCount = 6;

    private ID3D11PixelShader* ps;
    private ID3D11Buffer* psCb0, psCb1;
    private readonly void*[] psSrvs = new void*[PsSrvSlotCount];
    private ID3D11SamplerState* psSamp0, psSamp1;

    // RS
    private ID3D11RasterizerState* rasterizer;
    private readonly D3D11_VIEWPORT[] viewports = new D3D11_VIEWPORT[ViewportSlotCount];
    private uint viewportCount;
    private readonly RECT[] scissors = new RECT[ViewportSlotCount];
    private uint scissorCount;

    // OM
    private ID3D11BlendState* blend;
    private float blendFactor0, blendFactor1, blendFactor2, blendFactor3;
    private uint sampleMask;
    private ID3D11DepthStencilState* depthStencil;
    private uint stencilRef;
    private readonly void*[] rtvs = new void*[8];
    private ID3D11DepthStencilView* dsv;

    private bool captured;

    /// <summary>Captures every slot in the contract. Must be paired with <see cref="Restore"/> (run it in a finally).</summary>
    public void Capture(ID3D11DeviceContext* ctx)
    {
        Debug.Assert(!captured, "StateGuard.Capture called twice without Restore.");

        // IA
        fixed (ID3D11InputLayout** p = &inputLayout)
            ctx->IAGetInputLayout(p);
        fixed (D3D_PRIMITIVE_TOPOLOGY* p = &topology)
            ctx->IAGetPrimitiveTopology(p);

        var vbs = stackalloc ID3D11Buffer*[2];
        var strides = stackalloc uint[2];
        var offsets = stackalloc uint[2];
        ctx->IAGetVertexBuffers(0, 2, vbs, strides, offsets);
        vb0 = vbs[0]; vb1 = vbs[1];
        vb0Stride = strides[0]; vb1Stride = strides[1];
        vb0Offset = offsets[0]; vb1Offset = offsets[1];

        fixed (ID3D11Buffer** p = &indexBuffer)
        fixed (DXGI_FORMAT* f = &indexFormat)
        fixed (uint* o = &indexOffset)
            ctx->IAGetIndexBuffer(p, f, o);

        // VS
        fixed (ID3D11VertexShader** p = &vs)
            ctx->VSGetShader(p, null, null);
        var cbs = stackalloc ID3D11Buffer*[2];
        ctx->VSGetConstantBuffers(0, 2, cbs);
        vsCb0 = cbs[0]; vsCb1 = cbs[1];

        // PS
        fixed (ID3D11PixelShader** p = &ps)
            ctx->PSGetShader(p, null, null);
        ctx->PSGetConstantBuffers(0, 2, cbs);
        psCb0 = cbs[0]; psCb1 = cbs[1];
        fixed (void** p = psSrvs)
            ctx->PSGetShaderResources(0, PsSrvSlotCount, (ID3D11ShaderResourceView**)p);
        var samps = stackalloc ID3D11SamplerState*[2];
        ctx->PSGetSamplers(0, 2, samps);
        psSamp0 = samps[0]; psSamp1 = samps[1];

        // RS
        fixed (ID3D11RasterizerState** p = &rasterizer)
            ctx->RSGetState(p);

        uint vpCount = ViewportSlotCount;
        ctx->RSGetViewports(&vpCount, null);
        if (vpCount > 0)
        {
            fixed (D3D11_VIEWPORT* p = viewports)
                ctx->RSGetViewports(&vpCount, p);
        }

        viewportCount = vpCount;

        uint scCount = ViewportSlotCount;
        ctx->RSGetScissorRects(&scCount, null);
        if (scCount > 0)
        {
            fixed (RECT* p = scissors)
                ctx->RSGetScissorRects(&scCount, p);
        }

        scissorCount = scCount;

        // OM
        var factor = stackalloc float[4];
        fixed (ID3D11BlendState** p = &blend)
        fixed (uint* m = &sampleMask)
            ctx->OMGetBlendState(p, factor, m);
        blendFactor0 = factor[0]; blendFactor1 = factor[1]; blendFactor2 = factor[2]; blendFactor3 = factor[3];

        fixed (ID3D11DepthStencilState** p = &depthStencil)
        fixed (uint* r = &stencilRef)
            ctx->OMGetDepthStencilState(p, r);

        fixed (void** p = rtvs)
        fixed (ID3D11DepthStencilView** d = &dsv)
            ctx->OMGetRenderTargets(8, (ID3D11RenderTargetView**)p, d);

        captured = true;
        AssertUntouchedStagesClean(ctx);
    }

    /// <summary>Restores every captured slot exactly (null included) and releases the AddRef each getter took.</summary>
    public void Restore(ID3D11DeviceContext* ctx)
    {
        if (!captured)
            return;

        captured = false;

        // OM first (unbinds our SRV-vs-RTV hazards in the safest order).
        fixed (void** p = rtvs)
            ctx->OMSetRenderTargets(8, (ID3D11RenderTargetView**)p, dsv);
        var factor = stackalloc float[4] { blendFactor0, blendFactor1, blendFactor2, blendFactor3 };
        ctx->OMSetBlendState(blend, factor, sampleMask);
        ctx->OMSetDepthStencilState(depthStencil, stencilRef);

        // RS
        ctx->RSSetState(rasterizer);
        if (viewportCount > 0)
        {
            fixed (D3D11_VIEWPORT* p = viewports)
                ctx->RSSetViewports(viewportCount, p);
        }
        else
        {
            ctx->RSSetViewports(0, null);
        }

        if (scissorCount > 0)
        {
            fixed (RECT* p = scissors)
                ctx->RSSetScissorRects(scissorCount, p);
        }
        else
        {
            ctx->RSSetScissorRects(0, null);
        }

        // PS
        ctx->PSSetShader(ps, null, 0);
        var cbs = stackalloc ID3D11Buffer*[2] { psCb0, psCb1 };
        ctx->PSSetConstantBuffers(0, 2, cbs);
        fixed (void** p = psSrvs)
            ctx->PSSetShaderResources(0, PsSrvSlotCount, (ID3D11ShaderResourceView**)p);
        var samps = stackalloc ID3D11SamplerState*[2] { psSamp0, psSamp1 };
        ctx->PSSetSamplers(0, 2, samps);

        // VS
        ctx->VSSetShader(vs, null, 0);
        cbs[0] = vsCb0; cbs[1] = vsCb1;
        ctx->VSSetConstantBuffers(0, 2, cbs);

        // IA
        ctx->IASetInputLayout(inputLayout);
        ctx->IASetPrimitiveTopology(topology);
        var vbs = stackalloc ID3D11Buffer*[2] { vb0, vb1 };
        var strides = stackalloc uint[2] { vb0Stride, vb1Stride };
        var offsets = stackalloc uint[2] { vb0Offset, vb1Offset };
        ctx->IASetVertexBuffers(0, 2, vbs, strides, offsets);
        ctx->IASetIndexBuffer(indexBuffer, indexFormat, indexOffset);

        ReleaseAll();
    }

    /// <summary>
    /// Debug-only: verifies stages Draw3D never touches (and therefore never saves) are clean at the
    /// present-time callback - a null geometry shader and zero OM UAVs. If either assert ever fires,
    /// the slot enters the save/restore contract above.
    /// </summary>
    [Conditional("DEBUG")]
    private void AssertUntouchedStagesClean(ID3D11DeviceContext* ctx)
    {
        ID3D11GeometryShader* gs = null;
        ctx->GSGetShader(&gs, null, null);
        Debug.Assert(gs == null, "Draw3D: a geometry shader is bound at present time - add GS to the StateGuard slot contract.");
        ComPtrUtil.Release(ref gs);

        var uavs = stackalloc ID3D11UnorderedAccessView*[8];
        ctx->OMGetRenderTargetsAndUnorderedAccessViews(0, null, null, 0, 8, uavs);
        for (var i = 0; i < 8; i++)
        {
            Debug.Assert(uavs[i] == null, "Draw3D: an OM UAV is bound at present time - add UAVs to the StateGuard slot contract.");
            ComPtrUtil.Release(ref uavs[i]);
        }
    }

    private void ReleaseAll()
    {
        ComPtrUtil.Release(ref inputLayout);
        ComPtrUtil.Release(ref vb0);
        ComPtrUtil.Release(ref vb1);
        ComPtrUtil.Release(ref indexBuffer);
        ComPtrUtil.Release(ref vs);
        ComPtrUtil.Release(ref vsCb0);
        ComPtrUtil.Release(ref vsCb1);
        ComPtrUtil.Release(ref ps);
        ComPtrUtil.Release(ref psCb0);
        ComPtrUtil.Release(ref psCb1);
        for (var i = 0; i < psSrvs.Length; i++)
        {
            if (psSrvs[i] != null)
            {
                ((IUnknown*)psSrvs[i])->Release();
                psSrvs[i] = null;
            }
        }

        ComPtrUtil.Release(ref psSamp0);
        ComPtrUtil.Release(ref psSamp1);
        ComPtrUtil.Release(ref rasterizer);
        ComPtrUtil.Release(ref blend);
        ComPtrUtil.Release(ref depthStencil);
        for (var i = 0; i < rtvs.Length; i++)
        {
            if (rtvs[i] != null)
            {
                ((IUnknown*)rtvs[i])->Release();
                rtvs[i] = null;
            }
        }

        ComPtrUtil.Release(ref dsv);
    }
}
