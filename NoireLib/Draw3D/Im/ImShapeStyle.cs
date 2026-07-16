using System.Collections.Generic;

namespace NoireLib.Draw3D.Im;

/// <summary>How an immediate-mode shape is placed in the world.</summary>
public enum ImShapePlacement
{
    /// <summary>Projected onto the terrain as a ground decal (hugs stairs and slopes). The default for markers.</summary>
    Grounded = 0,

    /// <summary>A flat mesh at the given position's height (does not follow terrain).</summary>
    Flat = 1,
}

/// <summary>
/// Optional styling for <see cref="ImDraw3D"/> shapes. All fields have marker-friendly defaults.
/// </summary>
public readonly record struct ImShapeStyle
{
    // Every default that is not the zero value reads through a nullable backing field rather than a property
    // initializer. An initializer only runs for `new ImShapeStyle()`: `default(ImShapeStyle)` - which is what an
    // unassigned field, an explicit `default`, and `style ?? default` all produce - would zero the lot instead, leaving a
    // decal with no fill and no outline (it paints nothing at all) and a curve with no segments. Defaulting on read makes
    // `default` and `new()` behave identically, so a caller cannot land on the zeroed set by accident.
    private readonly float? outlineWidth;
    private readonly float? fillOpacity;
    private readonly float? decalHeight;
    private readonly int? segments;

    /// <summary>Ground-projected decal (the default) or flat mesh.</summary>
    public ImShapePlacement Placement { get; init; }

    /// <summary>Soft-edge width against world geometry, in world units (flat shapes only; decals hug the ground instead). Default 0 = hard edge.</summary>
    public float DepthFade { get; init; }

    /// <summary>Decal outline band width in SDF units (0..1 of the footprint). 0 = no outline. Default 0.08.</summary>
    public float OutlineWidth
    {
        get => outlineWidth ?? 0.08f;
        init => outlineWidth = value;
    }

    /// <summary>Decal fill opacity relative to the outline (the classic strong-rim decal look uses ~0.6, the default).</summary>
    public float FillOpacity
    {
        get => fillOpacity ?? 0.6f;
        init => fillOpacity = value;
    }

    /// <summary>Additive (glow-like, order-independent) instead of standard translucent blending.</summary>
    public bool Additive { get; init; }

    /// <summary>Flat shapes only: ignore world geometry entirely (x-ray).</summary>
    public bool IgnoreDepth { get; init; }

    /// <summary>
    /// Flat shapes only: draw on top of other Draw3D objects while staying occluded by the game world (walls / terrain).
    /// The editor-gizmo mix - visible over the objects it edits, still hidden behind a real wall. Ignored when
    /// <see cref="IgnoreDepth"/> is set (full x-ray wins).
    /// </summary>
    public bool OnTopOfObjects { get; init; }

    /// <summary>Draw layer (orders decals; higher draws later).</summary>
    public int Layer { get; init; }

    /// <summary>Decal volume height in world units - how far above/below the anchor the projection reaches. Default 4.</summary>
    public float DecalHeight
    {
        get => decalHeight ?? 4f;
        init => decalHeight = value;
    }

    /// <summary>
    /// Grounded decals only: world-space cylinders (one per actor) the decal will <b>not</b> paint on - so a
    /// character / monster / NPC standing in the decal is excluded from it, while the ground around their feet
    /// still gets the decal (no hole). Object-aware and fully per-decal: pass exactly the actors this decal should
    /// avoid (build them from the object table / <see cref="NoireDraw3D.GetActorExclusions"/>, or by hand).
    /// null or empty = paint over everything. Up to 64 volumes per decal are honored. No effect on flat shapes.
    /// </summary>
    public IReadOnlyList<ExcludeVolume>? ExcludeVolumes { get; init; }

    /// <summary>Segment count for flat curved shapes. Default 64.</summary>
    public int Segments
    {
        get => segments ?? 64;
        init => segments = value;
    }
}
