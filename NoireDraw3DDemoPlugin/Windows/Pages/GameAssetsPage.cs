using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// Loads models and their materials straight out of the game's archives and draws them with this renderer. The files
/// are read and decoded here; nothing is spawned in the game and no game function is called, so what appears is inert
/// geometry that only this layer knows about.
/// </summary>
internal sealed class GameAssetsPage : IDisposable
{
    private const string DefaultPath = "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl";

    /// <summary>Paths verified to exist, covering both vertex layouts and both material shader packages.</summary>
    private static readonly (string Label, string Path)[] Presets =
    [
        ("Furniture", "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl"),
        ("Furniture 2", "bgcommon/hou/indoor/general/0002/bgparts/fun_b0_m0002.mdl"),
        ("Body", "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl"),
        ("Equipment", "chara/equipment/e0001/model/c0101e0001_top.mdl"),
        ("Monster", "chara/monster/m0001/obj/body/b0001/model/m0001b0001.mdl"),
    ];

    /// <summary>
    /// One spawned model and everything that belongs to it.<br/>
    /// Grouped rather than flattened into one list of nodes because each model owns its own materials, and a
    /// material is only safe to dispose once nothing is still drawing with it. Sharing one dictionary across
    /// spawns meant loading a second model disposed the first one's textures while its nodes were still on
    /// screen, which showed up as the first model vanishing or the game crashing outright.
    /// </summary>
    private sealed class SpawnedModel
    {
        /// <summary>The decoded meshes, index-aligned with <see cref="Nodes"/>.</summary>
        public required GameModelMesh[] Meshes { get; init; }

        /// <summary>The materials this model resolved, keyed by material path. Owned here and disposed with it.</summary>
        public required Dictionary<string, GameMaterial> Materials { get; init; }

        /// <summary>Where it was loaded from, for the status line.</summary>
        public required string Path { get; init; }

        /// <summary>Which slot along the row this model stands in, so a second one lands beside the first rather than inside it.</summary>
        public required int Slot { get; init; }

        /// <summary>The nodes on screen, one per mesh.</summary>
        public List<SceneNode> Nodes { get; } = [];

        /// <summary>Destroys the nodes and leaves the materials alone, so the model can be rebuilt from the same decoded data.</summary>
        public void DestroyNodes()
        {
            foreach (var node in Nodes)
            {
                if (!node.IsDestroyed)
                    node.Destroy();
            }

            Nodes.Clear();
        }

        /// <summary>Destroys the nodes, then releases the materials they were drawing with. Order matters.</summary>
        public void Dispose()
        {
            DestroyNodes();

            foreach (var material in Materials.Values)
                material.Dispose();

            Materials.Clear();
        }
    }

    private readonly List<SpawnedModel> models = [];

    /// <summary>The next sideways slot to spawn into. Only reset by Clear, so removing one model never shuffles the others.</summary>
    private int nextSlot;

    /// <summary>
    /// Draw the spawned meshes into the game's own G-buffer instead of Draw3D's layer, so the game's deferred
    /// lighting pass lights them with every lamp, the sun and the ambient term, and walls occlude them at pixel
    /// precision. Off by default: this is the one path that draws inside the game's frame.
    /// </summary>
    private bool gameLit;

    /// <summary>Whether the albedo is being forced flat, which separates a wrong G-buffer from a downstream pass that never reads it.</summary>
    private bool flatAlbedo;

    /// <summary>The colour the albedo is forced to while <see cref="flatAlbedo"/> is on.</summary>
    private Vector3 flatAlbedoColor = Vector3.Zero;

    /// <summary>The materials of the load that has finished but not yet been spawned. Handed to the model it produces.</summary>
    private Dictionary<string, GameMaterial> pendingMaterials = new(StringComparer.Ordinal);

    /// <summary>
    /// The three values worth writing into rtv3's red channel. A slider is the wrong control for it: the game
    /// writes the half-float ceiling there and every other value in that span is one no surface holds, so the
    /// choice is between the measured value and the two that would show a channel driving an intensity term.
    /// </summary>
    private enum MiscRed
    {
        /// <summary>65504, what the channel holds where the game's geometry pass has not written it.</summary>
        Sentinel,

        /// <summary>1, the top of the range every other channel uses.</summary>
        One,

        /// <summary>0, the value the game's characters carry.</summary>
        Zero,
    }

    private MiscRed miscRed = MiscRed.Zero;

    /// <summary>
    /// How a loaded material is turned into something this renderer draws. One choice rather than several
    /// toggles, because the paths are alternatives: the mask path and the whole-surface diffuse path apply
    /// color in incompatible ways, and offering both as switches makes one silently outrank the other.
    /// </summary>
    private enum Shading
    {
        /// <summary>Opaque and lit, with the dye confined to the area the color map's alpha marks as dyeable. What the game does.</summary>
        Game,

        /// <summary>Lit, drawing the base color texture untouched.</summary>
        Lit,

        /// <summary>Lit, with the material's diffuse constant multiplied over every pixel.</summary>
        LitDiffuse,

