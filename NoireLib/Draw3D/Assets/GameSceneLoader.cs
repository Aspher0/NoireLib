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
/// Loads a scene definition (<c>.sgb</c>) out of the game archives and decodes every model it places.<br/>
/// Furniture is stored as a scene: the file the game spawns for an item is its sgb, which places one or
/// more models with transforms and may nest further scenes (planters, exterior sets). Loading the scene
/// rather than a single model is what makes a multi-part item appear whole.<br/>
/// Like <see cref="GameModelLoader"/>, only files are read: nothing is spawned in the game itself.
/// </summary>
public static class GameSceneLoader
{
    /// <summary>How deep nested scenes are followed. The archives nest one level; the cap only breaks reference cycles.</summary>
    private const int MaxDepth = 4;

    /// <summary>Loads a scene and decodes every model it places, including models placed by nested scenes.</summary>
    /// <param name="sgbPath">Archive path of the scene, such as <c>bgcommon/hou/indoor/general/0001/asset/fun_b0_m0001.sgb</c>.</param>
    /// <param name="lod">Level of detail to decode for each model, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Apply each model's vertex color channel. Off by default (see <see cref="GameModelLoader"/>).</param>
    /// <returns>One entry per placed model, or an empty array if the scene does not exist.</returns>
    public static GameScenePart[] Load(string sgbPath, int lod = 0, bool importVertexColors = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sgbPath);

        var scene = NoireService.DataManager.GetFile<GameSgbFile>(sgbPath);
        if (scene is null)
            return [];

        var parts = new List<GameScenePart>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { sgbPath };
        Collect(scene, Matrix4x4.Identity, lod, importVertexColors, parts, visited, depth: 0);
        return parts.ToArray();
    }

    /// <inheritdoc cref="Load"/>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<GameScenePart[]> LoadAsync(string sgbPath, int lod = 0, bool importVertexColors = false, CancellationToken ct = default)
        => Task.Run(() => Load(sgbPath, lod, importVertexColors), ct);

    /// <summary>The scene's default stain, or null when no readable scene sits at the path.</summary>
    /// <param name="sgbPath">Archive path of the scene.</param>
    public static ushort? DefaultStain(string sgbPath)
    {
        if (string.IsNullOrWhiteSpace(sgbPath))
            return null;

        try
        {
            return NoireService.DataManager.GetFile<GameSgbFile>(sgbPath)?.DefaultStain;
        }
        catch (InvalidOperationException)
        {
            return null; // a scene this layout cannot read states nothing
        }
    }

    /// <summary>The default stain of the scene placed beside a background model, resolved from the model's path.</summary>
    /// <param name="modelGamePath">The model's archive path, under <c>bgcommon/</c>.</param>
    public static ushort? DefaultStainForModel(string modelGamePath)
        => GameSgbFile.PathBesideModel(modelGamePath) is { } sgbPath ? DefaultStain(sgbPath) : null;

    private static void Collect(
        GameSgbFile scene,
        in Matrix4x4 parent,
        int lod,
        bool importVertexColors,
        List<GameScenePart> parts,
        HashSet<string> visited,
        int depth)
    {
        foreach (var placement in scene.Models)
        {
            var world = Compose(placement) * parent;
            if (!Matrix4x4.Decompose(world, out var scale, out var rotation, out var translation))
                continue; // a degenerate transform has nothing visible to place

            var meshes = GameModelLoader.Load(placement.Path, lod, importVertexColors);
            if (meshes.Length > 0)
                parts.Add(new GameScenePart(meshes, placement.Path, translation, rotation, scale));
        }

        if (depth >= MaxDepth)
            return;

        foreach (var placement in scene.Attachments)
        {
            if (!visited.Add(placement.Path))
                continue;

            var nested = NoireService.DataManager.GetFile<GameSgbFile>(placement.Path);
            if (nested is not null)
                Collect(nested, Compose(placement) * parent, lod, importVertexColors, parts, visited, depth + 1);
        }
    }

    /// <summary>A placement's local matrix: scale, then rotation X-Y-Z, then translation.</summary>
    private static Matrix4x4 Compose(in GameSgbPlacement placement) =>
        Matrix4x4.CreateScale(placement.Scale)
        * Matrix4x4.CreateRotationX(placement.Rotation.X)
        * Matrix4x4.CreateRotationY(placement.Rotation.Y)
        * Matrix4x4.CreateRotationZ(placement.Rotation.Z)
        * Matrix4x4.CreateTranslation(placement.Translation);
}
