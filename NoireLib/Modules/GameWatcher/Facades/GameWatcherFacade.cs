using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// The base of the public domain facades. Facades are thin: they expose three verbs per domain -
/// subscribe (scoped helpers), query (current state) and wait (through <see cref="GameConditions"/>) -
/// and route everything through the module core. Power users can bypass them entirely with
/// <see cref="NoireGameWatcher.Subscribe{TEvent}"/>.
/// </summary>
public abstract class GameWatcherFacade
{
    private protected GameWatcherFacade(NoireGameWatcher watcher)
        => Watcher = watcher;

    /// <summary>The owning watcher.</summary>
    protected NoireGameWatcher Watcher { get; }

    private protected NoireSubscriptionToken On<TEvent>(
        Action<TEvent>? handler,
        Func<TEvent, Task>? asyncHandler,
        NoireSubscriptionOptions<TEvent>? options,
        string description)
        where TEvent : notnull
    {
        if (handler == null && asyncHandler == null)
            throw new ArgumentNullException(nameof(handler));

        return Watcher.SubscribeCore(handler, asyncHandler, options, NoireGameWatcher.LookupSource(typeof(TEvent)), null, null, description);
    }

    /// <summary>
    /// Copies user options and injects an additional filter in front of the user's own.
    /// </summary>
    private protected static NoireSubscriptionOptions<TEvent> WithFilter<TEvent>(
        NoireSubscriptionOptions<TEvent>? options,
        Func<TEvent, bool> injectedFilter)
    {
        var userFilter = options?.Filter;

        return new NoireSubscriptionOptions<TEvent>
        {
            Key = options?.Key,
            Priority = options?.Priority ?? 0,
            Once = options?.Once ?? false,
            Owner = options?.Owner,
            Filter = userFilter == null ? injectedFilter : evt => injectedFilter(evt) && userFilter(evt),
        };
    }
}
