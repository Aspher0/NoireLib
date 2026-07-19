using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using NoireLib.Draw3D.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// Produces <see cref="GpuTexture"/>s for Draw3D materials by bridging Dalamud's texture pipeline -
/// decoding, caching and lifetime stay Dalamud's problem.<br/>
/// <b>Ownership:</b> every returned texture is owned by the caller - dispose it when done. The bridge
/// keeps its own reference to the underlying Dalamud resource, so the source wrap's lifetime stops mattering.
/// </summary>
public static class TextureLoader
{
    /// <summary>
    /// Bridges an existing Dalamud texture wrap into a material-ready texture.<br/>
    /// The wrap's low-level resource is shared (independent reference), then QueryInterface proves the
    /// handle really is a shader resource view, never assumed.
    /// </summary>
    /// <param name="wrap">The source wrap. It can be disposed freely after this call.</param>
    /// <returns>The bridged texture, or null when the handle is not a D3D11 SRV (logged once).</returns>
    public static unsafe GpuTexture? FromWrap(IDalamudTextureWrap wrap)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        NoireDraw3D.EnsureInitialized();

        var shared = wrap.CreateWrapSharingLowLevelResource();
        if (!ComPtrUtil.TryQi<ID3D11ShaderResourceView>((IUnknown*)(nint)shared.Handle.Handle, out var srv))
        {
            shared.Dispose();
            NoireLogger.LogError("TextureLoader: the wrap handle is not an ID3D11ShaderResourceView - cannot bridge.", "Draw3D");
            return null;
        }

        // GpuTexture adopts the QI reference and keeps the shared wrap alive until disposed.
        var tex = GpuTexture.FromSrv(srv.Get(), wrap.Width, wrap.Height, addRef: true, ownedWrap: shared);
        srv.Dispose();
        return tex;
    }

    /// <summary>Creates a texture from raw RGBA8 pixels (synchronous, any thread).</summary>
    /// <param name="rgbaPixels">Pixel data, row-major, width*height*4 bytes.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public static GpuTexture FromRgba(ReadOnlySpan<byte> rgbaPixels, int width, int height)
        => GpuTexture.CreateFromPixels(NoireDraw3D.RequireDevice(), rgbaPixels, width, height);

    /// <summary>Loads an image file (.png, .tex, and other well-known formats) from disk.</summary>
    /// <param name="path">Absolute file path.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static async Task<GpuTexture?> FromFileAsync(string path, CancellationToken ct = default)
    {
        NoireDraw3D.EnsureInitialized();
        using var wrap = await NoireService.TextureProvider.CreateFromImageAsync(await System.IO.File.ReadAllBytesAsync(path, ct), cancellationToken: ct).ConfigureAwait(false);
        return FromWrap(wrap);
    }

    /// <summary>Decodes image bytes (.png, .tex, and other well-known formats).</summary>
    /// <param name="bytes">Encoded image bytes.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static async Task<GpuTexture?> FromBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        NoireDraw3D.EnsureInitialized();
        using var wrap = await NoireService.TextureProvider.CreateFromImageAsync(bytes, cancellationToken: ct).ConfigureAwait(false);
        return FromWrap(wrap);
    }

    /// <summary>Loads a game icon by id.</summary>
    /// <param name="iconId">The icon id.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static async Task<GpuTexture?> FromGameIconAsync(uint iconId, CancellationToken ct = default)
    {
        NoireDraw3D.EnsureInitialized();
        using var wrap = await NoireService.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).RentAsync(ct).ConfigureAwait(false);
        return FromWrap(wrap);
    }

    /// <summary>Loads a texture from a game path (e.g. <c>ui/uld/...</c> .tex).</summary>
    /// <param name="gamePath">The internal game path.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static async Task<GpuTexture?> FromGamePathAsync(string gamePath, CancellationToken ct = default)
    {
        NoireDraw3D.EnsureInitialized();
        using var wrap = await NoireService.TextureProvider.GetFromGame(gamePath).RentAsync(ct).ConfigureAwait(false);
        return FromWrap(wrap);
    }
}

/// <summary>
/// Opens textures rendered by another process via DXGI shared handles - the socket that makes external
/// producers (e.g. an off-screen browser renderer) usable as ordinary material textures.
/// If the producer uses a keyed mutex, it is acquired/released automatically around each frame's use.
/// </summary>
public static unsafe class ExternalTexture
{
    /// <summary>
    /// Opens a shared texture.<br/>
    /// NT handles require ID3D11Device1 (captured at init; absence degrades only this feature).
    /// </summary>
    /// <param name="handle">The shared handle from the producing process.</param>
    /// <param name="ntHandle">True for NT handles (D3D11.1 sharing), false for legacy/KMT handles.</param>
    /// <returns>The texture, or null on failure (logged).</returns>
    public static GpuTexture? FromSharedHandle(nint handle, bool ntHandle)
    {
        if (handle == 0)
            return null;

        var device = NoireDraw3D.RequireDevice();

        ID3D11Texture2D* texture = null;
        HRESULT hr;
        if (ntHandle)
        {
            var device1 = device.Device1;
            if (device1 == null)
            {
                NoireLogger.LogError("ExternalTexture: NT-handle sharing requires ID3D11Device1, which this device does not expose.", "Draw3D");
                return null;
            }

            hr = device1->OpenSharedResource1((HANDLE)handle, __uuidof<ID3D11Texture2D>(), (void**)&texture);
        }
        else
        {
            hr = device.Device->OpenSharedResource((HANDLE)handle, __uuidof<ID3D11Texture2D>(), (void**)&texture);
        }

        if (hr < 0 || texture == null)
        {
            NoireLogger.LogError($"ExternalTexture: OpenSharedResource failed (hr=0x{(int)hr:X8}).", "Draw3D");
            return null;
        }


        D3D11_TEXTURE2D_DESC desc;
        texture->GetDesc(&desc);

        ID3D11ShaderResourceView* srv = null;
        if (device.Device->CreateShaderResourceView((ID3D11Resource*)texture, null, &srv) < 0 || srv == null)
        {
            texture->Release();
            NoireLogger.LogError("ExternalTexture: CreateShaderResourceView failed on the shared resource.", "Draw3D");
            return null;
        }

        var result = GpuTexture.FromSharedResource((ID3D11Resource*)texture, srv, (int)desc.Width, (int)desc.Height);
        texture->Release(); // FromSharedResource AddRef'd; drop the open's reference
        return result;
    }
}
