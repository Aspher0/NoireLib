using System.Numerics;

namespace NoireLib.Draw3D;

/// <summary>
/// What <see cref="NoireDraw3D.DrawGameLit(Scene.SceneNode)"/> writes into the game's G-buffer.<br/>
/// The defaults are the values measured off the game's own world geometry, so the everyday path is to set
/// nothing at all. Each property exists because the injection has to reproduce a surface the game authored,
/// and a channel whose meaning is still unknown is more useful as a lever than as a constant: changing one
/// and watching the lit result is what identifies it, and every reading on this workstream that came from
/// recognising a shape instead has been wrong.
/// </summary>
/// <remarks>
/// Reached through <see cref="NoireDraw3D.GameLit"/>. Read once per injected draw, so a change applies on the
/// next frame with no re-submission.
/// </remarks>
public sealed class Draw3DGameLit
{
    /// <summary>
    /// The largest value a half-float can represent, which rtv3's red channel holds across most of the
    /// screen.<br/>
    /// <b>It is not what the game's geometry writes there.</b> It was read off the target rather than off a
    /// surface, and a paired sample - the game's own copy of a model and ours in the same frame, same
    /// buffer - reads <c>0</c> on the game's furniture against this value on ours. What this is, then, is
    /// what the channel holds where the geometry pass has not written it. Kept named because the demo offers
    /// it as a comparison, not because a surface carries it.
    /// </summary>
    public const float MiscRedSentinel = 65504f;

    /// <summary>rtv0's alpha on the game's world geometry. Six discrete ids exist; this is the one an object in a room carries.</summary>
    public const byte WorldShadingModelId = 128;

    /// <summary>rtv0's alpha on the game's characters. Writing it gets an object shaded by the skin and hair path instead.</summary>
    public const byte CharacterShadingModelId = 32;

    /// <summary>The rtv1 scalars sampled off a real wood floor, used when a material carries no specular map.</summary>
    public static readonly Vector3 MeasuredMaterialParams = new(0.651f, 0.396f, 0f);

    /// <summary>
    /// The ceiling rtv1's channels are held below, because the lighting pass treats the very top of that range
    /// as a mode rather than a value.<br/>
    /// Measured by sweep: red at <c>1.0</c> or <c>0.999</c> turns the object's reflection green, and
    /// <c>0.998</c> does not. A material's specular map reaches <c>1.0</c> in places, so writing it through
    /// unchanged trips that mode in patches - which is exactly the blotching an injected object showed against
    /// the game's own copy of the same model. The game's world geometry sits far below this, so clamping costs
    /// nothing real and avoids selecting a behaviour nobody asked for.
    /// </summary>
    public const float DefaultMaterialCeiling = 0.99f;

    /// <summary>
    /// The stencil mark that gets injected geometry lit by the game's deferred light volumes.<br/>
    /// Established by sweep, not by inspection: with no mark the object receives no light from anything and
    /// comes out of the lighting pass black. <c>0x10</c>, <c>0x20</c> and <c>0x80</c> each light it, and
    /// produce no visible difference from one another; <c>0x40</c> and every bit below <c>0x10</c> do not.
    /// </summary>
    public const uint LitStencilMark = 0x10;

    /// <summary>
    /// The four channels of rtv3.<br/>
    /// Red and green are <c>0</c>: that is what the game's own furniture writes, established by sampling its
    /// copy of a model and ours in the same frame and comparing per channel, on two different surfaces of the
    /// same piece.<br/>
    /// Blue is a scale over the model's baked per-vertex occlusion (the position element's fourth component,
    /// carried in the vertex color's alpha), matching the game's own background shaders, which multiply that
    /// value by a per-instance sky visibility. <c>1</c> here writes exactly what the game writes for a
    /// normally placed object. Alpha reads <c>1</c> on both and its meaning is unmeasured.
    /// </summary>
    public Vector4 Misc { get; set; } = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// rtv0's alpha: which of the game's shading models the lighting pass runs over these pixels.<br/>
    /// <see cref="WorldShadingModelId"/> by default, which is what furniture and architecture carry. This has
    /// to be one of the game's own ids - an id it does not use is not a neutral value.
    /// </summary>
    public byte ShadingModelId { get; set; } = WorldShadingModelId;

    /// <summary>
    /// The rtv1 scalars. Used as the value when the material has no specular map, and as the value
    /// <see cref="MaterialOverride"/> blends toward when it does.
    /// </summary>
    public Vector3 MaterialParams { get; set; } = MeasuredMaterialParams;

