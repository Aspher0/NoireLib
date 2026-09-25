using NoireDraw3DDemoPlugin.Helpers;
using NoireDraw3DDemoPlugin.Models;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Services;

internal sealed class GameAssetService : IDisposable
{
    public enum Shading
    {
        // Opaque and lit, the dye confined to the color map's dyeable alpha, like the game.
        Game,

        // Base color texture untouched.
        Lit,

        // Diffuse constant multiplied over every pixel.
        LitDiffuse,

        // The texture's own colors.
        Unlit,

        // Diffuse constant over every pixel.
        UnlitDiffuse,
    }

    private const float SlotSpacing = 1.5f;

    private readonly List<SpawnedModel> models = [];

    public IReadOnlyList<SpawnedModel> Models => models;

    private int nextSlot;

    // Draws into the game's G-buffer, lit and occluded like the game's own geometry.
    public bool GameLit { get; set; }

    private Dictionary<string, GameMaterial> pendingMaterials = new(StringComparer.Ordinal);

    private Scene3D? scene;

    public string ModelPath { get; set; } = "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl";

    // Empty loads every layer.
    public string LayerFilter { get; set; } = string.Empty;
    public int Lod { get; set; }
    public int Variant { get; set; } = 1;
    public bool UseGameMaterials { get; set; } = true;
    public Shading Shade { get; set; } = Shading.Game;
    public bool OverrideDye { get; set; }
    public Vector3 Dye { get; set; } = new(0.82f, 0.68f, 0.45f);
    public bool IgnoreSceneLight { get; set; }
    public float DyeReference { get; set; }
    public float NormalStrength { get; set; } = 1f;
    public float SpecularStrength { get; set; }
    public bool ImportVertexColors { get; set; }
    public bool KeepCpuData { get; set; } = true;

    public bool UnjoinMeshes { get; set; }

    private bool appliedUnjoinMeshes;
    public Vector4 Tint { get; set; } = Vector4.One;
    public float Distance { get; set; } = 4f;
    public string Status { get; set; } = string.Empty;
    public bool Failed { get; private set; }
    public bool Loading { get; private set; }
    private GameScenePart[]? loaded;

    // Set off the draw thread.
    private ushort pendingStain;

    private bool pendingAtLevelPosition;
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

    private bool appliedPipelineReady;

    private string loadedFrom = string.Empty;

    public void Update()
    {
        ConsumeLoaded();
        RefreshMaterialsIfChanged();
    }

    public void BeginLoad()
    {
        Loading = true;
        Failed = false;
        Status = string.Empty;

        var requestedPath = ModelPath;
        var requestedLod = Lod;
        var requestedVariant = Variant;
        var colors = ImportVertexColors;
        var withMaterials = UseGameMaterials;
        var requestedLayers = LayerFilter.Trim();

        _ = LoadAsync(requestedPath, requestedLayers, requestedLod, requestedVariant, colors, withMaterials);
    }

