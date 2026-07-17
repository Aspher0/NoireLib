using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Scene;
using System.Collections.Generic;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// One scene the Scenes tab manages: the <see cref="Scene3D"/> itself, the <see cref="SceneEditor"/> that drives its
/// gizmo, the demo-spawned nodes tracked for the object list, and whether this section owns the scene (demo scenes are
/// disposable; the permanent <c>MainScene</c> is not). The object list is the nodes this section spawned, since the
/// scene graph itself is not publicly enumerable.
/// </summary>
internal sealed class DemoScene
{
    private readonly List<SceneNode> nodes = new();

    public DemoScene(Scene3D scene, bool owned, string label)
    {
        Scene = scene;
        Owned = owned;
        Label = label;
    }

    /// <summary>The live scene.</summary>
    public Scene3D Scene { get; }

    /// <summary>Whether this section disposes the scene (false for the permanent main scene).</summary>
    public bool Owned { get; }

    /// <summary>Display name in the scene list.</summary>
    public string Label { get; }

    /// <summary>The editor (click-to-select + gizmo) for this scene, created lazily when the scene is opened.</summary>
    public SceneEditor? Editor { get; set; }

    /// <summary>The demo-spawned root nodes in this scene, in spawn order (the object list).</summary>
    public IReadOnlyList<SceneNode> Nodes => nodes;

    /// <summary>The scene's selection (what the gizmo follows).</summary>
    public InteractSelection Selection => Scene.Selection;

    /// <summary>Tracks a freshly spawned node for the object list. Returns it for fluent use.</summary>
    public SceneNode Track(SceneNode node)
    {
        nodes.Add(node);
        return node;
    }

    /// <summary>Ensures the scene has an editor, creating one on first use. Returns it.</summary>
    public SceneEditor EnsureEditor(GizmoOp op = GizmoOp.Universal)
    {
        if (Editor is { IsDisposed: false })
            return Editor;

        Editor = Scene.CreateEditor(op);
        Editor.MultiSelect = true;
        Editor.SelectionOutline = new System.Numerics.Vector4(1f, 0.85f, 0.2f, 1f);
        return Editor;
    }

    /// <summary>Drops destroyed nodes from the object list (called each frame before drawing it).</summary>
    public void PruneDestroyed()
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i].IsDestroyed)
                nodes.RemoveAt(i);
        }
    }

    /// <summary>Removes and destroys a tracked node.</summary>
    public void DestroyNode(SceneNode node)
    {
        nodes.Remove(node);
        node.Destroy();
    }

    /// <summary>
    /// Tears the scene down: an owned scene is disposed outright (freeing its nodes and editor); the permanent main
    /// scene keeps living, but the editor this section attached and the demo nodes it spawned are released.
    /// </summary>
    public void TearDown()
    {
        if (Owned)
        {
            Scene.Dispose();
            return;
        }

        Editor?.Dispose();
        Editor = null;
        foreach (var node in nodes.ToArray())
        {
            if (!node.IsDestroyed)
                node.Destroy();
        }

        nodes.Clear();
    }
}
