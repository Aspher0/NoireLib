using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Interaction;

/// <summary>How a new pick combines with the current selection.</summary>
public enum SelectionMode
{
    /// <summary>Selection can hold at most one node. Picking replaces it.</summary>
    Single,

    /// <summary>Selection can hold many nodes. Modifiers add or toggle.</summary>
    Multi,
}

/// <summary>Modifier keys held during a selection pick.</summary>
[Flags]
public enum SelectionModifiers
{
    /// <summary>No modifier: the pick replaces the selection.</summary>
    None = 0,

    /// <summary>Ctrl: toggle the picked node in or out of the selection (Multi mode).</summary>
    Toggle = 1,

    /// <summary>Shift: add the picked node to the selection (Multi mode).</summary>
    Add = 2,
}

/// <summary>The ordered set of selected nodes the gizmo and editor read from, with Ctrl-toggle and Shift-add rules.</summary>
public sealed class InteractSelection
{
    private readonly List<SceneNode> nodes = new();

    /// <summary>Whether the selection holds one node or many.</summary>
    public SelectionMode Mode { get; set; } = SelectionMode.Single;

    /// <summary>Maximum node count in <see cref="SelectionMode.Multi"/>, 0 or less for unlimited. Exceeding it drops the oldest node.</summary>
    public int MaxCount { get; set; }

    /// <summary>The current selection, in the order nodes were added.</summary>
    public IReadOnlyList<SceneNode> Nodes => nodes;

    /// <summary>The number of selected nodes.</summary>
    public int Count => nodes.Count;

    /// <summary>The primary (last-picked) node, or null when nothing is selected.</summary>
    public SceneNode? Primary => nodes.Count > 0 ? nodes[^1] : null;

    /// <summary>Raised after any change to the set.</summary>
    public event Action? Changed;

    /// <summary>Whether <paramref name="node"/> is currently selected.</summary>
    /// <param name="node">The node to test.</param>
    public bool Contains(SceneNode node) => node != null && nodes.Contains(node);

    /// <summary>Applies a pick under the current <see cref="Mode"/>. Picking empty space clears the selection unless a modifier is held.</summary>
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

        // Shift. Include, and move to primary if already present.
        nodes.Remove(node);
        nodes.Add(node);
        TrimToMax();
        RaiseChanged();
    }

    /// <summary>Replaces the selection with exactly <paramref name="node"/>.</summary>
    /// <param name="node">The node to select.</param>
    public void SetSingle(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (nodes.Count == 1 && ReferenceEquals(nodes[0], node))
            return;

        nodes.Clear();
        nodes.Add(node);
        RaiseChanged();
    }

    /// <summary>Adds <paramref name="node"/> to the selection, replacing it in <see cref="SelectionMode.Single"/>.</summary>
    /// <param name="node">The node to add.</param>
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

    private void TrimToMax()
    {
        if (MaxCount <= 0)
            return;

        while (nodes.Count > MaxCount)
            nodes.RemoveAt(0);
    }

    /// <summary>Removes <paramref name="node"/> from the selection. Returns whether it was present.</summary>
    /// <param name="node">The node to remove.</param>
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
            NoireLogger.LogError(ex, "A NoireInteract selection Changed handler threw.", "[Draw3D] ");
        }
    }
}
