using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

/// <summary>
/// Vector shapes rendered by <see cref="NoireRaster"/> into a texture, rendered again off the draw thread when the
/// scale changes. Safe to read every frame.
/// </summary>
public sealed class NoireRasterTexture : IDisposable
{
    // How many frames a replaced texture is kept alive, since the frame that fetched it may still be drawing it.
    private const int RetireFrames = 3;

    private readonly object gate = new();
    private readonly IReadOnlyList<RasterShape> shapes;
    private readonly string debugName;

    private IDalamudTextureWrap? current;
    private IDalamudTextureWrap? retired;
    private int retireCountdown;
    private float builtScale;
    private float pendingScale;
    private int building;
    private bool disposed;

    /// <summary>Creates the texture. Nothing renders until the first <see cref="Get"/>.</summary>
    /// <param name="debugName">The name the texture carries in Dalamud's texture list.</param>
    /// <param name="shapes">The shapes, back to front.</param>
    /// <param name="viewSize">The size of the shapes' own coordinate space, fitted and centred into <paramref name="size"/>.</param>
    /// <param name="size">The size the texture is drawn at before scaling, in logical pixels.</param>
    public NoireRasterTexture(string debugName, IReadOnlyList<RasterShape> shapes, Vector2 viewSize, Vector2 size)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        this.debugName = debugName;
        this.shapes = shapes;
        ViewSize = viewSize;
        Size = size;
    }

    /// <summary>The size of the shapes' own coordinate space.</summary>
    public Vector2 ViewSize { get; }

    /// <summary>The size the texture is drawn at before scaling, in logical pixels.</summary>
    public Vector2 Size { get; }

    /// <summary>Where a point of the shapes' own space lands in the drawn texture, before scaling.</summary>
    /// <param name="point">The point, in the shapes' own units.</param>
    /// <returns>The position, in logical pixels from the texture's top left corner.</returns>
    public Vector2 ToLocal(Vector2 point)
    {
        var fit = Fit;
        return ((Size - (ViewSize * fit)) * 0.5f) + (point * fit);
    }

    private float Fit => MathF.Min(Size.X / ViewSize.X, Size.Y / ViewSize.Y);

    /// <summary>
    /// The texture rendered for a scale. While a new scale renders, the previous texture is returned, and null before the
    /// first render completes.
    /// </summary>
    /// <param name="scale">The scale the texture is drawn at, usually <see cref="NoireUI.Scale"/>.</param>
    /// <returns>A texture <see cref="Size"/> times <paramref name="scale"/> pixels large, or null.</returns>
    public IDalamudTextureWrap? Get(float scale)
    {
        lock (gate)
        {
            if (retired != null && --retireCountdown <= 0)
            {
                retired.Dispose();
                retired = null;
            }

            if (!disposed && MathF.Abs(scale - builtScale) > 0.001f && MathF.Abs(scale - pendingScale) > 0.001f
                && Interlocked.CompareExchange(ref building, 1, 0) == 0)
            {
                pendingScale = scale;
                StartBuild(scale);
            }

            return current;
        }
    }

    // Apart from Get: Get runs every frame and must hold no closure.
    private void StartBuild(float scale) => Task.Run(() => Build(scale));

    private void Build(float scale)
    {
        try
        {
            var width = Math.Max(1, (int)MathF.Ceiling(Size.X * scale));
            var height = Math.Max(1, (int)MathF.Ceiling(Size.Y * scale));
            var fit = Fit * scale;
            var offset = new Vector2((width - (ViewSize.X * fit)) * 0.5f, (height - (ViewSize.Y * fit)) * 0.5f);
            var pixels = NoireRaster.Render(shapes, width, height, fit, offset);
            var created = NoireService.TextureProvider.CreateFromRaw(RawImageSpecification.Rgba32(width, height), pixels, debugName);

            lock (gate)
            {
                if (disposed)
                {
                    created.Dispose();
                    return;
                }

                if (current != null)
                {
                    retired?.Dispose();
                    retired = current;
                    retireCountdown = RetireFrames;
                }

                current = created;
                builtScale = scale;
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogDebug($"Rendering {debugName} failed: {ex.Message}", "[NoireRasterTexture] ");

            // Held as built so a failing scale is not retried every frame.
            lock (gate)
                builtScale = scale;
        }
        finally
        {
            Interlocked.Exchange(ref building, 0);
        }
    }

    /// <summary>Releases the textures. A render still running discards its result.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            current?.Dispose();
            retired?.Dispose();
            current = null;
            retired = null;
        }
    }
}
