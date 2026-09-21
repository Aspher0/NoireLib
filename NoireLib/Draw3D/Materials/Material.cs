using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Enums;
using System.Numerics;

namespace NoireLib.Draw3D.Materials;

/// <summary>An immutable, shareable description of how a mesh is shaded. It owns none of its textures.</summary>
public sealed record Material
{
    /// <summary>Which shader family renders this material.</summary>
    public MaterialDomain Domain { get; init; } = MaterialDomain.Unlit;

    /// <summary>How pixels blend into the Draw3D layer. <see cref="MaterialDomain.GroundDecal"/> honours only <see cref="BlendMode.Additive"/> or <see cref="BlendMode.Premultiplied"/>.</summary>
    public BlendMode Blend { get; init; } = BlendMode.Premultiplied;

    /// <summary>Whether pixels are occluded by the game's world geometry, ignored by <see cref="MaterialDomain.GroundDecal"/>.</summary>
    public DepthMode Depth { get; init; } = DepthMode.TestOnly;

    /// <summary>What this material does on frames where the game's depth buffer cannot be read.</summary>
    public DepthUnavailableBehavior WhenDepthUnavailable { get; init; } = DepthUnavailableBehavior.Ignore;

    /// <summary>Which triangle faces are rasterized.</summary>
    public CullMode Cull { get; init; } = CullMode.Back;

    /// <summary>Base color multiplier in straight alpha.</summary>
    public Vector4 Color { get; init; } = new(1f, 1f, 1f, 1f);

    /// <summary>Optional texture sampled by textured shader variants (<c>BaseTex</c>). Referenced, never owned.</summary>
    public GpuTexture? Texture { get; init; }

    /// <summary>Optional second texture (<c>AuxTex0</c>) for custom pipelines and game-material normal maps. Referenced, never owned.</summary>
    public GpuTexture? AuxTexture0 { get; init; }

    /// <summary>Optional third texture (<c>AuxTex1</c>) for custom pipelines and game-material specular maps. Referenced, never owned.</summary>
    public GpuTexture? AuxTexture1 { get; init; }

    /// <summary>Soft-edge width in world units where the material meets world geometry (0 for a hard edge). Ignored by <see cref="BlendMode.Opaque"/>.</summary>
    public float DepthFade { get; init; }

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: the projected footprint shape.</summary>
    public DecalShape Shape { get; init; } = DecalShape.Circle;

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: shape parameters per <see cref="DecalShape"/> member, with W the fill opacity relative to the outline (default 0.6).</summary>
    public Vector4 ShapeParams { get; init; } = new(0f, 0f, 0f, 0.6f);

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: outline band width against the unit footprint (0..1, 0 for none), held at a constant world thickness.</summary>
    public float OutlineWidth { get; init; }

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: the outline color in straight alpha, where alpha 0 (the default) uses <see cref="Color"/>.</summary>
    public Vector4 OutlineColor { get; init; }

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: how strongly the decal fades near the top and bottom of its volume (0 none, 1 full).</summary>
    public float HeightFade { get; init; } = 1f;

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: how stacked surfaces in the footprint resolve. Needs <see cref="NoireDraw3D.CollisionHeightMap"/>.</summary>
    public DecalProjection Projection { get; init; } = DecalProjection.AllSurfaces;

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: the surface the decal is locked to by constraining its box orientation.</summary>
    public DecalSurface Surface { get; init; } = DecalSurface.Ground;

    /// <summary>Extra values for custom pipelines, arriving as <c>Params2</c>. Unavailable to <see cref="MaterialDomain.GroundDecal"/>.</summary>
    public Vector4 SurfaceParams { get; init; }

    /// <summary>Optional name of a custom pipeline registered via <see cref="NoireDraw3D.RegisterPipeline"/>. When set, replaces the <see cref="Domain"/> shader.</summary>
    public string? CustomPipeline { get; init; }

    /// <summary>Whether translucent draws with this material may render out of back-to-front order to batch more (always on for <see cref="BlendMode.Additive"/>).</summary>
    public bool UnorderedBatching { get; init; }

