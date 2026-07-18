using NoireLib.Core.Modules;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.EventBus;

/// <summary>
/// A module providing a type-safe pub/sub event bus for decoupled component communication.<br/>
/// Supports priority ordering, filtering, async handlers, and lifecycle management.<br/>
/// The subscription storage and dispatch run on the shared <see cref="NoireSubscriptionRegistry{TKey, TContext}"/>;
/// this module maps the public <see cref="EventSubscriptionToken"/> onto the registry's internal token and applies
/// its own <see cref="EventExceptionMode"/> policy per handler.
/// </summary>
public class NoireEventBus : NoireModuleBase<NoireEventBus>
{
    /// <summary>
    /// One subscription's outer/inner token pairing plus the metadata the per-type and per-owner unsubscribe
    /// operations need, which the registry does not expose per key.
    /// </summary>
    private sealed class Registration
    {
        public required EventSubscriptionToken Outer { get; init; }
        public required NoireSubscriptionToken Inner { get; init; }
        public required Type EventType { get; init; }
        public required int Priority { get; init; }
        public object? Owner { get; init; }
        public string? Key { get; init; }
    }

    // The registry runs with exception propagation on: each handler is wrapped so that this module's HandleException
    // applies the EventExceptionMode (counting, logging, and re-throwing for LogAndThrow), and propagation is what
    // lets a LogAndThrow re-throw reach the publisher and abort the remaining handlers.
    private readonly NoireSubscriptionRegistry<Type, object> registry = new(propagateHandlerExceptions: true);

    // The ledger is this module's view of its subscriptions, kept in sync with the registry under ledgerLock. Each
    // per-type list is held in the registry's dispatch order (priority descending, stable) so UnsubscribeFirst
    // resolves the same handler the registry would invoke first.
    private readonly Dictionary<Type, List<Registration>> ledger = new();
    private readonly Dictionary<EventSubscriptionToken, Registration> byOuterToken = new();
    private readonly Dictionary<string, Registration> byKey = new(StringComparer.Ordinal);
    private readonly object ledgerLock = new();

    // Publishing is allowed from any thread and is deliberately not serialized by ledgerLock, so these counters are
    // only ever touched through Interlocked. A plain increment would drop counts under concurrent publishes, and a
    // plain read is not guaranteed to observe a whole 64-bit value.
    private long totalEventsPublished;
    private long totalExceptionsCaught;

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireEventBus() : base() { }

    /// <summary>
    /// Creates a new EventBus module.
    /// </summary>
    /// <param name="moduleId">Optional module ID for multiple event bus instances.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="exceptionHandling">How to handle exceptions thrown by event handlers.</param>
    public NoireEventBus(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        EventExceptionMode exceptionHandling = EventExceptionMode.LogAndContinue) : base(moduleId, active, enableLogging, exceptionHandling) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireEventBus(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is EventExceptionMode exceptionHandling)
            ExceptionHandling = exceptionHandling;

        LogInfo("EventBus module initialized.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        LogInfo("EventBus module activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        LogInfo("EventBus module deactivated.");
    }

    /// <summary>
    /// Defines how exceptions thrown by event handlers are handled.
    /// </summary>
    public EventExceptionMode ExceptionHandling { get; set; } = EventExceptionMode.LogAndContinue;

    /// <summary>
    /// Sets how exceptions thrown by event handlers are handled.
    /// </summary>
    /// <param name="mode">The exception handling mode.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireEventBus SetExceptionHandling(EventExceptionMode mode)
    {
        ExceptionHandling = mode;
        return this;
    }

    #region Subscribe

    /// <summary>
    /// Subscribes to events of type <typeparamref name="TEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The handler to invoke when the event is published.</param>
    /// <param name="priority">The priority of this handler (higher values execute first).</param>
    /// <param name="filter">Optional filter to conditionally invoke the handler.</param>
    /// <param name="owner">Optional owner object for tracking subscriptions.</param>
    /// <returns>An <see cref="EventSubscriptionToken"/> that can be used to unsubscribe.</returns>
    public EventSubscriptionToken Subscribe<TEvent>(
        Action<TEvent> handler,
        int priority = 0,
        Func<TEvent, bool>? filter = null,
        object? owner = null)
        => SubscribeInternal(null, handler, priority, filter, owner);

