using Dalamud.Game.Inventory;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when an item is added to an inventory.
/// </summary>
/// <param name="ItemId">The item row id.</param>
/// <param name="Quantity">The quantity added.</param>
/// <param name="IsHq">Whether the item is high quality.</param>
/// <param name="Inventory">The inventory the item was added to.</param>
/// <param name="Slot">The slot the item was added to.</param>
public sealed record ItemAddedEvent(uint ItemId, int Quantity, bool IsHq, GameInventoryType Inventory, uint Slot);

/// <summary>
/// Fired when an item is removed from an inventory.
/// </summary>
/// <param name="ItemId">The item row id.</param>
/// <param name="Quantity">The quantity removed.</param>
/// <param name="Inventory">The inventory the item was removed from.</param>
/// <param name="Slot">The slot the item was removed from.</param>
public sealed record ItemRemovedEvent(uint ItemId, int Quantity, GameInventoryType Inventory, uint Slot);

/// <summary>
/// Fired when an item moves between inventory slots.
/// </summary>
/// <param name="ItemId">The item row id.</param>
/// <param name="SourceInventory">The inventory the item moved from.</param>
/// <param name="SourceSlot">The slot the item moved from.</param>
/// <param name="TargetInventory">The inventory the item moved to.</param>
/// <param name="TargetSlot">The slot the item moved to.</param>
public sealed record ItemMovedEvent(uint ItemId, GameInventoryType SourceInventory, uint SourceSlot, GameInventoryType TargetInventory, uint TargetSlot);

/// <summary>
/// Fired when an inventory item's properties change (quantity, spiritbond, …).
/// </summary>
/// <param name="ItemId">The item row id.</param>
/// <param name="PreviousQuantity">The quantity before the change.</param>
/// <param name="Quantity">The quantity after the change.</param>
/// <param name="Inventory">The inventory containing the item.</param>
/// <param name="Slot">The item's slot.</param>
public sealed record ItemChangedEvent(uint ItemId, int PreviousQuantity, int Quantity, GameInventoryType Inventory, uint Slot);

/// <summary>
/// Fired when two stacks are merged.
/// </summary>
/// <param name="ItemId">The item row id.</param>
public sealed record ItemMergedEvent(uint ItemId);

/// <summary>
/// Fired when a stack is split.
/// </summary>
/// <param name="ItemId">The item row id.</param>
public sealed record ItemSplitEvent(uint ItemId);

/// <summary>
/// Fired when the total count of an item across the main player inventories changes.
/// Produced for items watched through <c>watcher.Inventory.OnItemCountChanged</c>.
/// </summary>
/// <param name="ItemId">The item row id.</param>
/// <param name="PreviousCount">The previous total count.</param>
/// <param name="Count">The new total count.</param>
public sealed record ItemCountChangedEvent(uint ItemId, long PreviousCount, long Count);

/// <summary>
/// Fired when the local player's gil changes.
/// </summary>
/// <param name="PreviousGil">The previous gil amount.</param>
/// <param name="Gil">The new gil amount.</param>
public sealed record GilChangedEvent(long PreviousGil, long Gil);
