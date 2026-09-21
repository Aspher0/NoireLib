using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// CompositeRgb masks off the alpha write. The composite never writes the present buffer's alpha.
internal enum BlendKey { Opaque = 0, Premultiplied = 1, Additive = 2, CompositeRgb = 3, Max = 4 }

internal enum DepthKey { WriteGE = 0, ReadGE = 1, Disabled = 2 }

internal enum RasterKey { CullBack = 0, CullFront = 1, TwoSided = 2, Wire = 3 }

internal enum SamplerKey { PointClamp = 0, LinearWrap = 1, LinearClamp = 2 }

// Depth bias stays zero. The SRV-compare design needs none.
internal sealed unsafe class StateCache : IDisposable
{
    private readonly ComPtr<ID3D11BlendState>[] blends = new ComPtr<ID3D11BlendState>[5];
    private readonly ComPtr<ID3D11DepthStencilState>[] depths = new ComPtr<ID3D11DepthStencilState>[3];
    private readonly ComPtr<ID3D11RasterizerState>[] rasters = new ComPtr<ID3D11RasterizerState>[4];
    private readonly ComPtr<ID3D11SamplerState>[] samplers = new ComPtr<ID3D11SamplerState>[3];

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
                // Descriptor fields must be valid enum values even when disabled.
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
                // Blend factors are ignored for MAX but must be valid enum values.
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

    // Bilinear depth filtering produces halos.
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
