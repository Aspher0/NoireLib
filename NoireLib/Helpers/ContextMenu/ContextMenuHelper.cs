using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// Adds entries to the game's right-click menus. Disposing what <see cref="Register(ContextMenuEntry)"/> returns removes
/// the entry. Filters and click handlers run on the framework thread.
/// </summary>
public static class ContextMenuHelper
{
    private const string DisposeKey = "NoireLib.ContextMenuHelper";

    private static readonly List<ContextMenuRegistration> Entries = [];
    private static readonly object Gate = new();

    private static bool attached;

    #region Registering

    /// <summary>Shows an entry on every opening it matches.</summary>
    /// <param name="entry">The entry to show.</param>
    /// <returns>The registration, disposed to remove the entry.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="entry"/> is null.</exception>
    public static ContextMenuRegistration Register(ContextMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var registration = new ContextMenuRegistration(entry);

        lock (Gate)
        {
            Entries.Add(registration);
            Attach();
        }

        return registration;
    }

    /// <summary>Shows a plain entry on every opening of one menu.</summary>
    /// <param name="label">The text shown after the prefix.</param>
    /// <param name="onClick">Runs when the entry is clicked.</param>
    /// <param name="scope">Which menu the entry appears on.</param>
    /// <returns>The registration, disposed to remove the entry.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="onClick"/> is null.</exception>
    /// <exception cref="ArgumentException">If <paramref name="label"/> is null or blank.</exception>
    public static ContextMenuRegistration Register(
        string label,
        Action<ContextMenuClick> onClick,
        ContextMenuScope scope = ContextMenuScope.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onClick);

