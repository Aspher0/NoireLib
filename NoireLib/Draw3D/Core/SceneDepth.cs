using System;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// The game's own SRV can be a stencil view. Sampling stencil as depth inverts occlusion.
internal sealed unsafe class SceneDepth : IDisposable
{
    private ComPtr<ID3D11ShaderResourceView> srv;
    private GameRenderSources.DepthTextureInfo lastInfo;
    private bool valid;
    private bool loggedUnknownFormat;

    public ID3D11ShaderResourceView* Srv => valid ? srv.Get() : null;

    public bool IsValid => valid;

    public nint Texture => valid ? lastInfo.Texture : 0;

    // Maps display UVs into the texture's actual region under dynamic resolution.
    public Vector2 UvScale { get; private set; } = Vector2.One;

    public string Description { get; private set; } = "none";

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

        UvScale = new Vector2(
            info.ActualWidth / (float)info.AllocatedWidth,
            info.ActualHeight / (float)info.AllocatedHeight);

        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)info.Texture, out var texture))
            return false;

        using (texture)
        {
            D3D11_TEXTURE2D_DESC texDesc;
            texture.Get()->GetDesc(&texDesc);

            var srvFormat = DepthSrvFormat(texDesc.Format);
            if (srvFormat != DXGI_FORMAT.DXGI_FORMAT_UNKNOWN)
            {
                var srvDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC
                {
                    Format = srvFormat,
                    ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE2D,
                };
                srvDesc.Anonymous.Texture2D.MostDetailedMip = 0;
                srvDesc.Anonymous.Texture2D.MipLevels = 1;

                ID3D11ShaderResourceView* created = null;
                if (device.Device->CreateShaderResourceView((ID3D11Resource*)texture.Get(), &srvDesc, &created) >= 0 && created != null)
                {
                    srv.Attach(created);
                    valid = true;
                    Description = $"own SRV {srvFormat} over {texDesc.Format} ({info.ActualWidth}x{info.ActualHeight} in {info.AllocatedWidth}x{info.AllocatedHeight})";
                    return true;
                }
            }

            // Borrowed only when its description proves a depth-readable format.
            if (info.GameSrv != 0 && ComPtrUtil.TryQi<ID3D11ShaderResourceView>((IUnknown*)info.GameSrv, out var borrowed))
            {
                D3D11_SHADER_RESOURCE_VIEW_DESC desc;
                borrowed.Get()->GetDesc(&desc);
                if (desc.ViewDimension == D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE2D && IsDepthReadable(desc.Format))
                {
                    srv = borrowed;
                    valid = true;
                    Description = $"borrowed game SRV {desc.Format}";
                    return true;
                }

                borrowed.Dispose();
            }

            if (!loggedUnknownFormat)
            {
                loggedUnknownFormat = true;
                NoireLogger.LogError<SceneDepth>($"No depth-readable view possible for scene depth format {texDesc.Format} - running depth-off. Please report this so the format table can be extended.", "[Draw3D] ");
            }

            Description = $"unusable ({texDesc.Format})";
            return false;
        }
    }

    internal static DXGI_FORMAT DepthSrvFormat(DXGI_FORMAT textureFormat) => textureFormat switch
    {
        DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS => DXGI_FORMAT.DXGI_FORMAT_R24_UNORM_X8_TYPELESS,
        DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS => DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT,
        DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS => DXGI_FORMAT.DXGI_FORMAT_R16_UNORM,
        DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS => DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS,
        DXGI_FORMAT.DXGI_FORMAT_R24_UNORM_X8_TYPELESS or DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT or DXGI_FORMAT.DXGI_FORMAT_R16_UNORM => textureFormat,
        _ => DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
    };

    private static bool IsDepthReadable(DXGI_FORMAT format) => format
        is DXGI_FORMAT.DXGI_FORMAT_R24_UNORM_X8_TYPELESS
        or DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT
        or DXGI_FORMAT.DXGI_FORMAT_R16_UNORM
        or DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS;

    public void Invalidate()
    {
        srv.Dispose();
        srv = default;
        valid = false;
        lastInfo = default;
    }

    public void Dispose() => Invalidate();
}
