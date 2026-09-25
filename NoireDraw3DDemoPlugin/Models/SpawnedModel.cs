using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Scene;
using System.Collections.Generic;

namespace NoireDraw3DDemoPlugin.Models;

internal sealed class SpawnedModel
{
    // Every part's meshes, flattened in spawn order and index-aligned with Nodes.
    public required GameModelMesh[] Meshes { get; init; }

    // A scene places several models, a model file one.
    public required int PartCount { get; init; }

    // Keyed by material path, owned here and disposed with the model.
    public required Dictionary<string, GameMaterial> Materials { get; init; }

    public required string Path { get; init; }

    public required int Slot { get; init; }

    // The sgb's default stain, 0 when it states none.
    public required ushort DefaultStain { get; init; }

    // Parts read from a level file already stand at world positions.
    public required bool AtLevelPosition { get; init; }

    public List<SceneNode> Nodes { get; } = [];

    // Position and gizmo moves target this node.
    public SceneNode? Root { get; set; }

    // Joined: every part selects Root. Otherwise each part selects itself.
    public void SetJoined(bool joined)
    {
        foreach (var node in Nodes)
        {
            if (!node.IsDestroyed)
                node.SelectionProxy = joined ? Root : null;
        }
    }

    // Keeps the materials.
    public void DestroyNodes()
    {
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

    public void Dispose()
    {
        DestroyNodes();

        foreach (var material in Materials.Values)
            material.Dispose();

        Materials.Clear();
    }
}
