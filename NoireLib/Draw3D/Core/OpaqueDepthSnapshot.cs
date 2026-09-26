using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// A copy of the game's scene depth taken mid-frame, before the water and translucent passes write their surfaces into it.
// Taken on the render thread from inside the render-target tap. Bound only as an SRV, never a DSV.
internal sealed unsafe class OpaqueDepthSnapshot : IDisposable
{
    private ComPtr<ID3D11Texture2D> copy;
    private ComPtr<ID3D11ShaderResourceView> srv;
    private uint width;
    private uint height;
    private DXGI_FORMAT format;
    private bool unusable;
    private bool loggedUnusable;

    public ID3D11ShaderResourceView* Srv => srv.Get();

    public nint Texture => (nint)copy.Get();

    // The scene depth the last copy was taken from.
    public nint Source { get; private set; }

    // Resource creations since the last reset. Only a size or format change grows it.
    public int Allocations { get; private set; }

    public string Description { get; private set; } = "none";

    public bool Take(RenderDevice device, nint sourceTexture)
    {
        if (sourceTexture == 0 || !ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)sourceTexture, out var source))
            return false;

        try
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);

            var sameShape = desc.Width == width && desc.Height == height && desc.Format == format;
            if (sameShape && unusable)
                return false;

            if (copy.Get() == null || !sameShape)
            {
                if (!Recreate(device, in desc))
                    return false;
            }

            device.Context->CopyResource((ID3D11Resource*)copy.Get(), (ID3D11Resource*)source.Get());
            Source = sourceTexture;
            return true;
        }
        finally
        {
            source.Dispose();
        }
    }

    private bool Recreate(RenderDevice device, in D3D11_TEXTURE2D_DESC sourceDesc)
    {
        Release();
        width = sourceDesc.Width;
        height = sourceDesc.Height;
        format = sourceDesc.Format;

        // A typed depth format cannot back an SRV. The copy takes the typeless format of the same group.
        var copyFormat = TypelessFormat(sourceDesc.Format);
        var srvFormat = SceneDepth.DepthSrvFormat(copyFormat);
        unusable = srvFormat == DXGI_FORMAT.DXGI_FORMAT_UNKNOWN || sourceDesc.SampleDesc.Count != 1 || sourceDesc.ArraySize != 1;
        if (unusable)
        {
            Description = $"unusable ({sourceDesc.Format}, {sourceDesc.SampleDesc.Count} samples)";
            if (!loggedUnusable)
            {
                loggedUnusable = true;
                NoireLogger.LogError<OpaqueDepthSnapshot>($"The scene depth ({sourceDesc.Format}, {sourceDesc.SampleDesc.Count} samples) cannot be copied for the opaque-depth snapshot. Translucent surfaces occlude on this machine.", "[Draw3D] ");
            }

            return false;
        }

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = sourceDesc.Width,
            Height = sourceDesc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = copyFormat,
            SampleDesc = new DXGI_SAMPLE_DESC(1, 0),
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
        };

        if (device.Device->CreateTexture2D(&desc, null, copy.GetAddressOf()) < 0 || copy.Get() == null)
        {
            Release();
            return false;
        }

        var srvDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC
        {
            Format = srvFormat,
            ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE2D,
        };
        srvDesc.Anonymous.Texture2D.MostDetailedMip = 0;
        srvDesc.Anonymous.Texture2D.MipLevels = 1;

        if (device.Device->CreateShaderResourceView((ID3D11Resource*)copy.Get(), &srvDesc, srv.GetAddressOf()) < 0 || srv.Get() == null)
        {
            Release();
            return false;
        }

        Allocations++;
        Description = $"{srvFormat} over {copyFormat} ({width}x{height})";
        NoireLogger.LogDebug<OpaqueDepthSnapshot>($"Opaque-depth snapshot allocated: {Description}.", "[Draw3D] ");
        return true;
    }

    internal static DXGI_FORMAT TypelessFormat(DXGI_FORMAT format) => format switch
    {
        DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT => DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS,
        DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT => DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS,
        DXGI_FORMAT.DXGI_FORMAT_D16_UNORM => DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS,
        DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT => DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS,
        _ => format,
    };

    private void Release()
    {
        srv.Dispose();
        srv = default;
        copy.Dispose();
        copy = default;
    }

    public void Dispose()
    {
        Release();
        width = 0;
        height = 0;
        format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        unusable = false;
        Source = 0;
        Description = "none";
    }
}
