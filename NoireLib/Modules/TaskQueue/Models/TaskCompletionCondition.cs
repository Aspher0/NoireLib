using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Defines the completion condition for a <see cref="QueuedTask"/>, for use with <see cref="NoireTaskQueue"/>.
/// Use <see cref="TaskBuilder"/> to create and enqueue tasks.
/// </summary>
public class TaskCompletionCondition
{
    /// <summary>
    /// The type of completion condition.
    /// </summary>
    public CompletionConditionType Type { get; set; }

    /// <summary>
    /// The condition function that returns true when the task is complete, for <see cref="CompletionConditionType.Predicate"/>.
    /// </summary>
    public Func<bool>? Condition { get; set; }

    /// <summary>
    /// The event type to wait for, for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public Type? EventType { get; set; }

    /// <summary>
    /// Optional filter for the event to determine if it satisfies the condition, for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public Func<object, bool>? EventFilter { get; set; }

    /// <summary>
    /// Whether events can also be captured while the task's status is still Queued, not just Executing or WaitingForCompletion; for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public bool AllowEventCaptureWhileQueued { get; set; }

    /// <summary>
    /// Maximum depth from the current executing task where events can be captured, when <see cref="AllowEventCaptureWhileQueued"/> is true: null is unlimited, 0 is only the current task, N allows N tasks between current and target. For <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public int? EventCaptureDepth { get; set; }

    /// <summary>
    /// Defines how context boundaries are checked for event capture depth, for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public ContextDefinition EventCaptureBoundaryType { get; set; } = ContextDefinition.CrossContext;

    /// <summary>
    /// Internal flag to track if the event-based condition has been met.
    /// </summary>
    internal bool EventConditionMet { get; set; }

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
    /// <param name="allowCaptureWhileQueued">Whether to capture events while the task is still queued (default false).</param>
    /// <param name="eventCaptureDepth">Maximum depth from the current task where events can be captured, when <paramref name="allowCaptureWhileQueued"/> is true; null means no limit.</param>
    /// <param name="boundaryType">How context boundaries are checked for depth calculation (default CrossContext).</param>
    /// <returns>A new <see cref="TaskCompletionCondition"/>.</returns>
    public static TaskCompletionCondition FromEvent<TEvent>(
        Func<TEvent, bool>? eventFilter = null,
        bool allowCaptureWhileQueued = false,
        int? eventCaptureDepth = null,
        ContextDefinition boundaryType = ContextDefinition.CrossContext)
    {
        return new TaskCompletionCondition
        {
            Type = CompletionConditionType.EventBusEvent,
            EventType = typeof(TEvent),
            EventFilter = eventFilter != null ? (obj) => eventFilter((TEvent)obj) : null,
            AllowEventCaptureWhileQueued = allowCaptureWhileQueued,
            EventCaptureDepth = eventCaptureDepth,
            EventCaptureBoundaryType = boundaryType
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
            _ => false
        };
    }
}
