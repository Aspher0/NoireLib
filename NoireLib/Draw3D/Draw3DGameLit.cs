using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// What <see cref="NoireDraw3D.DrawGameLit(Scene.SceneNode)"/> writes into the game's G-buffer. The defaults are
/// the values measured off the game's own world geometry, so the everyday path is to set nothing at all.
/// </summary>
/// <remarks>
/// Reached through <see cref="NoireDraw3D.GameLit"/>. Read once per injected draw, so a change applies on the
/// next frame with no re-submission.
/// </remarks>
public sealed class Draw3DGameLit
{
    /// <summary>
    /// The largest value a half-float can represent, which rtv3's red channel holds across most of the screen.
    /// <b>Not what the game's geometry writes</b>: it is what the channel holds where the geometry pass never wrote
    /// it at all (the game's own furniture reads <c>0</c> there instead). Kept named for the demo's comparison, not
    /// because a surface carries it.
    /// </summary>
    public const float MiscRedSentinel = 65504f;

    /// <summary>rtv0's alpha on the game's world geometry. Six discrete ids exist; this is the one an object in a room carries.</summary>
    public const byte WorldShadingModelId = 128;

    /// <summary>rtv0's alpha on the game's characters. Writing it gets an object shaded by the skin and hair path instead.</summary>
    public const byte CharacterShadingModelId = 32;

    /// <summary>The rtv1 scalars sampled off a real wood floor, used when a material carries no specular map.</summary>
    public static readonly Vector3 MeasuredMaterialParams = new(0.651f, 0.396f, 0f);

    /// <summary>
    /// The ceiling rtv1's channels are held below: the lighting pass treats the very top of that range as a mode
    /// rather than a value. Measured by sweep: red at <c>1.0</c> or <c>0.999</c> turns the object's reflection
    /// green, <c>0.998</c> does not. A material's specular map reaches <c>1.0</c> in places, so writing it through
    /// unchanged trips that mode in patches. The game's world geometry sits far below this, so clamping costs
    /// nothing real.
    /// </summary>
    public const float DefaultMaterialCeiling = 0.99f;

    /// <summary>
    /// The stencil mark that gets injected geometry lit by the game's deferred light volumes. With no mark, the
    /// object receives no light and comes out of the lighting pass black. <c>0x10</c>, <c>0x20</c> and <c>0x80</c>
    /// each light it with no visible difference between them; <c>0x40</c> and every bit below <c>0x10</c> do not.
    /// </summary>
    public const uint LitStencilMark = 0x10;

    /// <summary>
    /// The four channels of rtv3. Red and green are <c>0</c> - what the game's own furniture writes. Blue is a
    /// scale over the model's baked per-vertex occlusion (the position element's fourth component, carried in the
    /// vertex color's alpha), matching the game's own background shaders, which multiply that value by a
    /// per-instance sky visibility; <c>1</c> here writes exactly what the game writes for a normally placed object.
    /// Alpha reads <c>1</c> on both and its meaning is unmeasured.
    /// </summary>
    public Vector4 Misc { get; set; } = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// rtv0's alpha: which of the game's shading models the lighting pass runs over these pixels.
    /// <see cref="WorldShadingModelId"/> by default (what furniture and architecture carry). Must be one of the
    /// game's own ids - an id it does not use is not a neutral value.
    /// </summary>
    public byte ShadingModelId { get; set; } = WorldShadingModelId;

    /// <summary>
    /// The rtv1 scalars. Used as the value when the material has no specular map, and as the value
    /// <see cref="MaterialOverride"/> blends toward when it does.
    /// </summary>
    public Vector3 MaterialParams { get; set; } = MeasuredMaterialParams;

    /// <summary>
    /// How much <see cref="MaterialParams"/> replaces the specular map a material samples into rtv1. 0, the
    /// default, samples the map as its author drew it; 1 writes the flat scalars instead. rtv1 feeds the lighting
    /// pass's specular response, the one term that ignores albedo and changes with the camera: an object lit from
    /// it stays bright with its albedo forced to black, and shifts as the view moves.
    /// </summary>
    public float MaterialOverride { get; set; }

    /// <summary>
    /// The highest value any rtv1 channel is allowed to take. <see cref="DefaultMaterialCeiling"/> by default; raise
    /// it to 1 to write a specular map through untouched. Per channel: <b>red</b> is reflection strength and turns
    /// the reflection green at the very top of its range, <b>green</b> moves and scales the highlight, and
    /// <b>blue</b> darkens the surface - fully lit at 0, heavily darkened at 1.
    /// </summary>
    public float MaterialCeiling { get; set; } = DefaultMaterialCeiling;

    /// <summary>
    /// The stencil mark written alongside the geometry. <see cref="LitStencilMark"/> by default; 0 writes none
    /// and the object then receives no light at all. The game's deferred light volumes test this mark in the scene
    /// depth-stencil's stencil plane, so geometry carrying none is skipped by every light and the lighting pass
    /// comes out black.<br/>
    /// <b>Reading this value back after the frame does not measure it.</b> The plane is rewritten several times
    /// across the frame and is back to <c>0x00</c> by the end; <c>/noire3d stencil</c> reads it end-of-frame, so a
    /// <c>0x00</c> there is not evidence of no mark. <c>/noire3d framedump</c> reads it mid-frame instead, where
    /// the mark is still present.<br/>
    /// <b>Do not use <see cref="NoireDraw3D.CharacterStencilValue"/> here.</b> That <c>0x08</c> is an end-of-frame
    /// value for decal exclusion, not a lit mark; writing it here reproduces the same unlit blow-out as writing no mark at all.
    /// </summary>
    public uint Stencil { get; set; } = LitStencilMark;

    /// <summary>
    /// Whether the injected draw writes the five G-buffer targets. On by default. Turning it off leaves the depth
    /// write as the only thing the injection puts into the frame - useful for isolating whether an artefact comes
    /// from the G-buffer or merely from occupying those pixels.
    /// </summary>
    public bool WriteColor { get; set; } = true;

    /// <summary>
    /// Whether the injected draw writes the game's depth buffer. On by default.
    /// <b>Not the counterpart of <see cref="WriteColor"/>: turning it off does not isolate depth.</b> The injection
    /// runs at the geometry pass's first draw, so with no depth written, those pixels keep the pass's clear value
    /// and every surface the game draws afterwards passes the depth test and paints over the object - it
    /// disappears from the targets as well as from the screen. The depth write keeps the colour write
    /// intact for the rest of the pass.
    /// </summary>
    public bool WriteDepth { get; set; } = true;

    /// <summary>
    /// Replaces the albedo with a flat colour: <c>rgb</c> is the colour, <c>a</c> is how much of it replaces
    /// what the material produced. Alpha 0, the default, leaves the albedo alone. Forcing it to black and seeing
    /// whether the object stays bright tests whether it is actually being lit from the albedo it wrote.
    /// </summary>
    public Vector4 AlbedoOverride { get; set; }

    /// <summary>
    /// Whether game-lit meshes are also drawn, depth-only, into the game's shadow maps, so they cast shadows as well
    /// as receive them. Off by default while the pass is being verified in game. Reaches every map the game
    /// re-renders: the near-field map (redrawn each frame, so it carries the shadow immediately), the sun's
    /// cascades, and lights near anything moving. A map the game rendered once and cached is not re-entered, so a
    /// static lamp shadows the object one refresh late.
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
