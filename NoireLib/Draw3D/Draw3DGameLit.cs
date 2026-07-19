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
    /// <summary>The largest value a half-float can represent, which is what the game's world geometry holds in rtv3's red channel.</summary>
    public const float MiscRedSentinel = 65504f;

    /// <summary>rtv0's alpha on the game's world geometry. Six discrete ids exist; this is the one an object in a room carries.</summary>
    public const byte WorldShadingModelId = 128;

    /// <summary>rtv0's alpha on the game's characters. Writing it gets an object shaded by the skin and hair path instead.</summary>
    public const byte CharacterShadingModelId = 32;

    /// <summary>The rtv1 scalars sampled off a real wood floor, used when a material carries no specular map.</summary>
    public static readonly Vector3 MeasuredMaterialParams = new(0.651f, 0.396f, 0f);

    /// <summary>
    /// The four channels of rtv3, written verbatim.<br/>
    /// Red is a sentinel on world geometry (<see cref="MiscRedSentinel"/>) and zero on characters; green is zero
    /// everywhere. Blue and alpha carry data in 0..1 whose meaning is unmeasured.<br/>
    /// <b>Red is the one to suspect first.</b> It is the only value the injection writes that is orders of
    /// magnitude outside 0..1, so if any downstream pass multiplies by it rather than testing it, this channel
    /// alone produces a blown-out object while every other channel reads correctly. Drop it to 0 and compare.
    /// </summary>
    public Vector4 Misc { get; set; } = new(MiscRedSentinel, 0f, 1f, 1f);

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
    /// The stencil value written alongside the geometry, or 0 to write none.<br/>
    /// The game marks object categories in the stencil plane of the scene depth-stencil: characters carry
    /// <see cref="NoireDraw3D.CharacterStencilValue"/>, and world geometry measures <c>0x00</c> - which is also
    /// what a pixel holds when nothing writes a stencil, so the default already matches the furniture beside it.
    /// Set this only to mark injected geometry as some other category on purpose.
    /// </summary>
    public uint Stencil { get; set; }

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

    /// <summary>Restores every measured default, discarding a sweep.</summary>
    public void Reset()
    {
        Misc = new Vector4(MiscRedSentinel, 0f, 1f, 1f);
        ShadingModelId = WorldShadingModelId;
        MaterialParams = MeasuredMaterialParams;
        MaterialOverride = 0f;
        Stencil = 0;
        AlbedoOverride = default;
        WriteColor = true;
        WriteDepth = true;
    }
}
