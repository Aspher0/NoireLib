using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

internal sealed unsafe class SceneStencil : System.IDisposable
{
    private ComPtr<ID3D11ShaderResourceView> srv;
    private GameRenderSources.DepthTextureInfo lastInfo;
    private bool valid;

    // uint2 with stencil in G. Null when unavailable.
    public ID3D11ShaderResourceView* Srv => valid ? srv.Get() : null;

    public bool IsValid => valid;

    public bool Update(RenderDevice device)
    {
        if (!GameRenderSources.TryGetDepthTexture(out var info))
        {
            Invalidate();
            return false;
        }

        if (valid && info == lastInfo)
            return true;

        Invalidate();
        lastInfo = info;

        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)info.Texture, out var texture))
            return false;

        using (texture)
        {
            D3D11_TEXTURE2D_DESC texDesc;
            texture.Get()->GetDesc(&texDesc);

            var stencilFormat = StencilSrvFormat(texDesc.Format);
            if (stencilFormat == DXGI_FORMAT.DXGI_FORMAT_UNKNOWN)
                return false;

            var srvDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC
            {
                Format = stencilFormat,
                ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE2D,
            };
            srvDesc.Anonymous.Texture2D.MostDetailedMip = 0;
            srvDesc.Anonymous.Texture2D.MipLevels = 1;

            ID3D11ShaderResourceView* created = null;
            if (device.Device->CreateShaderResourceView((ID3D11Resource*)texture.Get(), &srvDesc, &created) >= 0 && created != null)
            {
                srv.Attach(created);
                valid = true;
                return true;
            }

            return false;
        }
    }

    internal static DXGI_FORMAT StencilSrvFormat(DXGI_FORMAT textureFormat) => textureFormat switch
    {
        DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT
            => DXGI_FORMAT.DXGI_FORMAT_X24_TYPELESS_G8_UINT,
        DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT
            => DXGI_FORMAT.DXGI_FORMAT_X32_TYPELESS_G8X24_UINT,
        _ => DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
    };

    public void Invalidate()
    {
        srv.Dispose();
        srv = default;
        valid = false;
        lastInfo = default;
    }

    public void Dispose() => Invalidate();
}
