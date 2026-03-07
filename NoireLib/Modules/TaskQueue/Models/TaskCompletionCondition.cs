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
    /// Whether to allow capturing events while the task is still queued.
    /// If true, events can be captured in Queued, Executing, or WaitingForCompletion status.
    /// If false, events can only be captured in Executing or WaitingForCompletion status.
    /// Used for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public bool AllowEventCaptureWhileQueued { get; set; }

    /// <summary>
    /// Maximum depth (maximum number of tasks allowed between current and target tasks) from the current executing task where events can be captured. 
    /// Only applies when <see cref="AllowEventCaptureWhileQueued"/> is true.
    /// A value of null means no depth limit.
    /// A value of 0 means only the current executing task can capture events.
    /// A value of 5 means a maximum of 5 tasks can be between the current task and the target task (capturing events), etc.
    /// Used for <see cref="CompletionConditionType.EventBusEvent"/>.
    /// </summary>
    public int? EventCaptureDepth { get; set; }

    /// <summary>
    /// Defines how context boundaries are checked for event capture depth.
    /// Used for <see cref="CompletionConditionType.EventBusEvent"/>.
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
    /// <param name="allowCaptureWhileQueued">Whether to allow capturing events while the task is still queued and not yet executing. Default is false.</param>
    /// <param name="eventCaptureDepth">
    /// Maximum depth (max number of tasks separating the current task and the target task) from the current executing task where events can be captured. <br/>
    /// Only applies when <paramref name="allowCaptureWhileQueued"/> is true.<br/>
    /// Null means no depth limit.
    /// </param>
    /// <param name="boundaryType">Defines how context boundaries are checked for depth calculation. Default is CrossContext (fully cross-context).</param>
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
