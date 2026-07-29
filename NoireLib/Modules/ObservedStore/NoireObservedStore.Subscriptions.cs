using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.ObservedStore;

public partial class NoireObservedStore
{
    /// <summary>
    /// Called whenever an observation is written down. The event carries what it replaced, so a consumer can tell a
    /// value that changed from one that was merely confirmed.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnRecorded(
        Action<ObservationRecordedEvent> handler,
        NoireSubscriptionOptions<ObservationRecordedEvent>? options = null)
        => Subscribe(handler, options);

    /// <summary>Called whenever a single observation is deliberately forgotten.</summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnForgotten(
        Action<ObservationForgottenEvent> handler,
        NoireSubscriptionOptions<ObservationForgottenEvent>? options = null)
        => Subscribe(handler, options);

    /// <summary>Called whenever observations are removed in bulk, by prefix, by age, by expiry or by clearing.</summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnPruned(
        Action<ObservationsPrunedEvent> handler,
        NoireSubscriptionOptions<ObservationsPrunedEvent>? options = null)
        => Subscribe(handler, options);

    /// <summary>
    /// Subscribes to any store event type; keyed replacement, priority, filtering, one-shot and owner tagging all
    /// come from <see cref="NoireSubscriptionOptions{TContext}"/>.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The handler invoked for each dispatched event.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken Subscribe<TEvent>(
        Action<TEvent> handler,
        NoireSubscriptionOptions<TEvent>? options = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.Subscribe(typeof(TEvent), context => handler((TEvent)context), MapOptions(options));
    }

    /// <summary>
    /// Subscribes an asynchronous handler to any store event type. The returned task is fire-and-forget; faults are
    /// logged.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The async handler invoked for each dispatched event.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken SubscribeAsync<TEvent>(
        Func<TEvent, Task> handler,
        NoireSubscriptionOptions<TEvent>? options = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return registry.SubscribeAsync(typeof(TEvent), context => handler((TEvent)context), MapOptions(options));
    }

    /// <summary>Removes the subscription registered under a key, whatever its event type.</summary>
    /// <param name="key">The subscription key.</param>
    /// <returns>True when a subscription was removed.</returns>
    public bool Unsubscribe(string key) => registry.Unsubscribe(key);

    /// <summary>Removes every subscription registered with an owner, which is the one-line plugin teardown.</summary>
    /// <param name="owner">The owner passed on subscription.</param>
    /// <returns>How many subscriptions were removed.</returns>
    public int UnsubscribeOwner(object owner) => registry.UnsubscribeOwner(owner);

    /// <summary>How many subscriptions are live across every event type.</summary>
    public int SubscriptionCount => registry.TotalCount;

    #region Dispatch

    private static NoireSubscriptionOptions<object> MapOptions<TEvent>(NoireSubscriptionOptions<TEvent>? options)
        where TEvent : notnull
    {
        if (options == null)
            return new NoireSubscriptionOptions<object>();

        var typedFilter = options.Filter;

        return new NoireSubscriptionOptions<object>
        {
            Key = options.Key,
            Priority = options.Priority,
            Once = options.Once,
            Owner = options.Owner,
            Delivery = options.Delivery,
            Filter = typedFilter == null ? null : context => context is TEvent typed && typedFilter(typed),
        };
    }

    private void RaiseRecorded(ObservationInfo info, ObservationInfo? replaced)
        => Dispatch(new ObservationRecordedEvent(info, replaced));

    private void RaiseForgotten(ObservationInfo info)
        => Dispatch(new ObservationForgottenEvent(info));

    private void RaisePruned(int count, ObservationScope scope, ulong characterId, string reason)
        => Dispatch(new ObservationsPrunedEvent(count, scope, characterId, reason));

    private void Dispatch<TEvent>(TEvent evt) where TEvent : notnull
    {
        registry.Dispatch(typeof(TEvent), evt);
        PublishToEventBus(evt);
    }

    #endregion
}
