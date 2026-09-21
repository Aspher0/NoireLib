using Lumina.Data.Parsing.Layer;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Draw3D.Assets;

/// <summary>One placed model of a loaded scene: its decoded meshes and where it stands, local to the scene's origin.</summary>
/// <param name="Meshes">The decoded meshes, one entry per mesh of the model's chosen level of detail.</param>
/// <param name="ModelPath">The model file this part came from.</param>
/// <param name="Translation">Scene-local translation.</param>
/// <param name="Rotation">Scene-local rotation.</param>
/// <param name="Scale">Scene-local scale.</param>
public readonly record struct GameScenePart(
    GameModelMesh[] Meshes,
    string ModelPath,
    Vector3 Translation,
    Quaternion Rotation,
    Vector3 Scale);

/// <summary>
/// Loads an <c>.sgb</c> or <c>.lgb</c> from the game archives and decodes every model it places, nested groups included.<br/>
/// Furniture is stored as a scene.
/// </summary>
public static class GameSceneLoader
{
    // Furniture nests one level. The cap bounds a pathological file.
    private const int MaxDepth = 4;

    /// <summary>Loads a scene or level file and decodes every model it places, including models placed by nested scenes.</summary>
    /// <param name="path">Archive path of the <c>.sgb</c> or <c>.lgb</c>, such as <c>bgcommon/hou/indoor/general/0001/asset/fun_b0_m0001.sgb</c>.</param>
    /// <param name="lod">Level of detail to decode for each model, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Whether to apply each model's vertex color channel (see <see cref="GameModelLoader"/>).</param>
    /// <returns>One entry per placed model, or an empty array if the file does not exist.</returns>
    public static GameScenePart[] Load(string path, int lod = 0, bool importVertexColors = false)
        => Load(path, null, lod, importVertexColors);

    /// <summary>Loads the chosen layers of a scene or level file and decodes every model they place. A level file's parts stand at world positions.</summary>
    /// <param name="path">Archive path of the <c>.sgb</c> or <c>.lgb</c>.</param>
    /// <param name="layerFilter">Which of the file's own layers to load, null for all. Nested scenes are always loaded whole.</param>
    /// <param name="lod">Level of detail to decode for each model, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Whether to apply each model's vertex color channel.</param>
    /// <returns>One entry per placed model, or an empty array if the file does not exist.</returns>
    public static GameScenePart[] Load(string path, Func<LayerGroupLayer, bool>? layerFilter, int lod = 0, bool importVertexColors = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // A level file places the same model many times.
        var decoded = new Dictionary<string, GameModelMesh[]>(StringComparer.Ordinal);
        var parts = new List<GameScenePart>();
        foreach (var entry in LayerGroupHelper.Flatten(path, layerFilter, MaxDepth))
        {
            if (entry.Type != LayerEntryType.BG || entry.AssetPath.Length == 0)
                continue;

            if (!Matrix4x4.Decompose(entry.World, out var scale, out var rotation, out var translation))
                continue;

            if (!decoded.TryGetValue(entry.AssetPath, out var meshes))
                decoded[entry.AssetPath] = meshes = GameModelLoader.Load(entry.AssetPath, lod, importVertexColors);

            if (meshes.Length > 0)
                parts.Add(new GameScenePart(meshes, entry.AssetPath, translation, rotation, scale));
        }

        return parts.ToArray();
    }

    /// <summary>Loads a scene or level file and decodes every model it places off the calling thread.</summary>
    /// <param name="path">Archive path of the <c>.sgb</c> or <c>.lgb</c>.</param>
    /// <param name="lod">Level of detail to decode for each model, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Whether to apply each model's vertex color channel.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>One entry per placed model, or an empty array if the file does not exist.</returns>
    public static Task<GameScenePart[]> LoadAsync(string path, int lod = 0, bool importVertexColors = false, CancellationToken ct = default)
        => Task.Run(() => Load(path, null, lod, importVertexColors), ct);

    /// <summary>Loads the chosen layers of a scene or level file off the calling thread.</summary>
    /// <param name="path">Archive path of the <c>.sgb</c> or <c>.lgb</c>.</param>
    /// <param name="layerFilter">Which of the file's own layers to load, null for all.</param>
    /// <param name="lod">Level of detail to decode for each model, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Whether to apply each model's vertex color channel.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>One entry per placed model, or an empty array if the file does not exist.</returns>
    public static Task<GameScenePart[]> LoadAsync(
        string path,
        Func<LayerGroupLayer, bool>? layerFilter,
        int lod = 0,
        bool importVertexColors = false,
        CancellationToken ct = default)
        => Task.Run(() => Load(path, layerFilter, lod, importVertexColors), ct);
}
