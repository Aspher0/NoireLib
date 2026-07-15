using NoireLib.Draw3D.Core;
using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// A texture usable by Draw3D materials - a shader resource view plus optional owned resources.<br/>
/// <b>Ownership:</b> the creator disposes it. Materials and renderers only reference it; assigning one
/// texture to many materials is free.<br/>
/// Disposal is safe at any time from any thread: the wrapper is marked dead immediately (draws skip it)
/// and the GPU objects are released on the render thread at the start of the next frame.
/// </summary>
public sealed unsafe class GpuTexture : IDisposable
{
    private ComPtr<ID3D11ShaderResourceView> srv;
    private ComPtr<ID3D11Resource> resource;
    private ComPtr<IDXGIKeyedMutex> keyedMutex;
    private IDisposable? ownedWrap;
    private volatile bool disposed;

    /// <summary>Texture width in pixels (0 when unknown).</summary>
    public int Width { get; }

    /// <summary>Texture height in pixels (0 when unknown).</summary>
    public int Height { get; }

    /// <summary>True once disposed. Draws referencing a disposed texture are skipped and counted, never a crash.</summary>
    public bool IsDisposed => disposed;

    internal ID3D11ShaderResourceView* Srv => disposed ? null : srv.Get();

    internal nint SrvPointer => disposed ? 0 : (nint)srv.Get();

    internal bool HasKeyedMutex => !disposed && keyedMutex.Get() != null;

    private GpuTexture(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Wraps an existing SRV. When <paramref name="addRef"/> is true the SRV is borrowed (one AddRef now, one Release on dispose).</summary>
    internal static GpuTexture FromSrv(ID3D11ShaderResourceView* srvPtr, int width, int height, bool addRef, IDisposable? ownedWrap = null)
    {
        var tex = new GpuTexture(width, height) { ownedWrap = ownedWrap };
        if (addRef)
            srvPtr->AddRef();
        tex.srv.Attach(srvPtr);
        return tex;
    }

    /// <summary>Creates an owned RGBA8 texture from raw pixels. Safe from any thread.</summary>
    internal static GpuTexture CreateFromPixels(RenderDevice device, ReadOnlySpan<byte> rgbaPixels, int width, int height)
    {
        if (rgbaPixels.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than width*height*4.", nameof(rgbaPixels));

        var tex = new GpuTexture(width, height);
        fixed (byte* pixels = rgbaPixels)
        {
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_IMMUTABLE,
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            };
            var init = new D3D11_SUBRESOURCE_DATA { pSysMem = pixels, SysMemPitch = (uint)(width * 4) };

            ID3D11Texture2D* raw = null;
            if (device.Device->CreateTexture2D(&desc, &init, &raw) < 0 || raw == null)
                throw new InvalidOperationException("Draw3D: CreateTexture2D failed.");

            tex.resource.Attach((ID3D11Resource*)raw);
            ID3D11ShaderResourceView* srvRaw = null;
            if (device.Device->CreateShaderResourceView(tex.resource.Get(), null, &srvRaw) < 0 || srvRaw == null)
            {
                tex.resource.Dispose();
                throw new InvalidOperationException("Draw3D: CreateShaderResourceView failed.");
            }

            tex.srv.Attach(srvRaw);
        }

        return tex;
    }

    /// <summary>Wraps a shared-handle resource (external producer). Internal - see <see cref="ExternalTexture"/>.</summary>
    internal static GpuTexture FromSharedResource(ID3D11Resource* sharedResource, ID3D11ShaderResourceView* srvPtr, int width, int height)
    {
        var tex = new GpuTexture(width, height);
        sharedResource->AddRef();
        tex.resource.Attach(sharedResource);
        tex.srv.Attach(srvPtr);

        if (ComPtrUtil.TryQi<IDXGIKeyedMutex>((IUnknown*)sharedResource, out var mutex))
            tex.keyedMutex = mutex;

        return tex;
    }

    /// <summary>Acquires the producer's keyed mutex (key 0, non-blocking). No-op without a mutex.</summary>
    internal void AcquireSync()
    {
        var m = keyedMutex.Get();
        if (m != null)
            m->AcquireSync(0, 0);
    }

    /// <summary>Releases the producer's keyed mutex (key 0). No-op without a mutex.</summary>
    internal void ReleaseSync()
    {
        var m = keyedMutex.Get();
        if (m != null)
            m->ReleaseSync(0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // Defer the COM releases to the render thread so an in-progress frame can never bind a freed view.
        var srvCopy = srv;
        var resCopy = resource;
        var mutexCopy = keyedMutex;
        var wrapCopy = ownedWrap;
        srv = default;
        resource = default;
        keyedMutex = default;
        ownedWrap = null;

        NoireDraw3D.EnqueueRelease(() =>
        {
            srvCopy.Dispose();
            resCopy.Dispose();
            mutexCopy.Dispose();
            wrapCopy?.Dispose();
        });
    }
}
