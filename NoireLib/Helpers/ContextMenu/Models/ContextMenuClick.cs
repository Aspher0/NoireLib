using Dalamud.Game.Gui.ContextMenu;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One click on a context menu entry. Runs on the framework thread.
/// </summary>
public sealed class ContextMenuClick
{
    internal ContextMenuClick(ContextMenuEntry entry, ContextMenuContext context, IMenuItemClickedArgs? args)
    {
        Entry = entry;
        Context = context;
        Args = args;
    }

    /// <summary>The entry that was clicked.</summary>
    public ContextMenuEntry Entry { get; }

    /// <summary>What the game was showing when the menu opened.</summary>
    public ContextMenuContext Context { get; }

    /// <summary>Dalamud's own click arguments, null when the click was raised by hand.</summary>
    public IMenuItemClickedArgs? Args { get; }

    /// <summary>Opens a submenu under the clicked entry. Does nothing for an empty list.</summary>
    /// <param name="title">The title drawn at the top, empty for none.</param>
    /// <param name="entries">The submenu entries, reordered by priority.</param>
    public void OpenSubmenu(string title, IReadOnlyList<ContextMenuEntry> entries)
    {
        if (Args == null || entries.Count == 0)
            return;

        var ordered = ContextMenuHelper.Order(entries);
        var items = new List<IMenuItem>(ordered.Count);

        foreach (var entry in ordered)
            items.Add(ContextMenuHelper.BuildItem(entry, Context));

        if (title.Length == 0)
            Args.OpenSubmenu(items);
        else
            Args.OpenSubmenu(title, items);
    }
}
