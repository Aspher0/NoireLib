using NoireLib.Localizer;
using System;
using System.Collections.Generic;

namespace NoireLib.UI;

/// <summary>
/// A node of a window's component tree, unique among its siblings, drawn by the view the skin gives its type.
/// Disposing it disposes its children.
/// </summary>
public abstract class Component : IDisposable
{
    private readonly List<Component> children = [];
    private Dictionary<string, Component>? byId;
    private UiScopeName? scope;
    private NoireView? view;
    private NoireSkin? viewSkin;

    // Moves when a child is added, removed or reordered: a cached arrangement knows it is stale.
    internal int ChildrenVersion;

    // Cached by NoireSkin.DrawChildren against the layout and ChildrenVersion it was built from.
    internal Component[]? Arranged;
    internal ComponentLayout? ArrangedFor;
    internal int ArrangedLayoutVersion = -1;
    internal int ArrangedChildrenVersion = -1;

    // Set once a skin draws this component's children through DrawChildren; the layout editor lists only these.
    internal bool LaidOut;

    // The height the component drew last time DrawChildren placed it.
    internal float MeasuredHeight;

    // Used by ComponentList to find rows whose item left.
    internal int SyncStamp;

    /// <summary>The id, unique among its siblings. Empty until the component is added.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>The parent, or <see langword="null"/> before the component is added.</summary>
    public Component? Parent { get; private set; }

    /// <summary>The ids from the window to this component joined by <c>/</c>, built when the component is attached.</summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>The children, in the order they were added.</summary>
    public IReadOnlyList<Component> Children => children;

    /// <summary>The name the layout editor shows, or <see langword="null"/> to show the id.</summary>
    public NoireString? Name { get; init; }

    /// <summary>Whether it takes the height its siblings leave when its parent is drawn through <see cref="NoireSkin.DrawChildren(Component, UiRect)"/>.</summary>
    public bool Grows { get; init; }

    /// <summary>Whether the user may hide it.</summary>
    public bool CanHide { get; init; }

    /// <summary>Whether the user may move it among its siblings.</summary>
    public bool CanMove { get; init; } = true;

    /// <summary>Whether it has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    // The window at the root of the tree, cached when the component is attached.
    internal NoireSkinnedWindowBase? Window { get; private set; }

    /// <summary>Finds a descendant by a path relative to this component, such as <c>list/row[3012]/star</c>.</summary>
    /// <param name="path">The ids below this component joined by <c>/</c>.</param>
    /// <returns>The component, or <see langword="null"/> when there is none.</returns>
    public Component? Find(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var current = this;
        var rest = path.AsSpan();

        while (rest.Length > 0)
        {
            var slash = rest.IndexOf('/');
            var segment = slash < 0 ? rest : rest[..slash];
            rest = slash < 0 ? [] : rest[(slash + 1)..];

            if (segment.Length == 0)
                continue;

            if (current.byId == null || !current.byId.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(segment, out var next))
                return null;

            current = next;
        }

        return current;
    }

    /// <summary>Disposes this component, its view and its children, and detaches it from its parent.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        for (var i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            child.Parent = null;
            child.Dispose();
        }

        children.Clear();
        byId?.Clear();

        if (Parent is { IsDisposed: false } parent)
            parent.Detach(this);

        Parent = null;
        DropView();
        OnDisposed();
        GC.SuppressFinalize(this);
    }

    /// <summary>Registers a child under an id and attaches it.</summary>
    /// <typeparam name="T">The child's type.</typeparam>
    /// <param name="id">An id unique among this component's children, without <c>/</c>.</param>
    /// <param name="child">The child.</param>
    /// <returns>The child.</returns>
    /// <exception cref="ArgumentException">Thrown when the id is empty, holds a <c>/</c> or is already used by a sibling.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the child already has a parent, or either side is disposed.</exception>
    protected T Add<T>(string id, T child) where T : Component
    {
        AddChild(id, child);
        return child;
    }

    /// <summary>Detaches a child and disposes it.</summary>
    /// <param name="child">The child.</param>
    /// <exception cref="ArgumentException">Thrown when it is not a child of this component.</exception>
    protected void Remove(Component child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!ReferenceEquals(child.Parent, this))
            throw new ArgumentException($"'{child.Id}' is not a child of '{Path}'.", nameof(child));

        Detach(child);
        child.Parent = null;
        child.Dispose();
    }

    /// <summary>The view drawn when no skin gives one for this component's type, or <see langword="null"/> to draw the children in the user's layout.</summary>
    /// <returns>A new view.</returns>
    protected internal virtual NoireView? DefaultView() => null;

    /// <summary>Releases what the component holds; called once, after its children are disposed.</summary>
    protected virtual void OnDisposed()
    {
    }

    internal void AddChild(string id, Component child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentException.ThrowIfNullOrEmpty(id);

        if (id.Contains('/'))
            throw new ArgumentException($"The id '{id}' holds a '/', which separates the ids of a path.", nameof(id));

        if (IsDisposed || child.IsDisposed)
            throw new InvalidOperationException("A disposed component cannot be attached.");

        if (child.Parent != null || child is WindowRoot)
            throw new InvalidOperationException($"'{child.Id}' already has a parent.");

        byId ??= new Dictionary<string, Component>(StringComparer.Ordinal);

        if (!byId.TryAdd(id, child))
            throw new ArgumentException($"'{Path}' already has a child with the id '{id}'.", nameof(id));

        child.Id = id;
        child.Parent = this;
        children.Add(child);
        ChildrenVersion++;
        child.Attach();
    }

    internal void Detach(Component child)
    {
        children.Remove(child);
        byId?.Remove(child.Id);
        ChildrenVersion++;
    }

    // Children follow the given order; used by lists whose rows follow their items.
    internal void ReorderChildren<T>(List<T> ordered) where T : Component
    {
        children.Clear();

        for (var i = 0; i < ordered.Count; i++)
            children.Add(ordered[i]);

        ChildrenVersion++;
    }

    internal void SetRoot(string id, NoireSkinnedWindowBase window)
    {
        Id = id;
        Path = id;
        Window = window;
    }

    // The profiler scope is named by the path, resolved on first use while the profiler is on.
    internal UiScopeName? Scope => NoireUI.Profiler.Enabled ? scope ??= UiScopeName.For(Path) : null;

    internal NoireView? ViewFor(NoireSkin skin)
    {
        if (ReferenceEquals(viewSkin, skin))
            return view;

        DropView();
        viewSkin = skin;
        view = skin.CreateView(GetType()) ?? DefaultView();

        if (view != null)
            view.Skin = skin;

        return view;
    }

    internal void DropView()
    {
        view?.Dispose();
        view = null;
        viewSkin = null;
    }

    internal void DropViews()
    {
        DropView();

        for (var i = 0; i < children.Count; i++)
            children[i].DropViews();
    }

    // Paths and the window are cached here, for the whole subtree, whenever a subtree joins a tree.
    private void Attach()
    {
        Path = Parent is { Path.Length: > 0 } parent ? parent.Path + "/" + Id : Id;
        Window = Parent?.Window;
        scope = null;

        for (var i = 0; i < children.Count; i++)
            children[i].Attach();
    }
}
