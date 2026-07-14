using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// A writable depth-stencil view over the game's scene depth buffer, used ONLY by the opt-in native-UI
/// depth-write (<see cref="NoireDraw3D.NativeUiDepthWrite"/>): at pre-UI injection time Draw3D re-rasterizes
/// its opaque objects' depth into the game's buffer (greater-equal tested against the world's own depth), so
/// the game's later nameplate pass occludes against 3D objects that stand in front of a character.<br/>
/// This deliberately waives the usual Law 5 ("the game's depth is never written") — hence it is off by default,
/// fail-soft, and re-derives itself whenever the underlying texture changes (resolution, GPose, upscaler).
/// </summary>
internal sealed unsafe class GameDepthTarget : System.IDisposable
{
    private ComPtr<ID3D11DepthStencilView> dsv;
    private nint lastTexture;
    private bool loggedUnknownFormat;

    /// <summary>Actual (rendered) width of the depth region this frame — the viewport width for the depth-write.</summary>
    public uint Width { get; private set; }

    /// <summary>Actual (rendered) height of the depth region this frame — the viewport height for the depth-write.</summary>
    public uint Height { get; private set; }

    /// <summary>
    /// Ensures a DSV over the current scene depth texture, recreating it only when the texture pointer changes.
    /// Returns null when depth is unavailable or the format cannot back a depth-stencil view (then depth-write is skipped).
    /// </summary>
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
                    NoireLogger.LogError<GameDepthTarget>($"Scene depth format {texDesc.Format} cannot back a depth-stencil view — native-UI depth-write disabled. Report this to extend the table.", "Draw3D");
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

    /// <summary>Maps a (typeless or typed) depth texture format to the DSV format that writes its depth plane.</summary>
    private static DXGI_FORMAT DsvFormat(DXGI_FORMAT textureFormat) => textureFormat switch
    {
        DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT => DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT,
        DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT => DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT,
        DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT => DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT,
        DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_D16_UNORM => DXGI_FORMAT.DXGI_FORMAT_D16_UNORM,
        _ => DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
    };

    /// <summary>Drops the current DSV. The next <see cref="Ensure"/> re-acquires.</summary>
    public void Invalidate()
    {
        dsv.Dispose();
        dsv = default;
        lastTexture = 0;
    }

    /// <inheritdoc/>
    public void Dispose() => Invalidate();
}
