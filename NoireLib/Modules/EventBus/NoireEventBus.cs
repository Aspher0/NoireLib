using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoireLib.Core.Modules;

namespace NoireLib.EventBus;

/// <summary>
/// A module providing a type-safe pub/sub event bus for decoupled component communication.<br/>
/// Supports priority ordering, filtering, async handlers, and lifecycle management.
/// </summary>
public class NoireEventBus : NoireModuleBase
{
    private readonly Dictionary<Type, List<SubscriptionEntry>> subscriptions = new();
    private readonly object subscriptionLock = new();
    private long totalEventsPublished;
    private long totalExceptionsCaught;

    public NoireEventBus() : base() { }

    /// <summary>
    /// Creates a new EventBus module.
    /// </summary>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="moduleId">Optional module ID for multiple event bus instances.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="exceptionHandling">How to handle exceptions thrown by event handlers.</param>
    public NoireEventBus(
        bool active = true,
        string? moduleId = null,
        bool enableLogging = true,
        EventExceptionMode exceptionHandling = EventExceptionMode.LogAndContinue) : base(active, moduleId, enableLogging, exceptionHandling) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    public NoireEventBus(ModuleId? moduleId = null, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is EventExceptionMode exceptionHandling)
            ExceptionHandling = exceptionHandling;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "EventBus module initialized.");
    }

    protected override void OnActivated()
    {
        if (EnableLogging)
            NoireLogger.LogInfo(this, "EventBus module activated.");
    }

    protected override void OnDeactivated()
    {
        if (EnableLogging)
            NoireLogger.LogInfo(this, "EventBus module deactivated.");
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
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        var eventType = typeof(TEvent);
        var token = new EventSubscriptionToken(Guid.NewGuid());

        Action<object> wrappedHandler = evt => handler((TEvent)evt);
        Func<object, bool>? wrappedFilter = filter != null ? (Func<object, bool>)(evt => filter((TEvent)evt)) : null;

        var entry = new SubscriptionEntry(
            token,
            wrappedHandler,
            priority,
            wrappedFilter,
            owner,
            isAsync: false);

        lock (subscriptionLock)
        {
            if (!subscriptions.ContainsKey(eventType))
                subscriptions[eventType] = new List<SubscriptionEntry>();

            subscriptions[eventType].Add(entry);
            subscriptions[eventType] = subscriptions[eventType]
                .OrderByDescending(e => e.Priority)
                .ToList();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Subscribed to {eventType.Name} (Priority: {priority})");

        return token;
    }

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
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        var eventType = typeof(TEvent);
        var token = new EventSubscriptionToken(Guid.NewGuid());

        Func<object, Task> wrappedHandler = evt => handler((TEvent)evt);
        Func<object, bool>? wrappedFilter = filter != null ? (Func<object, bool>)(evt => filter((TEvent)evt)) : null;

        var entry = new SubscriptionEntry(
            token,
            wrappedHandler,
            priority,
            wrappedFilter,
            owner,
            isAsync: true);

        lock (subscriptionLock)
        {
            if (!subscriptions.ContainsKey(eventType))
                subscriptions[eventType] = new List<SubscriptionEntry>();

            subscriptions[eventType].Add(entry);
            subscriptions[eventType] = subscriptions[eventType]
                .OrderByDescending(e => e.Priority)
                .ToList();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Subscribed async to {eventType.Name} (Priority: {priority})");

        return token;
    }

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
        List<SubscriptionEntry>? handlers;

        lock (subscriptionLock)
        {
            if (!subscriptions.TryGetValue(eventType, out handlers))
            {
                if (EnableLogging)
                    NoireLogger.LogVerbose(this, $"Published {eventType.Name} with no subscribers.");
                return this;
            }

            handlers = handlers.ToList(); // Copy to avoid modification during iteration
        }

        totalEventsPublished++;

        if (EnableLogging)
            NoireLogger.LogVerbose(this, $"Publishing {eventType.Name} to {handlers.Count} subscriber(s).");

        foreach (var entry in handlers)
        {
            try
            {
                if (entry.Filter != null && !entry.Filter(eventData!))
                    continue;

                if (entry.IsAsync)
                {
                    var asyncHandler = (Func<object, Task>)entry.Handler;
                    var task = asyncHandler(eventData!);

                    // Fire and forget for async handlers in synchronous publish
                    _ = task.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            HandleException(t.Exception!.InnerException!, eventType);
                    }, TaskScheduler.Default);
                }
                else
                {
                    var syncHandler = (Action<object>)entry.Handler;
                    syncHandler(eventData!);
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, eventType);
            }
        }

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
        List<SubscriptionEntry>? handlers;

        lock (subscriptionLock)
        {
            if (!subscriptions.TryGetValue(eventType, out handlers))
            {
                if (EnableLogging)
                    NoireLogger.LogVerbose(this, $"Published {eventType.Name} with no subscribers.");
                return;
            }

            handlers = handlers.ToList();
        }

        totalEventsPublished++;

        if (EnableLogging)
            NoireLogger.LogVerbose(this, $"Publishing async {eventType.Name} to {handlers.Count} subscriber(s).");

        var tasks = new List<Task>();

        foreach (var entry in handlers)
        {
            try
            {
                if (entry.Filter != null && !entry.Filter(eventData!))
                    continue;

                if (entry.IsAsync)
                {
                    var asyncHandler = (Func<object, Task>)entry.Handler;
                    var task = asyncHandler(eventData!);
                    tasks.Add(task);
                }
                else
                {
                    var syncHandler = (Action<object>)entry.Handler;
                    syncHandler(eventData!);
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, eventType);
            }
        }

        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                HandleException(ex, eventType);
            }
        }
    }

    /// <summary>
    /// Unsubscribes a handler using its subscription token.
    /// </summary>
    /// <param name="token">The subscription token returned from Subscribe.</param>
    /// <returns>True if the subscription was found and removed.</returns>
    public bool Unsubscribe(EventSubscriptionToken token)
    {
        lock (subscriptionLock)
        {
            foreach (var kvp in subscriptions)
            {
                var removed = kvp.Value.RemoveAll(e => e.Token.Equals(token));
                if (removed > 0)
                {
                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Unsubscribed from {kvp.Key.Name}");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Unsubscribes the first handler found for the specified event type and owner.
    /// </summary>
    /// <returns>True if a subscription was found and removed.</returns>
    public bool UnsubscribeFirst<TEvent>(object? owner = null)
    {
        lock (subscriptionLock)
        {
            if (!subscriptions.TryGetValue(typeof(TEvent), out var handlers))
                return false;

            var toRemove = handlers.FirstOrDefault(e =>
                owner == null || ReferenceEquals(e.Owner, owner));

            if (toRemove != null)
            {
                handlers.Remove(toRemove);
                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Unsubscribed from {typeof(TEvent).Name}");
                return true;
            }
        }

        return false;
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

        int totalRemoved = 0;

        lock (subscriptionLock)
        {
            foreach (var kvp in subscriptions)
            {
                var removed = kvp.Value.RemoveAll(e => ReferenceEquals(e.Owner, owner));
                totalRemoved += removed;
            }
        }

        if (EnableLogging && totalRemoved > 0)
            NoireLogger.LogDebug(this, $"Unsubscribed {totalRemoved} handler(s) for owner {owner.GetType().Name}");

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
        lock (subscriptionLock)
        {
            if (!subscriptions.TryGetValue(typeof(TEvent), out var handlers))
                return 0;

            int removed;

            if (owner == null)
            {
                removed = handlers.Count;
                handlers.Clear();
            }
            else
            {
                removed = handlers.RemoveAll(e => ReferenceEquals(e.Owner, owner));
            }

            if (EnableLogging && removed > 0)
            {
                var ownerInfo = owner != null ? $" for owner {owner.GetType().Name}" : "";
                NoireLogger.LogDebug(this, $"Unsubscribed {removed} handler(s) from {typeof(TEvent).Name}{ownerInfo}");
            }

            return removed;
        }
    }

    /// <summary>
    /// Clears all event subscriptions.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireEventBus ClearAllSubscriptions()
    {
        lock (subscriptionLock)
        {
            var totalCount = subscriptions.Values.Sum(list => list.Count);
            subscriptions.Clear();

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Cleared {totalCount} subscription(s).");
        }

        return this;
    }

    /// <summary>
    /// Gets statistics about the event bus.
    /// </summary>
    /// <returns>An <see cref="EventBusStatistics"/> object containing statistics.</returns>
    public EventBusStatistics GetStatistics()
    {
        lock (subscriptionLock)
        {
            var subscriptionCount = subscriptions.Values.Sum(list => list.Count);
            var eventTypeCount = subscriptions.Count;

            return new EventBusStatistics(
                TotalEventsPublished: totalEventsPublished,
                TotalExceptionsCaught: totalExceptionsCaught,
                ActiveSubscriptions: subscriptionCount,
                RegisteredEventTypes: eventTypeCount
            );
        }
    }

    /// <summary>
    /// Gets the number of subscribers for a specific event type.
    /// </summary>
    /// <returns>The number of subscribers for the specified event type.</returns>
    public int GetSubscriberCount<TEvent>()
    {
        lock (subscriptionLock)
        {
            return subscriptions.TryGetValue(typeof(TEvent), out var handlers)
                ? handlers.Count
                : 0;
        }
    }

    private void HandleException(Exception ex, Type eventType)
    {
        totalExceptionsCaught++;

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

    public override void Dispose()
    {
        ClearAllSubscriptions();

        if (EnableLogging)
        {
            var stats = GetStatistics();
            NoireLogger.LogInfo(this, $"EventBus disposed. Published: {stats.TotalEventsPublished}, Exceptions: {stats.TotalExceptionsCaught}");
        }
    }
}
