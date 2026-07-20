using NoireLib.Draw3D.Geometry;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Draws meshes depth-only into the GAME's shadow maps, inside the game's own shadow passes, so injected
/// geometry casts shadows.<br/>
/// <b>How the light is found.</b> Nothing on the CPU hands over a light's transform; it lives in the
/// g_CameraParameter block the game's own shadow draws consume, whose layout the game's shader reflection
/// names: m_ViewMatrix (world to the pass's view space) at byte 0 and m_ProjectionMatrix (view to clip)
/// at byte 288 - the two a shadow pass fills. This runs at the END of each shadow bind, with the last
/// caster draw's constants still bound - the pass's own settled values; a bind's FIRST draw does not
/// reliably carry the pass that owns the map. Both windows are copied GPU-side into a small row buffer the
/// vertex shader reads - no CPU round trip, no stall. The near-field map carries a 64-byte buffer holding
/// one whole transform instead; a bind whose constants match neither shape is skipped. See
/// docs/Draw3D Game Assets Status.md.<br/>
/// <b>What is inherited.</b> Depth compare, depth bias, viewport and culling all stay as the game configured
/// them for the very map being rendered; this pass changes only the shaders, the input assembly and its own
/// constant slots, and restores those.<br/>
/// <b>What this cannot reach.</b> A bind that issues no draws never exposes its constants, so maps the game
/// renders once and caches receive no injected geometry: objects cast into every map the game re-renders,
/// which is the sun's cascades and the lights near anything moving.
/// </summary>
internal sealed unsafe class ShadowInject : IDisposable
{
    /// <summary>One mesh queued for the shadow passes.</summary>
    /// <param name="Mesh">The geometry.</param>
    /// <param name="World">Its world transform.</param>
    internal readonly record struct Item(Mesh Mesh, Matrix4x4 World);

    // The g_CameraParameter block, named by the game's own shader reflection: m_ViewMatrix (world to the
    // pass's view space, three rows) sits at byte 0 and m_ProjectionMatrix (view to clip, four rows) at
    // byte 288. Those two are what a shadow pass fills - the probe finds no matrix at 96 (viewProj) or
    // 352 (mainViewToProj) in shadow binds - and the game's own geometry shaders compose positions the
    // same way: an instance transform into the pass's view space, then the projection rows. See
    // docs/Draw3D Game Assets Status.md.
    private const int ViewOffset = 0;
    private const int ViewSize = 48;
    private const int ClipOffset = 288;
    private const int ClipSize = 64;

    /// <summary>The near-field map binds a constant buffer of exactly one matrix, applied whole.</summary>
    private const int DirectVpSize = 64;

    /// <summary>The copied rows the vertex shader reads: three view rows, then four clip rows.</summary>
    private const int MatrixBufferSize = ViewSize + ClipSize;

    private const int MaxScratchPool = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShadowCBData
    {
        public Matrix4x4 World;
        public Vector4 Mode;
    }

    // Submissions land in pending on the caller's thread; the render thread draws active. The swap happens
    // at the frame boundary, so the shadow passes - which run at the very start of a frame, before any
    // submission for that frame could arrive - draw the previous frame's complete set instead of racing it.
    private readonly object gate = new();
    private List<Item> pending = new(16);
    private List<Item> active = new(16);

    private readonly StateGuard guard = new();

    private GpuBuffer? objectCb;
    private ID3D11Buffer* matrixBuffer;
    private ID3D11ShaderResourceView* matrixSrv;

    // One readback of the copied rows per casting session and constant layout, so a report of "no shadow"
    // comes with the numbers instead of another blind round: the rows as copied, and a queued mesh's world
    // position pushed through them exactly as the vertex shader does it. Each traced frame stalls once.
    private readonly bool[] tracedModes = new bool[2];
    private ID3D11Buffer* traceStaging;
    private readonly ID3D11Buffer*[] scratchPool = new ID3D11Buffer*[MaxScratchPool];
    private readonly int[] scratchSizes = new int[MaxScratchPool];
    private int scratchCount;

    // No-cull variants of the game's own shadow raster states, keyed by the source state (AddRef'd so the
    // key cannot be recycled under us). The game's state carries the depth bias the map was tuned with, so
    // it is cloned rather than replaced; only the cull mode changes, because the game's front-face
    // convention is not this renderer's and inheriting it can cull every injected triangle.
    private readonly ID3D11RasterizerState*[] cullSources = new ID3D11RasterizerState*[MaxScratchPool];
    private readonly ID3D11RasterizerState*[] cullVariants = new ID3D11RasterizerState*[MaxScratchPool];
    private int cullCount;

    /// <summary>How many meshes were drawn into the last shadow bind that ran, for diagnostics.</summary>
    public int LastInjectedCount { get; private set; }