    private async System.Threading.Tasks.Task LoadAsync(
        string modelPath, string layerFragment, int requestedLod, int requestedVariant, bool colors, bool withMaterials)
    {
        try
        {
            GameScenePart[] parts;
            ushort defaultStain;
            var isLevel = modelPath.EndsWith(".lgb", StringComparison.OrdinalIgnoreCase);
            if (isLevel)
            {
                Func<LayerGroupLayer, bool>? filter = layerFragment.Length == 0
                    ? null
                    : layer => layer.Name.Contains(layerFragment, StringComparison.OrdinalIgnoreCase);
                parts = await GameSceneLoader.LoadAsync(modelPath, filter, requestedLod, colors).ConfigureAwait(false);
                defaultStain = 0;
            }
            else if (modelPath.EndsWith(".sgb", StringComparison.OrdinalIgnoreCase))
            {
                parts = await GameSceneLoader.LoadAsync(modelPath, requestedLod, colors).ConfigureAwait(false);
                defaultStain = StainHelper.DefaultStainForScene(modelPath) ?? 0;
            }
            else
            {
                var meshes = await GameModelLoader.LoadAsync(modelPath, requestedLod, colors).ConfigureAwait(false);
                parts = meshes.Length == 0
                    ? []
                    : [new GameScenePart(meshes, modelPath, Vector3.Zero, Quaternion.Identity, Vector3.One)];
                defaultStain = StainHelper.DefaultStainForModel(modelPath) ?? 0;
            }

            if (parts.Length == 0)
            {
                Failed = true;
                Status = $"Nothing decoded from '{modelPath}'. The path may not exist, or it has no geometry at level {requestedLod}.";
                return;
            }

            var resolvedMaterials = new Dictionary<string, GameMaterial>(StringComparer.Ordinal);
            if (withMaterials)
            {
                var resolvedModels = new HashSet<string>(StringComparer.Ordinal);
                foreach (var part in parts)
                {
                    if (!resolvedModels.Add(part.ModelPath))
                        continue;

                    var paths = new List<string>(part.Meshes.Length);
                    foreach (var mesh in part.Meshes)
                        paths.Add(mesh.MaterialPath);

                    var resolved = await GameMaterialLoader.LoadForModelAsync(part.ModelPath, paths, requestedVariant).ConfigureAwait(false);
                    foreach (var pair in resolved)
                    {
                        if (!resolvedMaterials.TryAdd(pair.Key, pair.Value))
                            pair.Value.Dispose(); // duplicate material
                    }
                }
            }

            pendingMaterials = resolvedMaterials;
            pendingStain = defaultStain;
            pendingAtLevelPosition = isLevel;
            loadedFrom = modelPath;
            loaded = parts;
        }
        catch (Exception ex)
        {
            Failed = true;
            Status = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    private void ConsumeLoaded()
    {
        if (loaded is null)
            return;

        var target = EnsureScene();
        if (target is null)
            return;

        var parts = loaded;
        loaded = null;

        var flattened = new List<GameModelMesh>();
        foreach (var part in parts)
            flattened.AddRange(part.Meshes);

        var model = new SpawnedModel
        {
            Meshes = flattened.ToArray(),
            PartCount = parts.Length,
            Materials = pendingMaterials,
            Path = loadedFrom,
            Slot = nextSlot++,
            DefaultStain = pendingStain,
            AtLevelPosition = pendingAtLevelPosition,
        };

        pendingMaterials = new Dictionary<string, GameMaterial>(StringComparer.Ordinal);

        var origin = model.AtLevelPosition ? Vector3.Zero : OriginFor(model.Slot);
        var textured = 0;

        model.Root = target.CreateNode($"GameAsset '{model.Path}'");
        model.Root.LocalPosition = origin;

        foreach (var part in parts)
        {
            var partNode = target.CreateNode($"GameAssetPart '{part.ModelPath}'");
            partNode.SetParent(model.Root);
            partNode.LocalPosition = part.Translation;
            partNode.LocalRotation = part.Rotation;
            partNode.LocalScale = part.Scale;

            foreach (var mesh in part.Meshes)
            {
                var material = Flat();
                if (UseGameMaterials && model.Materials.TryGetValue(mesh.MaterialPath, out var game))
                {
                    material = Build(game, model.DefaultStain);
                    if (game.BaseColor is not null)
                        textured++;
                }

                var node = target.Spawn(mesh.Geometry, material, default, "GameAsset", KeepCpuData);
                node.SetParent(partNode);
                node.MakeSelectable();
                model.Nodes.Add(node);
            }
        }

        model.SetJoined(!UnjoinMeshes);
        models.Add(model);

        MarkApplied();

        var partsSuffix = model.PartCount > 1 ? $" in {model.PartCount} parts" : string.Empty;
        Status = textured > 0
            ? $"Spawned {model.Nodes.Count} mesh(es){partsSuffix} from '{model.Path}', {textured} with a game texture. {models.Count} model(s) on screen."
            : $"Spawned {model.Nodes.Count} mesh(es){partsSuffix} from '{model.Path}' with the flat tint. {models.Count} model(s) on screen.";
    }

    private void RefreshMaterialsIfChanged()
    {
        if (models.Count == 0)
            return;

        if (Distance != appliedDistance)
        {
            appliedDistance = Distance;
            foreach (var model in models)
            {
                if (model.AtLevelPosition)
                    continue;

                var origin = OriginFor(model.Slot);
                if (model.Root is { IsDestroyed: false })
                    model.Root.LocalPosition = origin;
            }
        }

        if (UnjoinMeshes != appliedUnjoinMeshes)
        {
            appliedUnjoinMeshes = UnjoinMeshes;
            foreach (var model in models)
                model.SetJoined(!UnjoinMeshes);
        }

        if (Shade == appliedShading && Tint == appliedTint && Dye == appliedDye && OverrideDye == appliedOverrideDye
            && NormalStrength == appliedNormalStrength && DyeReference == appliedDyeReference && IgnoreSceneLight == appliedIgnoreSceneLight
            && SpecularStrength == appliedSpecularStrength && UseGameMaterials == appliedUseGameMaterials
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

                node.Renderer!.Material = UseGameMaterials && model.Materials.TryGetValue(model.Meshes[i].MaterialPath, out var game)
                    ? Build(game, model.DefaultStain)
                    : flat;
            }
        }
    }

    private void MarkApplied()
    {
        appliedShading = Shade;
        appliedTint = Tint;
        appliedDye = Dye;
        appliedOverrideDye = OverrideDye;
        appliedNormalStrength = NormalStrength;
        appliedDyeReference = DyeReference;
        appliedIgnoreSceneLight = IgnoreSceneLight;
        appliedSpecularStrength = SpecularStrength;
        appliedUseGameMaterials = UseGameMaterials;
        appliedDistance = Distance;
        appliedPipelineReady = GameMaterialPipeline.Ready;
    }

    private Material Flat()
        => Shade is Shading.Unlit or Shading.UnlitDiffuse ? Material.Unlit(Tint) : Material.Lit(Tint);

    private Material Build(GameMaterial game, ushort defaultStain) => Shade switch
    {
        Shading.Lit => game.ToLit(Tint),
        Shading.LitDiffuse => game.ToLit(Tint, applyDiffuseColor: true),
        Shading.Unlit => game.ToUnlit(Tint),
        Shading.UnlitDiffuse => game.ToUnlit(Tint, applyDiffuseColor: true),
        _ => game.ToGameShaded(EffectiveDye(defaultStain), Tint, NormalStrength, SpecularStrength, DyeReference, IgnoreSceneLight),
    };

    private Vector3? EffectiveDye(ushort defaultStain)
    {
        if (OverrideDye)
            return Dye;

        return defaultStain > 0 ? StainHelper.ColorOf(defaultStain) : null;
    }

    private Scene3D? EnsureScene()
    {
        if (scene is { IsDisposed: false })
            return scene;

        scene = NoireDraw3D.CreateScene("Draw3DDemo.GameAssets");

        // MakeSelectable clicks need an editor.
        scene.CreateEditor();

        scene.OnPrepareFrame += _ => SubmitGameLit();
        return scene;
    }

    public void Clear()
    {
        foreach (var model in models)
            model.Dispose();

        models.Clear();
        nextSlot = 0;
        DisposePending();
        loaded = null;
        loadedFrom = string.Empty;
        Status = string.Empty;
        Failed = false;
    }

    private void DisposePending()
    {
        foreach (var material in pendingMaterials.Values)
            material.Dispose();

        pendingMaterials.Clear();
    }

    private Vector3 OriginFor(int slot)
    {
        var forward = DemoPlayerHelper.Forward();
        var right = new Vector3(forward.Z, 0f, -forward.X);
        return DemoPlayerHelper.Position() + (forward * Distance) + (right * (slot * SlotSpacing));
    }

    public void Dispose()
    {
        var target = scene;
        scene = null;
        target?.Dispose();

        foreach (var model in models)
            model.Dispose();

        models.Clear();
        DisposePending();
    }

    // Every frame while GameLit is on.
    public void SubmitGameLit()
    {
        if (!GameLit)
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
