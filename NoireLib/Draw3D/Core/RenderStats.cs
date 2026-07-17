using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Frame counters plus a 4-deep GPU timestamp-query ring (resolved oldest-first, never stalling).
/// "Why is nothing drawing" must always be answerable without a debugger - every skip path increments a named counter.
/// </summary>
internal sealed unsafe class RenderStats : IDisposable
{
    private const int RingDepth = 4;

    // Cumulative counters (reset via ResetCounters).
    public long FramesRendered;
    public long FramesSkippedDisabled;
    public long FramesSkippedInitPending;
    public long FramesSkippedNoDevice;
    public long FramesSkippedNoCamera;
    public long FramesSkippedZeroSize;
    public long FramesSkippedEmpty;
    public long FramesSkippedUiHidden;
    public long DepthOffFrames;
    public long DisposedAssetDraws;
    public long DynamicGeometryOverflows;
    public long ImCommandsDropped;

    // Per-frame values (rewritten every rendered frame).
    public int DrawCalls;
    public int Instances;
    public int Triangles;
    public int Batches;
    public int CulledItems;
    public int VisibleItems;
    public int ProtectRects;
    public bool DepthAvailable;
    public bool UsedFallbackCamera;

    /// <summary>Last resolved GPU time for the scene pass, in milliseconds.</summary>
    public float SceneGpuMs { get; private set; }

    /// <summary>Last resolved GPU time for the composite, in milliseconds.</summary>
    public float CompositeGpuMs { get; private set; }

    private readonly ComPtr<ID3D11Query>[] disjoint = new ComPtr<ID3D11Query>[RingDepth];
    private readonly ComPtr<ID3D11Query>[] tsStart = new ComPtr<ID3D11Query>[RingDepth];
    private readonly ComPtr<ID3D11Query>[] tsScene = new ComPtr<ID3D11Query>[RingDepth];
    private readonly ComPtr<ID3D11Query>[] tsEnd = new ComPtr<ID3D11Query>[RingDepth];
    private readonly bool[] inFlight = new bool[RingDepth];
    private int writeIndex;
    private bool queriesCreated;

    /// <summary>Resets the per-frame values at the top of a rendered frame.</summary>
    public void BeginFrameCounters()
    {
        DrawCalls = 0;
        Instances = 0;
        Triangles = 0;
        Batches = 0;
        CulledItems = 0;
        VisibleItems = 0;
        ProtectRects = 0;
    }

    /// <summary>Begins GPU timing for this frame (resolves the oldest completed ring slot first).</summary>
    public void BeginGpuTiming(RenderDevice device, ID3D11DeviceContext* ctx)
    {
        if (!queriesCreated)
        {
            queriesCreated = true;
            for (var i = 0; i < RingDepth; i++)
            {
                var disjointDesc = new D3D11_QUERY_DESC { Query = D3D11_QUERY.D3D11_QUERY_TIMESTAMP_DISJOINT };
                var tsDesc = new D3D11_QUERY_DESC { Query = D3D11_QUERY.D3D11_QUERY_TIMESTAMP };
                device.Device->CreateQuery(&disjointDesc, disjoint[i].GetAddressOf());
                device.Device->CreateQuery(&tsDesc, tsStart[i].GetAddressOf());
                device.Device->CreateQuery(&tsDesc, tsScene[i].GetAddressOf());
                device.Device->CreateQuery(&tsDesc, tsEnd[i].GetAddressOf());
            }
        }

        // Resolve the oldest slot if its data is ready (query results arrive frames later - never block).
        var readIndex = (writeIndex + 1) % RingDepth;
        if (inFlight[readIndex])
            TryResolve(ctx, readIndex);

        if (disjoint[writeIndex].Get() == null || inFlight[writeIndex])
            return;

        ctx->Begin((ID3D11Asynchronous*)disjoint[writeIndex].Get());
        ctx->End((ID3D11Asynchronous*)tsStart[writeIndex].Get());
    }

    /// <summary>Marks the end of the scene pass on the GPU timeline.</summary>
    public void MarkSceneDone(ID3D11DeviceContext* ctx)
    {
        if (queriesCreated && !inFlight[writeIndex] && tsScene[writeIndex].Get() != null)
            ctx->End((ID3D11Asynchronous*)tsScene[writeIndex].Get());
    }

    /// <summary>Ends GPU timing for this frame and advances the ring.</summary>
    public void EndGpuTiming(ID3D11DeviceContext* ctx)
    {
        if (!queriesCreated || inFlight[writeIndex] || disjoint[writeIndex].Get() == null)
            return;

        ctx->End((ID3D11Asynchronous*)tsEnd[writeIndex].Get());
        ctx->End((ID3D11Asynchronous*)disjoint[writeIndex].Get());
        inFlight[writeIndex] = true;
        writeIndex = (writeIndex + 1) % RingDepth;
    }

    private void TryResolve(ID3D11DeviceContext* ctx, int index)
    {
        const uint DoNotFlush = 1; // D3D11_ASYNC_GETDATA_DONOTFLUSH

        D3D11_QUERY_DATA_TIMESTAMP_DISJOINT disjointData;
        if (ctx->GetData((ID3D11Asynchronous*)disjoint[index].Get(), &disjointData, (uint)sizeof(D3D11_QUERY_DATA_TIMESTAMP_DISJOINT), DoNotFlush) != 0)
            return; // not ready yet

        ulong start, scene, end;
        if (ctx->GetData((ID3D11Asynchronous*)tsStart[index].Get(), &start, sizeof(ulong), DoNotFlush) != 0
            || ctx->GetData((ID3D11Asynchronous*)tsScene[index].Get(), &scene, sizeof(ulong), DoNotFlush) != 0
            || ctx->GetData((ID3D11Asynchronous*)tsEnd[index].Get(), &end, sizeof(ulong), DoNotFlush) != 0)
            return;

        inFlight[index] = false;
        if (disjointData.Disjoint || disjointData.Frequency == 0)
            return;

        var toMs = 1000.0 / disjointData.Frequency;
        SceneGpuMs = (float)((scene - start) * toMs);
        CompositeGpuMs = (float)((end - scene) * toMs);
    }

    /// <summary>Resets the cumulative counters.</summary>
    public void ResetCounters()
    {
        FramesRendered = 0;
        FramesSkippedDisabled = 0;
        FramesSkippedInitPending = 0;
        FramesSkippedNoDevice = 0;
        FramesSkippedNoCamera = 0;
        FramesSkippedZeroSize = 0;
        FramesSkippedEmpty = 0;
        FramesSkippedUiHidden = 0;
        DepthOffFrames = 0;
        DisposedAssetDraws = 0;
        DynamicGeometryOverflows = 0;
        ImCommandsDropped = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        for (var i = 0; i < RingDepth; i++)
        {
            disjoint[i].Dispose();
            tsStart[i].Dispose();
            tsScene[i].Dispose();
            tsEnd[i].Dispose();
            inFlight[i] = false;
        }

        queriesCreated = false;
    }
}
