using System;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// One-frame diagnostic for the game's shadow passes: which depth-only binds the frame runs, what each one
/// renders into, and what sits in the VS constant buffers at each one's first draw.<br/>
/// Shadow casting needs the LIGHT's view-projection, per cascade, and nothing on the CPU side hands it over -
/// it has to be found in the constants the game's own shadow draws consume, the way the camera was. The camera
/// capture cannot be reused as-is because its whole matching strategy validates candidates against a
/// same-instant struct camera read, and no such reference exists for a light. So the first step is this probe:
/// read what is actually there, classify the matrix-shaped windows (an orthographic projection reads very
/// differently from an object's rigid world transform), and let the injection be built on those readings.<br/>
/// Armed for exactly one frame; every capture is a CopyResource plus a synchronous map, so the frame it runs
/// on stalls - a one-shot diagnostic, never a resident cost.
/// </summary>
internal sealed unsafe class ShadowProbe : IDisposable
{
    private const int MaxBinds = 12;
    private const int VsSlotCount = 14;
    private const int MaxBufferBytes = 4096;
    private const int MaxStagingPool = 8;

    private readonly StringBuilder report = new();
    private volatile bool armed;
    private bool pendingDraw;
    private int bindsSeen;
    private int bindsCaptured;

    private readonly ID3D11Buffer*[] stagingPool = new ID3D11Buffer*[MaxStagingPool];
    private readonly int[] stagingSizes = new int[MaxStagingPool];
    private int stagingCount;

    /// <summary>Whether the probe is watching the current frame.</summary>
    public bool Armed => armed;

    /// <summary>Arms the probe for the next frame's depth-only binds.</summary>
    public void Arm()
    {
        report.Clear();
        bindsSeen = 0;
        bindsCaptured = 0;
        pendingDraw = false;
        armed = true;
    }

    /// <summary>
    /// A depth-only bind just applied (no color target resolved, a depth-stencil present). Records what it
    /// renders into; the constants are read at the bind's first draw, because at the bind itself the VS slots
    /// still hold the previous pass's buffers - the same trap the camera capture documented.
    /// </summary>
    /// <param name="dsv">The bound depth-stencil view.</param>
    /// <param name="isMainSceneDepth">Whether this is the scene's own depth, so a pre-pass is not mistaken for a shadow map.</param>
    public void OnDepthOnlyBind(nint dsv, bool isMainSceneDepth)
    {
        if (!armed || dsv == 0)
            return;

        bindsSeen++;
        if (bindsCaptured >= MaxBinds)
            return;

        report.Append($"depth-only bind #{bindsSeen}: ");

        var view = (ID3D11DepthStencilView*)dsv;
        ID3D11Resource* resource = null;
        view->GetResource(&resource);
        if (resource != null)
        {
            if (ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var texture))
            {
                D3D11_TEXTURE2D_DESC desc;
                texture.Get()->GetDesc(&desc);
                report.Append($"{desc.Width}x{desc.Height} fmt {desc.Format} arraySize {desc.ArraySize}");
                texture.Dispose();
            }

            resource->Release();
        }

        D3D11_DEPTH_STENCIL_VIEW_DESC viewDesc;
        view->GetDesc(&viewDesc);
        if (viewDesc.ViewDimension == D3D11_DSV_DIMENSION.D3D11_DSV_DIMENSION_TEXTURE2DARRAY)
            report.Append($" slice {viewDesc.Texture2DArray.FirstArraySlice}");

