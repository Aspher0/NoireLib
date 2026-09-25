using Dalamud.Bindings.ImGui;
using NoireLib.Localizer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A complete look: theme, window frame, controls, overlays, icons, fonts and per-component views. What it lacks
/// comes from its fallback, ending at <see cref="StockSkin"/>.
/// </summary>
public abstract class NoireSkin
{
    private readonly Dictionary<Type, Func<NoireView>> views = new();
    private readonly Dictionary<Type, Presentation> presentations = new();

    private static readonly List<Component> OrderScratch = [];

    /// <summary>Creates a skin.</summary>
    /// <param name="id">A stable id, remembered as the user's choice.</param>
    /// <param name="name">The name shown to the user.</param>
    /// <param name="fallback">The skin that provides whatever this one does not, or <see langword="null"/> for <see cref="StockSkin"/>.</param>
    protected NoireSkin(string id, NoireString name, NoireSkin? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(name);

        Id = id;
        Name = name;
        Fallback = fallback ?? (this is StockSkin ? null : StockSkin.Instance);
    }

    /// <summary>The stable id.</summary>
    public string Id { get; }

    /// <summary>The name shown to the user.</summary>
    public NoireString Name { get; }

    /// <summary>The skin this one falls back on, <see langword="null"/> only for <see cref="StockSkin"/>.</summary>
    public NoireSkin? Fallback { get; }

    /// <summary>The skin's own colours, before the user's edits; <see cref="NoireSkins.Theme"/> is what to draw with.</summary>
    public virtual NoireTheme Theme => Fallback!.Theme;

    /// <summary>The colours the user may change.</summary>
    public virtual IReadOnlyList<ThemeRole> EditableColors => Fallback!.EditableColors;

    /// <summary>The frame around every window.</summary>
    public virtual IChromeSkin Chrome => Fallback!.Chrome;

    /// <summary>The controls.</summary>
    public virtual IControlSkin Controls => Fallback!.Controls;

    /// <summary>Menus, tooltips, cards, pills, confirmations and toasts.</summary>
    public virtual IOverlaySkin Overlays => Fallback!.Overlays;

    /// <summary>The icons.</summary>
    public virtual IIconSkin Icons => Fallback!.Icons;

    /// <summary>The typefaces.</summary>
    public virtual IFontSkin Fonts => Fallback!.Fonts;

    /// <summary>How settings pages and forms are laid out.</summary>
    public virtual ISettingsSkin Settings => Fallback!.Settings;

    /// <summary>Draws a miniature of the skin for the skin picker, in the colours given.</summary>
    /// <param name="area">Where it draws, in screen pixels.</param>
    /// <param name="theme">The skin's theme with the user's edits.</param>
    public virtual void DrawPreview(UiRect area, NoireTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (Fallback != null)
        {
            Fallback.DrawPreview(area, theme);
            return;
        }

        SkinPreview.Draw(area, theme);
    }

    /// <summary>The frame around one window; <see cref="Chrome"/> unless the skin frames that window differently.</summary>
    /// <param name="window">The window.</param>
    /// <returns>Its chrome.</returns>
    public virtual IChromeSkin ChromeFor(NoireSkinnedWindowBase window) => Chrome;

    /// <summary>Called when the skin becomes active, before its first frame; loads the fonts.</summary>
    public virtual void Activate() => Fonts.Load();

    /// <summary>Called when another skin takes over; releases what <see cref="Activate"/> loaded.</summary>
    public virtual void Deactivate() => Fonts.Unload();

    /// <summary>Draws a child at the ImGui cursor, in the space left in the current region.</summary>
    /// <param name="child">The component.</param>
    public void Draw(Component child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!UiDraw.Available)
            return;

