using NoireLib.Draw3D.Geometry;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

// Draws at the end of each shadow draw group, with the last caster's constants still bound. A bind's first draw does not reliably carry the owning pass.
// The game re-renders a cached light map only when it sees a change near that light.
internal sealed unsafe class ShadowInject : IDisposable
{
    internal readonly record struct Item(Mesh Mesh, Matrix4x4 World);

    // g_CameraParameter: m_ViewMatrix (three rows) at byte 0, m_ProjectionMatrix (four rows) at byte 288.
    private const int ViewOffset = 0;
    private const int ViewSize = 48;
    private const int ClipOffset = 288;
    private const int ClipSize = 64;

    // The near-field map binds a constant buffer of exactly one matrix.
    private const int DirectVpSize = 64;

    private const int MatrixBufferSize = ViewSize + ClipSize;

    private const int MaxScratchPool = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShadowCBData
    {
        public Matrix4x4 World;
        public Vector4 Mode;
    }

    // The shadow passes run before the frame's submissions arrive. They draw the previous frame's set.
    private readonly object gate = new();
    private List<Item> pending = new(16);
    private List<Item> active = new(16);

    private readonly StateGuard guard = new();

    private GpuBuffer? objectCb;
    private ID3D11Buffer* matrixBuffer;
    private ID3D11ShaderResourceView* matrixSrv;

    private readonly bool[] tracedModes = new bool[2];
    private ID3D11Buffer* traceStaging;
    private readonly ID3D11Buffer*[] scratchPool = new ID3D11Buffer*[MaxScratchPool];
    private readonly int[] scratchSizes = new int[MaxScratchPool];
    private int scratchCount;

    // Cloning keeps the map's depth bias. The game's front-face convention can cull every triangle.
    private readonly ID3D11RasterizerState*[] cullSources = new ID3D11RasterizerState*[MaxScratchPool];
    private readonly ID3D11RasterizerState*[] cullVariants = new ID3D11RasterizerState*[MaxScratchPool];
    private int cullCount;

    public int LastInjectedCount { get; private set; }

    // The near-field map is redrawn every frame.
    public int LastNearFieldCount { get; private set; }

    public int LastBindCount { get; private set; }

    public int LastEnteredCount { get; private set; }

    public int LastSkippedCount { get; private set; }

    private int bindsThisFrame;
    private int enteredThisFrame;
    private int skippedThisFrame;
    private int nearFieldThisFrame;

    public bool HasWork
    {
        get
        {
            lock (gate)
                return active.Count > 0 || pending.Count > 0;
        }
    }

    public void Enqueue(in Item item)
    {
        lock (gate)
            pending.Add(item);
    }

    public void Clear()
    {
        lock (gate)
        {
            pending.Clear();
            active.Clear();
        }

        tracedModes[0] = false;
        tracedModes[1] = false;
    }

    public void OnFrameBoundary()
    {
        lock (gate)
        {
            (active, pending) = (pending, active);
            pending.Clear();
        }

        LastBindCount = bindsThisFrame;
        LastEnteredCount = enteredThisFrame;
        LastSkippedCount = skippedThisFrame;
        LastNearFieldCount = nearFieldThisFrame;
        bindsThisFrame = 0;
        enteredThisFrame = 0;
        skippedThisFrame = 0;
        nearFieldThisFrame = 0;
    }

