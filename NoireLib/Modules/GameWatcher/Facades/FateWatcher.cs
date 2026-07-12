using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Fate facts in the current zone: spawn, expiry, progress and state changes (slow cadence by default).
/// </summary>
public sealed class FateWatcher : GameWatcherFacade
{
    internal FateWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to fates appearing in the zone.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnSpawned(Action<FateSpawnedEvent> handler, NoireSubscriptionOptions<FateSpawnedEvent>? options = null)
        => On(handler, null, options, nameof(OnSpawned));

    /// <inheritdoc cref="OnSpawned(Action{FateSpawnedEvent}, NoireSubscriptionOptions{FateSpawnedEvent}?)"/>
    public NoireSubscriptionToken OnSpawnedAsync(Func<FateSpawnedEvent, Task> handler, NoireSubscriptionOptions<FateSpawnedEvent>? options = null)
        => On(null, handler, options, nameof(OnSpawned));

    /// <summary>
    /// Subscribes to fates disappearing from the zone.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnExpired(Action<FateExpiredEvent> handler, NoireSubscriptionOptions<FateExpiredEvent>? options = null)
        => On(handler, null, options, nameof(OnExpired));

    /// <inheritdoc cref="OnExpired(Action{FateExpiredEvent}, NoireSubscriptionOptions{FateExpiredEvent}?)"/>
    public NoireSubscriptionToken OnExpiredAsync(Func<FateExpiredEvent, Task> handler, NoireSubscriptionOptions<FateExpiredEvent>? options = null)
        => On(null, handler, options, nameof(OnExpired));

    /// <summary>
    /// Subscribes to fate progress changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnProgressChanged(Action<FateProgressChangedEvent> handler, NoireSubscriptionOptions<FateProgressChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnProgressChanged));

    /// <inheritdoc cref="OnProgressChanged(Action{FateProgressChangedEvent}, NoireSubscriptionOptions{FateProgressChangedEvent}?)"/>
    public NoireSubscriptionToken OnProgressChangedAsync(Func<FateProgressChangedEvent, Task> handler, NoireSubscriptionOptions<FateProgressChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnProgressChanged));

    /// <summary>
    /// Subscribes to fate state changes (preparing, running, ending, …).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnStateChanged(Action<FateStateChangedEvent> handler, NoireSubscriptionOptions<FateStateChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnStateChanged));

    /// <inheritdoc cref="OnStateChanged(Action{FateStateChangedEvent}, NoireSubscriptionOptions{FateStateChangedEvent}?)"/>
    public NoireSubscriptionToken OnStateChangedAsync(Func<FateStateChangedEvent, Task> handler, NoireSubscriptionOptions<FateStateChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnStateChanged));

    /// <summary>
    /// Snapshots every fate currently in the zone. Live read (framework thread only);
    /// never activates anything.
    /// </summary>
    /// <returns>The fate snapshots.</returns>
    public IReadOnlyList<FateSnapshot> GetAll()
    {
        NoireGameWatcher.EnsureFrameworkThread();

        var now = DateTimeOffset.UtcNow;
        var result = new List<FateSnapshot>();

        foreach (var fate in NoireService.FateTable)
        {
            if (fate != null)
                result.Add(FateSource.Capture(fate, now));
        }

        return result;
    }
}
