using NoireLib.Draw3D.Enums;
using System;
using System.Numerics;

namespace NoireLib.Draw3D.Materials;

// Resolved at snapshot time. Value equality, textures by SRV pointer, is the batching key.
internal struct MaterialData : IEquatable<MaterialData>
{
    public MaterialDomain Domain;
    public BlendMode Blend;
    public DepthMode Depth;
    public DepthUnavailableBehavior WhenDepthUnavailable;
    public TranslucentOcclusion? Translucent; // null follows the renderer-wide setting
    public CullMode Cull;
    public bool Textured;
    public bool UnorderedBatching;
    public nint TexSrv;
    public nint AuxSrv0;
    public nint AuxSrv1;
    public Vector4 Params0;
    public Vector4 Params1; // x = DepthFade, y = shapeKind, z = outlineWidth, w = heightFade
    public Vector4 SurfaceParams; // custom pipelines only, arrives as Params2
    public float ProjectionMode; // ground decals: 0 = AllSurfaces, 1 = HighestOnly
    public float OutlineScaleRef; // ground decals: 0 for constant world thickness
    public Vector4 DecalOutlineColor; // ground decals: straight alpha, alpha 0 = decal colour
    public string? CustomPipeline;

    // 0 opaque, 1 ground decal, 2 transparent.
    public readonly int Bucket => Domain == MaterialDomain.GroundDecal ? 1 : Blend == BlendMode.Opaque ? 0 : 2;

    // False when a referenced texture is disposed.
    public static bool TryFrom(Material material, out MaterialData data)
    {
        data = default;

        nint srv = 0;
        if (material.Texture != null)
        {
            srv = material.Texture.SrvPointer;
            if (srv == 0)
                return false; // never bind a stale pointer
        }

        if (!TryResolveAux(material.AuxTexture0, out data.AuxSrv0) || !TryResolveAux(material.AuxTexture1, out data.AuxSrv1))
            return false;

        var domain = material.Domain;
        data.Domain = domain;
        // Decals cannot be opaque.
        data.Blend = domain == MaterialDomain.GroundDecal
            ? material.Blend == BlendMode.Additive ? BlendMode.Additive : BlendMode.Premultiplied
            : material.Blend;
        data.Depth = material.Depth;
        data.WhenDepthUnavailable = material.WhenDepthUnavailable;
        data.Translucent = material.TranslucentOcclusion;
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

    private static bool TryResolveAux(Assets.GpuTexture? texture, out nint srv)
    {
        srv = 0;
        if (texture == null)
            return true;

        srv = texture.SrvPointer;
        return srv != 0;
    }

    public readonly bool Equals(MaterialData other)
        => Domain == other.Domain
        && Blend == other.Blend
        && Depth == other.Depth
        && WhenDepthUnavailable == other.WhenDepthUnavailable
        && Translucent == other.Translucent
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

    public readonly override bool Equals(object? obj) => obj is MaterialData other && Equals(other);

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
