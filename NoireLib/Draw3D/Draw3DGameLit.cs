using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// What <see cref="NoireDraw3D.DrawGameLit(Scene.SceneNode)"/> writes into the game's G-buffer, defaulting to values
/// measured off the game's own world geometry and read once per injected draw.
/// </summary>
public sealed class Draw3DGameLit
{
    /// <summary>The largest half-float, held by rtv3 red where the geometry pass never wrote. The game's furniture writes <c>0</c>.</summary>
    public const float MiscRedSentinel = 65504f;

    /// <summary>The rtv0 alpha the game's world geometry carries, one of six discrete shading-model ids.</summary>
    public const byte WorldShadingModelId = 128;

    /// <summary>The rtv0 alpha of the game's characters, selecting the skin and hair shading path.</summary>
    public const byte CharacterShadingModelId = 32;

    /// <summary>The rtv1 scalars sampled off a real wood floor, used when a material carries no specular map.</summary>
    public static readonly Vector3 MeasuredMaterialParams = new(0.651f, 0.396f, 0f);

    /// <summary>
    /// The ceiling rtv1's channels are held below, since a red of <c>0.999</c> or more switches the lighting pass into
    /// a mode that turns the reflection green (<c>0.998</c> does not).
    /// </summary>
    public const float DefaultMaterialCeiling = 0.99f;

    /// <summary>
    /// The stencil mark the game's deferred light volumes test to light injected geometry, where <c>0x20</c> and
    /// <c>0x80</c> also work and <c>0x40</c> or any bit below <c>0x10</c> leaves the object black.
    /// </summary>
    public const uint LitStencilMark = 0x10;

    /// <summary>
    /// Gets or sets the four rtv3 channels: red and green <c>0</c> like the game's furniture, blue a scale over the
    /// model's baked per-vertex occlusion (<c>1</c> matches a normally placed object), and alpha <c>1</c> (unmeasured).
    /// </summary>
    public Vector4 Misc { get; set; } = new(0f, 0f, 1f, 1f);

    /// <summary>Gets or sets the rtv0 alpha selecting the game's shading model. Must be one of the game's own ids.</summary>
    public byte ShadingModelId { get; set; } = WorldShadingModelId;

    /// <summary>
    /// Gets or sets the rtv1 scalars written when the material has no specular map, and blended toward by
    /// <see cref="MaterialOverride"/> when it does.
    /// </summary>
    public Vector3 MaterialParams { get; set; } = MeasuredMaterialParams;

    /// <summary>
    /// Gets or sets how much <see cref="MaterialParams"/> replaces the material's specular map in rtv1, from 0 (the
    /// default, the map as drawn) to 1 (the flat scalars).
    /// </summary>
    public float MaterialOverride { get; set; }

    /// <summary>
    /// Gets or sets the highest value any rtv1 channel may take, where red is reflection strength, green moves and
    /// scales the highlight, and blue darkens the surface (<see cref="DefaultMaterialCeiling"/> by default, 1 to write
    /// a specular map untouched).
    /// </summary>
    public float MaterialCeiling { get; set; } = DefaultMaterialCeiling;

    /// <summary>
    /// Gets or sets the stencil mark written with the geometry, 0 leaving the object unlit, and never
    /// <see cref="NoireDraw3D.CharacterStencilValue"/>, an end-of-frame decal value
    /// (<see cref="LitStencilMark"/> by default).
    /// </summary>
    public uint Stencil { get; set; } = LitStencilMark;

    /// <summary>
    /// Gets or sets whether the injected draw writes the five G-buffer targets, on by default and turned off to leave
    /// the depth write as the injection's only output.
    /// </summary>
    public bool WriteColor { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the injected draw writes the game's depth buffer, on by default, since without it every
    /// later surface of the geometry pass paints over the object.
    /// </summary>
    public bool WriteDepth { get; set; } = true;

    /// <summary>
    /// Gets or sets a flat colour replacing the albedo, <c>rgb</c> the colour and <c>a</c> how much of it replaces the
    /// material's (0 by default).
    /// </summary>
    public Vector4 AlbedoOverride { get; set; }

    /// <summary>
    /// Gets or sets whether game-lit meshes are also drawn depth-only into every shadow map the game re-renders, off
    /// by default, a cached map picking the object up only at its next refresh.
    /// </summary>
    public bool CastShadows { get; set; }

    /// <summary>Restores every measured default, discarding a sweep.</summary>
    public void Reset()
    {
        Misc = new Vector4(0f, 0f, 1f, 1f);
        ShadingModelId = WorldShadingModelId;
        MaterialParams = MeasuredMaterialParams;
        MaterialOverride = 0f;
        MaterialCeiling = DefaultMaterialCeiling;
        Stencil = LitStencilMark;
        AlbedoOverride = default;
        WriteColor = true;
        WriteDepth = true;
        CastShadows = false;
    }
}
