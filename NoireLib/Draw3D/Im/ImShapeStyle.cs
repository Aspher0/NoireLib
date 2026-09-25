using System.Collections.Generic;

namespace NoireLib.Draw3D.Im;

/// <summary>How an immediate-mode shape is placed in the world.</summary>
public enum ImShapePlacement
{
    /// <summary>Projected onto the terrain as a ground decal (the default).</summary>
    Grounded = 0,

    /// <summary>A flat mesh at the given position's height (does not follow terrain).</summary>
    Flat = 1,
}

/// <summary>Optional styling for <see cref="ImDraw3D"/> shapes.</summary>
public readonly record struct ImShapeStyle
{
    // Non-zero defaults are applied on read. default(ImShapeStyle) matches new ImShapeStyle().
    private readonly float? outlineWidth;
    private readonly float? fillOpacity;
    private readonly float? decalHeight;
    private readonly int? segments;

    /// <summary>Ground-projected decal (the default) or flat mesh.</summary>
    public ImShapePlacement Placement { get; init; }

    /// <summary>Flat shapes only: soft-edge width against world geometry in world units (default 0, a hard edge).</summary>
    public float DepthFade { get; init; }

    /// <summary>Decal outline band width as a fraction of the footprint (default 0.08, 0 for none), proportional to the drawn radius.</summary>
    public float OutlineWidth
    {
        get => outlineWidth ?? 0.08f;
        init => outlineWidth = value;
    }

    /// <summary>Decal outline color in straight alpha, where alpha 0 (the default) uses the shape's own color.</summary>
    public System.Numerics.Vector4 OutlineColor { get; init; }

    /// <summary>Decal fill opacity relative to the outline (default 0.6).</summary>
    public float FillOpacity
    {
        get => fillOpacity ?? 0.6f;
        init => fillOpacity = value;
    }

    /// <summary>Whether to blend additively.</summary>
    public bool Additive { get; init; }

    /// <summary>Flat shapes only: whether to ignore world geometry entirely.</summary>
    public bool IgnoreDepth { get; init; }

    /// <summary>Whether water and other surfaces drawn after the game's opaque pass hide this shape, null following <see cref="NoireDraw3D.TranslucentOcclusion"/>.</summary>
    public Enums.TranslucentOcclusion? TranslucentOcclusion { get; init; }

    /// <summary>Flat shapes only: draws over other Draw3D objects while occluded by the game world. Ignored with <see cref="IgnoreDepth"/>.</summary>
    public bool OnTopOfObjects { get; init; }

    /// <summary>Draw layer ordering decals, higher drawing later.</summary>
    public int Layer { get; init; }

    /// <summary>Decal volume height in world units, spanning above and below the anchor (default 4).</summary>
    public float DecalHeight
    {
        get => decalHeight ?? 4f;
        init => decalHeight = value;
    }

    /// <summary>Grounded decals only: world cylinders the decal skips, up to 64. <see cref="NoireDraw3D.GetActorExclusions"/> builds them.</summary>
    public IReadOnlyList<ExcludeVolume>? ExcludeVolumes { get; init; }

    /// <summary>Segment count for flat curved shapes (default 64).</summary>
    public int Segments
    {
        get => segments ?? 64;
        init => segments = value;
    }
}
