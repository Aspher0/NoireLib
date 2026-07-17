namespace NoireLib.EventBus;

/// <summary>
/// Statistics about the EventBus module's activity.
/// </summary>
/// <param name="TotalEventsPublished">
/// The total number of events published while the bus was active, counting an event with no subscribers.<br/>
/// This counts publishes rather than handler invocations, so it does not grow with the number of subscribers.
/// </param>
/// <param name="TotalExceptionsCaught">The total number of exceptions caught from event handlers.</param>
/// <param name="ActiveSubscriptions">The current number of active subscriptions.</param>
/// <param name="RegisteredEventTypes">The number of different event types with active subscriptions.</param>
public record EventBusStatistics(
    long TotalEventsPublished,
    long TotalExceptionsCaught,
    int ActiveSubscriptions,
    int RegisteredEventTypes
);