        /// <summary>No shading at all, showing the texture's own colors.</summary>
        Unlit,

        /// <summary>Unlit, with the diffuse constant over every pixel.</summary>
        UnlitDiffuse,
    }

    private Scene3D? scene;
    private string path = DefaultPath;
    private int lod;
    private int variant = 1;
    private bool useGameMaterials = true;
    private Shading shading = Shading.Game;
    private bool overrideDye;
    private Vector3 dye = new(0.82f, 0.68f, 0.45f);
    private IReadOnlyList<GameStain>? stains;
    private int stainIndex = -1;
    private bool ignoreSceneLight;
    private float dyeReference;
    private float normalStrength = 1f;
    private float specularStrength;
    private bool importVertexColors;
    private bool keepCpuData = true;
    private Vector4 tint = Vector4.One;
    private float distance = 4f;
    private string status = string.Empty;
    private bool failed;
    private bool loading;
    private GameModelMesh[]? loaded;
    private Shading appliedShading;
    private bool appliedOverrideDye;
    private Vector3 appliedDye;
    private bool appliedIgnoreSceneLight;
    private float appliedDyeReference;
    private float appliedNormalStrength;
    private float appliedSpecularStrength;
    private Vector4 appliedTint = Vector4.One;
    private bool appliedUseGameMaterials = true;
    private float appliedDistance = 4f;

    /// <summary>Whether the game-material pipeline was registered when the nodes on screen were built.</summary>
    private bool appliedPipelineReady;

    private string loadedFrom = string.Empty;

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        ConsumeLoaded();
        RefreshMaterialsIfChanged();

        Ui.Section("Load a model");
        Ui.Note("Reads a .mdl and its .mtrl files from the game archives, decodes them here, and draws the result in front of you. Reading files is all that happens: no actor, no object table entry, nothing the game or its server knows about.");
        Ui.Gap();

        DrawPresets();
        Ui.Gap();

        // The fallback draws the texture and drops the dye, the normal map and the specular, so it looks like a
        // duller material rather than like a failure - and every reading taken off a model in that state is a
        // reading of the wrong material. Said out loud for that reason.
        if (!GameMaterialPipeline.Ready && GameMaterialPipeline.Unavailable is { } why)
            Ui.Callout($"Game materials are falling back to the plain lit shader: {why} Anything spawned now draws its texture without dye, normal map or specular. It repairs itself once the renderer is up.", ImGuiColors.DalamudOrange);

        using (Ui.Form("assets.load"))
        {
            Ui.Text("Game path", () => path, v => path = v, DefaultPath, 512,
                "Archive path of the model. Anything under bgcommon/ is a static prop; chara/ paths are character parts.");
            Ui.Int("Level of detail", () => lod, v => lod = Math.Clamp(v, 0, 2),
                "0 is the most detailed. Models carry up to three; asking for one a model does not have loads nothing.");
            Ui.Toggle("Use game materials", () => useGameMaterials, v => useGameMaterials = v,
                "Resolves each piece's material and draws with its base color texture. Off draws everything with the flat tint below, which is what the first pass of this loader did.");
            Ui.Enum<Shading>("Shading", () => shading, v => shading = v,
                "Game confines the dye below to the area the color map's alpha marks as dyeable, which is what the game itself does. The Diffuse entries multiply the material's diffuse constant over every pixel instead: most materials set it to white and nothing changes, but on dyeable furniture that colors the detailed areas the texture already colored correctly and reads as far too dark. Unlit draws the texture with no shading at all, which separates a texture problem from a lighting one.");
            Ui.Toggle("Apply a dye color", () => overrideDye, v => overrideDye = v,
                "Off renders the dyeable area as the game renders an unstained item: with Snow White, the stain the game itself defaults to, because a dyeable surface always has a stain multiplied in and there is no passthrough state. On replaces that default with the color below.");
            DrawStainPicker();
            Ui.Color3("Dye color", () => dye, v => dye = v,
                "What the dyeable area is tinted with when the toggle above is on; Game shading uses it and the other modes ignore it. Picking a dye above sets this to that dye's exact color.");
            Ui.Toggle("Light with the game's lights", () => gameLit, v => gameLit = v,
                "Draws into the game's own G-buffer so the game lights it: every lamp, the sun, ambient, and walls occluding it properly. The object turns opaque and loses outlines, fades and above-everything while this is on, and it casts no shadow of its own yet.");
            Ui.Toggle("Ignore this renderer's light", () => ignoreSceneLight, v => ignoreSceneLight = v,
                "Removes this renderer's lighting from Game shading, leaving the surface at the colors its texture and dye give it. This is the absence of our light, not the presence of the game's: use it to judge a color question without lighting confusing the comparison, since the two are impossible to tell apart by eye otherwise.");
            Ui.Slider("Dye reference", () => dyeReference, v => dyeReference = v, 0f, 1f,
                "How the dye meets the dyeable area. 0 multiplies the authored color by the dye, which is what the game does: three stains sampled in its own G-buffer land within 0.004 per channel under the multiply, so 0 is the correct value and the question this slider existed to settle is settled. Above 0 divides the area by that value first, so an area authored at it lands on the dye exactly - kept as an authoring tool, not as a model of the game.");
            Ui.Slider("Normal strength", () => normalStrength, v => normalStrength = v, 0f, 2f,
                "How far the material's normal map bends the surface normal under Game shading. 1 applies the map as its author drew it and is the value to leave it at; above 1 exaggerates the surface and 0 lights the model by its geometry alone. The tangent frame is derived from screen-space derivatives, so this needs none of the vertex tangents the game itself uses.");
            Ui.Slider("Specular strength", () => specularStrength, v => specularStrength = v, 0f, 2f,
                "How strongly the specular map adds a highlight, with its green channel read as roughness. Off by default: these surfaces are matte in game, and the community shader reference marks this map's mask channels as not fully understood, so anything above 0 is an experiment rather than a match.");
            Ui.Int("Material variant", () => variant, v => variant = Math.Max(0, v),
                "Character models store material paths relative and resolve them against a numbered variant folder. Background models ignore this.");
            Ui.Toggle("Import vertex colors", () => importVertexColors, v => importVertexColors = v,
                "Off by default. The game stores shader data in this channel (blend and wetness masks) rather than color, so applying it as a tint paints the model in false colors. Turn it on to see exactly that. Background models declare no color channel at all, so this changes nothing for furniture and props; only the character presets respond to it.");
            Ui.Toggle("Keep CPU data", () => keepCpuData, v => keepCpuData = v,
                "Retains the decoded geometry so exact per-triangle picking works on the spawned node.");
            Ui.Color4("Tint", () => tint, v => tint = v,
                "Multiplied over the texture. White leaves it untouched; with game materials off this is the whole color.");
            Ui.Slider("Distance", () => distance, v => distance = v, 1f, 20f,
                "How far in front of you the model is placed.");
        }