    /// <summary>
    /// How much <see cref="MaterialParams"/> replaces the specular map a material samples into rtv1. 0, the
    /// default, samples the map as its author drew it; 1 writes the flat scalars instead.<br/>
    /// rtv1 feeds the lighting pass's specular response, and that response is the one term that ignores albedo
    /// and changes with the camera - so it is what an object shows when it goes bright under room lights, stays
    /// bright with its albedo forced to black, and shifts as the view moves. The channels of a game specular
    /// map have been misread twice on this workstream, so writing them raw is an assumption, and this is how it
    /// gets tested rather than trusted.
    /// </summary>
    public float MaterialOverride { get; set; }

    /// <summary>
    /// The highest value any rtv1 channel is allowed to take. <see cref="DefaultMaterialCeiling"/> by default;
    /// raise it to 1 to write a specular map through untouched and see what the top of the range selects.<br/>
    /// The channels themselves, from sweeping each against the live result: <b>red</b> is reflection strength
    /// and turns the reflection green at the very top of its range, <b>green</b> moves and scales the
    /// highlight, and <b>blue</b> darkens the surface - fully lit at 0, heavily darkened at 1.
    /// </summary>
    public float MaterialCeiling { get; set; } = DefaultMaterialCeiling;

    /// <summary>
    /// The stencil mark written alongside the geometry. <see cref="LitStencilMark"/> by default; 0 writes none
    /// and the object then receives no light at all.<br/>
    /// The game marks pixels in the stencil plane of the scene depth-stencil during its geometry pass, and its
    /// deferred light volumes test that mark, so geometry carrying none is skipped by every light in the room
    /// and leaves the lighting pass black.<br/>
    /// <b>Reading this value back after the frame does not measure it.</b> The plane is rewritten several times
    /// across a frame: a mid-frame census reads <see cref="LitStencilMark"/> across the whole screen at the
    /// geometry pass, a second bit is set by the shadow-mask stage, and it is back to <c>0x00</c> by the end.
    /// That is why <c>/noire3d stencil</c> reporting <c>0x00</c> on world geometry was not evidence that no mark
    /// existed. <c>/noire3d framedump</c> reads the plane mid-frame, which is where it is still there.<br/>
    /// <b>Do not use <see cref="NoireDraw3D.CharacterStencilValue"/> here.</b> That <c>0x08</c> is an
    /// end-of-frame reading, which is the right moment for the decal exclusion that consumes it and the wrong
    /// one for this: it carries no lit mark, so writing it reproduces exactly the unlit blow-out that having no
    /// mark at all produces.
    /// </summary>
    public uint Stencil { get; set; } = LitStencilMark;

    /// <summary>
    /// Whether the injected draw writes the five G-buffer targets. On by default.<br/>
    /// Turning it off leaves the depth write as the only thing the injection puts into the game's frame, which
    /// is what separates a fault in what we describe from a fault in the fact that we occupy those pixels at
    /// all. An artefact that survives with this off owes nothing to the G-buffer.
    /// </summary>
    public bool WriteColor { get; set; } = true;

    /// <summary>
    /// Whether the injected draw writes the game's depth buffer. On by default.<br/>
    /// <b>This is not the counterpart of <see cref="WriteColor"/>, and turning it off does not isolate depth.</b>
    /// The injection runs at the geometry pass's first draw, so with no depth written those pixels keep the
    /// value the pass was cleared to, and every surface the game draws afterwards passes the depth test and
    /// paints over the object - it disappears from the targets as well as from the screen. The depth write is
    /// what makes the colour write survive the rest of the pass.
    /// </summary>
    public bool WriteDepth { get; set; } = true;

    /// <summary>
    /// Replaces the albedo with a flat colour: <c>rgb</c> is the colour, <c>a</c> is how much of it replaces
    /// what the material produced. Alpha 0, the default, leaves the albedo alone.<br/>
    /// This is the test that separates "the G-buffer is wrong" from "something downstream never reads it":
    /// force the albedo to black, and an object that stays bright is not being lit from the albedo it wrote.
    /// </summary>
    public Vector4 AlbedoOverride { get; set; }

    /// <summary>
    /// Whether game-lit meshes are also drawn, depth-only, into the game's shadow maps, so they cast
    /// shadows as well as receive them. Off by default while the pass is being verified in game.<br/>
    /// Casting reaches every map the game re-renders: the near-field map, which it redraws each frame and
    /// which therefore carries the shadow around the camera immediately, plus the sun's cascades and the
    /// lights near anything moving. A map the game rendered once and cached is not re-entered, so a static
    /// lamp that has not refreshed since the object appeared shadows it one refresh late.
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
