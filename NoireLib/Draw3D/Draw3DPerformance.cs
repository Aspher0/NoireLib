using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D;

/// <summary>
/// The world pass's performance settings, reached via <see cref="NoireDraw3D.Performance"/>: model level-of-detail
/// (which needs <c>generateLods: true</c> at import), distance and screen-size culling (both off by default), and
/// supersampling.
/// </summary>
public sealed class Draw3DPerformance
{
    private float[] lodScreenRadii = { 160f, 60f, 22f };

    internal Draw3DPerformance() { }

    /// <summary>
    /// Gets or sets whether meshes with a <see cref="Geometry.Mesh.LodCount"/> above 0 draw a coarser level as they
    /// shrink on screen, primitives and decals never being touched.
    /// </summary>
    public bool Lod { get; set; } = true;

    /// <summary>
    /// Gets or sets the multiplier on the <see cref="LodScreenRadii"/> switch points, above 1 to coarsen sooner and
    /// below 1 to keep detail longer, clamped to a minimum of 0.01.
    /// </summary>
    public float LodBias { get; set; } = 1f;

    /// <summary>
    /// Gets or sets the projected on-screen radii in pixels, sorted descending on assignment, below which each
    /// successive LOD level takes over, an empty or null list disabling the size-based switch.
    /// </summary>
    public IReadOnlyList<float> LodScreenRadii
    {
        get => lodScreenRadii;
        set
        {
            if (value == null || value.Count == 0)
            {
                lodScreenRadii = Array.Empty<float>();
                return;
            }

            var copy = new float[value.Count];
            for (var i = 0; i < value.Count; i++)
                copy[i] = MathF.Max(0f, value[i]);
            Array.Sort(copy, static (a, b) => b.CompareTo(a)); // LOD boundaries run largest to smallest
            lodScreenRadii = copy;
        }
    }

    /// <summary>
    /// Gets or sets the world-unit camera distance beyond which a retained object's bounds center is not drawn, zero
    /// meaning unlimited, the immediate layer never being distance-culled.
    /// </summary>
    public float MaxDrawDistance { get; set; }

    /// <summary>
    /// Gets or sets the projected on-screen radius in pixels below which a retained object is not drawn, zero meaning
    /// off, applied only under the game camera and never to outlined or selected objects.
    /// </summary>
    public float MinScreenPixels { get; set; }

    /// <summary>
    /// Gets or sets the multiple of the display resolution the main view's layer renders at before a box downsample
    /// at composite, clamped to 1..2 and falling back to 1 when the larger target cannot be allocated.
    /// </summary>
    public float Supersample { get; set; } = 1f;

    internal float SupersampleFactor => Math.Clamp(Supersample, 1f, 2f);

    /// <summary>
    /// Gets or sets whether standard single draws take the instanced pipeline, re-uploading the object constant buffer
    /// only when material parameters change, decals and custom pipelines always keeping the per-draw path.
    /// </summary>
    public bool BatchedObjectConstants { get; set; } = true;

    // Taken once per frame. A mid-frame change never tears a pass.
    internal readonly record struct Snapshot(bool Lod, float LodBias, float MaxDrawDistance, float MinScreenPixels, float[] LodScreenRadii, bool BatchedObjectConstants);

    internal Snapshot Take() => new(Lod, MathF.Max(0.01f, LodBias), MathF.Max(0f, MaxDrawDistance), MathF.Max(0f, MinScreenPixels), lodScreenRadii, BatchedObjectConstants);

    internal static int SelectLevel(float radiusPixels, int lodCount, in Snapshot s)
    {
        if (!s.Lod || lodCount <= 0)
            return 0;

        var radii = s.LodScreenRadii;
        var level = 0;
        for (var i = 0; i < radii.Length; i++)
        {
            if (radiusPixels < radii[i] * s.LodBias)
                level = i + 1;
            else
                break; // the radii descend
        }

        return Math.Min(level, lodCount);
    }
}
