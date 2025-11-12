using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Defines the completion condition for a <see cref="QueuedTask"/>.<br/>
/// Meant for use with the <see cref="NoireTaskQueue"/> module.<br/>
/// For ease of use, consider using the <see cref="TaskBuilder"/> to create tasks and enqueue them.
/// </summary>
public class TaskCompletionCondition
{
    /// <summary>
    /// The type of completion condition.
    /// </summary>
    public CompletionConditionType Type { get; set; }

    /// <summary>
    /// The condition function that returns true when the task is complete.
    /// Used for <see cref="CompletionConditionType.Predicate"/>.
    /// </summary>
    public Func<bool>? Condition { get; set; }

    /// <summary>
    /// The event type to wait for.
    /// Used for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public Type? EventType { get; set; }

    /// <summary>
    /// Optional filter for the event to determine if it satisfies the condition.
    /// Used for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public Func<object, bool>? EventFilter { get; set; }

    /// <summary>
    /// The delay duration for time-based completion.
    /// Used for <see cref="CompletionConditionType.Delay"/>.
    /// </summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>
    /// Internal flag to track if the event-based condition has been met.
    /// </summary>
    internal bool EventConditionMet { get; set; }

    /// <summary>
    /// Internal tick count for delay-based conditions.
    /// </summary>
    internal long? DelayStartTimeTicks { get; set; }

    /// <summary>
    /// Accumulated elapsed time in milliseconds for delay-based conditions, excluding paused time.
    /// </summary>
    internal long AccumulatedDelayMillis { get; set; }

    /// <summary>
    /// The tick count when the delay was last paused.
    /// </summary>
    internal long? DelayPausedAtTicks { get; set; }

    /// <summary>
    /// Creates a predicate-based completion condition.
    /// </summary>
    /// <param name="condition">The condition function.</param>
    /// <returns>A new <see cref="TaskCompletionCondition"/>.</returns>
    public static TaskCompletionCondition FromPredicate(Func<bool> condition)
    {
        return new TaskCompletionCondition
        {
            Type = CompletionConditionType.Predicate,
            Condition = condition
        };
    }

    /// <summary>
    /// Creates an event-based completion condition.
    /// </summary>
    /// <typeparam name="TEvent">The event type to wait for.</typeparam>
    /// <param name="eventFilter">Optional filter for the event.</param>
    /// <returns>A new <see cref="TaskCompletionCondition"/>.</returns>
    public static TaskCompletionCondition FromEvent<TEvent>(Func<TEvent, bool>? eventFilter = null)
    {
        return new TaskCompletionCondition
        {
            Type = CompletionConditionType.EventBusEvent,
            EventType = typeof(TEvent),
            EventFilter = eventFilter != null ? (obj) => eventFilter((TEvent)obj) : null
        };
    }

    /// <summary>
    /// Creates a delay-based completion condition.
    /// </summary>
    /// <param name="delay">The delay duration.</param>
    /// <returns>A new <see cref="TaskCompletionCondition"/>.</returns>
    public static TaskCompletionCondition FromDelay(TimeSpan delay)
    {
        return new TaskCompletionCondition
        {
            Type = CompletionConditionType.Delay,
            Delay = delay
        };
    }

    /// <summary>
    /// Creates an immediate completion condition (task completes as soon as execution finishes).
    /// </summary>
    /// <returns>A new <see cref="TaskCompletionCondition"/>.</returns>
    public static TaskCompletionCondition Immediate()
    {
        return new TaskCompletionCondition
        {
            Type = CompletionConditionType.Immediate
        };
    }

    /// <summary>
    /// Checks if the condition is met.
    /// </summary>
    /// <returns>True if the condition is satisfied.</returns>
    public bool IsMet()
    {
        return Type switch
        {
            CompletionConditionType.Immediate => true,
            CompletionConditionType.Predicate => Condition?.Invoke() ?? false,
            CompletionConditionType.EventBusEvent => EventConditionMet,
            CompletionConditionType.Delay => CheckDelayCondition(),
            _ => false
        };
    }

    /// <summary>
    /// Checks if the delay condition is met, accounting for paused time.
    /// </summary>
    private bool CheckDelayCondition()
    {
        if (!DelayStartTimeTicks.HasValue || !Delay.HasValue)
            return false;

        long totalElapsedMillis = AccumulatedDelayMillis;

        // If not currently paused, add the time since last resume
        if (!DelayPausedAtTicks.HasValue)
            totalElapsedMillis += Environment.TickCount64 - DelayStartTimeTicks.Value;

        return totalElapsedMillis >= Delay.Value.TotalMilliseconds;
    }

    /// <summary>
    /// Pauses the delay tracking for this condition.
    /// </summary>
    internal void PauseDelay()
    {
        if (Type != CompletionConditionType.Delay || !DelayStartTimeTicks.HasValue || DelayPausedAtTicks.HasValue)
            return;

        // Accumulate the time elapsed before pausing
        AccumulatedDelayMillis += Environment.TickCount64 - DelayStartTimeTicks.Value;
        DelayPausedAtTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Resumes the delay tracking for this condition.
    /// </summary>
    internal void ResumeDelay()
    {
        if (Type != CompletionConditionType.Delay || !DelayPausedAtTicks.HasValue)
            return;

        // Reset the start time to now
        DelayStartTimeTicks = Environment.TickCount64;
        DelayPausedAtTicks = null;
    }
}