        if (shading == Shading.Game && !GameMaterialPipeline.EnsureRegistered())
        {
            Ui.Gap();
            Ui.Callout(
                $"The mask shader is not in use: {GameMaterialPipeline.Unavailable} Materials fall back to the lit shader, "
                + "which cannot confine a color to the dyeable area, so the dye color above does nothing.",
                ImGuiColors.DalamudOrange);
        }

        Ui.Gap();
        // The flips are applied inside the loader, so they are baked into the decoded mesh and a change only
        // reaches an import that happens afterwards. Re-reading the current path is what makes the toggle
        // answer immediately instead of leaving the last result on screen contradicting it.
        if (Ui.ImportFlips("assets.flips") && !loading && path.Length > 0)
        {
            Clear();
            BeginLoad();
        }

        if (gameLit)
        {
            Ui.Gap();
            DrawGameLitChannels();
            Ui.Gap();
            DrawGBufferCompare();
        }

        Ui.Gap();
        DrawActions();

        if (status.Length > 0)
        {
            Ui.Gap();
            if (failed)
                Ui.Callout(status, ImGuiColors.DalamudRed);
            else
                Ui.Status(status);
        }

        DrawDecoded();
    }

    /// <summary>
    /// The values the injection writes into the channels the game authored. Every one of them defaults to what
    /// was measured off the game's own geometry, so this section is for identifying the channels whose meaning
    /// is still unknown: change one, look at the object, and a channel that moves the result is a channel that
    /// matters. Reading a value off a shape has been wrong every time on this workstream; watching what
    /// responds has been right every time.
    /// </summary>
    private void DrawGameLitChannels()
    {
        Ui.Section("Game-lit G-buffer channels");
        Ui.Note("What the object writes into the game's own frame. The defaults are the measured ones, so an object that looks right needs none of this.");
        Ui.Gap();

        Ui.Note("The injection puts two things into the game's frame: a description of the surface, in five color targets, and a depth value. Neither switch below isolates one cleanly. Turning the color off does not leave a neutral object, it leaves a depth surface with no material under it, which is its own separate defect. Turning depth off removes the object entirely, because it is drawn first in the pass and everything the game draws afterwards paints over the pixels it would have held.");
        Ui.Gap();

        using (Ui.Form("assets.gamelit.writes"))
        {
            Ui.Toggle("Write the color targets", () => NoireDraw3D.GameLit.WriteColor, v => NoireDraw3D.GameLit.WriteColor = v,
                "Off writes depth only and nothing into the five targets. Read the result carefully: an object with a depth value and an undescribed surface is not the same experiment as an object that describes itself correctly.");
            Ui.Toggle("Write depth", () => NoireDraw3D.GameLit.WriteDepth, v => NoireDraw3D.GameLit.WriteDepth = v,
                "Off leaves the game's depth buffer untouched, and the object then vanishes completely: the world draws over the pixels it would have occupied. Useful for removing the object without removing the injection, not for testing depth on its own.");
        }

        Ui.Gap();
        Ui.Note("Below: the individual channels of the surface description. Material is the one that drives the specular response, which is the only lighting term that ignores albedo and shifts as the camera moves.");
        Ui.Gap();

        using (Ui.Form("assets.gamelit"))
        {
            Ui.Enum<MiscRed>("Misc red", () => miscRed, v =>
            {
                miscRed = v;
                var misc = NoireDraw3D.GameLit.Misc;
                NoireDraw3D.GameLit.Misc = misc with { X = RedValue(v) };
            }, "rtv3's red channel, now 0 by default because that is what a paired sample reads on the game's own furniture. The half-float ceiling (65504) is what the channel holds where the geometry pass has not written it, which is most of the screen - reading it off the target rather than off a surface is how it came to be the default, and it is the only value the injection writes that is nowhere near 0 to 1.");

            Ui.Slider("Misc green", () => NoireDraw3D.GameLit.Misc.Y, v => NoireDraw3D.GameLit.Misc = NoireDraw3D.GameLit.Misc with { Y = v }, 0f, 1f,
                "Measured at zero everywhere in the game's own buffer, so this is here to confirm that rather than to tune it.");
            Ui.Slider("Misc blue", () => NoireDraw3D.GameLit.Misc.Z, v => NoireDraw3D.GameLit.Misc = NoireDraw3D.GameLit.Misc with { Z = v }, 0f, 1f,
                "Carries data in 0.25 to 1.0 in the game's buffer; what it means is unmeasured.");
            Ui.Slider("Misc alpha", () => NoireDraw3D.GameLit.Misc.W, v => NoireDraw3D.GameLit.Misc = NoireDraw3D.GameLit.Misc with { W = v }, 0f, 1f,
                "Carries data in 0.8 to 1.0 in the game's buffer; what it means is unmeasured.");

            Ui.Slider("Replace the material map", () => NoireDraw3D.GameLit.MaterialOverride, v => NoireDraw3D.GameLit.MaterialOverride = v, 0f, 1f,
                "How much the three values below replace the specular map this material samples into rtv1. 0 writes the map as its author drew it. 1 writes the flat values instead, which is the test for whether the map's channels are being written into the wrong slots: rtv1 drives the specular response, and the object going bright under room lights while its albedo is black points here rather than at the albedo.");
            Ui.Slider3("Material values", () => NoireDraw3D.GameLit.MaterialParams, v => NoireDraw3D.GameLit.MaterialParams = v, 0f, 1f,
                "The three rtv1 scalars, used whenever the slider above is above 0 and always on a material with no specular map. Red is reflection strength, green moves and scales the highlight, blue darkens the surface (fully lit at 0, heavily darkened at 1).");
            Ui.Slider("Material ceiling", () => NoireDraw3D.GameLit.MaterialCeiling, v => NoireDraw3D.GameLit.MaterialCeiling = v, 0.5f, 1f,
                "The highest value any rtv1 channel may take. The top of that range selects a mode rather than a value: red at 1.0 or 0.999 turns the reflection green, 0.998 does not. A specular map reaches 1.0 in places, so writing it through untouched trips that mode in patches, which is the green blotching. Raise this to 1 to see it happen.");

            Ui.Int("Shading model", () => NoireDraw3D.GameLit.ShadingModelId, v => NoireDraw3D.GameLit.ShadingModelId = (byte)Math.Clamp(v, 0, 255),
                "rtv0's alpha: which of the game's shading models runs over these pixels. 128 is what furniture and architecture carry and is the default; 32 is what characters carry. Six ids exist in total, and one the game does not use is not a neutral value.");

            Ui.Int("Stencil value", () => (int)NoireDraw3D.GameLit.Stencil, v => NoireDraw3D.GameLit.Stencil = (uint)Math.Clamp(v, 0, 255),
                "The mark stamped into the stencil plane alongside the geometry. The game's deferred light volumes test a mark written during its geometry pass, so geometry that writes none is skipped by every light in the room and leaves the lighting pass black. 16 is the value the game's own world writes and is the default. 8 is the character value read at the END of the frame and is not a lit mark: writing it here reproduces the unlit blow-out, whatever kind of model is spawned.");

            Ui.Toggle("Force a flat albedo", () => flatAlbedo, v =>
            {
                flatAlbedo = v;
                NoireDraw3D.GameLit.AlbedoOverride = new Vector4(flatAlbedoColor, v ? 1f : 0f);
            }, "Replaces the material's albedo with the color below. Black is the decisive test: an object whose albedo is black and which is still bright on screen is not being lit from the albedo it wrote, which moves the fault out of the G-buffer entirely.");

            Ui.Color3("Flat albedo color", () => flatAlbedoColor, v =>
            {
                flatAlbedoColor = v;
                if (flatAlbedo)
                    NoireDraw3D.GameLit.AlbedoOverride = new Vector4(v, 1f);
            }, "What the albedo is forced to. Black answers the question above; a saturated color confirms the write is landing at all.");
        }

        Ui.Gap();
        if (Ui.IconButton(FontAwesomeIcon.Undo, "Restore the measured defaults"))
        {
            NoireDraw3D.GameLit.Reset();
            miscRed = MiscRed.Zero;
            flatAlbedo = false;
            flatAlbedoColor = Vector3.Zero;
        }
    }

    /// <summary>Whether the live G-buffer comparison is running.</summary>
    private bool compareGBuffer;

    /// <summary>The last reading taken under the cursor, one value per G-buffer target.</summary>
    private readonly List<Vector4> cursorSample = [];

    /// <summary>A reading held for comparison, or empty. The game's own copy of a model goes here.</summary>
    private readonly List<Vector4> referenceSample = [];

    /// <summary>Whether <see cref="cursorSample"/> holds a real reading. Survives the cursor moving onto the UI, which is what makes the panel reachable.</summary>
    private bool sampleValid;

    /// <summary>Names for the five targets, in bind order, so a reading is legible without counting columns.</summary>
    private static readonly string[] TargetNames = ["normal + id", "material", "albedo", "misc", "geo normal"];

    /// <summary>
    /// Reads the game's G-buffer under the cursor and compares it against a held reading.<br/>
    /// This is the answer to "how do I know our object matches the game's": the game's own copy of the same
    /// model is in the same buffer, in the same frame, under the same lights, so hovering one and then the
    /// other turns a judgement about brightness into a per-channel difference that can be driven to zero.
    /// Every wrong reading on this feature came from looking at an image instead.
    /// </summary>
    private void DrawGBufferCompare()
    {
        Ui.Section("Compare against the game's own copy");
        Ui.Note("Hover a pixel of the game's own version of a model, move the mouse here and hold it as the reference, then hover ours. The reading freezes as soon as the cursor is over a window, so reaching for the button does not overwrite what you were pointing at. These are the values the lighting pass reads, before any of it runs, so the two objects do not need to stand in the same light or face the same way - only the surface has to be comparable. Landing on the same texel by hand is not possible, which is what the ratio row is for: watch whether it holds steady as the cursor wanders across a region rather than trying to read one sample exactly. Needs /noire3d rtlog to have run once so the G-buffer is identified.");
        Ui.Gap();

        using (Ui.Form("assets.gbuffer.compare"))
        {
            Ui.Toggle("Sample under the cursor", () => compareGBuffer, v => compareGBuffer = v,
                "Reads all five targets at the mouse position every frame. It copies only a small patch, so it is cheap, but it is still a readback and belongs off when it is not being used.");
        }

        if (!compareGBuffer)
            return;

        // Sampling stops the moment the cursor is over a window, so the reading holds on the last thing
        // hovered in the world. Without this the panel is unusable: reaching for the button is itself a mouse
        // move off the object, and the value would be overwritten with whatever pixel the button sits on.
        var overWorld = !ImGui.GetIO().WantCaptureMouse;
        if (overWorld)
            sampleValid = NoireDraw3D.TrySampleGameGBuffer(ImGui.GetMousePos(), cursorSample);

        Ui.Gap();

        if (!sampleValid || cursorSample.Count == 0)
        {
            Ui.Callout(
                cursorSample.Count == 0 && overWorld
                    ? "No G-buffer identified yet - run /noire3d rtlog once, then this reads every frame."
                    : "Hover a model in the world to take a reading.",
                ImGuiColors.DalamudOrange);
            return;
        }

        if (Ui.IconButton(FontAwesomeIcon.Crosshairs, "Hold this as the reference"))
        {
            referenceSample.Clear();
            referenceSample.AddRange(cursorSample);
        }

        ImGui.SameLine();
        Ui.Mono(overWorld ? "reading live" : "held (cursor is over the UI)", overWorld ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3);

        Ui.Gap();

        for (var i = 0; i < cursorSample.Count; i++)
        {
            var name = i < TargetNames.Length ? TargetNames[i] : $"rtv{i}";
            var v = cursorSample[i];
            Ui.Mono($"rtv{i} {name,-12} {v.X,7:F3} {v.Y,7:F3} {v.Z,7:F3} {v.W,7:F3}");

            if (i >= referenceSample.Count)
                continue;

            // The difference is the reading that matters, so it is printed beside the value rather than left
            // for the reader to subtract two columns of four floats in their head.
            var r = referenceSample[i];
            var d = v - r;
            var worst = MathF.Max(MathF.Abs(d.X), MathF.Max(MathF.Abs(d.Y), MathF.Max(MathF.Abs(d.Z), MathF.Abs(d.W))));
            Ui.Mono($"     vs reference {d.X,7:F3} {d.Y,7:F3} {d.Z,7:F3} {d.W,7:F3}",
                worst < 0.01f ? ImGuiColors.HealerGreen : worst < 0.05f ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed);

            // The ratio is what makes this usable without landing on the same texel twice, which is not
            // something a hand on a mouse can do. A difference confounds two causes: the two readings are of
            // different texels, and our shader disagrees with the game's. The ratio separates them - if it
            // holds steady as the cursor wanders over a region, the texture is cancelling out of it and what
            // is left is one factor we are not applying, whose value it states. If it scatters, the readings
            // are simply of different parts of the texture and nothing has been measured yet.
            Ui.Mono($"     ratio        {Ratio(v.X, r.X)} {Ratio(v.Y, r.Y)} {Ratio(v.Z, r.Z)} {Ratio(v.W, r.W)}",
                ImGuiColors.DalamudGrey3);
        }
    }

    /// <summary>
    /// One channel of the sample over the same channel of the reference, or dashes where the reference is too
    /// near zero for the division to mean anything - a ratio against nothing is noise wearing three decimals.
    /// </summary>
    private static string Ratio(float sample, float reference) =>
        MathF.Abs(reference) < 1e-3f ? "      -" : $"{sample / reference,7:F3}";

    /// <summary>The rtv3 red value each choice writes.</summary>
    private static float RedValue(MiscRed choice) => choice switch
    {
        MiscRed.One => 1f,
        MiscRed.Zero => 0f,
        _ => Draw3DGameLit.MiscRedSentinel,
    };

    /// <summary>
    /// Picks one of the game's own dyes, so the color applied is a value the game stains with rather than one
    /// chosen to look close. Restricted to the dyes the sheet marks as usable on housing furniture.
    /// </summary>
    private void DrawStainPicker()
    {
        stains ??= StainHelper.All(housingOnly: true);

        Ui.Row("Dye", "The game's own housing dyes, read from its dye table. Picking one sets the color below to that dye's exact value, which is what the game would stain the item with.");

        if (stains.Count == 0)
        {
            Ui.Mono("(no dyes read from the game)", ImGuiColors.DalamudGrey3);
            return;
        }

        // Sort stains by name
        stains = new List<GameStain>(stains);
        ((List<GameStain>)stains).Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        var names = new string[stains.Count];
        for (var i = 0; i < stains.Count; i++)
            names[i] = stains[i].Name.Length > 0 ? stains[i].Name : $"Dye {stains[i].Id}";

        var index = stainIndex;
        if (Ui.Combo("##assets.stain", names, ref index) && index >= 0 && index < stains.Count)
        {
            stainIndex = index;
            dye = stains[index].Color;
            overrideDye = true;
        }
    }

    private void DrawPresets()
    {
        for (var i = 0; i < Presets.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            if (ImGui.SmallButton(Presets[i].Label))
                path = Presets[i].Path;
        }

        ImGui.SameLine();
        Ui.HelpMarker("Paths confirmed to exist in a complete installation. The furniture entries are static meshes drawn with a background shader package; the character entries carry skinning data the decoder currently ignores and use a character package with a color table.");
    }

    private void DrawActions()
    {
        using (Ui.Disabled(loading || path.Length == 0))
        {
            if (Ui.IconButton(FontAwesomeIcon.Download, loading ? "Loading..." : "Load and spawn"))
                BeginLoad();
        }

        ImGui.SameLine();

        using (Ui.Disabled(models.Count == 0))
        {
            if (Ui.IconButton(FontAwesomeIcon.Trash, models.Count > 1 ? $"Clear all {models.Count}" : "Clear"))
                Clear();
        }
    }

    /// <summary>Describes the most recently spawned model. Loading another leaves the earlier ones standing, so this follows the newest rather than the only one.</summary>
    private void DrawDecoded()
    {
        if (models.Count == 0)
            return;

        var model = models[^1];
        if (model.Meshes.Length == 0)
            return;

        Ui.Section("What was decoded");

        var vertices = 0;
        var indices = 0;
        foreach (var mesh in model.Meshes)
        {
            vertices += mesh.Geometry.Vertices.Length;
            indices += mesh.Geometry.Indices.Length;
        }

        Ui.Mono($"{model.Meshes.Length} mesh(es)   {vertices} vertices   {indices / 3} triangles");
        Ui.Gap();

        for (var i = 0; i < model.Meshes.Length; i++)
        {
            var raw = model.Meshes[i].MaterialPath;
            model.Materials.TryGetValue(raw, out var material);

            var detail = material is null
                ? raw.Length > 0 ? $"{raw}  (not loaded)" : "(no material)"
                : $"{material.File.ShaderPackage}  maps [{Maps(material)}]"
                  + (material.DiffuseColor is { } d ? $"  diffuse {d.X:F2},{d.Y:F2},{d.Z:F2}" : string.Empty)
                  + (material.File.HasColorTable ? $"  color table {material.File.ColorTable.Length}B" : string.Empty);

            Ui.Mono($"[{i}] {model.Meshes[i].Geometry.Vertices.Length,6} verts   {detail}", ImGuiColors.DalamudGrey3);
        }

        if (model.Materials.Count > 0)
        {
            Ui.Gap();
            Ui.Note("Color, normal and specular maps are read and shaded. The diffuse constant above is parsed data only: it is not what an undyed item shows, which is Snow White through the stain system, measured against the game's own copies. The character color table is parsed but not applied, and nothing here casts a shadow, which is the largest remaining difference from the game's own look.");
            Ui.Note("Overall brightness is the Lighting page rather than anything here: this shading caps a fully lit surface at the texture's own color and dims from there, so a model that reads too bright next to the game is asking for lower ambient and light intensity to match the room it is standing in.");
        }
    }

    /// <summary>Which of the material's texture slots resolved, so an absent map reads as a fact rather than a shading bug.</summary>
    private static string Maps(GameMaterial material)
    {
        var present = new List<string>(3);
        if (material.BaseColor is not null)
            present.Add("color");
        if (material.Normal is not null)
            present.Add("normal");
        if (material.Specular is not null)
            present.Add("specular");

        return present.Count == 0 ? "none" : string.Join(", ", present);
    }

    private void BeginLoad()
    {
        loading = true;
        failed = false;
        status = string.Empty;

        var requestedPath = path;
        var requestedLod = lod;
        var requestedVariant = variant;
        var colors = importVertexColors;
        var withMaterials = useGameMaterials;

        _ = LoadAsync(requestedPath, requestedLod, requestedVariant, colors, withMaterials);
    }

    private async System.Threading.Tasks.Task LoadAsync(string modelPath, int requestedLod, int requestedVariant, bool colors, bool withMaterials)
    {
        try
        {
            var meshes = await GameModelLoader.LoadAsync(modelPath, requestedLod, colors).ConfigureAwait(false);
            if (meshes.Length == 0)
            {
                failed = true;
                status = $"Nothing decoded from '{modelPath}'. The path may not exist, or it has no geometry at level {requestedLod}.";
                return;
            }

            // The materials belong to the model this load is about to produce, not to the page: anything
            // already on screen keeps drawing with the materials it was spawned with, and only loses them when
            // that model itself is cleared.
            var resolvedMaterials = new Dictionary<string, GameMaterial>(StringComparer.Ordinal);
            if (withMaterials)
            {
                var paths = new List<string>(meshes.Length);
                foreach (var mesh in meshes)
                    paths.Add(mesh.MaterialPath);

                var resolved = await GameMaterialLoader.LoadForModelAsync(modelPath, paths, requestedVariant).ConfigureAwait(false);
                foreach (var pair in resolved)
                    resolvedMaterials[pair.Key] = pair.Value;
            }

            pendingMaterials = resolvedMaterials;
            loadedFrom = modelPath;
            loaded = meshes;
        }
        catch (Exception ex)
        {
            failed = true;
            status = ex.Message;
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>
    /// Spawns whatever the last load produced. Done from the draw loop rather than the load continuation so the scene is
    /// only ever touched from one thread, and so a load that finishes after this page is disposed has nothing to spawn into.
    /// </summary>
    private void ConsumeLoaded()
    {
        if (loaded is null)
            return;

        var target = EnsureScene();
        if (target is null)
            return;

        var meshes = loaded;
        loaded = null; // consumed: a load spawns exactly once, however many models are already standing

        // Slots count up and never reuse a gap, so clearing one model does not slide the rest sideways.
        var model = new SpawnedModel
        {
            Meshes = meshes,
            Materials = pendingMaterials,
            Path = loadedFrom,
            Slot = nextSlot++,
        };

        pendingMaterials = new Dictionary<string, GameMaterial>(StringComparer.Ordinal);

        var origin = OriginFor(model.Slot);
        var textured = 0;

        foreach (var mesh in meshes)
        {
            var material = Flat();
            if (useGameMaterials && model.Materials.TryGetValue(mesh.MaterialPath, out var game))
            {
                material = Build(game);
                if (game.BaseColor is not null)
                    textured++;
            }

            var node = target.Spawn(mesh.Geometry, material, origin, "GameAsset", keepCpuData);
            node.MakeSelectable();
            model.Nodes.Add(node);
        }

        models.Add(model);

        // After the materials are built, not before: building one is what registers the pipeline, so recording
        // the state first would say the materials were built without it and trigger a pointless rebuild.
        MarkApplied();

        status = textured > 0
            ? $"Spawned {model.Nodes.Count} mesh(es) from '{model.Path}', {textured} with a game texture. {models.Count} model(s) on screen."
            : $"Spawned {model.Nodes.Count} mesh(es) from '{model.Path}' with the flat tint. {models.Count} model(s) on screen.";
    }

    /// <summary>
    /// Re-applies the material settings to nodes that are already on screen. Without this the toggles above
    /// would only take effect on the next load, which reads as them doing nothing at all.
    /// </summary>
    private void RefreshMaterialsIfChanged()
    {
        if (models.Count == 0)
            return;

        // Distance moves what is already on screen instead of waiting for a respawn, which is what made the
        // slider read as doing nothing.
        if (distance != appliedDistance)
        {
            appliedDistance = distance;
            foreach (var model in models)
            {
                var origin = OriginFor(model.Slot);
                foreach (var node in model.Nodes)
                {
                    if (!node.IsDestroyed)
                        node.LocalPosition = origin;
                }
            }
        }

        // The pipeline registering is a change in what a material would be built as, exactly like a setting
        // moving, so it belongs in this check. Without it the first model of a session keeps the plain lit
        // material it fell back to while the renderer had no device - no dye, no normal map, no specular -
        // and the only way out is to nudge some unrelated control, which repairs it as a side effect and
        // makes the whole thing read as the first spawn being special.
        if (shading == appliedShading && tint == appliedTint && dye == appliedDye && overrideDye == appliedOverrideDye
            && normalStrength == appliedNormalStrength && dyeReference == appliedDyeReference && ignoreSceneLight == appliedIgnoreSceneLight
            && specularStrength == appliedSpecularStrength && useGameMaterials == appliedUseGameMaterials
            && GameMaterialPipeline.Ready == appliedPipelineReady)
            return;

        MarkApplied();

        var flat = Flat();
        foreach (var model in models)
        {
            for (var i = 0; i < model.Nodes.Count && i < model.Meshes.Length; i++)
            {
                var node = model.Nodes[i];
                if (node.IsDestroyed || node.Renderer is null)
                    continue;

                node.Renderer!.Material = useGameMaterials && model.Materials.TryGetValue(model.Meshes[i].MaterialPath, out var game)
                    ? Build(game)
                    : flat;
            }
        }
    }

    /// <summary>Records the settings the nodes on screen were built with, so a later change is detected exactly once.</summary>
    private void MarkApplied()
    {
        appliedShading = shading;
        appliedTint = tint;
        appliedDye = dye;
        appliedOverrideDye = overrideDye;
        appliedNormalStrength = normalStrength;
        appliedDyeReference = dyeReference;
        appliedIgnoreSceneLight = ignoreSceneLight;
        appliedSpecularStrength = specularStrength;
        appliedUseGameMaterials = useGameMaterials;
        appliedDistance = distance;
        appliedPipelineReady = GameMaterialPipeline.Ready;
    }

    /// <summary>The material for a mesh whose own material could not be resolved.</summary>
    private Material Flat()
        => shading is Shading.Unlit or Shading.UnlitDiffuse ? Material.Unlit(tint) : Material.Lit(tint);

    private Material Build(GameMaterial game) => shading switch
    {
        Shading.Lit => game.ToLit(tint),
        Shading.LitDiffuse => game.ToLit(tint, applyDiffuseColor: true),
        Shading.Unlit => game.ToUnlit(tint),
        Shading.UnlitDiffuse => game.ToUnlit(tint, applyDiffuseColor: true),
        _ => game.ToGameShaded(overrideDye ? dye : null, tint, normalStrength, specularStrength, dyeReference, ignoreSceneLight),
    };

    private Scene3D? EnsureScene()
    {
        if (scene is { IsDisposed: false })
            return scene;

        scene = NoireDraw3D.CreateScene("Draw3DDemo.GameAssets");

        // Without an editor, MakeSelectable makes a node hit-testable and nothing more: the click lands, the
        // selection updates, and there is no gizmo reading it, so it looks like the click did nothing at all.
        // Owned by the scene, so it goes away with it.
        scene.CreateEditor();

        // Per frame, not per UI draw: the injection queue is rebuilt every frame, and a page only draws while
        // its window is open, so submitting from Draw would make a game-lit object vanish when the window is
        // closed.
        scene.OnPrepareFrame += _ => SubmitGameLit();
        return scene;
    }

    private void Clear()
    {
        foreach (var model in models)
            model.Dispose();

        models.Clear();
        nextSlot = 0;
        DisposePending();
        loaded = null;
        loadedFrom = string.Empty;
        status = string.Empty;
        failed = false;
    }

    /// <summary>Releases materials from a load that never reached a model, so an abandoned load leaks nothing.</summary>
    private void DisposePending()
    {
        foreach (var material in pendingMaterials.Values)
            material.Dispose();

        pendingMaterials.Clear();
    }

    /// <summary>Sideways gap between two spawned models, in world units.</summary>
    private const float SlotSpacing = 1.5f;

    /// <summary>
    /// Where a model in a given slot stands: in front of the player at the chosen distance, stepped sideways so
    /// a second model lands beside the first instead of inside it.
    /// </summary>
    private Vector3 OriginFor(int slot)
    {
        var forward = Forward();
        var right = new Vector3(forward.Z, 0f, -forward.X);
        return PlayerPos() + (forward * distance) + (right * (slot * SlotSpacing));
    }

    private static Vector3 PlayerPos() => NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;

    private static Vector3 Forward()
    {
        var rotation = NoireService.ObjectTable.LocalPlayer?.Rotation ?? 0f;
        return new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var target = scene;
        scene = null;
        target?.Dispose(); // destroys the nodes; the materials they drew with are released below

        foreach (var model in models)
            model.Dispose();

        models.Clear();
        DisposePending();
    }

    /// <summary>
    /// Submits the spawned meshes to the G-buffer injection, every frame it is switched on. Submitting is all
    /// that is needed: the renderer suppresses the node's own draw for that frame, so nothing has to be hidden
    /// and the nodes stay clickable.<br/>
    /// Driven from the scene's per-frame event rather than from <see cref="Draw"/>, because the injection queue
    /// is rebuilt every frame and a UI page only draws while its window is open - submitting from there makes
    /// the object vanish the moment the window is closed.
    /// </summary>
    public void SubmitGameLit()
    {
        if (!gameLit)
            return;

        foreach (var model in models)
        {
            foreach (var node in model.Nodes)
            {
                if (!node.IsDestroyed)
                    NoireDraw3D.DrawGameLit(node);
            }
        }
    }
}