        return Register(new ContextMenuEntry { Label = label, OnClick = onClick, Scope = scope });
    }

    #endregion

    #region Building an item

    /// <summary>The first glyph, drawn by the game from the item's prefix slot. Null leaves Dalamud's own prefix.</summary>
    /// <param name="glyphs">The entry's glyphs.</param>
    /// <returns>The first glyph, or null when there are none.</returns>
    public static SeIconChar? GlyphPrefix(IReadOnlyList<SeIconChar> glyphs)
    {
        return glyphs.Count == 0 ? null : glyphs[0];
    }

    /// <summary>Builds the label an entry shows, with every glyph after the first. The first comes from the prefix slot.</summary>
    /// <param name="label">The text shown after the glyphs.</param>
    /// <param name="glyphs">The entry's glyphs, of which the first is skipped.</param>
    /// <param name="glyphColor">The UIColor row id the glyphs are drawn in.</param>
    /// <returns>The label, glyphs first, each followed by a space.</returns>
    public static SeString BuildLabel(
        string label,
        IReadOnlyList<SeIconChar> glyphs,
        ushort glyphColor = IMenuItem.DalamudDefaultPrefixColor)
    {
        if (glyphs.Count < 2)
            return label;

        var builder = new SeStringBuilder();

        for (var index = 1; index < glyphs.Count; index++)
            builder.AddUiForeground($"{glyphs[index].ToIconString()} ", glyphColor);

        return builder.AddText(label).Build();
    }

    /// <summary>Builds the Dalamud item an entry shows for one opening.</summary>
    /// <param name="entry">The entry to build.</param>
    /// <param name="context">What the game was showing when the menu opened.</param>
    /// <returns>The item, ready to hand to Dalamud.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="entry"/> or <paramref name="context"/> is null.</exception>
    public static MenuItem BuildItem(ContextMenuEntry entry, ContextMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);

        var item = new MenuItem
        {
            Name = BuildLabel(entry.Label, entry.Glyphs, entry.GlyphColor),
            Prefix = GlyphPrefix(entry.Glyphs),
            PrefixColor = entry.GlyphColor,
            UseDefaultPrefix = entry.Glyphs.Count == 0,
            Priority = entry.Priority,
            IsEnabled = entry.IsEnabled && (entry.EnabledWhen?.Invoke(context) ?? true),
            IsSubmenu = entry.Submenu != null,
            IsReturn = entry.IsReturn,
            OnClicked = clicked => Click(entry, context, clicked),
        };

        entry.OnConfigure?.Invoke(item);

        return item;
    }

    #endregion

    #region Filtering and ordering

    /// <summary>Whether an entry belongs on one opening. The scope, then the addon filter, then the predicate.</summary>
    /// <param name="entry">The entry to test.</param>
    /// <param name="context">What the game was showing when the menu opened.</param>
    /// <returns>True when the scope, the addon filter and the predicate all pass.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="entry"/> or <paramref name="context"/> is null.</exception>
    public static bool Matches(ContextMenuEntry entry, ContextMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);

        if (!InScope(entry.Scope, context))
            return false;

        if (entry.Addons != null && !entry.Addons.Contains(context.AddonName, StringComparer.Ordinal))
            return false;

        return entry.ShowWhen?.Invoke(context) ?? true;
    }

    /// <summary>Whether one opening falls under a scope, ignoring the addon filter and the predicate.</summary>
    /// <param name="scope">The scope to test.</param>
    /// <param name="context">What the game was showing when the menu opened.</param>
    /// <returns>True when the opening belongs to <paramref name="scope"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="context"/> is null.</exception>
    public static bool InScope(ContextMenuScope scope, ContextMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return scope switch
        {
            ContextMenuScope.Everywhere => true,
            ContextMenuScope.Item => context.ItemId != 0,
            ContextMenuScope.Character => context.TargetKind != ContextMenuTargetKind.None,
            ContextMenuScope.Player => context.TargetKind == ContextMenuTargetKind.Player,
            _ => scope == context.Menu,
        };
    }

    /// <summary>Puts entries in the order the game draws them, lowest priority first.</summary>
    /// <param name="entries">The entries to order.</param>
    /// <returns>The entries, ties left in the order they came in.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="entries"/> is null.</exception>
    public static IReadOnlyList<ContextMenuEntry> Order(IEnumerable<ContextMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return [.. entries.OrderBy(entry => entry.Priority)];
    }

    #endregion

    #region The Dalamud event

    internal static int RegistrationCount
    {
        get
        {
            lock (Gate)
                return Entries.Count;
        }
    }

    internal static bool IsAttached
    {
        get
        {
            lock (Gate)
                return attached;
        }
    }

    internal static void Remove(ContextMenuRegistration registration)
    {
        lock (Gate)
        {
            Entries.Remove(registration);

            if (Entries.Count == 0)
                Detach();
        }
    }

    private static void Attach()
    {
        if (attached || !NoireService.IsInitialized())
            return;

        NoireService.ContextMenu.OnMenuOpened += MenuOpened;
        NoireLibMain.RegisterOnDispose(DisposeKey, DisposeAll);
        attached = true;
    }

    private static void Detach()
    {
        if (!attached)
            return;

        NoireService.ContextMenu.OnMenuOpened -= MenuOpened;
        NoireLibMain.UnregisterOnDispose(DisposeKey);
        attached = false;
    }

    private static void DisposeAll()
    {
        lock (Gate)
        {
            foreach (var registration in Entries.ToArray())
                registration.Dispose();

            Entries.Clear();
            Detach();
        }
    }

    private static void MenuOpened(IMenuOpenedArgs args)
    {
        SafeExecutor.ExecuteSafely(() =>
        {
            ContextMenuRegistration[] snapshot;

            lock (Gate)
                snapshot = [.. Entries];

            if (snapshot.Length == 0)
                return;

            var context = ReadContext(args);
            var matched = new List<ContextMenuEntry>(snapshot.Length);

            foreach (var registration in snapshot)
            {
                if (!registration.IsDisposed && Matches(registration.Entry, context))
                    matched.Add(registration.Entry);
            }

            foreach (var entry in Order(matched))
                args.AddMenuItem(BuildItem(entry, context));
        });
    }

    private static ContextMenuContext ReadContext(IMenuArgs args)
    {
        var context = new ContextMenuContext
        {
            Menu = args.MenuType == ContextMenuType.Inventory ? ContextMenuScope.Inventory : ContextMenuScope.Default,
            AddonName = args.AddonName ?? string.Empty,
            Args = args,
        };

        switch (args.Target)
        {
            case MenuTargetInventory { TargetItem: { } item }:
                return context with
                {
                    Item = item,
                    ItemId = item.BaseItemId,
                    IsHqItem = item.IsHq,
                    ItemSource = ContextMenuItemSource.Inventory,
                };
            case MenuTargetDefault target:
                var (itemId, isHighQuality, source) = ContextMenuItemResolver.Resolve(ReadItemState(context.AddonName, target));

                return context with
                {
                    TargetKind = ContextMenuTargetResolver.Resolve(
                        target.TargetObject is IPlayerCharacter,
                        target.TargetObject is ICharacter,
                        target.TargetContentId),
                    TargetName = target.TargetName ?? string.Empty,
                    TargetObjectId = target.TargetObjectId,
                    TargetContentId = target.TargetContentId,
                    TargetHomeWorldId = target.TargetHomeWorld.RowId,
                    TargetObject = target.TargetObject,
                    ItemId = itemId,
                    IsHqItem = isHighQuality,
                    ItemSource = source,
                };
            default:
                return context;
        }
    }

    private static unsafe ContextMenuItemReading ReadItemState(string addonName, MenuTargetDefault target)
    {
        var chatLog = AgentChatLog.Instance();
        var recipeNote = AgentRecipeNote.Instance();
        var recipeList = AgentRecipeItemContext.Instance();

        return new ContextMenuItemReading(
            addonName,
            target.TargetContentId != 0 || target.TargetObject != null || !string.IsNullOrEmpty(target.TargetName),
            chatLog == null ? 0u : chatLog->ContextItemId,
            recipeNote == null ? 0u : recipeNote->ContextMenuResultItemId,
            recipeList == null ? 0u : recipeList->ResultItemId,
            NoireService.GameGui.HoveredItem);
    }

    private static void Click(ContextMenuEntry entry, ContextMenuContext context, IMenuItemClickedArgs args)
    {
        SafeExecutor.ExecuteSafely(() =>
        {
            var click = new ContextMenuClick(entry, context, args);

            entry.OnClick?.Invoke(click);

            if (entry.Submenu is { } submenu)
                click.OpenSubmenu(entry.SubmenuTitle, submenu.Invoke(context));
        });
    }

    #endregion
}
