using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using NoireLib.UI;
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

        /// <summary>The sgb's default stain for this furniture, 0 when it states none.</summary>
        public required ushort DefaultStain { get; init; }

        /// <summary>The nodes on screen, one per mesh, all children of <see cref="Root"/>.</summary>
        public List<SceneNode> Nodes { get; } = [];

        /// <summary>
        /// The group node the meshes hang under. Position and gizmo moves target this, so the model moves as
        /// one object regardless of how many meshes the file decoded into.
        /// </summary>
        public SceneNode? Root { get; set; }

        /// <summary>
        /// Points every part's selection at <see cref="Root"/>, or back at itself. Joined is how the game
        /// treats the object - one click selects all of it; unjoined exposes the individual meshes, which is
        /// what inspecting a single part wants.
        /// </summary>
        public void SetJoined(bool joined)
        {
            foreach (var node in Nodes)
            {
                if (!node.IsDestroyed)
                    node.SelectionProxy = joined ? Root : null;
            }
        }

        /// <summary>Destroys the nodes and leaves the materials alone, so the model can be rebuilt from the same decoded data.</summary>
        public void DestroyNodes()
        {
            // Destroying the root takes the children with it; the loop covers a node that was reparented out.
            if (Root is { IsDestroyed: false })
                Root.Destroy();

            foreach (var node in Nodes)
            {
                if (!node.IsDestroyed)
                    node.Destroy();
            }

            Nodes.Clear();
            Root = null;
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

    /// <summary>The materials of the load that has finished but not yet been spawned. Handed to the model it produces.</summary>
    private Dictionary<string, GameMaterial> pendingMaterials = new(StringComparer.Ordinal);

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
    private NoireComboBox<GameStain>? stainCombo;
    private bool ignoreSceneLight;
    private float dyeReference;
    private float normalStrength = 1f;
    private float specularStrength;
    private bool importVertexColors;
    private bool keepCpuData = true;

    /// <summary>Off spawns each model as one selectable object; on exposes its individual meshes to the click.</summary>
    private bool unjoinMeshes;

    /// <summary>What <see cref="unjoinMeshes"/> was when the proxies were last pointed, so a toggle applies exactly once.</summary>
    private bool appliedUnjoinMeshes;
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
        Ui.Note("Loads a model and its materials from the game archives and draws it. Only files are read: no actor, no object table entry, nothing the game or its server knows about.");
        Ui.Gap();

        DrawPresets();
        Ui.Gap();

        // The fallback draws the texture but drops the dye, normal map and specular, so it looks like a duller
        // material rather than like a failure - worth a visible banner while it lasts.
        if (!GameMaterialPipeline.Ready && GameMaterialPipeline.Unavailable is { } why)
            Ui.Callout($"Game materials are temporarily drawing without dye, normal or specular maps: {why}", ImGuiColors.DalamudOrange);

        using (Ui.Form("assets.load"))
        {
            Ui.Text("Game path", () => path, v => path = v, DefaultPath, 512,
                "Archive path of the model. bgcommon/ paths are props and furniture; chara/ paths are character parts.");
            Ui.Int("Level of detail", () => lod, v => lod = Math.Clamp(v, 0, 2),
                "0 is the most detailed. Models carry up to three levels.");
            Ui.Toggle("Use game materials", () => useGameMaterials, v => useGameMaterials = v,
                "Resolves each piece's material and textures. Off draws everything with the flat tint below.");
            Ui.Enum<Shading>("Shading", () => shading, v => shading = v,
                "Game matches the game's shading. Diffuse variants multiply the diffuse constant everywhere. Unlit draws the raw texture.");
            Ui.Toggle("Apply a dye color", () => overrideDye, v => overrideDye = v,
                "Off renders the item as an undyed placement shows in game. On applies the color below.");
            DrawStainPicker();
            Ui.Color3("Dye color", () => dye, v => dye = v,
                "Applied to the dyeable area while the toggle above is on. Picking a dye above sets this to its exact color.");
            Ui.Toggle("Light with the game's lights", () => gameLit, v => gameLit = v,
                "Draws into the game's own frame so its lighting, shadows and occlusion apply. The object loses outlines, fades and above-everything while this is on.");
            Ui.Toggle("Ignore this renderer's light", () => ignoreSceneLight, v => ignoreSceneLight = v,
                "Removes this renderer's lighting from Game shading, leaving the texture and dye colors untouched.");
            Ui.Slider("Dye reference", () => dyeReference, v => dyeReference = v, 0f, 1f,
                "0 multiplies the authored color by the dye, matching the game. Above 0 divides the area by that value first, so an area authored at it lands on the dye exactly.");
            Ui.Slider("Normal strength", () => normalStrength, v => normalStrength = v, 0f, 2f,
                "How far the normal map bends the surface normal. 1 is the authored strength; 0 disables the map.");
            Ui.Slider("Specular strength", () => specularStrength, v => specularStrength = v, 0f, 2f,
                "Highlight strength from the specular map. 0 is matte, matching the game's furniture.");
            Ui.Int("Material variant", () => variant, v => variant = Math.Max(0, v),
                "Character materials resolve against a numbered variant folder. Background models ignore this.");
            Ui.Toggle("Import vertex colors", () => importVertexColors, v => importVertexColors = v,
                "Off by default: the game stores shader masks in this channel rather than color.");
            Ui.Toggle("Keep CPU data", () => keepCpuData, v => keepCpuData = v,
                "Retains the decoded geometry so exact per-triangle picking works on the spawned model.");
            Ui.Toggle("Unjoin meshes", () => unjoinMeshes, v => unjoinMeshes = v,
                "Off selects and moves each model as one object. On exposes its individual meshes. Applies live.");
            Ui.Color4("Tint", () => tint, v => tint = v,
                "Multiplied over the texture. White leaves it untouched.");
            Ui.Slider("Distance", () => distance, v => distance = v, 1f, 20f,
                "How far in front of you the model is placed.");
        }

        if (shading == Shading.Game && !GameMaterialPipeline.EnsureRegistered())
        {
            Ui.Gap();
            Ui.Callout(
                $"The mask shader is unavailable: {GameMaterialPipeline.Unavailable} Dye colors have no effect until it loads.",
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

    /// <summary>Picks one of the game's housing dyes and sets the dye color to its exact table value.</summary>
    private void DrawStainPicker()
    {
        if (stainCombo is null)
        {
            var all = new List<GameStain>(StainHelper.All(housingOnly: true));
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            stainCombo = new NoireComboBox<GameStain>("assets.stain", all,
                stain => stain.Name.Length > 0 ? stain.Name : $"Dye {stain.Id}");
        }

        Ui.Row("Dye", "The game's housing dyes. Picking one sets the color below to its exact value.");

        if (stainCombo.Draw() && stainCombo.SelectedItem is { Id: > 0 } picked)
        {
            dye = picked.Color;
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
        Ui.HelpMarker("Sample paths from a complete installation. Character entries load without skinning applied.");
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

        Ui.Mono($"{model.Meshes.Length} mesh(es)   {vertices} vertices   {indices / 3} triangles"
            + (model.DefaultStain > 0 ? $"   {DefaultStainName(model.DefaultStain)}" : string.Empty));
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
            Ui.Note("The diffuse constant is the dye the item ships pre-applied. Character color tables are parsed but not applied yet, and nothing casts a shadow.");
        }
    }

    /// <summary>Names the item's default stain, read from its sgb.</summary>
    private static string DefaultStainName(ushort stainId)
        => StainHelper.TryGet(stainId, out var stain) ? $"default dye '{stain.Name}' ({stain.Id})" : $"default dye {stainId}";

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
            DefaultStain = GameFurnitureSgb.DefaultStainFor(loadedFrom) ?? 0,
        };

        pendingMaterials = new Dictionary<string, GameMaterial>(StringComparer.Ordinal);

        var origin = OriginFor(model.Slot);
        var textured = 0;

        // One group node per model, holding the position; the meshes hang under it at local zero. The game
        // treats the object as one thing, so clicking any part selects and moves the whole - the group is
        // what the gizmo attaches to, and each part's SelectionProxy is what routes the click there.
        model.Root = target.CreateNode($"GameAsset '{model.Path}'");
        model.Root.LocalPosition = origin;

        foreach (var mesh in meshes)
        {
            var material = Flat();
            if (useGameMaterials && model.Materials.TryGetValue(mesh.MaterialPath, out var game))
            {
                material = Build(game, model.DefaultStain);
                if (game.BaseColor is not null)
                    textured++;
            }

            var node = target.Spawn(mesh.Geometry, material, default, "GameAsset", keepCpuData);
            node.SetParent(model.Root);
            node.MakeSelectable();
            model.Nodes.Add(node);
        }

        model.SetJoined(!unjoinMeshes);
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
                if (model.Root is { IsDestroyed: false })
                    model.Root.LocalPosition = origin;
            }
        }

        // Joining is a live property of what is already on screen, like distance: flipping the checkbox
        // repoints the selection proxies without a respawn.
        if (unjoinMeshes != appliedUnjoinMeshes)
        {
            appliedUnjoinMeshes = unjoinMeshes;
            foreach (var model in models)
                model.SetJoined(!unjoinMeshes);
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
                    ? Build(game, model.DefaultStain)
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

    private Material Build(GameMaterial game, ushort defaultStain) => shading switch
    {
        Shading.Lit => game.ToLit(tint),
        Shading.LitDiffuse => game.ToLit(tint, applyDiffuseColor: true),
        Shading.Unlit => game.ToUnlit(tint),
        Shading.UnlitDiffuse => game.ToUnlit(tint, applyDiffuseColor: true),
        _ => game.ToGameShaded(EffectiveDye(defaultStain), tint, normalStrength, specularStrength, dyeReference, ignoreSceneLight),
    };

    /// <summary>
    /// The dye a build applies: the picked color, or the item's own default stain so an untouched spawn
    /// matches an undyed placement in game. Null falls to the undyed fallback.
    /// </summary>
    private Vector3? EffectiveDye(ushort defaultStain)
    {
        if (overrideDye)
            return dye;

        return defaultStain > 0 ? StainHelper.ColorOf(defaultStain) : null;
    }

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
