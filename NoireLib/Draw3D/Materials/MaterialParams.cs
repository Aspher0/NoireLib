using NoireLib.Draw3D.Enums;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Materials;

/// <summary>
/// The flattened, GPU-facing form of a <see cref="Material"/> resolved once at snapshot time.<br/>
/// Value equality (texture by SRV pointer) is the batching key, which lets the immediate layer produce
/// batchable draws without allocating Material records per call (Law 9).
/// </summary>
internal struct MaterialData : IEquatable<MaterialData>
{
    public MaterialDomain Domain;
    public BlendMode Blend;
    public DepthMode Depth;
    public DepthUnavailableBehavior WhenDepthUnavailable;
    public CullMode Cull;
    public bool Textured;
    public bool UnorderedBatching;
    public nint TexSrv;
    public Vector4 Params0;
    public Vector4 Params1; // x = DepthFade, y = shapeKind, z = outlineWidth, w = heightFade
    public float ProjectionMode; // ground decals: (float)DecalProjection (0 = AllSurfaces, 1 = HighestOnly)
    public string? CustomPipeline;

    /// <summary>The bucket this material renders in: 0 opaque, 1 ground decal, 2 transparent.</summary>
    public readonly int Bucket => Domain == MaterialDomain.GroundDecal ? 1 : Blend == BlendMode.Opaque ? 0 : 2;

    /// <summary>
    /// Resolves a material record. Returns false when the draw must be skipped (texture disposed).
    /// </summary>
    public static bool TryFrom(Material material, out MaterialData data)
    {
        data = default;

        nint srv = 0;
        if (material.Texture != null)
        {
            srv = material.Texture.SrvPointer;
            if (srv == 0)
                return false; // disposed texture - skip and count, never bind a stale pointer
        }

        var domain = material.Domain;
        data.Domain = domain;
        data.Blend = domain == MaterialDomain.GroundDecal ? BlendMode.Premultiplied : material.Blend;
        data.Depth = material.Depth;
        data.WhenDepthUnavailable = material.WhenDepthUnavailable;
        data.Cull = domain == MaterialDomain.GroundDecal ? CullMode.Front : material.Cull;
        data.TexSrv = srv;
        data.UnorderedBatching = material.UnorderedBatching || material.Blend == BlendMode.Additive;
        data.Textured = domain == MaterialDomain.GroundDecal
            ? material.Shape == DecalShape.Texture && srv != 0
            : srv != 0;
        data.Params0 = material.ShapeParams;
        data.Params1 = new Vector4(material.DepthFade, (float)material.Shape, material.OutlineWidth, material.HeightFade);
        data.ProjectionMode = (float)material.Projection;
        data.CustomPipeline = material.CustomPipeline;
        return true;
    }

    /// <inheritdoc/>
    public readonly bool Equals(MaterialData other)
        => Domain == other.Domain
        && Blend == other.Blend
        && Depth == other.Depth
        && WhenDepthUnavailable == other.WhenDepthUnavailable
        && Cull == other.Cull
        && Textured == other.Textured
        && UnorderedBatching == other.UnorderedBatching
        && TexSrv == other.TexSrv
        && Params0 == other.Params0
        && Params1 == other.Params1
        && ProjectionMode == other.ProjectionMode
        && string.Equals(CustomPipeline, other.CustomPipeline, StringComparison.Ordinal);

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is MaterialData other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode()
        => HashCode.Combine((int)Domain | ((int)Blend << 4) | ((int)Depth << 8) | ((int)Cull << 12), TexSrv, Params0, Params1, CustomPipeline);
}
