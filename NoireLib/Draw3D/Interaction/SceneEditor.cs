using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>A click-to-select controller whose <see cref="Gizmo"/> follows <see cref="Scene3D.Selection"/>. The scene owns and disposes it.</summary>
public sealed class SceneEditor : IDisposable
{
    private readonly Scene3D scene;
    private readonly SelectionMode originalMode;
    private readonly Action selectionChanged;
    private readonly List<SceneNode> outlined = new();
    private Vector4? selectionOutline;
    private bool disposed;

    internal SceneEditor(Scene3D scene, GizmoOp op)
    {
        this.scene = scene;
        originalMode = scene.Selection.Mode;
        Gizmo = new NoireGizmo(op);

        selectionChanged = OnSelectionChanged;
        scene.Selection.Changed += selectionChanged;
        OnSelectionChanged();
    }

    /// <summary>The gizmo the editor drives.</summary>
    public NoireGizmo Gizmo { get; }

    /// <summary>The selection the editor follows, the scene's own <see cref="Scene3D.Selection"/>.</summary>
    public InteractSelection Selection => scene.Selection;

    /// <summary>The scene this editor belongs to.</summary>
    public Scene3D Scene => scene;

    /// <summary>Whether picking builds a multi-node selection. The original selection mode is restored on <see cref="Dispose"/>.</summary>
    public bool MultiSelect
    {
        get => scene.Selection.Mode == SelectionMode.Multi;
        set => scene.Selection.Mode = value ? SelectionMode.Multi : SelectionMode.Single;
    }

    /// <summary>Whether the gizmo draws and interacts. The selection still tracks when false (default true).</summary>
    public bool Enabled
    {
        get => Gizmo.Enabled;
        set => Gizmo.Enabled = value;
    }

    /// <summary>Silhouette outline color applied to selected nodes and their subtrees, or null for none (the default).</summary>
    public Vector4? SelectionOutline
    {
        get => selectionOutline;
        set
        {
            selectionOutline = value;
            UpdateOutlines();
        }
    }

    /// <summary>Outline thickness in screen pixels for <see cref="SelectionOutline"/> (default 4). Set before enabling the outline.</summary>
    public float OutlineWidth { get; set; } = 4f;

    /// <summary>Whether the editor is disposed.</summary>
    public bool IsDisposed => disposed;

    private void OnSelectionChanged()
    {
        var nodes = scene.Selection.Nodes;
        if (nodes.Count == 0)
            Gizmo.Detach();
        else if (nodes.Count == 1)
            Gizmo.Attach(nodes[0]);
        else
            Gizmo.AttachGroup(nodes);

        UpdateOutlines();
    }

    private void UpdateOutlines()
    {
        // A group node draws nothing itself.
        var wanted = new List<SceneNode>();
        if (selectionOutline is not null)
        {
            foreach (var node in scene.Selection.Nodes)
                CollectSubtree(node, wanted);
        }

        for (var i = outlined.Count - 1; i >= 0; i--)
        {
            var node = outlined[i];
            if (node.Destroyed || !wanted.Contains(node))
            {
                if (!node.Destroyed)
                    node.HideOutline();
                outlined.RemoveAt(i);
            }
        }

        if (selectionOutline is { } color)
        {
            foreach (var node in wanted)
            {
                if (!outlined.Contains(node))
                {
                    node.ShowOutline(color, OutlineWidth);
                    outlined.Add(node);
                }
            }
        }
    }

    private static void CollectSubtree(SceneNode node, List<SceneNode> into)
    {
        if (node.Destroyed || into.Contains(node))
            return;

        into.Add(node);
        foreach (var child in node.Children)
            CollectSubtree(child, into);
    }

    /// <summary>Tears the editor down early, disposing the gizmo and restoring the selection mode. <see cref="Scene3D.Dispose"/> also does this.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        scene.Selection.Changed -= selectionChanged;

        foreach (var node in outlined)
        {
            if (!node.Destroyed)
                node.HideOutline();
        }

        outlined.Clear();
        Gizmo.Dispose();
        scene.Selection.Mode = originalMode;
        scene.Disown(this);
    }
}
