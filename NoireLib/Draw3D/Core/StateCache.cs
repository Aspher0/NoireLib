using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Blend state catalog keys.<br/>
/// <see cref="CompositeRgb"/> is premultiplied with the alpha write masked off - the backbuffer's
/// alpha channel is the game's native-UI coverage (our per-pixel mask source, and other overlay
/// libraries read it too), so the composite must never write into it.
/// </summary>
internal enum BlendKey { Opaque = 0, Premultiplied = 1, Additive = 2, CompositeRgb = 3, Max = 4 }

/// <summary>Depth-stencil state catalog keys (reversed-Z GREATER_EQUAL semantics).</summary>
internal enum DepthKey { WriteGE = 0, ReadGE = 1, Disabled = 2 }

/// <summary>Rasterizer state catalog keys.</summary>
internal enum RasterKey { CullBack = 0, CullFront = 1, TwoSided = 2, Wire = 3 }

/// <summary>Sampler catalog keys.</summary>
internal enum SamplerKey { PointClamp = 0, LinearWrap = 1, LinearClamp = 2 }

/// <summary>
/// Lazily-created, enum-keyed immutable pipeline state objects. Exact descriptor values are normative:
/// blending is premultiplied everywhere translucent, and depth bias stays zero (the SRV-compare design
/// needs none).
/// </summary>
internal sealed unsafe class StateCache : IDisposable
{
    private readonly ComPtr<ID3D11BlendState>[] blends = new ComPtr<ID3D11BlendState>[5];
    private readonly ComPtr<ID3D11DepthStencilState>[] depths = new ComPtr<ID3D11DepthStencilState>[3];
    private readonly ComPtr<ID3D11RasterizerState>[] rasters = new ComPtr<ID3D11RasterizerState>[4];
    private readonly ComPtr<ID3D11SamplerState>[] samplers = new ComPtr<ID3D11SamplerState>[3];

    /// <summary>Gets (creating on first use) the blend state for a key.</summary>
    public ID3D11BlendState* GetBlend(RenderDevice device, BlendKey key)
    {
        ref var slot = ref blends[(int)key];
        if (slot.Get() != null)
            return slot.Get();

        var desc = new D3D11_BLEND_DESC();
        ref var rt = ref desc.RenderTarget[0];
        rt.RenderTargetWriteMask = (byte)D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL;
        switch (key)
        {
            case BlendKey.Opaque:
                rt.BlendEnable = BOOL.FALSE;
                // All descriptor fields must still be valid enum values even when disabled.
                rt.SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.DestBlend = D3D11_BLEND.D3D11_BLEND_ZERO;
                rt.BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD;
                rt.SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ZERO;
                rt.BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD;
                break;
            case BlendKey.Premultiplied:
            case BlendKey.CompositeRgb:
                rt.BlendEnable = BOOL.TRUE;
                rt.SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.DestBlend = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA;
                rt.BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD;
                rt.SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA;
                rt.BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD;
                if (key == BlendKey.CompositeRgb)
                {
                    rt.RenderTargetWriteMask = (byte)(D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_RED
                        | D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_GREEN
                        | D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_BLUE);
                }

                break;
            case BlendKey.Additive:
                rt.BlendEnable = BOOL.TRUE;
                rt.SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.DestBlend = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD;
                rt.SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ZERO;
                rt.DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD;
                break;
            case BlendKey.Max:
                // Keep the maximum of src/dest - the top-down collision height-map accumulates the HIGHEST world Y per
                // texel. Blend factors are ignored for MIN/MAX ops but must still be valid enum values.
                rt.BlendEnable = BOOL.TRUE;
                rt.SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.DestBlend = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_MAX;
                rt.SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE;
                rt.BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_MAX;
                break;
        }

        device.Device->CreateBlendState(&desc, slot.GetAddressOf());
        return slot.Get();
    }

    /// <summary>Gets (creating on first use) the depth-stencil state for a key.</summary>
    public ID3D11DepthStencilState* GetDepth(RenderDevice device, DepthKey key)
    {
        ref var slot = ref depths[(int)key];
        if (slot.Get() != null)
            return slot.Get();

        var desc = new D3D11_DEPTH_STENCIL_DESC
        {
            DepthEnable = key == DepthKey.Disabled ? BOOL.FALSE : BOOL.TRUE,
            DepthWriteMask = key == DepthKey.WriteGE ? D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ALL : D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ZERO,
            DepthFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_GREATER_EQUAL,
            StencilEnable = BOOL.FALSE,
        };

        device.Device->CreateDepthStencilState(&desc, slot.GetAddressOf());
        return slot.Get();
    }

    /// <summary>Gets (creating on first use) the rasterizer state for a key.</summary>
    public ID3D11RasterizerState* GetRaster(RenderDevice device, RasterKey key)
    {
        ref var slot = ref rasters[(int)key];
        if (slot.Get() != null)
            return slot.Get();

        var desc = new D3D11_RASTERIZER_DESC
        {
            FillMode = key == RasterKey.Wire ? D3D11_FILL_MODE.D3D11_FILL_WIREFRAME : D3D11_FILL_MODE.D3D11_FILL_SOLID,
            CullMode = key switch
            {
                RasterKey.CullBack => D3D11_CULL_MODE.D3D11_CULL_BACK,
                RasterKey.CullFront => D3D11_CULL_MODE.D3D11_CULL_FRONT,
                _ => D3D11_CULL_MODE.D3D11_CULL_NONE,
            },
            DepthClipEnable = BOOL.TRUE,
            ScissorEnable = BOOL.TRUE,
            MultisampleEnable = BOOL.FALSE,
        };

        device.Device->CreateRasterizerState(&desc, slot.GetAddressOf());
        return slot.Get();
    }

    /// <summary>Gets (creating on first use) the sampler for a key. Scene depth must use PointClamp (bilinear depth = halo bug).</summary>
    public ID3D11SamplerState* GetSampler(RenderDevice device, SamplerKey key)
    {
        ref var slot = ref samplers[(int)key];
        if (slot.Get() != null)
            return slot.Get();

        var address = key == SamplerKey.LinearWrap ? D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP : D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP;
        var desc = new D3D11_SAMPLER_DESC
        {
            Filter = key == SamplerKey.PointClamp ? D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT : D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU = address,
            AddressV = address,
            AddressW = address,
            ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
            MaxLOD = float.MaxValue,
        };

        device.Device->CreateSamplerState(&desc, slot.GetAddressOf());
        return slot.Get();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        for (var i = 0; i < blends.Length; i++) blends[i].Dispose();
        for (var i = 0; i < depths.Length; i++) depths[i].Dispose();
        for (var i = 0; i < rasters.Length; i++) rasters[i].Dispose();
        for (var i = 0; i < samplers.Length; i++) samplers[i].Dispose();
        Array.Clear(blends);
        Array.Clear(depths);
        Array.Clear(rasters);
        Array.Clear(samplers);
    }
}
