using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Owns the (borrowed) game D3D11 device and immediate context, validated via QueryInterface.<br/>
/// The device is free-threaded (resource creation is safe from any thread); the immediate context
/// may only be used on the render thread inside the present callback.
/// </summary>
internal sealed unsafe class RenderDevice : IDisposable
{
    private ComPtr<ID3D11Device> device;
    private ComPtr<ID3D11DeviceContext> context;
    private ComPtr<ID3D11Device1> device1;
    private bool disposed;

    /// <summary>The game's D3D11 device. Never null while not disposed.</summary>
    public ID3D11Device* Device => device.Get();

    /// <summary>The immediate context. Render thread only.</summary>
    public ID3D11DeviceContext* Context => context.Get();

    /// <summary>The ID3D11Device1 interface when available (shared-handle textures); null otherwise.</summary>
    public ID3D11Device1* Device1 => device1.Get();

    /// <summary>The device feature level, captured at creation.</summary>
    public D3D_FEATURE_LEVEL FeatureLevel { get; private set; }

    private RenderDevice() { }

    /// <summary>
    /// Acquires the game device via <see cref="GameRenderSources.GetDeviceUnknown"/> (Kernel.Device primary,
    /// Dalamud's DeviceHandle fallback), QI-validated. Returns null when no device is reachable yet.
    /// </summary>
    public static RenderDevice? TryCreate()
    {
        var unknown = (IUnknown*)GameRenderSources.GetDeviceUnknown();
        if (unknown == null)
            return null;

        if (!ComPtrUtil.TryQi<ID3D11Device>(unknown, out var dev))
        {
            NoireLogger.LogError<RenderDevice>("The game device pointer does not QueryInterface to ID3D11Device.", "Draw3D");
            return null;
        }

        var result = new RenderDevice { device = dev };
        dev.Get()->GetImmediateContext(result.context.GetAddressOf());
        result.FeatureLevel = dev.Get()->GetFeatureLevel();

        if (ComPtrUtil.TryQi<ID3D11Device1>((IUnknown*)dev.Get(), out var dev1))
            result.device1 = dev1;

        NoireLogger.LogDebug<RenderDevice>($"Acquired D3D11 device (feature level 0x{(int)result.FeatureLevel:X}, ID3D11Device1: {(result.device1.Get() != null ? "yes" : "no")}).", "Draw3D");
        return result;
    }

    /// <summary>
    /// Developer affordance: when the D3D11 debug layer is active, break into the debugger on corruption/error messages.
    /// No-op when the debug layer is absent (the normal case).
    /// </summary>
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

    /// <summary>
    /// Debug-build leak audit: prints live device objects to the debug output when the debug layer is available.
    /// After a clean dispose only the device itself should remain.
    /// </summary>
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

    /// <inheritdoc/>
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
