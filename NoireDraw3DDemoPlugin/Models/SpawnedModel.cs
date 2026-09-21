using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Scene;
using System.Collections.Generic;

namespace NoireDraw3DDemoPlugin.Models;

internal sealed class SpawnedModel
{
    /// <summary>The decoded meshes of every part, flattened in spawn order and index-aligned with <see cref="Nodes"/>.</summary>
    public required GameModelMesh[] Meshes { get; init; }

    /// <summary>How many placed models the spawn decoded. A scene places several. A model file is one.</summary>
    public required int PartCount { get; init; }

    /// <summary>The materials this model resolved, keyed by material path. Owned here and disposed with it.</summary>
    public required Dictionary<string, GameMaterial> Materials { get; init; }

    public required string Path { get; init; }

    /// <summary>Which slot along the row this model stands in.</summary>
    public required int Slot { get; init; }

    /// <summary>The sgb's default stain for this furniture, 0 when it states none.</summary>
    public required ushort DefaultStain { get; init; }

    /// <summary>Whether the parts came from a level file and already stand at world positions.</summary>
    public required bool AtLevelPosition { get; init; }

    /// <summary>The nodes on screen, one per mesh, all children of <see cref="Root"/>.</summary>
    public List<SceneNode> Nodes { get; } = [];

    /// <summary>The group node the meshes hang under. Position and gizmo moves target this node.</summary>
    public SceneNode? Root { get; set; }

    /// <summary>Points every part's selection at <see cref="Root"/> when joined, or at itself when not.</summary>
    public void SetJoined(bool joined)
    {
        foreach (var node in Nodes)
        {
            if (!node.IsDestroyed)
                node.SelectionProxy = joined ? Root : null;
        }
    }

    /// <summary>Destroys the nodes without releasing the materials.</summary>
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

    /// <summary>Destroys the nodes, then releases their materials.</summary>
    public void Dispose()
    {
        DestroyNodes();

        foreach (var material in Materials.Values)
            material.Dispose();

        Materials.Clear();
    }
}
