using NoireLib.Core.Modules;
using NoireLib.Core.Subscriptions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.EventBus;

/// <summary>
/// A type-safe publish/subscribe event bus: priorities, filters, async handlers and owners, each handler under its
/// <see cref="EventExceptionMode"/>.
/// </summary>
public class NoireEventBus : NoireModuleBase<NoireEventBus>
{
    // Propagation on: each wrapped handler applies EventExceptionMode, and a LogAndThrow reaches the publisher.
    private readonly NoireSubscriptionRegistry<Type, object> registry = new(propagateHandlerExceptions: true);

    // Interlocked: publishing is allowed from any thread without a lock.
    private long totalEventsPublished;
    private long totalExceptionsCaught;

    /// <summary>The default constructor needed for internal purposes.</summary>
    public NoireEventBus() : base() { }

    /// <summary>Creates a new EventBus module.</summary>
    /// <param name="moduleId">Optional module ID for multiple event bus instances.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="exceptionHandling">How to handle exceptions thrown by event handlers.</param>
    public NoireEventBus(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        EventExceptionMode exceptionHandling = EventExceptionMode.LogAndContinue) : base(moduleId, active, enableLogging, exceptionHandling) { }

    internal NoireEventBus(ModuleId? moduleId, bool active = true, bool enableLogging = true) : base(moduleId, active, enableLogging) { }

    /// <summary>Initializes the module with optional initialization parameters.</summary>
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

    /// <summary>Defines how exceptions thrown by event handlers are handled.</summary>
    public EventExceptionMode ExceptionHandling { get; set; } = EventExceptionMode.LogAndContinue;

    /// <summary>Sets how exceptions thrown by event handlers are handled.</summary>
    /// <param name="mode">The exception handling mode.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireEventBus SetExceptionHandling(EventExceptionMode mode)
    {
        ExceptionHandling = mode;
        return this;
    }

    #region Subscribe

    /// <summary>Subscribes to events of type <typeparamref name="TEvent"/>.</summary>
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

    /// <summary>Subscribes to <typeparamref name="TEvent"/> under a key.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="key">The key. A subscription already under it is replaced.</param>
    /// <param name="handler">Called for each event.</param>
    /// <param name="priority">The priority, higher first.</param>
    /// <param name="filter">A filter the event must pass.</param>
    /// <param name="owner">The owner the subscription is removed with.</param>
    /// <returns>A token that unsubscribes.</returns>
    public EventSubscriptionToken Subscribe<TEvent>(
        string key,
        Action<TEvent> handler,
        int priority = 0,
        Func<TEvent, bool>? filter = null,
        object? owner = null)
        => SubscribeInternal(key, handler, priority, filter, owner);

    /// <summary>Subscribes to events of type <typeparamref name="TEvent"/> with an async handler.</summary>
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

    /// <summary>Subscribes an async handler to <typeparamref name="TEvent"/> under a key.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="key">The key. A subscription already under it is replaced.</param>
    /// <param name="handler">Started for each event.</param>
    /// <param name="priority">The priority, higher first.</param>
    /// <param name="filter">A filter the event must pass.</param>
    /// <param name="owner">The owner the subscription is removed with.</param>
    /// <returns>A token that unsubscribes.</returns>
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

        // Filtered inside the wrapper: a throwing filter follows the same exception policy as a handler.
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

        var token = new EventSubscriptionToken(registry.Subscribe(eventType, wrapped, BuildOptions(key, priority, owner)));
        LogSubscribed(key, eventType, priority, isAsync: false);
        return token;
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

