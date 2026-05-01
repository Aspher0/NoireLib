using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using NoireLib.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks inventory changes by wrapping the <see cref="IGameInventory.InventoryChanged"/> event with an <see cref="EventWrapper"/>.
/// </summary>
public sealed class InventoryTracker : GameStateSubTracker
{
    private readonly EventWrapper inventoryChangedEvent;
    private readonly LinkedList<InventoryChangedEvent> changeHistory = new();
    private readonly object historyLock = new();
    private readonly int historyCapacity;
    private long totalChangesObserved;
    private DateTimeOffset? lastChangeAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    /// <param name="historyCapacity">Unused parameter reserved for future inventory history tracking.</param>
    internal InventoryTracker(NoireGameStateWatcher owner, bool active, int historyCapacity = 0) : base(owner, active)
    {
        this.historyCapacity = historyCapacity > 0 ? historyCapacity : 50;
        inventoryChangedEvent = new(NoireService.GameInventory, nameof(IGameInventory.InventoryChanged), name: $"{nameof(InventoryTracker)}.InventoryChanged");

        inventoryChangedEvent.AddCallback("handler", HandleInventoryChanged);
    }

    /// <summary>
    /// Gets the total number of inventory change batches observed since activation.
    /// </summary>
    public long TotalChangesObserved => totalChangesObserved;

    /// <summary>
    /// Gets the configured maximum history size.
    /// </summary>
    public int HistoryCapacity => historyCapacity;

    /// <summary>
    /// Gets the current number of entries stored in the history buffer.
    /// </summary>
    public int HistoryCount
    {
        get
        {
            lock (historyLock)
                return changeHistory.Count;
        }
    }

    /// <summary>
    /// Gets the UTC timestamp of the most recent observed inventory change, if any.
    /// </summary>
    public DateTimeOffset? LastChangeAt => lastChangeAt;

    /// <summary>
    /// Gets a value indicating whether any inventory changes have been observed since the last reset.
    /// </summary>
    public bool HasObservedChanges => TotalChangesObserved > 0;

    /// <summary>
    /// Raised when the game inventory changes.
    /// </summary>
    public event Action<InventoryChangedEvent>? OnInventoryChanged;

    /// <summary>
    /// Resets the <see cref="TotalChangesObserved"/> counter to zero.
    /// </summary>
    public void ResetCounter()
    {
        totalChangesObserved = 0;
        lastChangeAt = null;
    }

    /// <summary>
    /// Returns a snapshot of the recent inventory change history, newest first.
    /// </summary>
    /// <returns>An array of recent inventory change events.</returns>
    public InventoryChangedEvent[] GetRecentChanges()
    {
        lock (historyLock)
            return changeHistory.ToArray();
    }

    /// <summary>
    /// Returns recent inventory change events that match the specified predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>An array of matching inventory change events.</returns>
    public InventoryChangedEvent[] GetRecentChanges(Func<InventoryChangedEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (historyLock)
            return changeHistory.Where(predicate).ToArray();
    }

    /// <summary>
    /// Returns the most recent inventory change event, or <see langword="null"/> if none have been observed.
    /// </summary>
    /// <returns>The most recent inventory change event, or <see langword="null"/> if none exist.</returns>
    public InventoryChangedEvent? GetLastChange()
    {
        lock (historyLock)
            return changeHistory.First?.Value;
    }

    /// <summary>
    /// Clears the inventory change history and resets the counter state.
    /// </summary>
    public void ClearHistory()
    {
        lock (historyLock)
            changeHistory.Clear();

        ResetCounter();
    }

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> after the specified number of inventory change batches has been observed.
    /// </summary>
    /// <param name="minimumBatchCount">The minimum number of observed inventory change batches required.</param>
    /// <returns>A predicate returning <see langword="true"/> when the threshold has been reached.</returns>
    public Func<bool> WaitForAnyChange(long minimumBatchCount = 1) => () => TotalChangesObserved >= minimumBatchCount;

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        ClearHistory();
        inventoryChangedEvent.Enable();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(InventoryTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        inventoryChangedEvent.Disable();
        ClearHistory();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(InventoryTracker)} deactivated.");
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        inventoryChangedEvent.Dispose();
    }

    private void HandleInventoryChanged(IReadOnlyCollection<InventoryEventArgs> changes)
    {
        totalChangesObserved++;
        lastChangeAt = DateTimeOffset.UtcNow;

        var evt = new InventoryChangedEvent(changes.ToArray());

        lock (historyLock)
        {
            changeHistory.AddFirst(evt);

            while (changeHistory.Count > historyCapacity)
                changeHistory.RemoveLast();
        }

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"Inventory changed ({changes.Count} entries).");

        PublishEvent(OnInventoryChanged, evt);
    }
}
