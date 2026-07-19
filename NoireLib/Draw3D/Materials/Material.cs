using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Enums;
using System.Numerics;

namespace NoireLib.Draw3D.Materials;

/// <summary>
/// An immutable description of how a mesh is shaded. Share one material across as many renderers as you like;
/// derive variations with <c>with</c>-mutation.<br/>
/// A material owns nothing - its <see cref="Texture"/> is a reference whose lifetime belongs to whoever created it.
/// </summary>
public sealed record Material
{
    /// <summary>Which shader family renders this material.</summary>
    public MaterialDomain Domain { get; init; } = MaterialDomain.Unlit;

    /// <summary>
    /// How pixels blend into the Draw3D layer. <see cref="BlendMode.Opaque"/> materials also z-test each other in hardware.<br/>
    /// <see cref="MaterialDomain.GroundDecal"/> honours only <see cref="BlendMode.Additive"/> (stacked decals sum toward white)
    /// or <see cref="BlendMode.Premultiplied"/> - a decal is never opaque - so <see cref="Decal"/>'s <c>additive</c> flag is the ergonomic switch.
    /// </summary>
    public BlendMode Blend { get; init; } = BlendMode.Premultiplied;

    /// <summary>
    /// Whether pixels are occluded by the game's world geometry.<br/>
    /// Ignored by <see cref="MaterialDomain.GroundDecal"/> - projection <i>replaces</i> testing there.
    /// </summary>
    public DepthMode Depth { get; init; } = DepthMode.TestOnly;

    /// <summary>What this material does on frames where the game's depth buffer cannot be read.</summary>
    public DepthUnavailableBehavior WhenDepthUnavailable { get; init; } = DepthUnavailableBehavior.Ignore;

    /// <summary>Which triangle faces are rasterized.</summary>
    public CullMode Cull { get; init; } = CullMode.Back;

    /// <summary>Base color multiplier (straight alpha; premultiplication happens inside the shader).</summary>
    public Vector4 Color { get; init; } = new(1f, 1f, 1f, 1f);

    /// <summary>Optional texture sampled by textured shader variants (<c>BaseTex</c>). Referenced, never owned.</summary>
    public GpuTexture? Texture { get; init; }

    /// <summary>
    /// Optional second texture, for custom pipelines that need more than a base color (<c>AuxTex0</c>).<br/>
    /// The standard shaders ignore it; what it means is the pipeline's business. Game materials bind their
    /// normal map here. Referenced, never owned.
    /// </summary>
    public GpuTexture? AuxTexture0 { get; init; }

    /// <summary>
    /// Optional third texture, for custom pipelines that need more than two (<c>AuxTex1</c>).<br/>
    /// The standard shaders ignore it. Game materials bind their specular map here. Referenced, never owned.
    /// </summary>
    public GpuTexture? AuxTexture1 { get; init; }

    /// <summary>
    /// Soft-edge width, in world units, where the material meets world geometry. 0 = hard edge.<br/>
    /// Blended domains only - <see cref="BlendMode.Opaque"/> occlusion is a hard pixel kill, so fade is ignored there.
    /// </summary>
    public float DepthFade { get; init; }

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: the projected footprint shape.</summary>
    public DecalShape Shape { get; init; } = DecalShape.Circle;

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: shape parameters (see <see cref="DecalShape"/> members). W = fill opacity relative to the outline (default 0.6).</summary>
    public Vector4 ShapeParams { get; init; } = new(0f, 0f, 0f, 0.6f);

    /// <summary>
    /// <see cref="MaterialDomain.GroundDecal"/> only: outline band width, measured against the unit footprint (SDF units,
    /// 0..1). The rim is held at a <b>constant world thickness regardless of the decal's scale</b> - scaling the box up or
    /// down never widens or narrows it - so this reads as a fixed thickness rather than a fraction of the footprint. 0 = no outline.
    /// </summary>
    public float OutlineWidth { get; init; }

