using System;
using System.Runtime.CompilerServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

internal sealed unsafe class GpuBuffer : IDisposable
{
    private TerraFX.Interop.Windows.ComPtr<ID3D11Buffer> buffer;

    public ID3D11Buffer* Buffer => buffer.Get();

    public uint SizeBytes { get; private init; }

    private GpuBuffer() { }

    // Safe from any thread. The device is free-threaded.
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

    // Render thread only.
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

    public void Dispose()
    {
        buffer.Dispose();
        buffer = default;
    }
}

// A resize after warm-up is a steady-state GPU allocation.
internal sealed unsafe class DynamicRing : IDisposable
{
    private readonly D3D11_BIND_FLAG bind;
    private readonly string name;
    private GpuBuffer? buffer;
    private uint cursor;
    private bool discardNext = true;

    public ID3D11Buffer* Buffer => buffer != null ? buffer.Buffer : null;

    public uint CapacityBytes => buffer?.SizeBytes ?? 0;

    public DynamicRing(D3D11_BIND_FLAG bind, uint initialCapacityBytes, string name)
    {
        this.bind = bind;
        this.name = name;
        InitialCapacity = initialCapacityBytes;
    }

    private uint InitialCapacity { get; }

    public void BeginFrame() => discardNext = true;

    // Render thread only.
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
                NoireLogger.LogDebug<DynamicRing>($"Growing {name} ring {buffer.SizeBytes} to {newSize} bytes.", "[Draw3D] ");

            buffer?.Dispose(); // in-flight GPU commands hold their own reference
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

    public void Dispose()
    {
        buffer?.Dispose();
        buffer = null;
    }
}
