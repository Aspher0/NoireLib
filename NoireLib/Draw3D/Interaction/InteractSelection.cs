using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Interaction;

/// <summary>How a new pick combines with the current selection.</summary>
public enum SelectionMode
{
    /// <summary>Selection can hold at most one node; picking replaces it.</summary>
    Single,

    /// <summary>Selection can hold many nodes; modifiers add / toggle / range as in a typical editor.</summary>
    Multi,
}

/// <summary>Modifier keys held during a selection pick (add / toggle semantics).</summary>
[Flags]
public enum SelectionModifiers
{
    /// <summary>No modifier: the pick replaces the selection.</summary>
    None = 0,

    /// <summary>Ctrl: toggle the picked node in/out of the selection (Multi mode).</summary>
    Toggle = 1,

    /// <summary>Shift: add the picked node to the selection (Multi mode).</summary>
    Add = 2,
}

/// <summary>
/// The selection the gizmo and editor read from: an ordered set of nodes with Single/Multi semantics and the
/// familiar Ctrl-toggle / Shift-add rules. Pure and observable: <see cref="Changed"/> fires whenever the set moves.
/// </summary>
public sealed class InteractSelection
{
    private readonly List<SceneNode> nodes = new();

    /// <summary>Whether the selection holds one node or many.</summary>
    public SelectionMode Mode { get; set; } = SelectionMode.Single;

    /// <summary>
    /// Maximum number of nodes the selection may hold in <see cref="SelectionMode.Multi"/> (0 or less means unlimited).
    /// When an add would exceed it, the oldest node is dropped so the newest is always included. Default 0.
    /// </summary>
    public int MaxCount { get; set; }

    /// <summary>The current selection, in the order nodes were added. Do not mutate; use the methods.</summary>
    public IReadOnlyList<SceneNode> Nodes => nodes;

    /// <summary>The number of selected nodes.</summary>
    public int Count => nodes.Count;

    /// <summary>The primary (last-picked) node, or null when nothing is selected.</summary>
    public SceneNode? Primary => nodes.Count > 0 ? nodes[^1] : null;

    /// <summary>Raised after any change to the set.</summary>
    public event Action? Changed;

    /// <summary>True when <paramref name="node"/> is currently selected.</summary>
    public bool Contains(SceneNode node) => node != null && nodes.Contains(node);

    /// <summary>
    /// Applies a pick to the selection under the current <see cref="Mode"/> and the given modifiers.
    /// Picking empty space (<paramref name="node"/> null) clears the selection unless a modifier is held.
    /// </summary>
    /// <param name="node">The picked node, or null for empty space.</param>
    /// <param name="modifiers">Modifier keys held during the pick.</param>
    public void Pick(SceneNode? node, SelectionModifiers modifiers = SelectionModifiers.None)
    {
        if (node == null)
        {
            if (modifiers == SelectionModifiers.None)
                Clear();
            return;
        }

        if (Mode == SelectionMode.Single || modifiers == SelectionModifiers.None)
        {
            SetSingle(node);
            return;
        }

        if ((modifiers & SelectionModifiers.Toggle) != 0)
        {
            if (!nodes.Remove(node))
                nodes.Add(node);
            TrimToMax();
            RaiseChanged();
            return;
        }

        // Add (Shift): include without removing, move to primary if already present.
        nodes.Remove(node);
        nodes.Add(node);
        TrimToMax();
        RaiseChanged();
    }

    /// <summary>Replaces the selection with exactly <paramref name="node"/>.</summary>
    public void SetSingle(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (nodes.Count == 1 && ReferenceEquals(nodes[0], node))
            return;

        nodes.Clear();
        nodes.Add(node);
        RaiseChanged();
    }

    /// <summary>Adds <paramref name="node"/> to the selection (no-op if already present in Single mode's single slot).</summary>
    public void Add(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (Mode == SelectionMode.Single)
        {
            SetSingle(node);
            return;
        }

        if (nodes.Contains(node))
            return;

        nodes.Add(node);
        TrimToMax();
        RaiseChanged();
    }

    /// <summary>Drops the oldest nodes until the count is within <see cref="MaxCount"/> (no-op when unlimited).</summary>
    private void TrimToMax()
    {
        if (MaxCount <= 0)
            return;

        while (nodes.Count > MaxCount)
            nodes.RemoveAt(0);
    }

    /// <summary>Removes <paramref name="node"/> from the selection. Returns whether it was present.</summary>
    public bool Remove(SceneNode node)
    {
        if (node == null || !nodes.Remove(node))
            return false;

        RaiseChanged();
        return true;
    }

    /// <summary>Empties the selection.</summary>
    public void Clear()
    {
        if (nodes.Count == 0)
            return;

        nodes.Clear();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "A NoireInteract selection Changed handler threw.", "Draw3D");
        }
    }
}
