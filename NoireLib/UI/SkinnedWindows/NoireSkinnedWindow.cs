using NoireLib.Localizer;

namespace NoireLib.UI;

/// <summary>
/// A skinned window whose content is the active skin's view of the window's own type, or its components stacked in the
/// user's layout when no skin gives one. The components outlive skin switches; the views are rebuilt for the new skin.
/// </summary>
public abstract class NoireSkinnedWindow : NoireSkinnedWindowBase
{
    private NoireView? view;
    private NoireSkin? viewSkin;

    /// <summary>Creates the window.</summary>
    /// <param name="id">A stable id: the ImGui id, the root of its component paths, and the key of its layout and options.</param>
    /// <param name="title">The title.</param>
    protected NoireSkinnedWindow(string id, NoireString title)
        : base(id, title)
    {
    }

    /// <summary>The view drawn when no skin gives one for this window's type, or <see langword="null"/> to stack the components.</summary>
    /// <returns>A new view.</returns>
    protected virtual NoireView? DefaultView() => null;

    /// <summary>Draws the window's view, or its components in the user's layout.</summary>
    protected sealed override void DrawBody()
    {
        var skin = NoireSkins.Active;
        var area = UiRect.FromBounds(BodyMin, BodyMax);
        var current = ViewFor(skin);

        using var profile = NoireUI.Profiler.Measure(Root.Scope);

        if (current == null)
        {
            skin.DrawChildren(Root, area);
            return;
        }

        current.Area = area;
        current.DrawTarget(this);
    }

    /// <summary>Draws the view's overlay.</summary>
    protected sealed override void DrawOverlay() => view?.OverlayTarget(this);

    internal override void DropViews()
    {
        base.DropViews();
        view?.Dispose();
        view = null;
        viewSkin = null;
    }

    private NoireView? ViewFor(NoireSkin skin)
    {
        if (ReferenceEquals(viewSkin, skin))
            return view;

        view?.Dispose();
        viewSkin = skin;
        view = skin.CreateView(GetType()) ?? DefaultView();

        if (view != null)
        {
            view.Skin = skin;
            view.Window = this;
        }

        return view;
    }
}
