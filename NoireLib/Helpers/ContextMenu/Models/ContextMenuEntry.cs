using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One context menu entry, described once and rebuilt on every opening that passes its filters.
/// Reconfigure a copy with <c>with { }</c>. Never assemble a new one by hand.
/// </summary>
public sealed record ContextMenuEntry
{
    /// <summary>The text shown after the glyphs.</summary>
    public required string Label { get; init; }

    /// <summary>The boxed glyphs shown ahead of the label, in order. Empty means Dalamud's own prefix.</summary>
    public IReadOnlyList<SeIconChar> Glyphs { get; init; } = [];

    /// <summary>The UIColor row id the glyphs are drawn in.</summary>
    public ushort GlyphColor { get; init; } = IMenuItem.DalamudDefaultPrefixColor;

    /// <summary>Which menu the entry appears on.</summary>
    public ContextMenuScope Scope { get; init; } = ContextMenuScope.Default;

    /// <summary>Where the entry sits, lowest first. Below zero puts it above the game's own entries.</summary>
    public int Priority { get; init; }

    /// <summary>Whether the entry can be clicked. A disabled entry is drawn faded.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Whether the entry is drawn with a back arrow.</summary>
    public bool IsReturn { get; init; }

    /// <summary>Decides whether the entry appears at all for one opening, or null to always appear.</summary>
    public Func<ContextMenuContext, bool>? ShowWhen { get; init; }

    /// <summary>Decides whether the entry is clickable for one opening, or null to follow <see cref="IsEnabled"/>.</summary>
    public Func<ContextMenuContext, bool>? EnabledWhen { get; init; }

    /// <summary>The addon names the entry appears over, matched exactly, or null for any addon.</summary>
    public IReadOnlyCollection<string>? Addons { get; init; }

    /// <summary>Runs when the entry is clicked, before any submenu opens.</summary>
    public Action<ContextMenuClick>? OnClick { get; init; }

    /// <summary>Builds the submenu opened on click, or null for a plain entry. An empty list opens nothing.</summary>
    public Func<ContextMenuContext, IReadOnlyList<ContextMenuEntry>>? Submenu { get; init; }

    /// <summary>The title drawn at the top of the submenu, empty for none.</summary>
    public string SubmenuTitle { get; init; } = string.Empty;

    /// <summary>Runs on the finished Dalamud item, for anything this record does not expose.</summary>
    public Action<MenuItem>? OnConfigure { get; init; }
}