        var token = new EventSubscriptionToken(registry.SubscribeAsync(eventType, wrapped, BuildOptions(key, priority, owner)));
        LogSubscribed(key, eventType, priority, isAsync: true);
        return token;
    }

    private static NoireSubscriptionOptions<object> BuildOptions(string? key, int priority, object? owner)
        => new() { Key = key, Priority = priority, Owner = owner };

    private void LogSubscribed(string? key, Type eventType, int priority, bool isAsync)
    {
        var keyInfo = key != null ? $" with key '{key}'" : "";
        LogDebug($"Subscribed{(isAsync ? " async" : "")} to {eventType.Name}{keyInfo} (Priority: {priority})");
    }

    #endregion

    #region Publish

    /// <summary>Publishes an event to all subscribers.</summary>
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

        // Counts publishes, not deliveries: an event nobody listens to still counts.
        Interlocked.Increment(ref totalEventsPublished);

        if (!registry.HasSubscribers(eventType))
        {
            LogVerbose($"Published {eventType.Name} with no subscribers.");
            return this;
        }

        LogVerbose($"Publishing {eventType.Name} to {registry.Count(eventType)} subscriber(s).");

        // A LogAndThrow re-throw surfaces here and aborts the remaining handlers.
        registry.Dispatch(eventType, eventData!);

        return this;
    }

    /// <summary>Publishes an event to all subscribers asynchronously, awaiting all async handlers.</summary>
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

    /// <summary>Unsubscribes a handler using its subscription token.</summary>
    /// <param name="token">The subscription token returned from Subscribe.</param>
    /// <returns>True if the subscription was found and removed.</returns>
    public bool Unsubscribe(EventSubscriptionToken token)
    {
        if (token.Subscription == null || !registry.Unsubscribe(token.Subscription))
            return false;

        LogDebug($"Unsubscribed subscription {token}");
        return true;
    }

    /// <summary>Unsubscribes a handler using its subscription key.</summary>
    /// <param name="key">The subscription key provided during Subscribe.</param>
    /// <returns>True if the subscription was found and removed.</returns>
    public bool Unsubscribe(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !registry.Unsubscribe(key))
            return false;

        LogDebug($"Unsubscribed using key '{key}'");
        return true;
    }

    /// <summary>Unsubscribes the first handler found for the specified event type and owner.</summary>
    /// <returns>True if a subscription was found and removed.</returns>
    public bool UnsubscribeFirst<TEvent>(object? owner = null)
    {
        if (!registry.UnsubscribeFirst(typeof(TEvent), owner))
            return false;

        LogDebug($"Unsubscribed from {typeof(TEvent).Name}");
        return true;
    }

    /// <summary>Unsubscribes all handlers registered by a specific owner.</summary>
    /// <param name="owner">The owner object whose subscriptions should be removed.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeAll(object owner)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));

        var totalRemoved = registry.UnsubscribeOwner(owner);

        if (totalRemoved > 0)
            LogDebug($"Unsubscribed {totalRemoved} handler(s) for owner {owner.GetType().Name}");

        return totalRemoved;
    }

    /// <summary>Unsubscribes all handlers for the specified event type, optionally filtered by owner.</summary>
    /// <typeparam name="TEvent">The event type to unsubscribe from.</typeparam>
    /// <param name="owner">Optional owner to filter subscriptions. If null, removes all handlers for this event type.</param>
    /// <returns>The number of subscriptions removed.</returns>
    public int UnsubscribeAll<TEvent>(object? owner = null)
    {
        var removed = owner == null
            ? registry.Clear(typeof(TEvent))
            : registry.UnsubscribeOwner(typeof(TEvent), owner);

        if (removed > 0)
        {
            var ownerInfo = owner != null ? $" for owner {owner.GetType().Name}" : "";
            LogDebug($"Unsubscribed {removed} handler(s) from {typeof(TEvent).Name}{ownerInfo}");
        }

        return removed;
    }

    /// <summary>Clears all event subscriptions.</summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireEventBus ClearAllSubscriptions()
    {
        var totalCount = registry.ClearAll();

        LogInfo($"Cleared {totalCount} subscription(s).");

        return this;
    }

    #endregion

    #region Statistics

    /// <summary>Gets statistics about the event bus.</summary>
    /// <returns>An <see cref="EventBusStatistics"/> object containing statistics.</returns>
    public EventBusStatistics GetStatistics()
    {
        return new EventBusStatistics(
            TotalEventsPublished: Interlocked.Read(ref totalEventsPublished),
            TotalExceptionsCaught: Interlocked.Read(ref totalExceptionsCaught),
            ActiveSubscriptions: registry.TotalCount,
            RegisteredEventTypes: registry.Keys.Count
        );
    }

    /// <summary>Gets the number of subscribers for a specific event type.</summary>
    /// <returns>The number of subscribers for the specified event type.</returns>
    public int GetSubscriberCount<TEvent>()
        => registry.Count(typeof(TEvent));

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

    /// <summary>Internal dispose method called when the module is disposed.</summary>
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
