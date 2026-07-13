using System;
using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Read-only access to the game's scene depth buffer (Law 5: bound only as an SRV, never as a DSV).<br/>
/// Prefers borrowing the game's own pre-made SRV (QI-validated); otherwise creates one from the
/// typeless texture family. Re-derives itself whenever the underlying texture changes (resolution,
/// GPose, upscaler changes) and fails soft to depth-off mode on anything unknown.
/// </summary>
internal sealed unsafe class SceneDepth : IDisposable
{
    private ComPtr<ID3D11ShaderResourceView> srv;
    private GameRenderSources.DepthTextureInfo lastInfo;
    private bool valid;
    private bool loggedUnknownFormat;

    /// <summary>The depth SRV for this frame, or null in depth-off mode.</summary>
    public ID3D11ShaderResourceView* Srv => valid ? srv.Get() : null;

    /// <summary>True when the depth buffer is readable this frame.</summary>
    public bool IsValid => valid;

    /// <summary>UV scale mapping display UVs into the depth texture's actual region (dynamic resolution).</summary>
    public Vector2 UvScale { get; private set; } = Vector2.One;

    /// <summary>Human-readable description of the active depth source (route + format) for stats/probe.</summary>
    public string Description { get; private set; } = "none";

    /// <summary>
    /// Per-frame validation and (re)acquisition. Cheap when nothing changed (a record-struct compare).
    /// Returns true when depth is usable this frame.
    /// </summary>
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

        // Route 1 (primary): create our own SRV from the typeless texture — WE control which plane it
        // reads. This deliberately comes before borrowing: the game's own pre-made SRV can legally be a
        // STENCIL view of the same resource, and sampling stencil as depth inverts occlusion everywhere
        // geometry was drawn (sky = stencil 0 stays visible) — the "only draws against the sky" bug.
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

            // Route 2 (fallback): borrow the game's own SRV — but only when it is a known depth-readable
            // format (never a stencil or color view; Law 8: QI + desc prove it, never assume).
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
                NoireLogger.LogError<SceneDepth>($"No depth-readable view possible for scene depth format {texDesc.Format} — running depth-off. Please report this so the format table can be extended.", "Draw3D");
            }

            Description = $"unusable ({texDesc.Format})";
            return false;
        }
    }

    /// <summary>Maps a depth texture's (typeless) format to the SRV format that reads its <i>depth</i> plane.</summary>
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

    /// <summary>Drops the current SRV (borrowed refs released exactly once). The next Update re-acquires.</summary>
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
