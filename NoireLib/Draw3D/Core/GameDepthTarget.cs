using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// Writes Draw3D's opaque depth into the game's buffer so the nameplate pass occludes against it. The one exception to read-only depth.
internal sealed unsafe class GameDepthTarget : System.IDisposable
{
    private ComPtr<ID3D11DepthStencilView> dsv;
    private nint lastTexture;
    private bool loggedUnknownFormat;

    // The rendered size.
    public uint Width { get; private set; }

    public uint Height { get; private set; }

    public ID3D11DepthStencilView* Ensure(RenderDevice device)
    {
        if (!GameRenderSources.TryGetDepthTexture(out var info))
        {
            Invalidate();
            return null;
        }

        Width = info.ActualWidth;
        Height = info.ActualHeight;

        if (info.Texture == lastTexture && dsv.Get() != null)
            return dsv.Get();

        Invalidate();

        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)info.Texture, out var texture))
            return null;

        using (texture)
        {
            D3D11_TEXTURE2D_DESC texDesc;
            texture.Get()->GetDesc(&texDesc);

            var dsvFormat = DsvFormat(texDesc.Format);
            if (dsvFormat == DXGI_FORMAT.DXGI_FORMAT_UNKNOWN)
            {
                if (!loggedUnknownFormat)
                {
                    loggedUnknownFormat = true;
                    NoireLogger.LogError<GameDepthTarget>($"Scene depth format {texDesc.Format} cannot back a depth-stencil view - native-UI depth-write disabled. Report this to extend the table.", "Draw3D");
                }

                return null;
            }

            var desc = new D3D11_DEPTH_STENCIL_VIEW_DESC
            {
                Format = dsvFormat,
                ViewDimension = D3D11_DSV_DIMENSION.D3D11_DSV_DIMENSION_TEXTURE2D,
            };
            desc.Anonymous.Texture2D.MipSlice = 0;

            ID3D11DepthStencilView* created = null;
            if (device.Device->CreateDepthStencilView((ID3D11Resource*)texture.Get(), &desc, &created) < 0 || created == null)
                return null;

            dsv.Attach(created);
            lastTexture = info.Texture;
            return dsv.Get();
        }
    }

    private static DXGI_FORMAT DsvFormat(DXGI_FORMAT textureFormat) => textureFormat switch
    {
        DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT => DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT,
        DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT => DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT,
        DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT => DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT,
        DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D16_UNORM => DXGI_FORMAT.DXGI_FORMAT_D16_UNORM,
        _ => DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
    };

    public void Invalidate()
    {
        dsv.Dispose();
        dsv = default;
        lastTexture = 0;
    }

    public void Dispose() => Invalidate();
}
