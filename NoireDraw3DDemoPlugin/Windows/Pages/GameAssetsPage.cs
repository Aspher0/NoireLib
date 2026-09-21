using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using NoireDraw3DDemoPlugin.Services;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Assets;
using NoireLib.Helpers;
using NoireLib.UI;
using System;
using System.Collections.Generic;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

internal sealed class GameAssetsPage : IDisposable
{
    private const string DefaultPath = "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl";

    // A level file can decode into thousands of meshes.
    private const int MaxListedMeshes = 64;

    // Both vertex layouts, both material shader packages, and a multi-part scene.
    private static readonly (string Label, string Path)[] Presets =
    [
        ("Furniture", "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl"),
        ("Scene", "bgcommon/hou/indoor/general/0116/asset/fun_b0_m0116.sgb"),
        ("Body", "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl"),
        ("Equipment", "chara/equipment/e0001/model/c0101e0001_top.mdl"),
        ("Monster", "chara/monster/m0001/obj/body/b0001/model/m0001b0001.mdl"),
    ];

    private readonly GameAssetService assetService = new();
    private NoireComboBox<GameStain>? stainCombo;

    public void Draw()
    {
        assetService.Update();

        Ui.Section("Load a model");
        Ui.Note("Loads a model and its materials from the game archives and draws it. Only files are read. No actor, no object table entry, nothing the game or its server knows about.");
        Ui.Gap();

        DrawPresets();
        Ui.Gap();

        // The fallback drops dye, normal and specular.
        if (!GameMaterialPipeline.Ready && GameMaterialPipeline.Unavailable is { } why)
            Ui.Callout($"Game materials are temporarily drawing without dye, normal or specular maps: {why}", ImGuiColors.DalamudOrange);

        using (Ui.Form("assets.load"))
        {
            Ui.Text("Game path", () => assetService.ModelPath, v => assetService.ModelPath = v, DefaultPath, 512,
                "Archive path of a model (.mdl), a scene (.sgb) or a level file (.lgb). A scene spawns every model it places. Furniture items are scenes. A level file's models stand where the game places them.");
            Ui.Text("Layer filter", () => assetService.LayerFilter, v => assetService.LayerFilter = v, string.Empty, 128,
                "Level files only. Loads the layers whose name contains this text. Empty loads every layer, which can be thousands of models.");
            Ui.Int("Level of detail", () => assetService.Lod, v => assetService.Lod = Math.Clamp(v, 0, 2),
                "0 is the most detailed. Models carry up to three levels.");
            Ui.Toggle("Use game materials", () => assetService.UseGameMaterials, v => assetService.UseGameMaterials = v,
                "Resolves each piece's material and textures. Off draws everything with the flat tint below.");
            Ui.Enum<GameAssetService.Shading>("Shading", () => assetService.Shade, v => assetService.Shade = v,
                "Game matches the game's shading. Diffuse variants multiply the diffuse constant everywhere. Unlit draws the raw texture.");
            Ui.Toggle("Apply a dye color", () => assetService.OverrideDye, v => assetService.OverrideDye = v,
                "Off renders the item as an undyed placement shows in game. On applies the color below.");
            DrawStainPicker();
            Ui.Color3("Dye color", () => assetService.Dye, v => assetService.Dye = v,
                "Applied to the dyeable area while the toggle above is on. Picking a dye above sets this to its exact color.");
            Ui.Toggle("Light with the game's lights", () => assetService.GameLit, v => assetService.GameLit = v,
                "Draws into the game's own frame so its lighting, shadows and occlusion apply. The object loses outlines, fades and above-everything while this is on.");
            Ui.Toggle("Cast shadows", () => NoireDraw3D.GameLit.CastShadows, v => NoireDraw3D.GameLit.CastShadows = v,
                "Also draws the object into the game's shadow maps while the toggle above is on. Experimental.");
            Ui.Toggle("Ignore this renderer's light", () => assetService.IgnoreSceneLight, v => assetService.IgnoreSceneLight = v,
                "Removes this renderer's lighting from Game shading, leaving the texture and dye colors untouched.");
            Ui.Slider("Dye reference", () => assetService.DyeReference, v => assetService.DyeReference = v, 0f, 1f,
                "0 multiplies the authored color by the dye, matching the game. Above 0 divides the area by that value first. An area authored at it lands on the dye exactly.");
            Ui.Slider("Normal strength", () => assetService.NormalStrength, v => assetService.NormalStrength = v, 0f, 2f,
                "How far the normal map bends the surface normal. 1 is the authored strength; 0 disables the map.");
            Ui.Slider("Specular strength", () => assetService.SpecularStrength, v => assetService.SpecularStrength = v, 0f, 2f,
                "Highlight strength from the specular map. 0 is matte, matching the game's furniture.");
            Ui.Int("Material variant", () => assetService.Variant, v => assetService.Variant = Math.Max(0, v),
                "Character materials resolve against a numbered variant folder. Background models ignore this.");
            Ui.Toggle("Import vertex colors", () => assetService.ImportVertexColors, v => assetService.ImportVertexColors = v,
                "Off by default. The game uses this channel for shader masks.");
            Ui.Toggle("Keep CPU data", () => assetService.KeepCpuData, v => assetService.KeepCpuData = v,
                "Retains the decoded geometry so exact per-triangle picking works on the spawned model.");
            Ui.Toggle("Unjoin meshes", () => assetService.UnjoinMeshes, v => assetService.UnjoinMeshes = v,
                "Off selects and moves each model as one object. On exposes its individual meshes. Applies live.");
            Ui.Color4("Tint", () => assetService.Tint, v => assetService.Tint = v,
                "Multiplied over the texture. White leaves it untouched.");
            Ui.Slider("Distance", () => assetService.Distance, v => assetService.Distance = v, 1f, 20f,
                "How far in front of you the model is placed.");
        }

        if (assetService.Shade == GameAssetService.Shading.Game && !GameMaterialPipeline.EnsureRegistered())
        {
            Ui.Gap();
            Ui.Callout(
                $"The mask shader is unavailable: {GameMaterialPipeline.Unavailable} Dye colors have no effect until it loads.",
                ImGuiColors.DalamudOrange);
        }

        Ui.Gap();
        DrawActions();

        if (assetService.Status.Length > 0)
        {
            Ui.Gap();
            if (assetService.Failed)
                Ui.Callout(assetService.Status, ImGuiColors.DalamudRed);
            else
                Ui.Status(assetService.Status);
        }

        DrawDecoded();
    }

