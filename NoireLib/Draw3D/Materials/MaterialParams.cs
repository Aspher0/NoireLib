using NoireLib.Draw3D.Enums;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Materials;

/// <summary>
/// The flattened, GPU-facing form of a <see cref="Material"/> resolved once at snapshot time. Value equality
/// (texture by SRV pointer) is the batching key: the immediate layer produces batchable draws without allocating
/// Material records per call, keeping the steady-state path allocation-free.
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
    public nint AuxSrv0;
    public nint AuxSrv1;
    public Vector4 Params0;
    public Vector4 Params1; // x = DepthFade, y = shapeKind, z = outlineWidth, w = heightFade
    public Vector4 SurfaceParams; // custom pipelines only: arrives as Params2 (decals need that register themselves)
    public float ProjectionMode; // ground decals: (float)DecalProjection (0 = AllSurfaces, 1 = HighestOnly)
    public float OutlineScaleRef; // ground decals: outline rim reference footprint scale; 0 = constant world thickness
                                  // (scene decals), else proportional to the immediate layer's own footprint scale
    public Vector4 DecalOutlineColor; // ground decals: rim colour, straight alpha; alpha 0 = unset (rim uses the decal colour)
    public string? CustomPipeline;

    /// <summary>The bucket this material renders in: 0 opaque, 1 ground decal, 2 transparent.</summary>
    public readonly int Bucket => Domain == MaterialDomain.GroundDecal ? 1 : Blend == BlendMode.Opaque ? 0 : 2;

    /// <summary>Resolves a material record; returns false when the draw must be skipped (texture disposed).</summary>
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

        // Same rule for auxiliary textures: a disposed one skips the draw rather than binding a stale pointer.
        if (!TryResolveAux(material.AuxTexture0, out data.AuxSrv0) || !TryResolveAux(material.AuxTexture1, out data.AuxSrv1))
            return false;

        var domain = material.Domain;
        data.Domain = domain;
        // Decals cannot be opaque; Additive passes through (stacked decals sum toward white), anything else resolves to Premultiplied.
        data.Blend = domain == MaterialDomain.GroundDecal
            ? material.Blend == BlendMode.Additive ? BlendMode.Additive : BlendMode.Premultiplied
            : material.Blend;
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
        data.SurfaceParams = material.SurfaceParams;
        data.ProjectionMode = (float)material.Projection;
        data.DecalOutlineColor = material.OutlineColor;
        data.CustomPipeline = material.CustomPipeline;
        return true;
    }

    /// <summary>Resolves an optional auxiliary texture; returns false when it exists but has been disposed.</summary>
    private static bool TryResolveAux(Assets.GpuTexture? texture, out nint srv)
    {
        srv = 0;
        if (texture == null)
            return true;

        srv = texture.SrvPointer;
        return srv != 0;
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
        && AuxSrv0 == other.AuxSrv0
        && AuxSrv1 == other.AuxSrv1
        && Params0 == other.Params0
        && Params1 == other.Params1
        && SurfaceParams == other.SurfaceParams
        && ProjectionMode == other.ProjectionMode
        && OutlineScaleRef == other.OutlineScaleRef
        && DecalOutlineColor == other.DecalOutlineColor
        && string.Equals(CustomPipeline, other.CustomPipeline, StringComparison.Ordinal);

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is MaterialData other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode()
        => HashCode.Combine(
            (int)Domain | ((int)Blend << 4) | ((int)Depth << 8) | ((int)Cull << 12),
            HashCode.Combine(TexSrv, AuxSrv0, AuxSrv1),
            Params0,
            HashCode.Combine(Params1, SurfaceParams),
            ProjectionMode,
            OutlineScaleRef,
            DecalOutlineColor,
            CustomPipeline);
}
