using NoireLib.Localizer;
using System;

namespace NoireLib.UI;

/// <summary>A menu's body as <see cref="NoireUI.Menu{T}"/> hands it out: a title, items and separators.</summary>
public sealed class NoireMenu
{
    private IOverlaySkin overlays = StockOverlaySkin.Instance;
    private bool separatorPending;
    private bool anyItem;

    internal NoireMenu()
    {
    }

    internal bool Clicked { get; private set; }

    /// <summary>A title line at the top of the menu.</summary>
    /// <param name="title">The title.</param>
    /// <param name="gameIcon">A game icon before it, or 0.</param>
    public void Title(string title, uint gameIcon = 0) => overlays.MenuTitle(title, gameIcon);

    /// <summary>An action on the menu's target: hidden or disabled while unavailable, run when clicked.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="action">The action.</param>
    /// <param name="target">The target.</param>
    public void Item<T>(UiAction<T> action, T target)
    {
        var available = action.IsAvailable(target);

        if (!available && action.HideWhenUnavailable)
            return;

        if (Item(action.Label, action.Icon, available, action.Hint, action.RequiresCtrl))
            action.Run(target);
    }

    /// <summary>A one-off item.</summary>
    /// <param name="label">The label.</param>
    /// <param name="icon">The icon, or <see langword="null"/> for none.</param>
    /// <param name="enabled">Whether it can be clicked.</param>
    /// <param name="hint">A tooltip, also shown as the reason while disabled or waiting for Ctrl.</param>
    /// <param name="requiresCtrl">Whether it only works while Ctrl is held.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public bool Item(NoireString label, NoireIcon? icon, bool enabled = true, NoireString? hint = null, bool requiresCtrl = false)
        => Item(label.Text, icon, enabled, hint?.Text, requiresCtrl);

    /// <summary>A one-off item whose text is built at run time, such as one that carries a count.</summary>
    /// <param name="label">The label, in the active language.</param>
    /// <param name="icon">The icon, or <see langword="null"/> for none.</param>
    /// <param name="enabled">Whether it can be clicked.</param>
    /// <param name="hint">A tooltip, also shown as the reason while disabled or waiting for Ctrl.</param>
    /// <param name="requiresCtrl">Whether it only works while Ctrl is held.</param>
    /// <returns>True on the frame it is clicked.</returns>
    public bool Item(string label, NoireIcon? icon, bool enabled = true, string? hint = null, bool requiresCtrl = false)
    {
        ArgumentNullException.ThrowIfNull(label);

        FlushSeparator();
        anyItem = true;

        if (!overlays.MenuItem(label, icon, enabled, hint, requiresCtrl))
            return false;

        Clicked = true;
        return true;
    }

    /// <summary>A separator. One before any item, one after the last item and one right after another are dropped.</summary>
    public void Separator() => separatorPending = anyItem;

    internal void Begin(IOverlaySkin skin)
    {
        overlays = skin;
        separatorPending = false;
        anyItem = false;
        Clicked = false;
    }

    private void FlushSeparator()
    {
        if (!separatorPending)
            return;

        separatorPending = false;
        overlays.MenuSeparator();
    }
}
