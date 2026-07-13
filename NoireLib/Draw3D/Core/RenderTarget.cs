using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// The offscreen premultiplied scene color target (R8G8B8A8, RTV + SRV), recreated on resize.
/// </summary>
internal sealed unsafe class RenderTarget : IDisposable
{
    private ComPtr<ID3D11Texture2D> texture;
    private ComPtr<ID3D11RenderTargetView> rtv;
    private ComPtr<ID3D11ShaderResourceView> srv;

    /// <summary>Current width in pixels (0 before first creation).</summary>
    public uint Width { get; private set; }

    /// <summary>Current height in pixels (0 before first creation).</summary>
    public uint Height { get; private set; }

    /// <summary>The render target view (null before first creation).</summary>
    public ID3D11RenderTargetView* Rtv => rtv.Get();

    /// <summary>The shader resource view for compositing (null before first creation).</summary>
    public ID3D11ShaderResourceView* Srv => srv.Get();

    /// <summary>Recreates the target when the requested size differs. Returns false when creation failed.</summary>
    public bool EnsureSize(RenderDevice device, uint width, uint height)
    {
        if (width == 0 || height == 0)
            return false;

        if (Width == width && Height == height && rtv.Get() != null)
            return true;

        Release();

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE),
        };

        if (device.Device->CreateTexture2D(&desc, null, texture.GetAddressOf()) < 0)
            return false;
        if (device.Device->CreateRenderTargetView((ID3D11Resource*)texture.Get(), null, rtv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        if (device.Device->CreateShaderResourceView((ID3D11Resource*)texture.Get(), null, srv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        Width = width;
        Height = height;
        return true;
    }

    /// <summary>Releases GPU objects (recreated by the next EnsureSize).</summary>
    public void Release()
    {
        srv.Dispose();
        srv = default;
        rtv.Dispose();
        rtv = default;
        texture.Dispose();
        texture = default;
        Width = Height = 0;
    }

    /// <inheritdoc/>
    public void Dispose() => Release();
}

/// <summary>
/// The private D32_FLOAT depth buffer for Draw3D↔Draw3D depth (Law 5: the game's depth is never written).
/// Cleared to 0.0 — reversed-Z "far".
/// </summary>
internal sealed unsafe class DepthTarget : IDisposable
{
    private ComPtr<ID3D11Texture2D> texture;
    private ComPtr<ID3D11DepthStencilView> dsv;

    /// <summary>Current width in pixels (0 before first creation).</summary>
    public uint Width { get; private set; }

    /// <summary>Current height in pixels (0 before first creation).</summary>
    public uint Height { get; private set; }

    /// <summary>The depth-stencil view (null before first creation).</summary>
    public ID3D11DepthStencilView* Dsv => dsv.Get();

    /// <summary>Recreates the buffer when the requested size differs. Returns false when creation failed.</summary>
    public bool EnsureSize(RenderDevice device, uint width, uint height)
    {
        if (width == 0 || height == 0)
            return false;

        if (Width == width && Height == height && dsv.Get() != null)
            return true;

        Release();

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_DEPTH_STENCIL,
        };

        if (device.Device->CreateTexture2D(&desc, null, texture.GetAddressOf()) < 0)
            return false;
        if (device.Device->CreateDepthStencilView((ID3D11Resource*)texture.Get(), null, dsv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        Width = width;
        Height = height;
        return true;
    }

    /// <summary>Releases GPU objects (recreated by the next EnsureSize).</summary>
    public void Release()
    {
        dsv.Dispose();
        dsv = default;
        texture.Dispose();
        texture = default;
        Width = Height = 0;
    }

    /// <inheritdoc/>
    public void Dispose() => Release();
}