    private void DrawStainPicker()
    {
        if (stainCombo is null)
        {
            var all = new List<GameStain>(StainHelper.All(housingOnly: true));
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            stainCombo = new NoireComboBox<GameStain>("assets.stain", all,
                stain => stain.Name.Length > 0 ? stain.Name : $"Dye {stain.Id}");
            stainCombo.FilterEnabled = true;
            stainCombo.PreviewPlaceholder = "Pick a dye...";
        }

        Ui.Row("Dye", "The game's housing dyes. Picking one sets the color below to its exact value.");

        if (stainCombo.Draw() && stainCombo.SelectedItem is { Id: > 0 } picked)
        {
            assetService.Dye = picked.Color;
            assetService.OverrideDye = true;
        }
    }

    private void DrawPresets()
    {
        for (var i = 0; i < Presets.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            if (Ui.SmallButton(Presets[i].Label))
                assetService.ModelPath = Presets[i].Path;
        }

        ImGui.SameLine();
        Ui.HelpMarker("Sample paths from a complete installation. Character entries load without skinning applied.");
    }

    private void DrawActions()
    {
        using (Ui.Disabled(assetService.Loading || assetService.ModelPath.Length == 0))
        {
            if (Ui.IconButton(FontAwesomeIcon.Download, assetService.Loading ? "Loading..." : "Load and spawn"))
                assetService.BeginLoad();
        }

        ImGui.SameLine();

        using (Ui.Disabled(assetService.Models.Count == 0))
        {
            if (Ui.IconButton(FontAwesomeIcon.Trash, assetService.Models.Count > 1 ? $"Clear all {assetService.Models.Count}" : "Clear"))
                assetService.Clear();
        }
    }

    private void DrawDecoded()
    {
        var models = assetService.Models;
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

        Ui.Mono($"{model.Meshes.Length} mesh(es)"
            + (model.PartCount > 1 ? $" in {model.PartCount} parts" : string.Empty)
            + $"   {vertices} vertices   {indices / 3} triangles"
            + (model.DefaultStain > 0 ? $"   {DefaultStainName(model.DefaultStain)}" : string.Empty));
        Ui.Gap();

        var listed = Math.Min(model.Meshes.Length, MaxListedMeshes);
        for (var i = 0; i < listed; i++)
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

        if (model.Meshes.Length > listed)
            Ui.Note($"{model.Meshes.Length - listed} more meshes not listed.");

        if (model.Materials.Count > 0)
        {
            Ui.Gap();
            Ui.Note("Character color tables are parsed but not applied yet.");
        }
    }

    private static string DefaultStainName(ushort stainId)
        => StainHelper.TryGet(stainId, out var stain) ? $"default dye '{stain.Name}' ({stain.Id})" : $"default dye {stainId}";

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

    public void Dispose() => assetService.Dispose();
}