    /// <summary>
    /// Subscribes to events of type <typeparamref name="TEvent"/> with a custom key for easy unsubscription.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="key">A unique string key to identify this subscription. If the key already exists, the previous subscription will be replaced.</param>
    /// <param name="handler">The handler to invoke when the event is published.</param>
    /// <param name="priority">The priority of this handler (higher values execute first).</param>
    /// <param name="filter">Optional filter to conditionally invoke the handler.</param>
    /// <param name="owner">Optional owner object for tracking subscriptions.</param>
    /// <returns>An <see cref="EventSubscriptionToken"/> that can be used to unsubscribe.</returns>
    public EventSubscriptionToken Subscribe<TEvent>(
        string key,
        Action<TEvent> handler,
        int priority = 0,
        Func<TEvent, bool>? filter = null,
        object? owner = null)
        => SubscribeInternal(key, handler, priority, filter, owner);

    /// <summary>
    /// Subscribes to events of type <typeparamref name="TEvent"/> with an async handler.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The async handler to invoke when the event is published.</param>
    /// <param name="priority">The priority of this handler (higher values execute first).</param>
    /// <param name="filter">Optional filter to conditionally invoke the handler.</param>
    /// <param name="owner">Optional owner object for tracking subscriptions.</param>
    /// <returns>An <see cref="EventSubscriptionToken"/> that can be used to unsubscribe.</returns>
    public EventSubscriptionToken SubscribeAsync<TEvent>(
        Func<TEvent, Task> handler,
        int priority = 0,
        Func<TEvent, bool>? filter = null,
        object? owner = null)
        => SubscribeAsyncInternal(null, handler, priority, filter, owner);

    /// <summary>
    /// Subscribes to events of type <typeparamref name="TEvent"/> with an async handler and a custom key for easy unsubscription.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="key">A unique string key to identify this subscription. If the key already exists, the previous subscription will be replaced.</param>
    /// <param name="handler">The async handler to invoke when the event is published.</param>
    /// <param name="priority">The priority of this handler (higher values execute first).</param>
    /// <param name="filter">Optional filter to conditionally invoke the handler.</param>
    /// <param name="owner">Optional owner object for tracking subscriptions.</param>
    /// <returns>An <see cref="EventSubscriptionToken"/> that can be used to unsubscribe.</returns>
    public EventSubscriptionToken SubscribeAsync<TEvent>(
        string key,
        Func<TEvent, Task> handler,
        int priority = 0,
        Func<TEvent, bool>? filter = null,
        object? owner = null)
        => SubscribeAsyncInternal(key, handler, priority, filter, owner);

    private EventSubscriptionToken SubscribeInternal<TEvent>(
        string? key,
        Action<TEvent> handler,
        int priority,
        Func<TEvent, bool>? filter,
        object? owner)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        if (key != null && string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Subscription key cannot be empty.", nameof(key));

        var eventType = typeof(TEvent);

        // The filter runs inside the wrapper (not in the registry) so a throwing filter is routed through
        // HandleException like a throwing handler, and so the whole exception policy lives in one place.
        Action<object> wrapped = evt =>
        {
            try
            {
                if (filter != null && !filter((TEvent)evt))
                    return;

                handler((TEvent)evt);
            }
            catch (Exception ex)
            {
                HandleException(ex, eventType);
            }
        };

        return Register(key, eventType, priority, owner, isAsync: false, () => registry.Subscribe(eventType, wrapped, BuildOptions(priority, owner)));
    }

    private EventSubscriptionToken SubscribeAsyncInternal<TEvent>(
        string? key,
        Func<TEvent, Task> handler,
        int priority,
        Func<TEvent, bool>? filter,
        object? owner)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        if (key != null && string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Subscription key cannot be empty.", nameof(key));

        var eventType = typeof(TEvent);

        Func<object, Task> wrapped = async evt =>
        {
            try
            {
                if (filter != null && !filter((TEvent)evt))
                    return;

                await handler((TEvent)evt);
            }
            catch (Exception ex)
            {
                HandleException(ex, eventType);
            }
        };

        return Register(key, eventType, priority, owner, isAsync: true, () => registry.SubscribeAsync(eventType, wrapped, BuildOptions(priority, owner)));
    }

    private static NoireSubscriptionOptions<object> BuildOptions(int priority, object? owner)
        => new() { Priority = priority, Owner = owner };

    /// <summary>
    /// Creates the registry subscription and records the outer/inner pairing, applying keyed replacement first.
    /// </summary>
    private EventSubscriptionToken Register(string? key, Type eventType, int priority, object? owner, bool isAsync, Func<NoireSubscriptionToken> subscribe)
    {
        var outer = new EventSubscriptionToken(Guid.NewGuid());

        lock (ledgerLock)
        {
            if (key != null && byKey.TryGetValue(key, out var existing))
            {
                RemoveFromLedger(existing, disposeInner: true);
                LogDebug($"Replaced existing subscription with key '{key}'");
            }

            var inner = subscribe();

            AddToLedger(new Registration
            {
                Outer = outer,
                Inner = inner,
                EventType = eventType,
                Priority = priority,
                Owner = owner,
                Key = key,
            });
        }

        var keyInfo = key != null ? $" with key '{key}'" : "";
        LogDebug($"Subscribed{(isAsync ? " async" : "")} to {eventType.Name}{keyInfo} (Priority: {priority})");

        return outer;
    }

