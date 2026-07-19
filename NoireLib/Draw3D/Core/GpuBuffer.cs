using System;
using System.Runtime.CompilerServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// A thin ID3D11Buffer wrapper: immutable (mesh data), dynamic (rings), or default-usage constant buffers.
/// </summary>
internal sealed unsafe class GpuBuffer : IDisposable
{
    private TerraFX.Interop.Windows.ComPtr<ID3D11Buffer> buffer;

    /// <summary>The raw buffer pointer (null after dispose).</summary>
    public ID3D11Buffer* Buffer => buffer.Get();

    /// <summary>Buffer capacity in bytes.</summary>
    public uint SizeBytes { get; private init; }

    private GpuBuffer() { }

    /// <summary>Creates an immutable buffer with initial data. Safe from any thread (devices are free-threaded).</summary>
    public static GpuBuffer CreateImmutable(RenderDevice device, void* data, uint sizeBytes, D3D11_BIND_FLAG bind)
    {
        var desc = new D3D11_BUFFER_DESC
        {
            ByteWidth = sizeBytes,
            Usage = D3D11_USAGE.D3D11_USAGE_IMMUTABLE,
            BindFlags = (uint)bind,
        };
        var init = new D3D11_SUBRESOURCE_DATA { pSysMem = data };

        var result = new GpuBuffer { SizeBytes = sizeBytes };
        ThrowIfFailed(device.Device->CreateBuffer(&desc, &init, result.buffer.GetAddressOf()), "immutable buffer");
        return result;
    }

    /// <summary>Creates a CPU-writable dynamic buffer (ring usage: WRITE_DISCARD / WRITE_NO_OVERWRITE).</summary>
    public static GpuBuffer CreateDynamic(RenderDevice device, uint sizeBytes, D3D11_BIND_FLAG bind)
    {
        var desc = new D3D11_BUFFER_DESC
        {
            ByteWidth = sizeBytes,
            Usage = D3D11_USAGE.D3D11_USAGE_DYNAMIC,
            BindFlags = (uint)bind,
            CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE,
        };

        var result = new GpuBuffer { SizeBytes = sizeBytes };
        ThrowIfFailed(device.Device->CreateBuffer(&desc, null, result.buffer.GetAddressOf()), "dynamic buffer");
        return result;
    }

    /// <summary>Creates a default-usage constant buffer updated via UpdateSubresource. Size is rounded up to 16 bytes.</summary>
    public static GpuBuffer CreateConstant(RenderDevice device, uint sizeBytes)
    {
        sizeBytes = (sizeBytes + 15u) & ~15u;
        var desc = new D3D11_BUFFER_DESC
        {
            ByteWidth = sizeBytes,
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER,
        };

        var result = new GpuBuffer { SizeBytes = sizeBytes };
        ThrowIfFailed(device.Device->CreateBuffer(&desc, null, result.buffer.GetAddressOf()), "constant buffer");
        return result;
    }

    /// <summary>Uploads a struct into a default-usage constant buffer. Render thread only.</summary>
    public void UpdateConstant<T>(ID3D11DeviceContext* ctx, in T value) where T : unmanaged
    {
        var copy = value;
        ctx->UpdateSubresource((ID3D11Resource*)Buffer, 0, null, Unsafe.AsPointer(ref copy), 0, 0);
    }

    private static void ThrowIfFailed(TerraFX.Interop.Windows.HRESULT hr, string what)
    {
        if (hr < 0)
            throw new InvalidOperationException($"Draw3D: failed to create {what} (hr=0x{(int)hr:X8}).");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        buffer.Dispose();
        buffer = default;
    }
}

/// <summary>
/// A growable dynamic-buffer ring: WRITE_DISCARD on the first map of each frame or on wrap,
/// WRITE_NO_OVERWRITE for appends within a frame. Growth doubles capacity and is logged, since a resize
/// after warm-up means a steady-state frame just allocated a GPU resource, which should not happen.
/// </summary>
internal sealed unsafe class DynamicRing : IDisposable
{
    private readonly D3D11_BIND_FLAG bind;
    private readonly string name;
    private GpuBuffer? buffer;
    private uint cursor;
    private bool discardNext = true;

    /// <summary>The current raw buffer pointer (may change on growth; null before first use).</summary>
    public ID3D11Buffer* Buffer => buffer != null ? buffer.Buffer : null;

    /// <summary>Current capacity in bytes.</summary>
    public uint CapacityBytes => buffer?.SizeBytes ?? 0;

    /// <summary>Creates a ring for the given bind point.</summary>
    public DynamicRing(D3D11_BIND_FLAG bind, uint initialCapacityBytes, string name)
    {
        this.bind = bind;
        this.name = name;
        InitialCapacity = initialCapacityBytes;
    }

    private uint InitialCapacity { get; }

    /// <summary>Marks the start of a frame: the next write discards and rewinds the ring.</summary>
    public void BeginFrame() => discardNext = true;

    /// <summary>
    /// Copies <paramref name="bytes"/> bytes into the ring and returns the byte offset they landed at.
    /// Grows (double, logged) when a single write exceeds capacity. Render thread only.
    /// </summary>
    public bool TryWrite(RenderDevice device, ID3D11DeviceContext* ctx, void* src, uint bytes, uint alignment, out uint offset)
    {
        offset = 0;
        if (bytes == 0)
            return true;

        if (buffer == null || bytes > buffer.SizeBytes)
        {
            var newSize = Math.Max(InitialCapacity, buffer?.SizeBytes ?? 0);
            while (newSize < bytes)
                newSize *= 2;

            if (buffer != null)
                NoireLogger.LogDebug<DynamicRing>($"Growing {name} ring {buffer.SizeBytes} to {newSize} bytes.", "Draw3D");

            buffer?.Dispose(); // in-flight GPU commands hold their own reference; safe.
            buffer = GpuBuffer.CreateDynamic(device, newSize, bind);
            cursor = 0;
            discardNext = true;
        }

        var aligned = (cursor + (alignment - 1)) & ~(alignment - 1);
        var discard = discardNext || aligned + bytes > buffer.SizeBytes;
        if (discard)
            aligned = 0;

        D3D11_MAPPED_SUBRESOURCE mapped;
        var mapType = discard ? D3D11_MAP.D3D11_MAP_WRITE_DISCARD : D3D11_MAP.D3D11_MAP_WRITE_NO_OVERWRITE;
        if (ctx->Map((ID3D11Resource*)buffer.Buffer, 0, mapType, 0, &mapped) < 0)
            return false;

        System.Buffer.MemoryCopy(src, (byte*)mapped.pData + aligned, buffer.SizeBytes - aligned, bytes);
        ctx->Unmap((ID3D11Resource*)buffer.Buffer, 0);

        cursor = aligned + bytes;
        discardNext = false;
        offset = aligned;
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        buffer?.Dispose();
        buffer = null;
    }
}
