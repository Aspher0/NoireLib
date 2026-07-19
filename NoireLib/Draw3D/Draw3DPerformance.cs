using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D;

/// <summary>
/// Performance knobs for the world pass, reached via <see cref="NoireDraw3D.Performance"/>: level-of-detail for imported
/// models, and optional distance / screen-size culling for dense scenes.<br/>
/// Everything here is <b>opt-in and off the default path</b> - a full-detail model renders cheaply, so nothing is
/// culled or swapped unless you ask. Turn LOD on per-load (<c>generateLods: true</c> on <see cref="Scene.Draw3DModels"/>
/// / <see cref="Assets.GltfLoader"/>) for scenes with many heavy models at once; the two culls default off so nothing
/// disappears unexpectedly. <b>Open all the way down:</b> every threshold the selection branches on is a public property.
/// </summary>
public sealed class Draw3DPerformance
{
    private float[] lodScreenRadii = { 160f, 60f, 22f };

    internal Draw3DPerformance() { }

    /// <summary>
    /// Whether meshes that carry a LOD chain draw a coarser level as they shrink on screen. Default <b>true</b>, but
    /// <b>a LOD chain is only built when a model is imported with <c>generateLods: true</c></b> - so with the default
    /// import (no chain) this does nothing and every model draws at full resolution. Turn it off to force full detail
    /// even on models that do carry a chain. Only affects meshes with <see cref="Geometry.Mesh.LodCount"/> &gt; 0;
    /// primitives, small meshes, and decals are never touched.
    /// </summary>
    public bool Lod { get; set; } = true;

    /// <summary>
    /// Global multiplier on the LOD switch distances (<see cref="LodScreenRadii"/>). 1 = as configured; &gt;1 drops to a
    /// coarser LOD sooner (more aggressive, faster, softer); &lt;1 keeps detail longer. Default 1. Clamped to >= 0.01.
    /// </summary>
    public float LodBias { get; set; } = 1f;

    /// <summary>
    /// The projected on-screen radii (in pixels), highest first, at which each successive LOD level takes over: an
    /// object whose radius is above the first value draws at full detail, below the first uses LOD 1, below the second
    /// LOD 2, and so on (clamped to the mesh's available levels). The default <c>[160, 60, 22]</c> keeps full detail on
    /// anything sizeable and only coarsens distant/small objects. Values are sorted descending on assignment; an empty
    /// or null list disables the size-based switch (everything draws at full detail).
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
            Array.Sort(copy, static (a, b) => b.CompareTo(a)); // descending: LOD boundaries from largest to smallest
            lodScreenRadii = copy;
        }
    }

    /// <summary>
    /// Objects whose bounds center is farther than this from the camera (world units) are not drawn. <b>0 (default) =
    /// unlimited</b> - no distance culling. Set it to skip far scenery entirely in large scenes. Applies to every
    /// retained object (meshes and decals); the immediate layer is never distance-culled.
    /// </summary>
    public float MaxDrawDistance { get; set; }

    /// <summary>
    /// Objects whose projected on-screen radius is below this many pixels are not drawn (they cover a pixel or two at
    /// most). <b>0 (default) = off.</b> A small value such as 1-2 is a near-free win in scenes with many tiny/distant
    /// objects; outlined/selected objects are exempt so a highlighted item never vanishes. Applies to retained objects
    /// only, and only when the game camera is active (never in a render-to-texture view).
    /// </summary>
    public float MinScreenPixels { get; set; }

    /// <summary>
    /// Supersampling anti-aliasing for the 3D layer: it renders the scene at this multiple of the display resolution and
    /// box-downsamples at composite, removing the jagged/shimmering edges a dense mesh shows at a distance (the layer has
    /// no MSAA of its own, unlike the game world - so a full-detail model aliases where the world does not). <b>Default 1
    /// = off.</b> <b>2</b> is the sweet spot (exactly 2 gives a perfect 2x2 box filter) but costs 4x the layer's fill and
    /// VRAM - a real cost at 4K, which is why it is opt-in. Clamped to 1..2. Affects only the main game view, never a
    /// render-to-texture pass; fail-soft (renders at 1x for any frame the larger target cannot be allocated).
    /// <br/>
    /// The lighter-weight alternative is model LOD (<see cref="Lod"/> + <c>generateLods</c>), which also removes the
    /// aliasing by thinning distant geometry - trading detail instead of fill.
    /// </summary>
    public float Supersample { get; set; } = 1f;

    /// <summary>The clamped supersample factor (1..2), read once per frame when sizing the scene target.</summary>
    internal float SupersampleFactor => Math.Clamp(Supersample, 1f, 2f);

    /// <summary>An immutable, thread-safe copy of the settings for one frame's collection pass.</summary>
    internal readonly record struct Snapshot(bool Lod, float LodBias, float MaxDrawDistance, float MinScreenPixels, float[] LodScreenRadii);

    /// <summary>Takes a frame snapshot (read once on the render thread, so a mid-frame settings change never tears a pass).</summary>
    internal Snapshot Take() => new(Lod, MathF.Max(0.01f, LodBias), MathF.Max(0f, MaxDrawDistance), MathF.Max(0f, MinScreenPixels), lodScreenRadii);

    /// <summary>
    /// The LOD level to draw at, from an object's projected on-screen radius: 0 (full detail) down to the coarsest level
    /// the mesh provides. Returns 0 when LOD is off, the mesh has no chain, or the size-based switch is disabled.
    /// </summary>
    /// <param name="radiusPixels">The object's projected screen radius in pixels.</param>
    /// <param name="lodCount">The mesh's available coarser-level count.</param>
    /// <param name="s">The frame's settings snapshot.</param>
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
                break; // radii are descending: once we clear a boundary, the smaller ones are cleared too
        }

        return Math.Min(level, lodCount);
    }
}