    /// <summary>Creates a flat-color unlit material (premultiplied blending, world depth test).</summary>
    /// <param name="color">Base color, straight alpha.</param>
    /// <param name="depthFade">Optional soft edge against world geometry, in world units.</param>
    public static Material Unlit(Vector4 color, float depthFade = 0f)
        => new() { Domain = MaterialDomain.Unlit, Color = color, DepthFade = depthFade };

    /// <summary>Creates a textured unlit material (premultiplied blending, world depth test).</summary>
    /// <param name="texture">Texture to sample. Referenced, never owned.</param>
    /// <param name="tint">Color multiplier, straight alpha.</param>
    public static Material UnlitTextured(GpuTexture texture, Vector4? tint = null)
        => new() { Domain = MaterialDomain.Unlit, Texture = texture, Color = tint ?? new Vector4(1f, 1f, 1f, 1f) };

    /// <summary>Creates a stylized lit material (half-Lambert against <see cref="NoireDraw3D.Lighting"/>).</summary>
    /// <param name="color">Base color, straight alpha.</param>
    /// <param name="opaque">True renders in the opaque bucket (hardware z between Draw3D meshes). False renders translucent.</param>
    public static Material Lit(Vector4 color, bool opaque = true)
        => new() { Domain = MaterialDomain.Lit, Color = color, Blend = opaque ? BlendMode.Opaque : BlendMode.Premultiplied };

    /// <summary>Creates a terrain-hugging ground-decal material.</summary>
    /// <param name="shape">Footprint shape.</param>
    /// <param name="color">Base color, straight alpha.</param>
    /// <param name="shapeParams">Shape parameters per <see cref="DecalShape"/> member, or null for defaults.</param>
    /// <param name="outlineWidth">Outline band width, see <see cref="OutlineWidth"/>.</param>
    /// <param name="surface">The surface the decal is locked to.</param>
    /// <param name="projection">How stacked surfaces in the footprint resolve.</param>
    /// <param name="additive">Whether the decal blends additively.</param>
    /// <param name="outlineColor">Outline color in straight alpha, or null to use <paramref name="color"/>.</param>
    public static Material Decal(DecalShape shape, Vector4 color, Vector4? shapeParams = null, float outlineWidth = 0.08f, DecalSurface surface = DecalSurface.Ground, DecalProjection projection = DecalProjection.AllSurfaces, bool additive = false, Vector4? outlineColor = null)
        => new()
        {
            Domain = MaterialDomain.GroundDecal,
            Shape = shape,
            Color = color,
            ShapeParams = shapeParams ?? new Vector4(0f, 0f, 0f, 0.6f),
            OutlineWidth = outlineWidth,
            OutlineColor = outlineColor ?? default,
            Surface = surface,
            Projection = projection,
            Blend = additive ? BlendMode.Additive : BlendMode.Premultiplied,
            Cull = CullMode.Front,
        };

    /// <summary>Creates a material rendered by a custom pipeline, falling back to the <see cref="Domain"/> shader while it is unregistered.</summary>
    /// <param name="pipeline">The pipeline name registered via <see cref="NoireDraw3D.RegisterPipeline"/>.</param>
    /// <param name="color">Base color multiplier, straight alpha.</param>
    /// <param name="blend">How pixels blend into the layer.</param>
    /// <param name="texture">Optional texture for textured custom shaders (<c>BaseTex</c>). Referenced, never owned.</param>
    /// <param name="auxTexture0">Optional second texture (<c>AuxTex0</c>). Referenced, never owned.</param>
    /// <param name="auxTexture1">Optional third texture (<c>AuxTex1</c>). Referenced, never owned.</param>
    public static Material Custom(
        string pipeline,
        Vector4 color,
        BlendMode blend = BlendMode.Premultiplied,
        GpuTexture? texture = null,
        GpuTexture? auxTexture0 = null,
        GpuTexture? auxTexture1 = null)
        => new()
        {
            CustomPipeline = pipeline,
            Color = color,
            Blend = blend,
            Texture = texture,
            AuxTexture0 = auxTexture0,
            AuxTexture1 = auxTexture1,
        };
}
