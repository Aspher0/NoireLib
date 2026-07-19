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

    private readonly List<SceneNode> spawned = [];
    private readonly Dictionary<string, GameMaterial> materials = new(StringComparer.Ordinal);

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
                "Off leaves the dyeable area at the near-neutral color it was authored with, which is the closest match to an unstained item available so far. On applies the color below, standing in for a stain. The material's own diffuse constant was tried as the undyed color and came out far too dark against the game, so it is not used.");
            DrawStainPicker();
            Ui.Color3("Dye color", () => dye, v => dye = v,
                "What the dyeable area is tinted with when the toggle above is on; Game shading uses it and the other modes ignore it. Picking a dye above sets this to that dye's exact color.");
            Ui.Toggle("Ignore this renderer's light", () => ignoreSceneLight, v => ignoreSceneLight = v,
                "Removes this renderer's lighting from Game shading, leaving the surface at the colors its texture and dye give it. This is the absence of our light, not the presence of the game's: use it to judge a color question without lighting confusing the comparison, since the two are impossible to tell apart by eye otherwise.");
            Ui.Slider("Dye reference", () => dyeReference, v => dyeReference = v, 0f, 1f,
                "How the dye meets the dyeable area. 0 multiplies the authored color by the dye, so a light dye darkens a light surface. Above 0 divides the area by that value first, so an area authored at it lands on the dye exactly and the texture carries only shading. 0.78 is the measured average of those texels. Which one the game does is unresolved: dye the real item a strong color, match it here, and the two readings are far enough apart to tell at a glance.");
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

        using (Ui.Disabled(spawned.Count == 0))
        {
            if (Ui.IconButton(FontAwesomeIcon.Trash, "Clear"))
                Clear();
        }
    }

    private void DrawDecoded()
    {
        if (loaded is null || loaded.Length == 0)
            return;

        Ui.Section("What was decoded");

        var vertices = 0;
        var indices = 0;
        foreach (var mesh in loaded)
        {
            vertices += mesh.Geometry.Vertices.Length;
            indices += mesh.Geometry.Indices.Length;
        }

        Ui.Mono($"{loaded.Length} mesh(es)   {vertices} vertices   {indices / 3} triangles");
        Ui.Gap();

        for (var i = 0; i < loaded.Length; i++)
        {
            var raw = loaded[i].MaterialPath;
            materials.TryGetValue(raw, out var material);

            var detail = material is null
                ? raw.Length > 0 ? $"{raw}  (not loaded)" : "(no material)"
                : $"{material.File.ShaderPackage}  maps [{Maps(material)}]"
                  + (material.DiffuseColor is { } d ? $"  diffuse {d.X:F2},{d.Y:F2},{d.Z:F2}" : string.Empty)
                  + (material.File.HasColorTable ? $"  color table {material.File.ColorTable.Length}B" : string.Empty);

            Ui.Mono($"[{i}] {loaded[i].Geometry.Vertices.Length,6} verts   {detail}", ImGuiColors.DalamudGrey3);
        }

        if (materials.Count > 0)
        {
            Ui.Gap();
            Ui.Note("Color, normal and specular maps are read and shaded. The character color table is parsed but not applied, and nothing here casts or receives a shadow, which is the largest remaining difference from the game's own look.");
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

            if (withMaterials)
            {
                var paths = new List<string>(meshes.Length);
                foreach (var mesh in meshes)
                    paths.Add(mesh.MaterialPath);

                var resolved = await GameMaterialLoader.LoadForModelAsync(modelPath, paths, requestedVariant).ConfigureAwait(false);
                DisposeMaterials();
                foreach (var pair in resolved)
                    materials[pair.Key] = pair.Value;
            }
            else
            {
                DisposeMaterials();
            }

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
        if (loaded is null || spawned.Count > 0)
            return;

        var target = EnsureScene();
        if (target is null)
            return;

        var origin = PlayerPos() + (Forward() * distance);
        var textured = 0;

        MarkApplied();

        foreach (var mesh in loaded)
        {
            var material = Flat();
            if (materials.TryGetValue(mesh.MaterialPath, out var game))
            {
                material = Build(game);
                if (game.BaseColor is not null)
                    textured++;
            }

            var node = target.Spawn(mesh.Geometry, material, origin, "GameAsset", keepCpuData);
            node.MakeSelectable();
            spawned.Add(node);
        }

        status = textured > 0
            ? $"Spawned {spawned.Count} mesh(es) from '{loadedFrom}', {textured} with a game texture."
            : $"Spawned {spawned.Count} mesh(es) from '{loadedFrom}' with the flat tint.";
    }

    /// <summary>
    /// Re-applies the material settings to nodes that are already on screen. Without this the toggles above
    /// would only take effect on the next load, which reads as them doing nothing at all.
    /// </summary>
    private void RefreshMaterialsIfChanged()
    {
        if (loaded is null || spawned.Count == 0)
            return;

        if (shading == appliedShading && tint == appliedTint && dye == appliedDye && overrideDye == appliedOverrideDye
            && normalStrength == appliedNormalStrength && dyeReference == appliedDyeReference && ignoreSceneLight == appliedIgnoreSceneLight
            && specularStrength == appliedSpecularStrength)
            return;

        MarkApplied();

        var flat = Flat();
        for (var i = 0; i < spawned.Count && i < loaded.Length; i++)
        {
            if (spawned[i].IsDestroyed || spawned[i].Renderer is null)
                continue;

            spawned[i].Renderer!.Material = materials.TryGetValue(loaded[i].MaterialPath, out var game)
                ? Build(game)
                : flat;
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
        return scene;
    }

    private void Clear()
    {
        foreach (var node in spawned)
        {
            if (!node.IsDestroyed)
                node.Destroy();
        }

        spawned.Clear();
        DisposeMaterials();
        loaded = null;
        loadedFrom = string.Empty;
        status = string.Empty;
        failed = false;
    }

    private void DisposeMaterials()
    {
        foreach (var material in materials.Values)
            material.Dispose();

        materials.Clear();
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
        target?.Dispose();
        spawned.Clear();
        DisposeMaterials();
    }
}
