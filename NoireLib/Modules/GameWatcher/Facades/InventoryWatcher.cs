using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Inventory facts: typed item added/removed/moved/changed/merged/split events, per-item total-count watching
/// and gil changes.
/// </summary>
public sealed class InventoryWatcher : GameWatcherFacade
{
    internal InventoryWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to items being added to inventories.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnItemAdded(Action<ItemAddedEvent> handler, NoireSubscriptionOptions<ItemAddedEvent>? options = null)
        => On(handler, null, options, nameof(OnItemAdded));

    /// <inheritdoc cref="OnItemAdded(Action{ItemAddedEvent}, NoireSubscriptionOptions{ItemAddedEvent}?)"/>
    public NoireSubscriptionToken OnItemAddedAsync(Func<ItemAddedEvent, Task> handler, NoireSubscriptionOptions<ItemAddedEvent>? options = null)
        => On(null, handler, options, nameof(OnItemAdded));

    /// <summary>
    /// Subscribes to items being removed from inventories.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnItemRemoved(Action<ItemRemovedEvent> handler, NoireSubscriptionOptions<ItemRemovedEvent>? options = null)
        => On(handler, null, options, nameof(OnItemRemoved));

    /// <inheritdoc cref="OnItemRemoved(Action{ItemRemovedEvent}, NoireSubscriptionOptions{ItemRemovedEvent}?)"/>
    public NoireSubscriptionToken OnItemRemovedAsync(Func<ItemRemovedEvent, Task> handler, NoireSubscriptionOptions<ItemRemovedEvent>? options = null)
        => On(null, handler, options, nameof(OnItemRemoved));

    /// <summary>
    /// Subscribes to items moving between inventory slots.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnItemMoved(Action<ItemMovedEvent> handler, NoireSubscriptionOptions<ItemMovedEvent>? options = null)
        => On(handler, null, options, nameof(OnItemMoved));

    /// <inheritdoc cref="OnItemMoved(Action{ItemMovedEvent}, NoireSubscriptionOptions{ItemMovedEvent}?)"/>
    public NoireSubscriptionToken OnItemMovedAsync(Func<ItemMovedEvent, Task> handler, NoireSubscriptionOptions<ItemMovedEvent>? options = null)
        => On(null, handler, options, nameof(OnItemMoved));

    /// <summary>
    /// Subscribes to item property changes (quantity, spiritbond, …).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnItemChanged(Action<ItemChangedEvent> handler, NoireSubscriptionOptions<ItemChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnItemChanged));

    /// <inheritdoc cref="OnItemChanged(Action{ItemChangedEvent}, NoireSubscriptionOptions{ItemChangedEvent}?)"/>
    public NoireSubscriptionToken OnItemChangedAsync(Func<ItemChangedEvent, Task> handler, NoireSubscriptionOptions<ItemChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnItemChanged));

    /// <summary>
    /// Subscribes to stack merges.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnItemMerged(Action<ItemMergedEvent> handler, NoireSubscriptionOptions<ItemMergedEvent>? options = null)
        => On(handler, null, options, nameof(OnItemMerged));

    /// <inheritdoc cref="OnItemMerged(Action{ItemMergedEvent}, NoireSubscriptionOptions{ItemMergedEvent}?)"/>
    public NoireSubscriptionToken OnItemMergedAsync(Func<ItemMergedEvent, Task> handler, NoireSubscriptionOptions<ItemMergedEvent>? options = null)
        => On(null, handler, options, nameof(OnItemMerged));

    /// <summary>
    /// Subscribes to stack splits.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnItemSplit(Action<ItemSplitEvent> handler, NoireSubscriptionOptions<ItemSplitEvent>? options = null)
        => On(handler, null, options, nameof(OnItemSplit));

    /// <inheritdoc cref="OnItemSplit(Action{ItemSplitEvent}, NoireSubscriptionOptions{ItemSplitEvent}?)"/>
    public NoireSubscriptionToken OnItemSplitAsync(Func<ItemSplitEvent, Task> handler, NoireSubscriptionOptions<ItemSplitEvent>? options = null)
        => On(null, handler, options, nameof(OnItemSplit));

    /// <summary>
    /// Subscribes to total-count changes of a specific item across the main player inventories.
    /// </summary>
    /// <param name="itemId">The item row id to watch.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnItemCountChanged(uint itemId, Action<ItemCountChangedEvent> handler, NoireSubscriptionOptions<ItemCountChangedEvent>? options = null)
        => WatchCount(itemId, handler, null, options);

    /// <inheritdoc cref="OnItemCountChanged(uint, Action{ItemCountChangedEvent}, NoireSubscriptionOptions{ItemCountChangedEvent}?)"/>
    public NoireSubscriptionToken OnItemCountChangedAsync(uint itemId, Func<ItemCountChangedEvent, Task> handler, NoireSubscriptionOptions<ItemCountChangedEvent>? options = null)
        => WatchCount(itemId, null, handler, options);

    private NoireSubscriptionToken WatchCount(
        uint itemId,
        Action<ItemCountChangedEvent>? handler,
        Func<ItemCountChangedEvent, Task>? asyncHandler,
        NoireSubscriptionOptions<ItemCountChangedEvent>? options)
    {
        if (handler == null && asyncHandler == null)
            throw new ArgumentNullException(nameof(handler));

        var remove = Watcher.GetSource<InventorySource>(SourceKind.Inventory).AddItemCountWatch(itemId);

        return Watcher.SubscribeCore(
            handler,
            asyncHandler,
            WithFilter(options, evt => evt.ItemId == itemId),
            SourceKind.Inventory,
            null,
            remove,
            $"{nameof(OnItemCountChanged)}({itemId})");
    }

    /// <summary>
    /// Subscribes to gil changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnGilChanged(Action<GilChangedEvent> handler, NoireSubscriptionOptions<GilChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnGilChanged));

    /// <inheritdoc cref="OnGilChanged(Action{GilChangedEvent}, NoireSubscriptionOptions{GilChangedEvent}?)"/>
    public NoireSubscriptionToken OnGilChangedAsync(Func<GilChangedEvent, Task> handler, NoireSubscriptionOptions<GilChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnGilChanged));

    /// <summary>
    /// Counts an item across the main player inventories. Live read (framework thread only);
    /// never activates anything.
    /// </summary>
    /// <param name="itemId">The item row id.</param>
    /// <returns>The total count.</returns>
    public long CountItem(uint itemId)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return InventorySource.CountItem(itemId);
    }

    /// <summary>The local player's gil, or -1 while unavailable. Live read (framework thread only).</summary>
    public long Gil
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return InventorySource.ReadGil();
        }
    }
}
