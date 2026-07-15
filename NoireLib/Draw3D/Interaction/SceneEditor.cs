using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// The packaged "click to select, gizmo follows the selection" controller, created from and owned by a scene via
/// <see cref="Scene3D.CreateEditor"/>. It subscribes to the scene's <see cref="Scene3D.Selection"/> and attaches its
/// <see cref="Gizmo"/> to the current pick (one node, or the whole group), so the ~15-line follow-selection branch is
/// gone.<br/>
/// <b>Owned by the scene:</b> <see cref="Scene3D.Dispose"/> disposes the editor. <see cref="Dispose"/> is available for
/// early teardown but is <b>never required</b>.<br/>
/// <b>Scoped multi-select:</b> <see cref="MultiSelect"/> sets the scene selection's mode and restores it on dispose -
/// no lingering global. Because the scene's selection only ever holds that scene's nodes, the editor naturally reacts
/// to picks in its own scene only.
/// </summary>
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
        OnSelectionChanged(); // sync to whatever is already selected
    }

    /// <summary>The gizmo the editor drives. Configure it via the flattened surface (<c>editor.Gizmo.Space = …</c>, <c>editor.Gizmo.Snap = 0.5f</c>) or its <see cref="NoireGizmo.Options"/>.</summary>
    public NoireGizmo Gizmo { get; }

    /// <summary>The selection the editor follows - the scene's own <see cref="Scene3D.Selection"/>.</summary>
    public InteractSelection Selection => scene.Selection;

    /// <summary>The scene this editor belongs to.</summary>
    public Scene3D Scene => scene;

    /// <summary>
    /// Whether picking builds a multi-node selection (Ctrl-toggle / Shift-add). Setting it drives the scene selection's
    /// mode as a <b>scoped</b> setting - the mode in effect when the editor was created is restored on <see cref="Dispose"/>,
    /// so this never leaves a lingering global.
    /// </summary>
    public bool MultiSelect
    {
        get => scene.Selection.Mode == SelectionMode.Multi;
        set => scene.Selection.Mode = value ? SelectionMode.Multi : SelectionMode.Single;
    }

    /// <summary>Master enable: when false the gizmo neither draws nor interacts (selection still tracks). Default true.</summary>
    public bool Enabled
    {
        get => Gizmo.Enabled;
        set => Gizmo.Enabled = value;
    }

    /// <summary>
    /// Optional: when set, selected nodes get a real silhouette outline in this color (via
    /// <see cref="SceneNode.ShowOutline"/>), removed on deselect. Off by default (the default selection feedback is
    /// the gizmo plus the per-node hover tint). Set to null to turn outlines off.
    /// </summary>
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

    /// <summary>True once disposed.</summary>
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

    /// <summary>Applies / removes selection outlines to match the current selection and <see cref="SelectionOutline"/>.</summary>
    private void UpdateOutlines()
    {
        // Drop outlines from nodes no longer selected (or when outlining is off / the node is gone).
        for (var i = outlined.Count - 1; i >= 0; i--)
        {
            var node = outlined[i];
            if (selectionOutline is null || node.Destroyed || !scene.Selection.Contains(node))
            {
                if (!node.Destroyed)
                    node.HideOutline();
                outlined.RemoveAt(i);
            }
        }

        if (selectionOutline is { } color)
        {
            foreach (var node in scene.Selection.Nodes)
            {
                if (!outlined.Contains(node))
                {
                    node.ShowOutline(color, OutlineWidth);
                    outlined.Add(node);
                }
            }
        }
    }

    /// <summary>
    /// Early teardown: unwires the selection follow, disposes the gizmo and restores the selection mode. Optional -
    /// <see cref="Scene3D.Dispose"/> does all of this for you. Idempotent.
    /// </summary>
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
        scene.Disown(this); // release from the scene's ownership (no-op when the scene is already tearing down)
    }
}
