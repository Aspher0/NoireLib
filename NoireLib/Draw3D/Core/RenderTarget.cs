using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

internal sealed unsafe class RenderTarget : IDisposable
{
    private ComPtr<ID3D11Texture2D> texture;
    private ComPtr<ID3D11RenderTargetView> rtv;
    private ComPtr<ID3D11ShaderResourceView> srv;
    private readonly DXGI_FORMAT format;

    public RenderTarget(DXGI_FORMAT format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM) => this.format = format;

    public uint Width { get; private set; }

    public uint Height { get; private set; }

    public ID3D11RenderTargetView* Rtv => rtv.Get();

    public ID3D11ShaderResourceView* Srv => srv.Get();

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
            Format = format,
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

    public void Dispose() => Release();
}

// Draw3D never writes the game's depth. Cleared to 0.0 (reversed-Z far).
internal sealed unsafe class DepthTarget : IDisposable
{
    private ComPtr<ID3D11Texture2D> texture;
    private ComPtr<ID3D11DepthStencilView> dsv;
    private ComPtr<ID3D11ShaderResourceView> srv;

    public uint Width { get; private set; }

    public uint Height { get; private set; }

    public ID3D11DepthStencilView* Dsv => dsv.Get();

    public ID3D11ShaderResourceView* Srv => srv.Get();

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
            Format = DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(D3D11_BIND_FLAG.D3D11_BIND_DEPTH_STENCIL | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE),
        };

        if (device.Device->CreateTexture2D(&desc, null, texture.GetAddressOf()) < 0)
            return false;

        var dsvDesc = new D3D11_DEPTH_STENCIL_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT,
            ViewDimension = D3D11_DSV_DIMENSION.D3D11_DSV_DIMENSION_TEXTURE2D,
        };
        if (device.Device->CreateDepthStencilView((ID3D11Resource*)texture.Get(), &dsvDesc, dsv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        var srvDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT,
            ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D,
        };
        srvDesc.Anonymous.Texture2D.MipLevels = 1;
        if (device.Device->CreateShaderResourceView((ID3D11Resource*)texture.Get(), &srvDesc, srv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        Width = width;
        Height = height;
        return true;
    }

    public void Release()
    {
        srv.Dispose();
        srv = default;
        dsv.Dispose();
        dsv = default;
        texture.Dispose();
        texture = default;
        Width = Height = 0;
    }

    public void Dispose() => Release();
}

// The collision world's device-z. Cleared to 0.0 (reversed-Z far: no collision).
internal sealed unsafe class DepthTargetSrv : IDisposable
{
    private ComPtr<ID3D11Texture2D> texture;
    private ComPtr<ID3D11DepthStencilView> dsv;
    private ComPtr<ID3D11ShaderResourceView> srv;

    public uint Width { get; private set; }

    public uint Height { get; private set; }

    public ID3D11DepthStencilView* Dsv => dsv.Get();

    public ID3D11ShaderResourceView* Srv => srv.Get();

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
            Format = DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(D3D11_BIND_FLAG.D3D11_BIND_DEPTH_STENCIL | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE),
        };
        if (device.Device->CreateTexture2D(&desc, null, texture.GetAddressOf()) < 0)
            return false;

        var dsvDesc = new D3D11_DEPTH_STENCIL_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT,
            ViewDimension = D3D11_DSV_DIMENSION.D3D11_DSV_DIMENSION_TEXTURE2D,
        };
        if (device.Device->CreateDepthStencilView((ID3D11Resource*)texture.Get(), &dsvDesc, dsv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        var srvDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT,
            ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D,
        };
        srvDesc.Anonymous.Texture2D.MipLevels = 1;
        if (device.Device->CreateShaderResourceView((ID3D11Resource*)texture.Get(), &srvDesc, srv.GetAddressOf()) < 0)
        {
            Release();
            return false;
        }

        Width = width;
        Height = height;
        return true;
    }

    public void Release()
    {
        srv.Dispose();
        srv = default;
        dsv.Dispose();
        dsv = default;
        texture.Dispose();
        texture = default;
        Width = Height = 0;
    }

    public void Dispose() => Release();
}