        Draw(child, new UiRect(ImGui.GetCursorScreenPos(), ImGui.GetContentRegionAvail()));
    }

    /// <summary>Draws a child in an area with the view this skin gives its type, under the child's id as ImGui id scope.</summary>
    /// <param name="child">The component.</param>
    /// <param name="area">Where it draws; the ImGui cursor is placed at its top left corner and every new line starts at its left edge.</param>
    public void Draw(Component child, UiRect area)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!UiDraw.Available)
            return;

        ImGui.SetCursorScreenPos(area.Position);
        ImGui.PushID(child.Id);
        ImGui.BeginGroup();

        try
        {
            using var profile = NoireUI.Profiler.Measure(child.Scope);
            DrawComponent(child, area);
        }
        finally
        {
            ImGui.EndGroup();
            ImGui.PopID();
        }
    }

    /// <summary>Draws a parent's children stacked in an area, in the user's order, skipping those the user hid.</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="area">Where they stack. Growing children share the height the others leave.</param>
    public void DrawChildren(Component parent, UiRect area)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (!UiDraw.Available)
            return;

        parent.LaidOut = true;
        var ordered = Arrange(parent, parent.Window?.LayoutFor(this));
        var fixedHeight = 0f;
        var growers = 0;

        foreach (var child in ordered)
        {
            if (child.Grows)
                growers++;
            else
                fixedHeight += child.MeasuredHeight;
        }

        var growHeight = growers > 0 ? MathF.Max(0f, (area.Size.Y - fixedHeight) / growers) : 0f;
        var y = area.Top;

        foreach (var child in ordered)
        {
            var height = child.Grows ? growHeight : MathF.Max(0f, area.Bottom - y);
            Draw(child, new UiRect(new Vector2(area.Left, y), new Vector2(area.Size.X, height)));

            var used = child.Grows ? height : MathF.Max(0f, ImGui.GetCursorScreenPos().Y - y);

            if (!child.Grows)
                child.MeasuredHeight = used;

            y += used;
        }

        ImGui.SetCursorScreenPos(new Vector2(area.Left, y));
    }

    /// <summary>Draws a window's top-level components stacked in an area, in the user's order.</summary>
    /// <param name="window">The window.</param>
    /// <param name="area">Where they stack.</param>
    public void DrawChildren(NoireSkinnedWindowBase window, UiRect area)
    {
        ArgumentNullException.ThrowIfNull(window);
        DrawChildren(window.Root, area);
    }

    /// <summary>A parent's children in the user's order without those the user hid, for a view that places them itself.</summary>
    /// <param name="parent">The parent.</param>
    /// <returns>The children to draw, cached until the layout or the children change.</returns>
    public IReadOnlyList<Component> Arranged(Component parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        parent.LaidOut = true;
        return Arrange(parent, parent.Window?.LayoutFor(this));
    }

    /// <summary>A window's top-level components in the user's order without those the user hid.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The components to draw.</returns>
    public IReadOnlyList<Component> Arranged(NoireSkinnedWindowBase window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Arranged(window.Root);
    }

    /// <summary>Gives this skin its own view of a component or window type.</summary>
    /// <typeparam name="TTarget">The component or window type.</typeparam>
    /// <typeparam name="TView">The view.</typeparam>
    protected void View<TTarget, TView>()
        where TTarget : class
        where TView : NoireView<TTarget>, new()
        => views[typeof(TTarget)] = static () => new TView();

    /// <summary>Changes how this skin shows a window: as a modal over the window it is attached to, or not at all.</summary>
    /// <typeparam name="TWindow">The window type.</typeparam>
    /// <param name="presentation">How it is shown.</param>
    protected void Present<TWindow>(Presentation presentation) where TWindow : NoireSkinnedWindowBase
        => presentations[typeof(TWindow)] = presentation;

    // The first view of the type found walking the chain from this skin, or null.
    internal NoireView? CreateView(Type type)
    {
        for (var skin = this; skin != null; skin = skin.Fallback)
        {
            if (skin.views.TryGetValue(type, out var make))
                return make();
        }

        return null;
    }

    internal Presentation PresentationOf(Type window)
    {
        for (var skin = this; skin != null; skin = skin.Fallback)
        {
            if (skin.presentations.TryGetValue(window, out var presentation))
                return presentation;

            if (skin.views.ContainsKey(window))
                return Presentation.Window;
        }

        return Presentation.Window;
    }

    internal void DrawComponent(Component child, UiRect area)
    {
        var view = child.ViewFor(this);

        if (view == null)
        {
            DrawChildren(child, area);
            return;
        }

        view.Area = area;
        view.Window = child.Window!;
        view.DrawTarget(child);
    }

    // The visible children in the user's order, rebuilt only when the layout or the children changed.
    internal static Component[] Arrange(Component parent, ComponentLayout? layout)
    {
        if (parent.Arranged != null
            && ReferenceEquals(parent.ArrangedFor, layout)
            && parent.ArrangedLayoutVersion == (layout?.Version ?? 0)
            && parent.ArrangedChildrenVersion == parent.ChildrenVersion)
        {
            return parent.Arranged;
        }

        var ordered = new List<Component>(parent.Children.Count);
        OrderOf(parent, layout, ordered);

        // A loop rather than RemoveAll: a lambda capturing the parameters would allocate on every call, cached or not.
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].CanHide && layout != null && layout.IsHidden(parent.Path, ordered[i].Id))
                ordered.RemoveAt(i);
        }

        parent.Arranged = ordered.ToArray();
        parent.ArrangedFor = layout;
        parent.ArrangedLayoutVersion = layout?.Version ?? 0;
        parent.ArrangedChildrenVersion = parent.ChildrenVersion;
        return parent.Arranged;
    }

    // Every child in the user's order, hidden ones included: movable children follow the stored order, then their
    // declared order; fixed children keep their declared index.
    internal static void OrderOf(Component parent, ComponentLayout? layout, List<Component> into)
    {
        var children = parent.Children;
        var movable = OrderScratch;
        movable.Clear();
        into.Clear();

        if (layout != null && layout.Order.TryGetValue(parent.Path, out var order))
        {
            foreach (var id in order)
            {
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];

                    if (child.CanMove && child.Id == id && !movable.Contains(child))
                    {
                        movable.Add(child);
                        break;
                    }
                }
            }
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].CanMove && !movable.Contains(children[i]))
                movable.Add(children[i]);
        }

        var next = 0;

        for (var i = 0; i < children.Count; i++)
            into.Add(children[i].CanMove ? movable[next++] : children[i]);
    }
}
