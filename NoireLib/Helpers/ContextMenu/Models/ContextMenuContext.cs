using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;

namespace NoireLib.Helpers;

/// <summary>What the game showed when a context menu opened. Fields a menu does not carry stay empty.</summary>
public sealed record ContextMenuContext
{
    /// <summary>Which menu opened.</summary>
    public ContextMenuScope Menu { get; init; } = ContextMenuScope.Default;

    /// <summary>The addon the menu opened over, empty when the game names none.</summary>
    public string AddonName { get; init; } = string.Empty;

    /// <summary>The targeted inventory slot, null on any other menu.</summary>
    public GameInventoryItem? Item { get; init; }

    /// <summary>The row id of the item the menu opened on, without its quality offset, or zero.</summary>
    public uint ItemId { get; init; }

    /// <summary>Whether the item the menu opened on is high quality.</summary>
    public bool IsHqItem { get; init; }

    /// <summary>Where <see cref="ItemId"/> was read from.</summary>
    public ContextMenuItemSource ItemSource { get; init; }

    /// <summary>What kind of character the menu opened on.</summary>
    public ContextMenuTargetKind TargetKind { get; init; }

    /// <summary>The targeted name, empty when the menu carries none.</summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>The targeted object's id, or zero.</summary>
    public ulong TargetObjectId { get; init; }

    /// <summary>The targeted character's content id, or zero.</summary>
    public ulong TargetContentId { get; init; }

    /// <summary>The targeted character's home world row id, or zero.</summary>
    public uint TargetHomeWorldId { get; init; }

    /// <summary>The targeted game object, null when the menu did not open over one.</summary>
    public IGameObject? TargetObject { get; init; }

    /// <summary>Dalamud's own arguments for this opening, null when the context was built by hand.</summary>
    public IMenuArgs? Args { get; init; }
}
