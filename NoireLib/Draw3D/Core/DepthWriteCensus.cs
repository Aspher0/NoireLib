using System;
using System.Collections.Generic;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// Names every pass of one frame that changes the scene depth. Each bind that had the scene depth bound is read back
// when it finishes and diffed against the previous read. Every read is a full copy and a synchronous map: the frame stalls.
internal sealed unsafe class DepthWriteCensus : IDisposable
{
    private readonly record struct Entry(int Bind, uint Targets, DXGI_FORMAT Format, uint Width, uint Height, int Draws, int Changed, int Nearer, bool Snapshot);

    private readonly List<Entry> entries = new();
    private ComPtr<ID3D11Texture2D> staging;
    private float[]? previous;
    private float[]? current;
    private uint texWidth;
    private uint texHeight;
    private DXGI_FORMAT texFormat;
    private int snapshotBind = -1;

    public bool Active { get; private set; }

    public void Begin()
    {
        entries.Clear();
        previous = null;
        current = null;
        snapshotBind = -1;
        Active = true;
    }

    public void MarkSnapshot(int bind) => snapshotBind = bind;

    public void OnBindFinished(RenderDevice device, nint depthTexture, int bind, uint targets, DXGI_FORMAT format, uint width, uint height, int draws)
    {
        if (!Active || depthTexture == 0)
            return;

        if (!Read(device, depthTexture))
            return;

        var changed = 0;
        var nearer = 0;
        var count = current!.Length;
        for (var i = 0; i < count; i++)
        {
            var before = previous != null ? previous[i] : 0f;
            var after = current[i];
            if (after == before)
                continue;

            changed++;
            if (after > before) // reversed-Z: a larger value is nearer
                nearer++;
        }

        entries.Add(new Entry(bind, targets, format, width, height, draws, changed, nearer, bind == snapshotBind));
        (previous, current) = (current, previous);
    }

    private bool Read(RenderDevice device, nint depthTexture)
    {
        if (!ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)depthTexture, out var source))
            return false;

        try
        {
            D3D11_TEXTURE2D_DESC desc;
            source.Get()->GetDesc(&desc);

            if (staging.Get() == null || desc.Width != texWidth || desc.Height != texHeight || desc.Format != texFormat)
            {
                staging.Dispose();
                staging = default;
                var stagingDesc = desc;
                stagingDesc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
                stagingDesc.BindFlags = 0;
                stagingDesc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
                stagingDesc.MiscFlags = 0;
                if (device.Device->CreateTexture2D(&stagingDesc, null, staging.GetAddressOf()) < 0 || staging.Get() == null)
                    return false;

                texWidth = desc.Width;
                texHeight = desc.Height;
                texFormat = desc.Format;
                previous = null;
            }

            var length = (int)(desc.Width * desc.Height);
            if (current == null || current.Length != length)
                current = new float[length];

            var ctx = device.Context;
            ctx->CopyResource((ID3D11Resource*)staging.Get(), (ID3D11Resource*)source.Get());

            D3D11_MAPPED_SUBRESOURCE mapped;
            if (ctx->Map((ID3D11Resource*)staging.Get(), 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0)
                return false;

            try
            {
                var i = 0;
                for (var y = 0; y < (int)desc.Height; y++)
                {
                    for (var x = 0; x < (int)desc.Width; x++)
                        current[i++] = DepthReadback.ReadDepthTexel(in mapped, desc.Format, x, y) ?? 0f;
                }
            }
            finally
            {
                ctx->Unmap((ID3D11Resource*)staging.Get(), 0);
            }

            return true;
        }
        finally
        {
            source.Dispose();
        }
    }

    public string Finish()
    {
        Active = false;

        var sb = new StringBuilder();
        sb.AppendLine($"Draw3D depth-write census, one frame ({texWidth}x{texHeight} {texFormat}). Binds that left the scene depth changed:");
        sb.AppendLine("  bind | #rtv | rtv0 format                  |  size     | draws | changed px | nearer px");
        var writers = 0;
        foreach (var e in entries)
        {
            if (e.Changed == 0 && !e.Snapshot)
                continue;

            writers++;
            sb.AppendLine($"  {e.Bind,4} |  {e.Targets,2}  | {e.Format,-28} | {e.Width,4}x{e.Height,-4} | {e.Draws,5} | {e.Changed,10} | {e.Nearer,9}{(e.Snapshot ? "  <- opaque snapshot taken here" : string.Empty)}");
        }

        if (writers == 0)
            sb.AppendLine("  (none: no bind with the scene depth attached changed it)");

        sb.AppendLine($"  {entries.Count} bind(s) with the scene depth attached were read.");

        entries.Clear();
        previous = null;
        current = null;
        staging.Dispose();
        staging = default;
        texWidth = 0;
        texHeight = 0;
        return sb.ToString();
    }

    public void Dispose()
    {
        Active = false;
        entries.Clear();
        previous = null;
        current = null;
        staging.Dispose();
        staging = default;
    }
}
