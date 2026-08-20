using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D;

/// <summary>
/// Performance settings for the world pass, reached via <see cref="NoireDraw3D.Performance"/>: level-of-detail for
/// imported models and optional distance and screen-size culling. Nothing here changes what is drawn until it is
/// configured: LOD needs <c>generateLods: true</c> at import, and both culls default off.
/// </summary>
public sealed class Draw3DPerformance
{
    private float[] lodScreenRadii = { 160f, 60f, 22f };

    internal Draw3DPerformance() { }

    /// <summary>
    /// Whether meshes carrying a LOD chain draw a coarser level as they shrink on screen. Only affects meshes with
    /// <see cref="Geometry.Mesh.LodCount"/> &gt; 0, which requires importing with <c>generateLods: true</c>;
    /// primitives, small meshes and decals are never touched.
    /// </summary>
    public bool Lod { get; set; } = true;

    /// <summary>
    /// Multiplier on the <see cref="LodScreenRadii"/> switch points, above 1 to coarsen sooner and below 1 to keep
    /// detail longer. Clamped to a minimum of 0.01.
    /// </summary>
    public float LodBias { get; set; } = 1f;

    /// <summary>
    /// The projected on-screen radii in pixels, highest first, below which each successive LOD level takes over,
    /// clamped to the mesh's available levels. Assignment sorts descending, and an empty or null list disables the
    /// size-based switch.
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
    /// The world-unit distance from the camera beyond which a retained object's bounds center is not drawn, or zero
    /// for unlimited. The immediate layer is never distance-culled.
    /// </summary>
    public float MaxDrawDistance { get; set; }

    /// <summary>
    /// The projected on-screen radius in pixels below which a retained object is not drawn, or zero for off.
    /// Outlined and selected objects are exempt, and the cull only applies while the game camera is active.
    /// </summary>
    public float MinScreenPixels { get; set; }

    /// <summary>
    /// Renders the 3D layer at this multiple of the display resolution and box-downsamples at composite, clamped to
    /// 1..2. Applies to the main game view only, and falls back to 1 when the larger target cannot be allocated.
    /// </summary>
    public float Supersample { get; set; } = 1f;

    /// <summary>The clamped supersample factor, read once per frame when sizing the scene target.</summary>
    internal float SupersampleFactor => Math.Clamp(Supersample, 1f, 2f);

    /// <summary>
    /// Routes every standard single draw through the instanced pipeline, so world and tint travel in the
    /// per-instance vertex stream and the object constant buffer is re-uploaded only when material parameters
    /// change. Decals and custom pipelines always keep the per-draw path.
    /// </summary>
    public bool BatchedObjectConstants { get; set; } = true;

    /// <summary>An immutable copy of the settings for one frame's collection pass.</summary>
    /// <param name="Lod">Whether LOD selection runs.</param>
    /// <param name="LodBias">The clamped <see cref="LodBias"/>.</param>
    /// <param name="MaxDrawDistance">The clamped <see cref="MaxDrawDistance"/>.</param>
    /// <param name="MinScreenPixels">The clamped <see cref="MinScreenPixels"/>.</param>
    /// <param name="LodScreenRadii">The descending LOD switch radii.</param>
    /// <param name="BatchedObjectConstants">Whether single draws go through the instanced pipeline.</param>
    internal readonly record struct Snapshot(bool Lod, float LodBias, float MaxDrawDistance, float MinScreenPixels, float[] LodScreenRadii, bool BatchedObjectConstants);

    /// <summary>Takes a frame snapshot, read once on the render thread so a mid-frame change never tears a pass.</summary>
    /// <returns>The snapshot.</returns>
    internal Snapshot Take() => new(Lod, MathF.Max(0.01f, LodBias), MathF.Max(0f, MaxDrawDistance), MathF.Max(0f, MinScreenPixels), lodScreenRadii, BatchedObjectConstants);

    /// <summary>Selects the LOD level to draw at from an object's projected on-screen radius.</summary>
    /// <param name="radiusPixels">The object's projected screen radius in pixels.</param>
    /// <param name="lodCount">The mesh's available coarser-level count.</param>
    /// <param name="s">The frame's settings snapshot.</param>
    /// <returns>The level, 0 for full detail and also when LOD is off or the mesh has no chain.</returns>
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
                break; // the radii descend, so clearing one boundary clears every smaller one
        }

        return Math.Min(level, lodCount);
    }
}
