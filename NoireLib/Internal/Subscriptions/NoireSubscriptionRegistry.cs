using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Core.Subscriptions;

/// <summary>
/// A thread-safe, generic subscription registry - the shared primitive behind NoireLib callback systems.<br/>
/// Supports priority ordering (higher first), keyed replacement, filters, one-shot subscriptions, async handlers,
/// owner-based bulk unsubscription, and per-subscription delivery thread selection.
/// </summary>
/// <typeparam name="TKey">The type used to group subscriptions (e.g. an event <see cref="Type"/>, a channel name, or a constant for single-channel registries).</typeparam>
/// <typeparam name="TContext">The context type passed to handlers on dispatch.</typeparam>
public sealed class NoireSubscriptionRegistry<TKey, TContext> where TKey : notnull
{
    private sealed class Entry
    {
        public required NoireSubscriptionToken Token { get; init; }
        public required Delegate Handler { get; init; }
        public required bool IsAsync { get; init; }
        public Func<TContext, bool>? Filter { get; init; }
        public bool Once { get; init; }
        public object? Owner { get; init; }
        public SubscriptionDelivery Delivery { get; init; }
        public int Priority { get; init; }

        private int onceClaimed;

        public bool TryClaimOnce()
            => Interlocked.Exchange(ref onceClaimed, 1) == 0;
    }

    private readonly Dictionary<TKey, List<Entry>> subscriptions = new();
    private readonly Dictionary<string, (TKey GroupKey, Entry Entry)> keyedSubscriptions = new();
    private readonly object gate = new();
    private readonly Action<Exception, string>? exceptionHandler;
    private readonly bool propagateHandlerExceptions;

    /// <summary>
    /// Creates a new subscription registry.
    /// </summary>
    /// <param name="exceptionHandler">
    /// An optional callback invoked when a handler throws, receiving the exception and a short description of the failing subscription.<br/>
    /// When null, exceptions are logged through <see cref="NoireLogger"/>. Not used when <paramref name="propagateHandlerExceptions"/> is set.
    /// </param>
    /// <param name="propagateHandlerExceptions">
    /// When set, the registry does not catch exceptions thrown by handlers: a synchronous throw propagates out of
    /// <see cref="Dispatch"/> / <see cref="DispatchAsync"/> and aborts the remaining handlers in the snapshot, and an
    /// async fault is left to the caller (awaited by <see cref="DispatchAsync"/>, or fire-and-forget under
    /// <see cref="Dispatch"/>). This is for callers such as the EventBus that apply their own per-handler exception
    /// policy and need a handler fault to reach the publisher. Defaults to <see langword="false"/>, the catch-and-report behavior.
    /// </param>
    public NoireSubscriptionRegistry(Action<Exception, string>? exceptionHandler = null, bool propagateHandlerExceptions = false)
    {
        this.exceptionHandler = exceptionHandler;
        this.propagateHandlerExceptions = propagateHandlerExceptions;
    }

    /// <summary>
    /// The total number of active subscriptions across all keys.
    /// </summary>
    public int TotalCount
    {
        get
        {
            lock (gate)
                return subscriptions.Values.Sum(list => list.Count);
        }
    }

    /// <summary>
    /// Gets the keys that currently have at least one subscription.
    /// </summary>
    public IReadOnlyCollection<TKey> Keys
    {
        get
        {
            lock (gate)
                return subscriptions.Keys.ToArray();
        }
    }

    /// <summary>
    /// Subscribes a synchronous handler under the given key.
    /// </summary>
    /// <param name="key">The key to group the subscription under.</param>
    /// <param name="handler">The handler to invoke when a context is dispatched for the key.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken Subscribe(TKey key, Action<TContext> handler, NoireSubscriptionOptions<TContext>? options = null)
        => AddEntry(key, handler, isAsync: false, options);

    /// <summary>
    /// Subscribes an asynchronous handler under the given key.<br/>
    /// The returned task is not awaited by dispatch; faults are reported to the registry's exception handler.
    /// </summary>
    /// <param name="key">The key to group the subscription under.</param>
    /// <param name="handler">The async handler to invoke when a context is dispatched for the key.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken SubscribeAsync(TKey key, Func<TContext, Task> handler, NoireSubscriptionOptions<TContext>? options = null)
        => AddEntry(key, handler, isAsync: true, options);