    #endregion

    #region Publish

    /// <summary>
    /// Publishes an event to all subscribers.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="eventData">The event data to publish.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireEventBus Publish<TEvent>(TEvent eventData)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Cannot publish {typeof(TEvent).Name} - EventBus is not active.");
            return this;
        }

        var eventType = typeof(TEvent);

        // Counted before the subscribers are looked up, because this counts publishes rather than deliveries.
        // An event type nobody has subscribed to is still an event that was published.
        Interlocked.Increment(ref totalEventsPublished);

        if (!registry.HasSubscribers(eventType))
        {
            LogVerbose($"Published {eventType.Name} with no subscribers.");
            return this;
        }

        LogVerbose($"Publishing {eventType.Name} to {registry.Count(eventType)} subscriber(s).");

        // Dispatch invokes handlers outside the registry lock; the per-handler wrapper applies the exception policy.
        // A LogAndThrow re-throw surfaces here and aborts the remaining handlers, matching the documented behavior.
        registry.Dispatch(eventType, eventData!);

        return this;
    }

    /// <summary>
    /// Publishes an event to all subscribers asynchronously, awaiting all async handlers.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="eventData">The event data to publish.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PublishAsync<TEvent>(TEvent eventData)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, $"Cannot publish {typeof(TEvent).Name} - EventBus is not active.");
            return;
        }

        var eventType = typeof(TEvent);

        // Counted before the subscribers are looked up, for the same reason as the synchronous publish above.
        Interlocked.Increment(ref totalEventsPublished);

        if (!registry.HasSubscribers(eventType))
        {
            LogVerbose($"Published {eventType.Name} with no subscribers.");
            return;
        }

        LogVerbose($"Publishing async {eventType.Name} to {registry.Count(eventType)} subscriber(s).");

        await registry.DispatchAsync(eventType, eventData!);
    }

    #endregion

    #region Unsubscribe

    /// <summary>
    /// Unsubscribes a handler using its subscription token.
    /// </summary>
    /// <param name="token">The subscription token returned from Subscribe.</param>
    /// <returns>True if the subscription was found and removed.</returns>
    public bool Unsubscribe(EventSubscriptionToken token)
    {
        lock (ledgerLock)
        {
            if (!byOuterToken.TryGetValue(token, out var reg))
                return false;

            RemoveFromLedger(reg, disposeInner: true);
            LogDebug($"Unsubscribed from {reg.EventType.Name}");
            return true;
        }
    }

    /// <summary>
    /// Unsubscribes a handler using its subscription key.
    /// </summary>
    /// <param name="key">The subscription key provided during Subscribe.</param>
    /// <returns>True if the subscription was found and removed.</returns>
    public bool Unsubscribe(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        lock (ledgerLock)
        {
            if (!byKey.TryGetValue(key, out var reg))
                return false;

            RemoveFromLedger(reg, disposeInner: true);
            LogDebug($"Unsubscribed from {reg.EventType.Name} using key '{key}'");
            return true;
        }
    }

    /// <summary>
    /// Unsubscribes the first handler found for the specified event type and owner.
    /// </summary>
    /// <returns>True if a subscription was found and removed.</returns>
    public bool UnsubscribeFirst<TEvent>(object? owner = null)
    {
        lock (ledgerLock)
        {
            if (!ledger.TryGetValue(typeof(TEvent), out var list))
                return false;

            var reg = list.FirstOrDefault(r => owner == null || ReferenceEquals(r.Owner, owner));

            if (reg == null)
                return false;

            RemoveFromLedger(reg, disposeInner: true);
            LogDebug($"Unsubscribed from {typeof(TEvent).Name}");
            return true;
        }
    }

    /// <summary>
    /// Unsubscribes all handlers registered by a specific owner.
    /// </summary>
    /// <param name="owner">The owner object whose subscriptions should be removed.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeAll(object owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        int totalRemoved;

        lock (ledgerLock)
        {
            var matches = byOuterToken.Values.Where(r => ReferenceEquals(r.Owner, owner)).ToList();

            foreach (var reg in matches)
                RemoveFromLedger(reg, disposeInner: true);

            totalRemoved = matches.Count;
        }

        if (totalRemoved > 0)
            LogDebug($"Unsubscribed {totalRemoved} handler(s) for owner {owner.GetType().Name}");

        return totalRemoved;
    }

    /// <summary>
    /// Unsubscribes all handlers for the specified event type, optionally filtered by owner.
    /// </summary>
    /// <typeparam name="TEvent">The event type to unsubscribe from.</typeparam>
    /// <param name="owner">Optional owner to filter subscriptions. If null, removes all handlers for this event type.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeAll<TEvent>(object? owner = null)
    {
        int removed;

        lock (ledgerLock)
        {
            if (!ledger.TryGetValue(typeof(TEvent), out var list))
                return 0;

            var matches = (owner == null ? list : list.Where(r => ReferenceEquals(r.Owner, owner))).ToList();

            foreach (var reg in matches)
                RemoveFromLedger(reg, disposeInner: true);

            removed = matches.Count;
        }

        if (removed > 0)
        {
            var ownerInfo = owner != null ? $" for owner {owner.GetType().Name}" : "";
            LogDebug($"Unsubscribed {removed} handler(s) from {typeof(TEvent).Name}{ownerInfo}");
        }

        return removed;
    }

    /// <summary>
    /// Clears all event subscriptions.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireEventBus ClearAllSubscriptions()
    {
        int totalCount;

        lock (ledgerLock)
        {
            totalCount = byOuterToken.Count;

            registry.ClearAll();
            ledger.Clear();
            byOuterToken.Clear();
            byKey.Clear();
        }

        LogInfo($"Cleared {totalCount} subscription(s).");

        return this;
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets statistics about the event bus.
    /// </summary>
    /// <returns>An <see cref="EventBusStatistics"/> object containing statistics.</returns>
    public EventBusStatistics GetStatistics()
    {
        lock (ledgerLock)
        {
            return new EventBusStatistics(
                TotalEventsPublished: Interlocked.Read(ref totalEventsPublished),
                TotalExceptionsCaught: Interlocked.Read(ref totalExceptionsCaught),
                ActiveSubscriptions: byOuterToken.Count,
                RegisteredEventTypes: ledger.Count
            );
        }
    }

    /// <summary>
    /// Gets the number of subscribers for a specific event type.
    /// </summary>
    /// <returns>The number of subscribers for the specified event type.</returns>
    public int GetSubscriberCount<TEvent>()
    {
        lock (ledgerLock)
        {
            return ledger.TryGetValue(typeof(TEvent), out var list) ? list.Count : 0;
        }
    }

    #endregion

    #region Ledger

    /// <summary>
    /// Adds a registration to the outer-token, key and per-type indexes. The per-type list is kept in the registry's
    /// dispatch order (priority descending, stable) so order-sensitive operations agree with dispatch. Call under <see cref="ledgerLock"/>.
    /// </summary>
    private void AddToLedger(Registration reg)
    {
        byOuterToken[reg.Outer] = reg;

        if (reg.Key != null)
            byKey[reg.Key] = reg;

        if (!ledger.TryGetValue(reg.EventType, out var list))
        {
            list = new List<Registration>();
            ledger[reg.EventType] = list;
        }

        var index = list.Count;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Priority < reg.Priority)
            {
                index = i;
                break;
            }
        }

        list.Insert(index, reg);
    }

    /// <summary>
    /// Removes a registration from every index and, when requested, disposes its inner token so the registry drops
    /// the underlying subscription. Call under <see cref="ledgerLock"/>.
    /// </summary>
    private void RemoveFromLedger(Registration reg, bool disposeInner)
    {
        byOuterToken.Remove(reg.Outer);

        if (reg.Key != null && byKey.TryGetValue(reg.Key, out var keyed) && ReferenceEquals(keyed, reg))
            byKey.Remove(reg.Key);

        if (ledger.TryGetValue(reg.EventType, out var list))
        {
            list.Remove(reg);

            if (list.Count == 0)
                ledger.Remove(reg.EventType);
        }

        if (disposeInner)
            reg.Inner.Dispose();
    }

    #endregion

    private void HandleException(Exception ex, Type eventType)
    {
        Interlocked.Increment(ref totalExceptionsCaught);

        switch (ExceptionHandling)
        {
            case EventExceptionMode.LogAndContinue:
                NoireLogger.LogError(this, ex, $"Exception in event handler for {eventType.Name}");
                break;
            case EventExceptionMode.LogAndThrow:
                NoireLogger.LogError(this, ex, $"Exception in event handler for {eventType.Name}");
                throw new EventBusException($"Exception in event handler for {eventType.Name}", ex);
            case EventExceptionMode.Suppress:
                break;
        }
    }

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
        ClearAllSubscriptions();

        if (EnableLogging)
        {
            var stats = GetStatistics();
            LogInfo($"EventBus disposed. Published: {stats.TotalEventsPublished}, Exceptions: {stats.TotalExceptionsCaught}");
        }
    }
}