    public void Execute(RenderDevice device, ShaderLibrary shaders, ID3D11DeviceContext* ctx)
    {
        lock (gate)
        {
            if (active.Count == 0)
                return;
        }

        enteredThisFrame++;

        if (shaders.GetShadowDepth(device) is not { } pipeline || !EnsureResources(device))
        {
            skippedThisFrame++;
            return;
        }

        ID3D11Buffer* gameCb = null;
        ctx->VSGetConstantBuffers(0, 1, &gameCb);
        if (gameCb == null)
        {
            skippedThisFrame++;
            return;
        }

        D3D11_BUFFER_DESC desc;
        gameCb->GetDesc(&desc);

        float mode;
        if (desc.ByteWidth >= ClipOffset + ClipSize)
        {
            mode = 0f;
        }
        else if (desc.ByteWidth == DirectVpSize)
        {
            mode = 1f;
        }
        else
        {
            gameCb->Release();
            skippedThisFrame++;
            return;
        }

        // A constant buffer cannot be the source of a partial copy on every runtime.
        var scratch = AcquireScratch(device, (int)desc.ByteWidth);
        if (scratch == null)
        {
            gameCb->Release();
            skippedThisFrame++;
            return;
        }

        ctx->CopyResource((ID3D11Resource*)scratch, (ID3D11Resource*)gameCb);
        gameCb->Release();

        if (mode < 0.5f)
        {
            CopyWindow(ctx, scratch, ViewOffset, 0, ViewSize);
            CopyWindow(ctx, scratch, ClipOffset, ViewSize, ClipSize);
        }
        else
        {
            CopyWindow(ctx, scratch, 0, ViewSize, DirectVpSize);
        }

        var modeIndex = mode < 0.5f ? 0 : 1;
        if (!tracedModes[modeIndex])
        {
            tracedModes[modeIndex] = true;
            TraceOnce(device, ctx, mode);
        }

        guard.Capture(ctx);
        try
        {
            ctx->IASetInputLayout(pipeline.Layout);
            ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            ctx->VSSetShader(pipeline.Vs, null, 0);
            ctx->PSSetShader(null, null, 0); // the bound target set has no color views

            ID3D11RasterizerState* gameRaster = null;
            ctx->RSGetState(&gameRaster);
            var noCull = CullNoneVariant(device, gameRaster);
            if (gameRaster != null)
                gameRaster->Release();
            if (noCull != null)
                ctx->RSSetState(noCull);

            var srv = matrixSrv;
            ctx->VSSetShaderResources(0, 1, &srv);
            var ocb = objectCb!.Buffer;
            ctx->VSSetConstantBuffers(1, 1, &ocb);

            var drawn = DrawActive(ctx, mode);

            LastInjectedCount = drawn;
            if (drawn > 0)
            {
                bindsThisFrame++;
                if (mode >= 0.5f)
                    nearFieldThisFrame++;
            }
        }
        finally
        {
            guard.Restore(ctx);
        }
    }

    private int DrawActive(ID3D11DeviceContext* ctx, float mode)
    {
        var drawn = 0;
        lock (gate)
        {
            foreach (var item in active)
            {
                if (item.Mesh is not { IndexCount: > 0 } mesh || mesh.Vb == null || mesh.Ib == null)
                    continue;

                var data = default(ShadowCBData);
                data.World = Matrix4x4.Transpose(item.World);
                data.Mode = new Vector4(mode, 0f, 0f, 0f);
                objectCb!.UpdateConstant(ctx, data);

                var vb = mesh.Vb;
                var stride = (uint)sizeof(Vertex3D);
                var offset = 0u;
                ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
                ctx->IASetIndexBuffer(mesh.Ib, mesh.IndexFormat, 0);
                ctx->DrawIndexed((uint)mesh.IndexCount, 0, 0);
                drawn++;
            }
        }

        return drawn;
    }

    private bool EnsureResources(RenderDevice device)
    {
        if (matrixSrv != null && objectCb != null)
            return true;

        objectCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(ShadowCBData));
        if (objectCb == null)
            return false;

        if (matrixBuffer == null)
        {
            var desc = new D3D11_BUFFER_DESC
            {
                ByteWidth = MatrixBufferSize,
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            };

            fixed (ID3D11Buffer** p = &matrixBuffer)
            {
                if (device.Device->CreateBuffer(&desc, null, p) < 0)
                    return false;
            }
        }

        if (matrixSrv == null)
        {
            var view = new D3D11_SHADER_RESOURCE_VIEW_DESC
            {
                Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
                ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_BUFFER,
            };
            view.Buffer.FirstElement = 0;
            view.Buffer.NumElements = MatrixBufferSize / 16;

            fixed (ID3D11ShaderResourceView** p = &matrixSrv)
            {
                if (device.Device->CreateShaderResourceView((ID3D11Resource*)matrixBuffer, &view, p) < 0)
                    return false;
            }
        }

