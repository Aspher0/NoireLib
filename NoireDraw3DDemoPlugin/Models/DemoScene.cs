using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Scene;
using System.Collections.Generic;

namespace NoireDraw3DDemoPlugin.Models;

internal sealed class DemoScene
{
    private readonly List<SceneNode> nodes = new();

    public DemoScene(Scene3D scene, bool owned, string label)
    {
        Scene = scene;
        Owned = owned;
        Label = label;
    }

    public Scene3D Scene { get; }

    // False for the permanent main scene.
    public bool Owned { get; }

    public string Label { get; }

    // Created lazily.
    public SceneEditor? Editor { get; set; }

    public IReadOnlyList<SceneNode> Nodes => nodes;

    public InteractSelection Selection => Scene.Selection;

    public SceneNode Track(SceneNode node)
    {
        nodes.Add(node);
        return node;
    }

    public SceneEditor EnsureEditor(GizmoOp op = GizmoOp.Universal)
    {
        if (Editor is { IsDisposed: false })
            return Editor;

        Editor = Scene.CreateEditor(op);
        Editor.MultiSelect = true;
        Editor.SelectionOutline = new System.Numerics.Vector4(1f, 0.85f, 0.2f, 1f);
        return Editor;
    }

    // Called each frame before the object list is drawn.
    public void PruneDestroyed()
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i].IsDestroyed)
                nodes.RemoveAt(i);
        }
    }

    public void DestroyNode(SceneNode node)
    {
        nodes.Remove(node);
        node.Destroy();
    }

    // Disposes the scene when owned. Otherwise releases its editor and spawned nodes.
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