    /// <summary>
    /// <see cref="MaterialDomain.GroundDecal"/> only: the outline rim's own color, straight alpha.<br/>
    /// Alpha 0 (the default) leaves the rim the decal's <see cref="Color"/>, so rim and fill differ only in opacity - the
    /// classic look. Give it an alpha above 0 to color the border independently of the fill.
    /// </summary>
    public Vector4 OutlineColor { get; init; }

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: how strongly the decal feathers out near the top/bottom of its volume (0 = none, 1 = full).</summary>
    public float HeightFade { get; init; } = 1f;

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: how to resolve stacked surfaces in the footprint (paint all, or only the topmost). Needs <see cref="NoireDraw3D.CollisionHeightMap"/>.</summary>
    public DecalProjection Projection { get; init; } = DecalProjection.AllSurfaces;

    /// <summary><see cref="MaterialDomain.GroundDecal"/> only: locks the decal to a surface by constraining the box's orientation - <see cref="DecalSurface.Ground"/> (kept horizontal, projects down onto the floor), <see cref="DecalSurface.Wall"/> (kept vertical, projects into the wall it faces), or <see cref="DecalSurface.Both"/> (free - orientation decides). Default <see cref="DecalSurface.Ground"/>.</summary>
    public DecalSurface Surface { get; init; } = DecalSurface.Ground;

    /// <summary>
    /// Extra per-material values for custom pipelines, arriving in the shader as <c>Params2</c>.<br/>
    /// The standard shaders ignore it, and <see cref="MaterialDomain.GroundDecal"/> cannot use it because its
    /// own shader needs that register for projection data.
    /// </summary>
    public Vector4 SurfaceParams { get; init; }

    /// <summary>Optional name of a custom pipeline registered via <see cref="NoireDraw3D.RegisterPipeline"/>. When set, it replaces the <see cref="Domain"/> shader.</summary>
    public string? CustomPipeline { get; init; }

    /// <summary>
    /// Opt-in: translucent draws with this material may render in any order relative to each other,
    /// letting the renderer instance large groups aggressively instead of preserving strict back-to-front
    /// order. Great for hundreds of identical markers; leave off when shapes of this material visibly
    /// overlap each other. <see cref="BlendMode.Additive"/> is order-independent and always batches this way.
    /// </summary>
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
    /// <param name="opaque">True renders in the opaque bucket (hardware z between Draw3D meshes); false renders translucent.</param>
    public static Material Lit(Vector4 color, bool opaque = true)
        => new() { Domain = MaterialDomain.Lit, Color = color, Blend = opaque ? BlendMode.Opaque : BlendMode.Premultiplied };

    /// <summary>Creates a terrain-hugging ground-decal material (paints its shape onto the world surface).</summary>
    /// <param name="shape">Footprint shape.</param>
    /// <param name="color">Base color, straight alpha.</param>
    /// <param name="shapeParams">Shape parameters (see <see cref="DecalShape"/> members); when null, sensible defaults are used.</param>
    /// <param name="outlineWidth">Outline band width (default 0.08 - the classic strong-rim look). Held at a constant world thickness regardless of the decal's scale; see <see cref="OutlineWidth"/>.</param>
    /// <param name="surface">Locks the decal to a surface by constraining the box orientation: ground (kept horizontal), wall (kept vertical), or both (free). Default <see cref="DecalSurface.Ground"/>.</param>
    /// <param name="projection">How stacked surfaces in the footprint resolve: paint all, or only the topmost. <see cref="DecalProjection.HighestOnly"/> needs <see cref="NoireDraw3D.CollisionHeightMap"/> on. Default <see cref="DecalProjection.AllSurfaces"/>.</param>
    /// <param name="additive">True blends additively - stacking coloured decals sums their light toward white (a red, green and blue decal overlapping read white). False is the standard translucent blend. Equivalent to setting <see cref="Blend"/> to <see cref="BlendMode.Additive"/>.</param>
    /// <param name="outlineColor">The border's own color, straight alpha. Null (the default) leaves the rim the decal's <paramref name="color"/>, differing only in opacity; pass one to color the border independently of the fill.</param>
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

    /// <summary>
    /// Creates a material rendered by a custom pipeline registered via <see cref="NoireDraw3D.RegisterPipeline"/>
    /// (the open shader floor), over the standard vertex layout. Falls back to the <see cref="Domain"/> shader if the
    /// named pipeline isn't registered.
    /// </summary>
    /// <param name="pipeline">The registered pipeline name (see <see cref="NoireDraw3D.RegisterPipeline"/>).</param>
    /// <param name="color">Base color multiplier, straight alpha.</param>
    /// <param name="blend">How pixels blend into the layer (default premultiplied translucent).</param>
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
