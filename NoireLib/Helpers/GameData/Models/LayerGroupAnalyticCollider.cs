using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>The primitive an analytic collider is, the client's <c>FileLayerGroupAnalyticCollider.Type</c>.</summary>
public enum AnalyticColliderType
{
    /// <summary>No collider.</summary>
    None = 0,

    /// <summary>A box, spanning minus one to plus one on each axis before its transform.</summary>
    Box = 1,

    /// <summary>A sphere of radius one before its transform.</summary>
    Sphere = 2,

    /// <summary>A cylinder the client builds as a sixteen-sided mesh, radius one and height two before its transform.</summary>
    Cylinder = 3,

    /// <summary>A plane.</summary>
    Plane = 4,
}

/// <summary>
/// The collider a <see cref="LayerEntryType.BG"/> placement collides with when its collision is analytic: a primitive
/// with its own transform, composed with the placement's. The client reads it from the layer group file
/// (<c>FileLayerGroupInstanceBgPart.OffsetColliderAnalyticData</c>) and builds it in
/// <c>BgPartsLayoutInstance.CreateSecondary</c> (0x14073E740).
/// </summary>
public sealed record LayerGroupAnalyticCollider
{
    /// <summary>The primitive.</summary>
    public required AnalyticColliderType Type { get; init; }

    /// <summary>The collision material the primitive writes, before the placement's own replaces it.</summary>
    public required uint MaterialId { get; init; }

    /// <summary>Which material bits <see cref="MaterialId"/> sets.</summary>
    public required uint MaterialMask { get; init; }

    /// <summary>The primitive's transform in the placement's local space: scale, then rotation, then translation.</summary>
    public required Matrix4x4 Local { get; init; }

    /// <summary>The bounds the file stores for the primitive, in the placement's local space.</summary>
    public required Vector3 BoundsMin { get; init; }

    /// <summary>The upper corner of <see cref="BoundsMin"/>.</summary>
    public required Vector3 BoundsMax { get; init; }
}