        report.AppendLine(isMainSceneDepth ? "  (MAIN scene depth - a pre-pass, not a shadow map)" : string.Empty);
        pendingDraw = true;
    }

    /// <summary>First draw after a recorded depth-only bind: reads the VS constant buffers as bound right now.</summary>
    public void OnGameDraw(ID3D11DeviceContext* ctx)
    {
        if (!armed || !pendingDraw)
            return;

        pendingDraw = false;
        bindsCaptured++;

        var bound = stackalloc ID3D11Buffer*[VsSlotCount];
        ctx->VSGetConstantBuffers(0, VsSlotCount, bound);

        for (var slot = 0; slot < VsSlotCount; slot++)
        {
            var buffer = bound[slot];
            if (buffer == null)
                continue;

            D3D11_BUFFER_DESC desc;
            buffer->GetDesc(&desc);
            if (desc.ByteWidth <= MaxBufferBytes)
                ScanBuffer(ctx, buffer, slot, (int)desc.ByteWidth);
            else
                report.AppendLine($"  vs b{slot}: {desc.ByteWidth} B (beyond the probe's copy bound)");

            buffer->Release();
        }
    }

    /// <summary>Frame over: logs the report and disarms. Idempotent when not armed.</summary>
    public void OnFrameBoundary()
    {
        if (!armed)
            return;

        armed = false;
        pendingDraw = false;

        if (bindsSeen == 0)
            report.AppendLine("no depth-only binds this frame - shadows may be disabled in the game's settings.");
        if (bindsSeen > bindsCaptured)
            report.AppendLine($"({bindsSeen} depth-only binds seen, first {bindsCaptured} captured)");

        NoireLogger.LogInfo($"Draw3D shadow probe:\n{report}", "Draw3D");
        report.Clear();
    }

    /// <summary>
    /// Copies one bound constant buffer to the CPU and reports every window that reads as a matrix. The
    /// classification is deliberately shallow - the probe's job is to make the candidates visible, not to
    /// decide; the row norms are what tell an orthographic projection (small, axis-dependent scales) from an
    /// object's rigid world transform (unit rows), and the reader does that with the numbers in front of them.
    /// </summary>
    private void ScanBuffer(ID3D11DeviceContext* ctx, ID3D11Buffer* buffer, int slot, int byteWidth)
    {
        var staging = AcquireStaging(ctx, byteWidth);
        if (staging == null)
        {
            report.AppendLine($"  vs b{slot}: {byteWidth} B (no staging buffer - skipped)");
            return;
        }

        ctx->CopyResource((ID3D11Resource*)staging, (ID3D11Resource*)buffer);

        D3D11_MAPPED_SUBRESOURCE mapped;
        if (ctx->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0 || mapped.pData == null)
        {
            report.AppendLine($"  vs b{slot}: {byteWidth} B (map failed)");
            return;
        }

        try
        {
            report.AppendLine($"  vs b{slot}: {byteWidth} B");
            var floats = new ReadOnlySpan<float>(mapped.pData, byteWidth / sizeof(float));
            for (var offset = 0; offset + 16 <= floats.Length; offset += 4)
                DescribeWindow(floats.Slice(offset, 16), slot, offset * sizeof(float));
        }
        finally
        {
            ctx->Unmap((ID3D11Resource*)staging, 0);
        }
    }

    /// <summary>Reports one 16-float window when it is matrix-shaped, in both layouts it could be stored in.</summary>
    private void DescribeWindow(ReadOnlySpan<float> window, int slot, int byteOffset)
    {
        // Copied out because a span cannot be captured by the accessor below; sixteen floats.
        var w = new float[16];
        for (var i = 0; i < 16; i++)
        {
            if (!float.IsFinite(window[i]))
                return;

            w[i] = window[i];
        }

        for (var transposed = 0; transposed < 2; transposed++)
        {
            // Read as row-vector convention (the camera's): rows 0..2 are the basis, row 3 the translation,
            // column 3 the projective part. The transposed pass reads the same bytes column-major.
            float At(int r, int c) => transposed == 0 ? w[(r * 4) + c] : w[(c * 4) + r];

            var col3 = (X: At(0, 3), Y: At(1, 3), Z: At(2, 3), W: At(3, 3));
            var r0 = MathF.Sqrt((At(0, 0) * At(0, 0)) + (At(0, 1) * At(0, 1)) + (At(0, 2) * At(0, 2)));
            var r1 = MathF.Sqrt((At(1, 0) * At(1, 0)) + (At(1, 1) * At(1, 1)) + (At(1, 2) * At(1, 2)));
            var r2 = MathF.Sqrt((At(2, 0) * At(2, 0)) + (At(2, 1) * At(2, 1)) + (At(2, 2) * At(2, 2)));
            if (r0 < 1e-6f || r1 < 1e-6f || r2 < 1e-6f)
                continue;

            var affine = MathF.Abs(col3.X) < 1e-6f && MathF.Abs(col3.Y) < 1e-6f && MathF.Abs(col3.Z) < 1e-6f
                         && MathF.Abs(col3.W - 1f) < 1e-4f;
            var perspective = MathF.Abs(col3.W) < 1e-4f
                              && MathF.Sqrt((col3.X * col3.X) + (col3.Y * col3.Y) + (col3.Z * col3.Z)) is > 0.9f and < 1.1f;
            if (!affine && !perspective)
                continue;

            // An identity is a matrix too, and reporting hundreds of them would bury the real candidates.
            var identity = affine
                           && MathF.Abs(r0 - 1f) < 1e-5f && MathF.Abs(r1 - 1f) < 1e-5f && MathF.Abs(r2 - 1f) < 1e-5f
                           && MathF.Abs(At(3, 0)) < 1e-5f && MathF.Abs(At(3, 1)) < 1e-5f && MathF.Abs(At(3, 2)) < 1e-5f;
            if (identity)
                continue;

            var translation = MathF.Sqrt((At(3, 0) * At(3, 0)) + (At(3, 1) * At(3, 1)) + (At(3, 2) * At(3, 2)));
            report.AppendLine(
                $"    @{byteOffset,4} {(transposed == 0 ? "as-is" : "transposed")} {(perspective ? "PERSPECTIVE" : "affine")}"
                + $" rows {r0:G4}/{r1:G4}/{r2:G4} translation {translation:G4}");
        }
    }

    /// <summary>A CPU-readable staging buffer of the given size, pooled per distinct size.</summary>
    private ID3D11Buffer* AcquireStaging(ID3D11DeviceContext* ctx, int byteWidth)
    {
        for (var i = 0; i < stagingCount; i++)
        {
            if (stagingSizes[i] == byteWidth)
                return stagingPool[i];
        }

        if (stagingCount >= MaxStagingPool)
            return null;

        ID3D11Device* device = null;
        ctx->GetDevice(&device);
        if (device == null)
            return null;

        var desc = new D3D11_BUFFER_DESC
        {
            ByteWidth = (uint)byteWidth,
            Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
            CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
        };

        ID3D11Buffer* buffer = null;
        var created = device->CreateBuffer(&desc, null, &buffer) >= 0 && buffer != null;
        device->Release();
        if (!created)
            return null;

        stagingPool[stagingCount] = buffer;
        stagingSizes[stagingCount] = byteWidth;
        stagingCount++;
        return buffer;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        for (var i = 0; i < stagingCount; i++)
        {
            if (stagingPool[i] != null)
            {
                stagingPool[i]->Release();
                stagingPool[i] = null;
            }
        }

        stagingCount = 0;
        armed = false;
    }
}
