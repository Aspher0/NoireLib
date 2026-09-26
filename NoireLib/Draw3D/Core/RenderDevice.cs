using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// The device is free-threaded. The immediate context is render thread only.
internal sealed unsafe class RenderDevice : IDisposable
{
    private ComPtr<ID3D11Device> device;
    private ComPtr<ID3D11DeviceContext> context;
    private ComPtr<ID3D11Device1> device1;
    private bool disposed;

    public ID3D11Device* Device => device.Get();

    public ID3D11DeviceContext* Context => context.Get();

    // Null without ID3D11Device1.
    public ID3D11Device1* Device1 => device1.Get();

    public D3D_FEATURE_LEVEL FeatureLevel { get; private set; }

    private RenderDevice() { }

    public static RenderDevice? TryCreate()
    {
        var unknown = (IUnknown*)GameRenderSources.GetDeviceUnknown();
        if (unknown == null)
            return null;

        if (!ComPtrUtil.TryQi<ID3D11Device>(unknown, out var dev))
        {
            NoireLogger.LogError<RenderDevice>("The game device pointer does not QueryInterface to ID3D11Device.", "[Draw3D] ");
            return null;
        }

        var result = new RenderDevice { device = dev };
        dev.Get()->GetImmediateContext(result.context.GetAddressOf());
        result.FeatureLevel = dev.Get()->GetFeatureLevel();

        if (ComPtrUtil.TryQi<ID3D11Device1>((IUnknown*)dev.Get(), out var dev1))
            result.device1 = dev1;

        NoireLogger.LogDebug<RenderDevice>($"Acquired D3D11 device (feature level 0x{(int)result.FeatureLevel:X}, ID3D11Device1: {(result.device1.Get() != null ? "yes" : "no")}).", "[Draw3D] ");
        return result;
    }

    // A no-op without the D3D11 debug layer.
    public bool TryEnableInfoQueueBreaks()
    {
        if (disposed || device.Get() == null)
            return false;

        if (!ComPtrUtil.TryQi<ID3D11InfoQueue>((IUnknown*)device.Get(), out var queue))
            return false;

        using (queue)
        {
            queue.Get()->SetBreakOnSeverity(D3D11_MESSAGE_SEVERITY.D3D11_MESSAGE_SEVERITY_CORRUPTION, TerraFX.Interop.Windows.BOOL.TRUE);
            queue.Get()->SetBreakOnSeverity(D3D11_MESSAGE_SEVERITY.D3D11_MESSAGE_SEVERITY_ERROR, TerraFX.Interop.Windows.BOOL.TRUE);
        }

        return true;
    }

    // After a clean dispose only the device itself should remain.
    public void ReportLiveObjects()
    {
        if (device.Get() == null)
            return;

        if (ComPtrUtil.TryQi<ID3D11Debug>((IUnknown*)device.Get(), out var dbg))
        {
            using (dbg)
                dbg.Get()->ReportLiveDeviceObjects(D3D11_RLDO_FLAGS.D3D11_RLDO_DETAIL | D3D11_RLDO_FLAGS.D3D11_RLDO_IGNORE_INTERNAL);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        device1.Dispose();
        context.Dispose();
        device.Dispose();
    }
}