    /// <summary>
    /// Dispatches a context to all subscriptions registered under the given key, in priority order (higher first).
    /// </summary>
    /// <param name="key">The key to dispatch for.</param>
    /// <param name="context">The context passed to each handler.</param>
    /// <returns>The number of subscriptions the context was delivered to (before filtering on non-inline deliveries).</returns>
    public int Dispatch(TKey key, TContext context)
    {
        Entry[] snapshot;

        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var list) || list.Count == 0)
                return 0;

            snapshot = list.ToArray();
        }

        var delivered = 0;

        foreach (var entry in snapshot)
        {
            if (!entry.Token.IsActive)
                continue;

            if (entry.Delivery == SubscriptionDelivery.FrameworkThread
                && NoireService.IsInitialized()
                && !NoireService.Framework.IsInFrameworkUpdateThread)
            {
                // Counted before filtering (per the return-value contract): the filter runs, and a one-shot is
                // only claimed, later on the framework thread inside Deliver.
                delivered++;
                NoireService.Framework.RunOnFrameworkThread(() => Deliver(key, entry, context));
            }
            else if (Deliver(key, entry, context))
            {
                delivered++;
            }
        }

        return delivered;
    }

    /// <summary>
    /// Dispatches a context to all subscriptions for the given key in priority order, awaiting async handlers.<br/>
    /// Synchronous handlers run inline in order; asynchronous handlers are started in order and all awaited together
    /// before this returns. Filtering, one-shot claiming and priority ordering are shared with <see cref="Dispatch"/>
    /// so the two paths cannot drift.
    /// </summary>
    /// <param name="key">The key to dispatch for.</param>
    /// <param name="context">The context passed to each handler.</param>
    /// <returns>The number of subscriptions the context was delivered to (before filtering on non-inline deliveries).</returns>
    public async Task<int> DispatchAsync(TKey key, TContext context)
    {
        Entry[] snapshot;

        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var list) || list.Count == 0)
                return 0;

            snapshot = list.ToArray();
        }

        var delivered = 0;
        List<Task>? pending = null;

        foreach (var entry in snapshot)
        {
            if (!entry.Token.IsActive)
                continue;

            if (entry.Delivery == SubscriptionDelivery.FrameworkThread
                && NoireService.IsInitialized()
                && !NoireService.Framework.IsInFrameworkUpdateThread)
            {
                // Counted before filtering (per the return-value contract), and the filter, once-claim and handler
                // all run on the framework thread, just like the synchronous Dispatch marshals them there.
                delivered++;
                await AsyncHelper.StartOnFrameworkThreadAsync(async () =>
                {
                    if (ShouldDeliver(key, entry, context))
                        await InvokeEntryAsync(entry, context);
                });
                continue;
            }

            if (!ShouldDeliver(key, entry, context))
                continue;

            // A synchronous handler runs inline here (its task is already completed); an async handler is started
            // and collected. A synchronous throw under propagation surfaces at this call and aborts the loop.
            (pending ??= new List<Task>()).Add(InvokeEntryAsync(entry, context));
            delivered++;
        }

        if (pending != null)
            await Task.WhenAll(pending);

        return delivered;
    }

    /// <summary>
    /// Determines whether at least one subscription exists for the given key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key has subscribers; otherwise, false.</returns>
    public bool HasSubscribers(TKey key)
    {
        lock (gate)
            return subscriptions.TryGetValue(key, out var list) && list.Count > 0;
    }

    /// <summary>
    /// Gets the number of subscriptions registered under the given key.
    /// </summary>
    /// <param name="key">The key to count subscribers for.</param>
    /// <returns>The number of subscriptions.</returns>
    public int Count(TKey key)
    {
        lock (gate)
            return subscriptions.TryGetValue(key, out var list) ? list.Count : 0;
    }

    /// <summary>
    /// Unsubscribes the subscription identified by its string key.
    /// </summary>
    /// <param name="subscriptionKey">The string key the subscription was registered under.</param>
    /// <returns>True if a subscription was removed; otherwise, false.</returns>
    public bool Unsubscribe(string subscriptionKey)
    {
        lock (gate)
        {
            if (!keyedSubscriptions.TryGetValue(subscriptionKey, out var keyed))
                return false;

            RemoveEntryUnderLock(keyed.GroupKey, keyed.Entry);
            keyed.Entry.Token.Invalidate();
            return true;
        }
    }

    /// <summary>
    /// Unsubscribes all subscriptions registered with the given owner object.
    /// </summary>
    /// <param name="owner">The owner whose subscriptions should be removed.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (gate)
        {
            var toRemove = subscriptions
                .SelectMany(pair => pair.Value.Where(e => ReferenceEquals(e.Owner, owner)).Select(e => (pair.Key, Entry: e)))
                .ToList();

            foreach (var (groupKey, entry) in toRemove)
            {
                RemoveEntryUnderLock(groupKey, entry);
                entry.Token.Invalidate();
            }

            return toRemove.Count;
        }
    }

    /// <summary>
    /// Removes all subscriptions registered under the given key.
    /// </summary>
    /// <param name="key">The key to clear.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int Clear(TKey key)
    {
        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var list))
                return 0;

            foreach (var entry in list)
            {
                entry.Token.Invalidate();

                if (entry.Token.Key != null)
                    keyedSubscriptions.Remove(entry.Token.Key);
            }

            var removed = list.Count;
            subscriptions.Remove(key);
            return removed;
        }
    }

    /// <summary>
    /// Removes all subscriptions from the registry.
    /// </summary>
    /// <returns>The number of subscriptions removed.</returns>
    public int ClearAll()
    {
        lock (gate)
        {
            var removed = 0;

            foreach (var list in subscriptions.Values)
            {
                foreach (var entry in list)
                    entry.Token.Invalidate();

                removed += list.Count;
            }

            subscriptions.Clear();
            keyedSubscriptions.Clear();
            return removed;
        }
    }

    private NoireSubscriptionToken AddEntry(TKey key, Delegate handler, bool isAsync, NoireSubscriptionOptions<TContext>? options)
    {
        ArgumentNullException.ThrowIfNull(handler);

        options ??= new NoireSubscriptionOptions<TContext>();

        Entry? entry = null;

        var token = new NoireSubscriptionToken(options.Key, options.Priority, _ =>
        {
            if (entry != null)
                RemoveEntry(key, entry, invalidateToken: false);
        });

        entry = new Entry
        {
            Token = token,
            Handler = handler,
            IsAsync = isAsync,
            Filter = options.Filter,
            Once = options.Once,
            Owner = options.Owner,
            Delivery = options.Delivery,
            Priority = options.Priority,
        };

        lock (gate)
        {
            if (options.Key != null && keyedSubscriptions.TryGetValue(options.Key, out var existing))
            {
                RemoveEntryUnderLock(existing.GroupKey, existing.Entry);
                existing.Entry.Token.Invalidate();
            }

            if (!subscriptions.TryGetValue(key, out var list))
            {
                list = new List<Entry>();
                subscriptions[key] = list;
            }

            // Sorted insert: descending priority, stable (equal priorities keep subscription order).
            var index = list.Count;

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Priority < entry.Priority)
                {
                    index = i;
                    break;
                }
            }

            list.Insert(index, entry);

            if (options.Key != null)
                keyedSubscriptions[options.Key] = (key, entry);
        }

        return token;
    }

    private void RemoveEntry(TKey key, Entry entry, bool invalidateToken)
    {
        lock (gate)
        {
            RemoveEntryUnderLock(key, entry);
        }

        if (invalidateToken)
            entry.Token.Invalidate();
    }

    private void RemoveEntryUnderLock(TKey key, Entry entry)
    {
        if (subscriptions.TryGetValue(key, out var list))
        {
            list.Remove(entry);

            if (list.Count == 0)
                subscriptions.Remove(key);
        }

        if (entry.Token.Key != null
            && keyedSubscriptions.TryGetValue(entry.Token.Key, out var keyed)
            && ReferenceEquals(keyed.Entry, entry))
        {
            keyedSubscriptions.Remove(entry.Token.Key);
        }
    }

    /// <summary>
    /// Applies the entry's filter, then - for one-shot subscriptions - claims and removes it, and finally invokes
    /// the handler. The filter is evaluated <b>before</b> the once-claim so a non-matching context never consumes a
    /// filtered one-shot subscription. Runs on the caller's thread: inline for inline delivery, or on the framework
    /// thread for marshaled delivery, so a FrameworkThread filter still sees game state from the framework thread.
    /// </summary>
    /// <returns>True if the handler was invoked; false if the filter rejected the context or the once-claim was lost.</returns>
    private bool Deliver(TKey key, Entry entry, TContext context)
    {
        if (!ShouldDeliver(key, entry, context))
            return false;

        InvokeEntry(entry, context);
        return true;
    }

    /// <summary>
    /// The filter-and-once core shared by <see cref="Dispatch"/> and <see cref="DispatchAsync"/>, so the two paths
    /// decide delivery identically. Applies the filter first, then, for a one-shot subscription, claims and removes
    /// it, so a non-matching context never consumes a filtered one-shot.
    /// </summary>
    /// <returns>True when the handler should be invoked; false when the filter rejected the context or the once-claim was lost.</returns>
    private bool ShouldDeliver(TKey key, Entry entry, TContext context)
    {
        if (entry.Filter != null && !SafeFilter(entry, context))
            return false;

        if (entry.Once)
        {
            if (!entry.TryClaimOnce())
                return false;

            RemoveEntry(key, entry, invalidateToken: true);
        }

        return true;
    }

    private bool SafeFilter(Entry entry, TContext context)
    {
        try
        {
            return entry.Filter!(context);
        }
        catch (Exception ex)
        {
            ReportException(ex, $"filter of subscription {entry.Token}");
            return false;
        }
    }

    private void InvokeEntry(Entry entry, TContext context)
    {
        if (propagateHandlerExceptions)
        {
            // The caller owns exception handling: a synchronous throw propagates out of Dispatch and aborts the
            // rest of the snapshot, and an async handler is started fire-and-forget without a fault reporter.
            if (entry.IsAsync)
                _ = ((Func<TContext, Task>)entry.Handler)(context);
            else
                ((Action<TContext>)entry.Handler)(context);

            return;
        }

        try
        {
            if (entry.IsAsync)
            {
                var task = ((Func<TContext, Task>)entry.Handler)(context);

                _ = task.ContinueWith(
                    t => ReportException(t.Exception!.GetBaseException(), $"async handler of subscription {entry.Token}"),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            else
            {
                ((Action<TContext>)entry.Handler)(context);
            }
        }
        catch (Exception ex)
        {
            ReportException(ex, $"handler of subscription {entry.Token}");
        }
    }

    /// <summary>
    /// Awaited counterpart to <see cref="InvokeEntry"/>, used by <see cref="DispatchAsync"/>. A synchronous handler
    /// runs inline (returning a completed task); an async handler is awaited. Under propagation, a fault surfaces to
    /// the awaiting dispatcher; otherwise it is caught and reported.
    /// </summary>
    private async Task InvokeEntryAsync(Entry entry, TContext context)
    {
        if (propagateHandlerExceptions)
        {
            if (entry.IsAsync)
                await ((Func<TContext, Task>)entry.Handler)(context);
            else
                ((Action<TContext>)entry.Handler)(context);

            return;
        }

        try
        {
            if (entry.IsAsync)
                await ((Func<TContext, Task>)entry.Handler)(context);
            else
                ((Action<TContext>)entry.Handler)(context);
        }
        catch (Exception ex)
        {
            ReportException(ex, $"handler of subscription {entry.Token}");
        }
    }

    private void ReportException(Exception ex, string description)
    {
        if (exceptionHandler != null)
        {
            try
            {
                exceptionHandler(ex, description);
                return;
            }
            catch
            {
                // Fall through to the default logger if the custom handler itself throws.
            }
        }

        NoireLogger.LogError(ex, $"Unhandled exception in {description}.");
    }
}
