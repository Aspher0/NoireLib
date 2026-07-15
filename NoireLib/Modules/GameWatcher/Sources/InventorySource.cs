using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Wraps the granular native inventory events into typed item events, plus item-count and gil conveniences
/// computed over the main player inventories. Event-driven with a light per-change recount.
/// </summary>
internal sealed class InventorySource : GameWatcherSource
{
    private static readonly GameInventoryType[] CountedInventories =
    {
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4,
    };

    // Granular item events are reported only for the player's own carried inventories. Transient staging
    // containers (trade hand-in, mail, examine, reconstruction buyback, …) and storage that is not the player's
    // pockets (retainer, free company, housing, market) are excluded - otherwise a trade or retainer session
    // spams add/remove/move churn as items shuffle through those containers. Gil (item id 1) is never reported
    // as an item either; it has its own GilChangedEvent.
    private static readonly HashSet<GameInventoryType> OwnedInventories = new()
    {
        GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4,
        GameInventoryType.EquippedItems, GameInventoryType.Currency, GameInventoryType.Crystals, GameInventoryType.KeyItems,
        GameInventoryType.ArmoryMainHand, GameInventoryType.ArmoryOffHand, GameInventoryType.ArmoryHead, GameInventoryType.ArmoryBody,
        GameInventoryType.ArmoryHands, GameInventoryType.ArmoryWaist, GameInventoryType.ArmoryLegs, GameInventoryType.ArmoryFeets,
        GameInventoryType.ArmoryEar, GameInventoryType.ArmoryNeck, GameInventoryType.ArmoryWrist, GameInventoryType.ArmoryRings,
        GameInventoryType.ArmorySoulCrystal,
        GameInventoryType.SaddleBag1, GameInventoryType.SaddleBag2, GameInventoryType.PremiumSaddleBag1, GameInventoryType.PremiumSaddleBag2,
        GameInventoryType.Cosmopouch1, GameInventoryType.Cosmopouch2,
    };

    private static bool IsOwned(GameInventoryType type) => OwnedInventories.Contains(type);

    // Item id 0 is an empty slot, item id 1 is gil (reported via GilChangedEvent, never as an item event).
    private static bool IsReportableItem(uint itemId) => itemId > 1;

    private readonly Dictionary<uint, int> watchedItemCounts = new();
    private readonly object watchGate = new();
    private readonly Dictionary<uint, long> lastCounts = new();
    private long lastGil = -1;

    public InventorySource(NoireGameWatcher owner) : base(owner, SourceKind.Inventory) { }

    /// <inheritdoc/>
    public override bool IsPolling => false;

    /// <summary>Registers an item id whose total count should be watched, returns the removal action.</summary>
    internal Action AddItemCountWatch(uint itemId)
    {
        lock (watchGate)
        {
            watchedItemCounts.TryGetValue(itemId, out var count);
            watchedItemCounts[itemId] = count + 1;

            if (IsRunning && count == 0)
                lastCounts[itemId] = CountItem(itemId);
        }

        return () =>
        {
            lock (watchGate)
            {
                if (!watchedItemCounts.TryGetValue(itemId, out var count))
                    return;

                if (count <= 1)
                {
                    watchedItemCounts.Remove(itemId);
                    lastCounts.Remove(itemId);
                }
                else
                {
                    watchedItemCounts[itemId] = count - 1;
                }
            }
        };
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        lock (watchGate)
        {
            lastCounts.Clear();

            foreach (var itemId in watchedItemCounts.Keys)
                lastCounts[itemId] = CountItem(itemId);
        }

        lastGil = ReadGil();

        NoireService.GameInventory.InventoryChanged += OnInventoryChanged;
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        NoireService.GameInventory.InventoryChanged -= OnInventoryChanged;

        lock (watchGate)
            lastCounts.Clear();

        lastGil = -1;
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        foreach (var args in events)
        {
            try
            {
                DispatchTyped(args);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(Owner, ex, "Failed to process an inventory change.");
            }
        }

        RecountWatchedItems();
        CheckGil();
    }

