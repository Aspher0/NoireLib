using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Read-only access to the <b>stencil</b> plane of the game's scene depth-stencil buffer (bound only as an SRV, never a
/// DSV). The game marks object categories in stencil (characters carry a distinct value), so a ground decal can occlude
/// itself exactly along an excluded character's silhouette without any volume. Creates its own <c>*_G8_UINT</c> view of
/// the typeless depth-stencil texture; only formats that actually carry a stencil plane are supported (others leave this
/// off and the decal simply paints as before). Mirrors <see cref="SceneDepth"/>; re-derives on any texture change.
/// </summary>
internal sealed unsafe class SceneStencil : System.IDisposable
{
    private ComPtr<ID3D11ShaderResourceView> srv;
    private GameRenderSources.DepthTextureInfo lastInfo;
    private bool valid;

    /// <summary>The stencil SRV for this frame (a <c>uint2</c> texture; stencil is the G channel), or null when unavailable.</summary>
    public ID3D11ShaderResourceView* Srv => valid ? srv.Get() : null;

    /// <summary>True when the stencil plane is readable this frame.</summary>
    public bool IsValid => valid;

    /// <summary>Per-frame validation and (re)acquisition. Cheap when nothing changed. Returns true when stencil is usable.</summary>
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
                return false; // this depth format carries no stencil plane - feature stays off

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

    /// <summary>Maps a depth-stencil texture's (typeless) format to the SRV format that reads its <i>stencil</i> plane, or UNKNOWN when it has none.</summary>
    internal static DXGI_FORMAT StencilSrvFormat(DXGI_FORMAT textureFormat) => textureFormat switch
    {
        DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT
            => DXGI_FORMAT.DXGI_FORMAT_X24_TYPELESS_G8_UINT,
        DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT
            => DXGI_FORMAT.DXGI_FORMAT_X32_TYPELESS_G8X24_UINT,
        _ => DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
    };

    /// <summary>Drops the current SRV. The next Update re-acquires.</summary>
    public void Invalidate()
    {
        srv.Dispose();
        srv = default;
        valid = false;
        lastInfo = default;
    }

    /// <inheritdoc/>
    public void Dispose() => Invalidate();
}