    /// <summary>How many shadow binds received geometry last frame, for diagnostics.</summary>
    public int LastBindCount { get; private set; }

    /// <summary>How many shadow binds were entered with work queued last frame, for diagnostics.</summary>
    public int LastEnteredCount { get; private set; }

    /// <summary>
    /// How many entered binds were skipped last frame because their constants matched neither measured
    /// shape. Entered high with drawn zero means the constant layout moved; entered zero means the passes
    /// are not being seen at all.
    /// </summary>
    public int LastSkippedCount { get; private set; }

    private int bindsThisFrame;
    private int enteredThisFrame;
    private int skippedThisFrame;

    /// <summary>Whether anything is queued for the coming shadow passes.</summary>
    public bool HasWork
    {
        get
        {
            lock (gate)
                return active.Count > 0 || pending.Count > 0;
        }
    }

    /// <summary>Queues a mesh for the next frame's shadow passes. Called from the normal render path, not the render thread.</summary>
    public void Enqueue(in Item item)
    {
        lock (gate)
            pending.Add(item);
    }

    /// <summary>Drops everything queued, so a lapsed caller's meshes do not reappear at stale positions when it resumes.</summary>
    public void Clear()
    {
        lock (gate)
        {
            pending.Clear();
            active.Clear();
        }

        tracedModes[0] = false; // the next casting session logs its rows again
        tracedModes[1] = false;
    }

    /// <summary>Frame over (render thread): this frame's submissions become the set the next frame's shadow passes draw.</summary>
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
        bindsThisFrame = 0;
        enteredThisFrame = 0;
        skippedThisFrame = 0;
    }

    /// <summary>
    /// Draws the active meshes into the currently bound shadow map. Runs on the render thread at the END of
    /// a shadow bind, with the game's own depth target, viewport, raster state and last-draw constants
    /// still bound.
    /// </summary>
    /// <param name="device">The render device.</param>
    /// <param name="shaders">The shader library supplying the depth-only pipeline.</param>
    /// <param name="ctx">The game's immediate context.</param>
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

        // The light's view-projection, copied GPU-side out of whatever the game's own draws are about to
        // consume. Read before the state capture: nothing here changes pipeline state.
        ID3D11Buffer* gameCb = null;
        ctx->VSGetConstantBuffers(0, 1, &gameCb);
        if (gameCb == null)
        {
            skippedThisFrame++;
            return;
        }

        D3D11_BUFFER_DESC desc;
        gameCb->GetDesc(&desc);

        // The camera-parameter layout needs both named matrices present; the near-field map's constants
        // are a single whole transform instead. Anything else is not a layout a light has been measured in.
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

        // Two copies rather than one: a constant buffer cannot be the source or target of a partial copy on
        // every runtime this may run on, so the whole buffer is cloned to a plain scratch buffer first and
        // the matrix windows lifted out of that.
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
            ctx->PSSetShader(null, null, 0); // depth-only: the bound target set has no color views

            // The game's raster state, with the culling turned off: its depth bias is the map's tuning and
            // must survive, but its front-face convention is not this renderer's, and a wrong inherited
            // cull silently removes every injected triangle.
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
                    objectCb.UpdateConstant(ctx, data);

                    var vb = mesh.Vb;
                    var stride = (uint)sizeof(Vertex3D);
                    var offset = 0u;
                    ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
                    ctx->IASetIndexBuffer(mesh.Ib, mesh.IndexFormat, 0);
                    ctx->DrawIndexed((uint)mesh.IndexCount, 0, 0);
                    drawn++;
                }
            }

            LastInjectedCount = drawn;
            if (drawn > 0)
                bindsThisFrame++;
        }
        finally
        {
            guard.Restore(ctx);
        }
    }

    /// <summary>Creates the matrix buffer, its view and the object constants once. Returns whether they are usable.</summary>
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

    /// <summary>
    /// Logs the copied rows and a queued mesh's world position pushed through them the way the vertex
    /// shader does it. Once per casting session, on the render thread; the copy-and-map stalls that frame.
    /// </summary>
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

            // The same arithmetic the shader runs, so the log answers where this mesh lands in this map.
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

    /// <summary>Lifts one matrix window out of the scratch clone into the row buffer the vertex shader reads.</summary>
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

    /// <summary>The no-cull clone of a game raster state, created once per distinct source state.</summary>
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

        // The source pointer is the cache key, so it is pinned: a released-and-reallocated state at the
        // same address would silently serve another map's bias.
        if (source != null)
            source->AddRef();

        cullSources[cullCount] = source;
        cullVariants[cullCount] = created;
        cullCount++;
        return created;
    }

    /// <summary>A plain copy-target buffer of the given size, pooled per distinct size like the probe's staging pool.</summary>
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

    /// <inheritdoc/>
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