    private void DispatchTyped(InventoryEventArgs args)
    {
        switch (args)
        {
            case InventoryItemAddedArgs added when IsOwned(added.Inventory) && IsReportableItem(added.Item.ItemId):
                Owner.DispatchEvent(new ItemAddedEvent(added.Item.ItemId, added.Item.Quantity, added.Item.IsHq, added.Inventory, added.Slot));
                break;

            case InventoryItemRemovedArgs removed when IsOwned(removed.Inventory) && IsReportableItem(removed.Item.ItemId):
                Owner.DispatchEvent(new ItemRemovedEvent(removed.Item.ItemId, removed.Item.Quantity, removed.Inventory, removed.Slot));
                break;

            // A move is only an "inventory" move when both endpoints are the player's own - a transfer to a
            // staging container (trade, mail, retainer, …) is that container's business, not an inventory move.
            case InventoryItemMovedArgs moved when IsReportableItem(moved.Item.ItemId) && IsOwned(moved.SourceInventory) && IsOwned(moved.TargetInventory):
                Owner.DispatchEvent(new ItemMovedEvent(moved.Item.ItemId, moved.SourceInventory, moved.SourceSlot, moved.TargetInventory, moved.TargetSlot));
                break;

            case InventoryItemChangedArgs changed when IsOwned(changed.Inventory) && IsReportableItem(changed.Item.ItemId):
                Owner.DispatchEvent(new ItemChangedEvent(changed.Item.ItemId, changed.OldItemState.Quantity, changed.Item.Quantity, changed.Inventory, changed.Slot));
                break;

            case InventoryItemMergedArgs merged when IsReportableItem(merged.Item.ItemId) && IsOwned(merged.SourceInventory) && IsOwned(merged.TargetInventory):
                Owner.DispatchEvent(new ItemMergedEvent(merged.Item.ItemId));
                break;

            case InventoryItemSplitArgs split when IsReportableItem(split.Item.ItemId) && IsOwned(split.SourceInventory) && IsOwned(split.TargetInventory):
                Owner.DispatchEvent(new ItemSplitEvent(split.Item.ItemId));
                break;
        }
    }

    private void RecountWatchedItems()
    {
        (uint ItemId, long Previous)[] toCheck;

        lock (watchGate)
        {
            if (watchedItemCounts.Count == 0)
                return;

            toCheck = watchedItemCounts.Keys.Select(id => (id, lastCounts.TryGetValue(id, out var c) ? c : 0L)).ToArray();
        }

        foreach (var (itemId, previous) in toCheck)
        {
            var current = CountItem(itemId);

            if (current == previous)
                continue;

            lock (watchGate)
                lastCounts[itemId] = current;

            Owner.DispatchEvent(new ItemCountChangedEvent(itemId, previous, current));
        }
    }

    private void CheckGil()
    {
        var gil = ReadGil();

        if (gil < 0 || gil == lastGil)
            return;

        var previous = lastGil;
        lastGil = gil;

        if (previous >= 0)
            Owner.DispatchEvent(new GilChangedEvent(previous, gil));
    }

    /// <summary>Counts an item across the main player inventories (live read).</summary>
    internal static long CountItem(uint itemId)
    {
        long total = 0;

        foreach (var inventoryType in CountedInventories)
        {
            var items = NoireService.GameInventory.GetInventoryItems(inventoryType);

            foreach (var item in items)
            {
                if (item.ItemId == itemId)
                    total += item.Quantity;
            }
        }

        return total;
    }

    /// <summary>Reads the local player's gil (live read), or -1 when unavailable.</summary>
    internal static unsafe long ReadGil()
    {
        var manager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
        return manager == null ? -1 : manager->GetGil();
    }
}