        return true;
    }

    private void TraceOnce(RenderDevice device, ID3D11DeviceContext* ctx, float mode)
    {
        if (traceStaging == null)
        {
            var desc = new D3D11_BUFFER_DESC
            {
                ByteWidth = MatrixBufferSize,
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            };

            fixed (ID3D11Buffer** p = &traceStaging)
            {
                if (device.Device->CreateBuffer(&desc, null, p) < 0)
                    return;
            }
        }

        ctx->CopyResource((ID3D11Resource*)traceStaging, (ID3D11Resource*)matrixBuffer);

        D3D11_MAPPED_SUBRESOURCE mapped;
        if (ctx->Map((ID3D11Resource*)traceStaging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0 || mapped.pData == null)
            return;

        try
        {
            var rows = new Vector4[MatrixBufferSize / 16];
            var floats = new ReadOnlySpan<float>(mapped.pData, rows.Length * 4);
            for (var i = 0; i < rows.Length; i++)
                rows[i] = new Vector4(floats[i * 4], floats[(i * 4) + 1], floats[(i * 4) + 2], floats[(i * 4) + 3]);

            var world = Vector4.UnitW;
            lock (gate)
            {
                if (active.Count > 0)
                {
                    var m = active[0].World;
                    world = new Vector4(m.M41, m.M42, m.M43, 1f);
                }
            }

            var view = mode < 0.5f
                ? new Vector4(Dot(rows[0], world), Dot(rows[1], world), Dot(rows[2], world), 1f)
                : world;
            var clip = new Vector4(Dot(rows[3], view), Dot(rows[4], view), Dot(rows[5], view), Dot(rows[6], view));
            var ndc = clip.W != 0f ? new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W) : Vector3.Zero;

            var text = new System.Text.StringBuilder();
            text.AppendLine($"Draw3D shadow trace (mode {mode}):");
            for (var i = 0; i < rows.Length; i++)
                text.AppendLine($"  row{i}: {rows[i].X:F4} {rows[i].Y:F4} {rows[i].Z:F4} {rows[i].W:F4}");
            text.AppendLine($"  world {world.X:F2},{world.Y:F2},{world.Z:F2} -> view {view.X:F2},{view.Y:F2},{view.Z:F2} -> clip {clip.X:F2},{clip.Y:F2},{clip.Z:F2},{clip.W:F2} -> ndc {ndc.X:F3},{ndc.Y:F3},{ndc.Z:F3}");
            NoireLogger.LogInfo(text.ToString(), "Draw3D");
        }
        finally
        {
            ctx->Unmap((ID3D11Resource*)traceStaging, 0);
        }
    }

    private static float Dot(Vector4 a, Vector4 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W);

    private void CopyWindow(ID3D11DeviceContext* ctx, ID3D11Buffer* scratch, int sourceOffset, int destinationOffset, int size)
    {
        var box = new D3D11_BOX
        {
            left = (uint)sourceOffset,
            right = (uint)(sourceOffset + size),
            top = 0,
            bottom = 1,
            front = 0,
            back = 1,
        };
        ctx->CopySubresourceRegion((ID3D11Resource*)matrixBuffer, 0, (uint)destinationOffset, 0, 0, (ID3D11Resource*)scratch, 0, &box);
    }

    private ID3D11RasterizerState* CullNoneVariant(RenderDevice device, ID3D11RasterizerState* source)
    {
        for (var i = 0; i < cullCount; i++)
        {
            if (cullSources[i] == source)
                return cullVariants[i];
        }

        if (cullCount >= cullSources.Length)
            return null;

        var desc = default(D3D11_RASTERIZER_DESC);
        if (source != null)
        {
            source->GetDesc(&desc);
        }
        else
        {
            desc.FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID;
            desc.DepthClipEnable = 1;
        }

        desc.CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE;

        ID3D11RasterizerState* created = null;
        if (device.Device->CreateRasterizerState(&desc, &created) < 0 || created == null)
            return null;

        // A state reallocated at the same address would serve another map's bias.
        if (source != null)
            source->AddRef();

        cullSources[cullCount] = source;
        cullVariants[cullCount] = created;
        cullCount++;
        return created;
    }

    private ID3D11Buffer* AcquireScratch(RenderDevice device, int byteWidth)
    {
        for (var i = 0; i < scratchCount; i++)
        {
            if (scratchSizes[i] == byteWidth)
                return scratchPool[i];
        }

        if (scratchCount >= MaxScratchPool)
            return null;

        var desc = new D3D11_BUFFER_DESC
        {
            ByteWidth = (uint)byteWidth,
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
        };

        ID3D11Buffer* buffer = null;
        if (device.Device->CreateBuffer(&desc, null, &buffer) < 0 || buffer == null)
            return null;

        scratchPool[scratchCount] = buffer;
        scratchSizes[scratchCount] = byteWidth;
        scratchCount++;
        return buffer;
    }

    public void Dispose()
    {
        Clear();

        objectCb?.Dispose();
        objectCb = null;

        if (matrixSrv != null) { matrixSrv->Release(); matrixSrv = null; }
        if (matrixBuffer != null) { matrixBuffer->Release(); matrixBuffer = null; }
        if (traceStaging != null) { traceStaging->Release(); traceStaging = null; }

        for (var i = 0; i < scratchCount; i++)
        {
            if (scratchPool[i] != null)
            {
                scratchPool[i]->Release();
                scratchPool[i] = null;
            }
        }

        scratchCount = 0;

        for (var i = 0; i < cullCount; i++)
        {
            if (cullSources[i] != null) { cullSources[i]->Release(); cullSources[i] = null; }
            if (cullVariants[i] != null) { cullVariants[i]->Release(); cullVariants[i] = null; }
        }

        cullCount = 0;
    }
}
