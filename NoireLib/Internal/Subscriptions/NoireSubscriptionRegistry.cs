using NoireLib.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Core.Subscriptions;

/// <summary>
/// The thread-safe subscription registry behind NoireLib's callbacks: priorities (higher first), keyed replacement,
/// filters, one-shots, async handlers, owner-based removal and a delivery thread per subscription.
/// </summary>
/// <typeparam name="TKey">What subscriptions are grouped by, such as an event type or a channel name.</typeparam>
/// <typeparam name="TContext">The context handlers receive.</typeparam>
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

    // Entries is touched under the gate. Snapshot is rebuilt from it there and read without it by dispatch.
    private sealed class Bucket
    {
        public readonly List<Entry> Entries = new();
        public volatile Entry[] Snapshot = Array.Empty<Entry>();

        public void Republish()
            => Snapshot = Entries.Count == 0 ? Array.Empty<Entry>() : Entries.ToArray();
    }

    private readonly ConcurrentDictionary<TKey, Bucket> subscriptions = new();
    private readonly Dictionary<string, (TKey GroupKey, Entry Entry)> keyedSubscriptions = new();
    private readonly object gate = new();
    private readonly Action<Exception, string>? exceptionHandler;
    private readonly bool propagateHandlerExceptions;

    /// <summary>Creates a new subscription registry.</summary>
    /// <param name="exceptionHandler">Receives a throwing handler's exception and description. Null logs through <see cref="NoireLogger"/>.</param>
    /// <param name="propagateHandlerExceptions">
    /// Whether handler exceptions reach the dispatcher, aborting the remaining handlers. A marshaled framework-thread
    /// delivery has no caller left and is still reported.
    /// </param>
    public NoireSubscriptionRegistry(Action<Exception, string>? exceptionHandler = null, bool propagateHandlerExceptions = false)
    {
        this.exceptionHandler = exceptionHandler;
        this.propagateHandlerExceptions = propagateHandlerExceptions;
    }

    // Replaces the framework-thread hop of Dispatch: when set, every FrameworkThread entry is handed to it.
    internal Func<Action, Task>? FrameworkRunnerOverride { get; set; }

    /// <summary>The total number of active subscriptions across all keys.</summary>
    public int TotalCount
    {
        get
        {
            lock (gate)
            {
                var total = 0;

                foreach (var bucket in subscriptions.Values)
                    total += bucket.Entries.Count;

                return total;
            }
        }
    }

    /// <summary>Gets the keys that currently have at least one subscription.</summary>
    public IReadOnlyCollection<TKey> Keys
    {
        get
        {
            lock (gate)
                return subscriptions.Keys.ToArray();
        }
    }

    /// <summary>Subscribes a synchronous handler under the given key.</summary>
    /// <param name="key">The key to group the subscription under.</param>
    /// <param name="handler">The handler to invoke when a context is dispatched for the key.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes the handler when disposed.</returns>
    public NoireSubscriptionToken Subscribe(TKey key, Action<TContext> handler, NoireSubscriptionOptions<TContext>? options = null)
        => AddEntry(key, handler, isAsync: false, options);

    /// <summary>Subscribes an async handler. Dispatch does not await it: faults go to the exception handler.</summary>
    /// <param name="key">The key to group the subscription under.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">The subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken SubscribeAsync(TKey key, Func<TContext, Task> handler, NoireSubscriptionOptions<TContext>? options = null)
        => AddEntry(key, handler, isAsync: true, options);

    /// <summary>Dispatches a context to a key's subscriptions, higher priority first. Inline deliveries allocate nothing.</summary>
    /// <param name="key">The key to dispatch for.</param>
    /// <param name="context">The context passed to each handler.</param>
    /// <returns>How many subscriptions received it, counted before filtering on non-inline deliveries.</returns>
    public int Dispatch(TKey key, TContext context)
    {
        if (!subscriptions.TryGetValue(key, out var bucket))
            return 0;

        var delivered = 0;

        foreach (var entry in bucket.Snapshot)
        {
            if (!entry.Token.IsActive)
                continue;

            if (entry.Delivery == SubscriptionDelivery.FrameworkThread && IsOffFrameworkThread())
            {
                // Counted before filtering: the filter and the one-shot claim run later, on the framework thread.
                delivered++;
                DeliverOnFrameworkThread(key, entry, context);
            }
            else if (Deliver(key, entry, context))
            {
                delivered++;
            }
        }

        return delivered;
    }

    /// <summary>Dispatches like <see cref="Dispatch"/>, then awaits every async handler it started.</summary>
    /// <param name="key">The key to dispatch for.</param>
    /// <param name="context">The context passed to each handler.</param>
    /// <returns>How many subscriptions received it, counted before filtering on non-inline deliveries.</returns>
    public async Task<int> DispatchAsync(TKey key, TContext context)
    {
        if (!subscriptions.TryGetValue(key, out var bucket))
            return 0;

        var delivered = 0;
        List<Task>? pending = null;

        foreach (var entry in bucket.Snapshot)
        {
            if (!entry.Token.IsActive)
                continue;

            if (entry.Delivery == SubscriptionDelivery.FrameworkThread
                && NoireService.IsInitialized()
                && !NoireService.Framework.IsInFrameworkUpdateThread)
            {
                // Counted before filtering: the filter, claim and handler run on the framework thread.
                delivered++;
                await DeliverOnFrameworkThreadAsync(key, entry, context);
                continue;
            }

            if (!ShouldDeliver(key, entry, context))
                continue;

            // A synchronous handler runs inline. A synchronous throw under propagation aborts the loop here.
            (pending ??= new List<Task>()).Add(InvokeEntryAsync(entry, context));
            delivered++;
        }

        if (pending != null)
            await Task.WhenAll(pending);

        return delivered;
    }

    /// <summary>Determines whether at least one subscription exists for the given key.</summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key has subscribers; otherwise, false.</returns>
    public bool HasSubscribers(TKey key)
        => subscriptions.TryGetValue(key, out var bucket) && bucket.Snapshot.Length > 0;

    /// <summary>Gets the number of subscriptions registered under the given key.</summary>
    /// <param name="key">The key to count subscribers for.</param>
    /// <returns>The number of subscriptions.</returns>
    public int Count(TKey key)
        => subscriptions.TryGetValue(key, out var bucket) ? bucket.Snapshot.Length : 0;

    /// <summary>Unsubscribes the subscription identified by its string key.</summary>
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

    /// <summary>Unsubscribes the subscription a token represents, when this registry issued the token.</summary>
    /// <param name="token">A token returned by <see cref="Subscribe"/> or <see cref="SubscribeAsync"/>.</param>
    /// <returns>True if this call removed the subscription; otherwise, false.</returns>
    public bool Unsubscribe(NoireSubscriptionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return ReferenceEquals(token.Issuer, this) && token.TryDispose();
    }

    /// <summary>Unsubscribes all subscriptions registered with the given owner object.</summary>
    /// <param name="owner">The owner whose subscriptions should be removed.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (gate)
        {
            var removed = 0;

            foreach (var pair in subscriptions)
                removed += RemoveOwnedUnderLock(pair.Key, pair.Value, owner);

            return removed;
        }
    }

    /// <summary>
    /// Unsubscribes the subscriptions under the given key that were registered with the given owner object.
    /// </summary>
    /// <param name="key">The key whose subscriptions are searched.</param>
    /// <param name="owner">The owner whose subscriptions should be removed.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeOwner(TKey key, object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (gate)
            return subscriptions.TryGetValue(key, out var bucket) ? RemoveOwnedUnderLock(key, bucket, owner) : 0;
    }

    /// <summary>
    /// Unsubscribes the first subscription under the given key in dispatch order, optionally restricted to an owner.
    /// </summary>
    /// <param name="key">The key whose subscriptions are searched.</param>
    /// <param name="owner">The owner the removed subscription must have, or null for any subscription.</param>
    /// <returns>True if a subscription was removed; otherwise, false.</returns>
    public bool UnsubscribeFirst(TKey key, object? owner = null)
    {
        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var bucket))
                return false;

            foreach (var entry in bucket.Entries)
            {
                if (owner != null && !ReferenceEquals(entry.Owner, owner))
                    continue;

                RemoveEntryUnderLock(key, entry);
                entry.Token.Invalidate();
                return true;
            }

            return false;
        }
    }

    /// <summary>Removes all subscriptions registered under the given key.</summary>
    /// <param name="key">The key to clear.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int Clear(TKey key)
    {
        lock (gate)
        {
            if (!subscriptions.TryRemove(key, out var bucket))
                return 0;

            foreach (var entry in bucket.Entries)
            {
                entry.Token.Invalidate();
                ForgetKeyUnderLock(entry);
            }

            var removed = bucket.Entries.Count;
            bucket.Entries.Clear();
            bucket.Republish();
            return removed;
        }
    }

    /// <summary>Removes all subscriptions from the registry.</summary>
    /// <returns>The number of subscriptions removed.</returns>
    public int ClearAll()
    {
        lock (gate)
        {
            var removed = 0;

            foreach (var bucket in subscriptions.Values)
            {
                foreach (var entry in bucket.Entries)
                    entry.Token.Invalidate();

                removed += bucket.Entries.Count;
                bucket.Entries.Clear();
                bucket.Republish();
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

        var token = new NoireSubscriptionToken(this, options.Key, options.Priority, _ =>
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

            if (!subscriptions.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket();
                subscriptions[key] = bucket;
            }

            var list = bucket.Entries;

            // Descending priority, stable for equal priorities.
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
            bucket.Republish();

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
        if (subscriptions.TryGetValue(key, out var bucket) && bucket.Entries.Remove(entry))
            RepublishOrDropUnderLock(key, bucket);

        ForgetKeyUnderLock(entry);
    }

    private int RemoveOwnedUnderLock(TKey key, Bucket bucket, object owner)
    {
        var removed = 0;

        for (var i = bucket.Entries.Count - 1; i >= 0; i--)
        {
            var entry = bucket.Entries[i];

            if (!ReferenceEquals(entry.Owner, owner))
                continue;

            bucket.Entries.RemoveAt(i);
            ForgetKeyUnderLock(entry);
            entry.Token.Invalidate();
            removed++;
        }

        if (removed > 0)
            RepublishOrDropUnderLock(key, bucket);

        return removed;
    }

    private void RepublishOrDropUnderLock(TKey key, Bucket bucket)
    {
        bucket.Republish();

        if (bucket.Entries.Count == 0)
            subscriptions.TryRemove(key, out _);
    }

    private void ForgetKeyUnderLock(Entry entry)
    {
        if (entry.Token.Key != null
            && keyedSubscriptions.TryGetValue(entry.Token.Key, out var keyed)
            && ReferenceEquals(keyed.Entry, entry))
        {
            keyedSubscriptions.Remove(entry.Token.Key);
        }
    }

    private bool IsOffFrameworkThread()
        => FrameworkRunnerOverride != null
            || (NoireService.IsInitialized() && !NoireService.Framework.IsInFrameworkUpdateThread);

    // Apart from Dispatch: its closure is only built on this path.
    private void DeliverOnFrameworkThread(TKey key, Entry entry, TContext context)
    {
        var delivery = FrameworkRunnerOverride != null
            ? FrameworkRunnerOverride(() => Deliver(key, entry, context))
            : NoireService.Framework.RunOnFrameworkThread(() => Deliver(key, entry, context));

        ReportFault(delivery, entry, "framework-thread delivery");
    }

    // Apart: its closure is only built when there is a task to observe.
    private void ReportFault(Task task, Entry entry, string what)
        => _ = task.ContinueWith(
            t => ReportException(t.Exception!.GetBaseException(), $"{what} of subscription {entry.Token}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private Task DeliverOnFrameworkThreadAsync(TKey key, Entry entry, TContext context)
        => AsyncHelper.StartOnFrameworkThreadAsync(async () =>
        {
            if (ShouldDeliver(key, entry, context))
                await InvokeEntryAsync(entry, context);
        });

    // The filter runs before the one-shot claim: a non-matching context never consumes a filtered one-shot.
    private bool Deliver(TKey key, Entry entry, TContext context)
    {
        if (!ShouldDeliver(key, entry, context))
            return false;

        InvokeEntry(entry, context);
        return true;
    }

    // Shared by Dispatch and DispatchAsync: both decide delivery identically.
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
            // Propagation: the caller owns exception handling, and an async handler runs without a fault reporter.
            if (entry.IsAsync)
                _ = ((Func<TContext, Task>)entry.Handler)(context);
            else
                ((Action<TContext>)entry.Handler)(context);

            return;
        }

        try
        {
            if (entry.IsAsync)
                ReportFault(((Func<TContext, Task>)entry.Handler)(context), entry, "async handler");
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
                // A throwing custom handler falls through to the default logger.
            }
        }

        NoireLogger.LogError(ex, $"Unhandled exception in {description}.");
    }
}
