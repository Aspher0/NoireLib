using System;
using NoireLib.EventBus;

namespace NoireLib.TaskQueue;

/// <summary>
/// Represents a task in the queue with its execution logic and completion conditions.<br/>
/// Meant to be used with the <see cref="NoireTaskQueue"/> module.<br/>
/// For ease of use, consider using the <see cref="TaskBuilder"/> to create tasks and enqueue them.
/// </summary>
public class QueuedTask
{
    /// <summary>
    /// Unique system-generated identifier for this task.
    /// </summary>
    public Guid SystemId { get; }

    /// <summary>
    /// Optional user-defined identifier for this task.
    /// </summary>
    public string? CustomId { get; set; }

    /// <summary>
    /// Whether this task blocks subsequent tasks from executing until it completes.<br/>
    /// When true, the queue will wait for this task to complete before starting the next task.
    /// </summary>
    public bool IsBlocking { get; set; }

    /// <summary>
    /// The action to execute when the task starts.<br/>
    /// When null, the task is considered a no-op and will complete based on the completion condition, useful for awaiting a condition only.
    /// </summary>
    public Action? ExecuteAction { get; set; }

    /// <summary>
    /// The completion condition that determines when the task is done.<br/>
    /// See <see cref="TaskCompletionCondition"/> for possible conditions.
    /// </summary>
    public TaskCompletionCondition? CompletionCondition { get; set; }

    /// <summary>
    /// Optional callback invoked when the task completes successfully.<br/>
    /// Returns the task.
    /// </summary>
    public Action<QueuedTask>? OnCompleted { get; set; }

    /// <summary>
    /// Optional callback invoked when the task is cancelled.<br/>
    /// Returns the task.
    /// </summary>
    public Action<QueuedTask>? OnCancelled { get; set; }

    /// <summary>
    /// Optional callback invoked when the task fails.<br/>
    /// Returns the task and the exception that caused the failure.
    /// </summary>
    public Action<QueuedTask, Exception>? OnFailed { get; set; }

    /// <summary>
    /// The current status of this task.<br/>
    /// See <see cref="TaskStatus"/> for possible values.
    /// </summary>
    public TaskStatus Status { get; set; }

    /// <summary>
    /// The tick count when this task was added to the queue.
    /// </summary>
    internal long QueuedAtTicks { get; }

    /// <summary>
    /// The tick count when this task started executing.
    /// </summary>
    internal long? StartedAtTicks { get; set; }

    /// <summary>
    /// The tick count when this task completed, cancelled, or failed.
    /// </summary>
    internal long? FinishedAtTicks { get; set; }

    /// <summary>
    /// Optional timeout for task completion. If null, no timeout is enforced.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Accumulated elapsed time in milliseconds for timeout tracking, excluding paused time.
    /// </summary>
    internal long AccumulatedTimeoutMillis { get; set; }

    /// <summary>
    /// The tick count when the timeout tracking was last paused.
    /// </summary>
    internal long? TimeoutPausedAtTicks { get; set; }

    /// <summary>
    /// The exception that caused the task to fail, if any.
    /// </summary>
    public Exception? FailureException { get; set; }

    /// <summary>
    /// Optional custom metadata associated with this task.
    /// </summary>
    public object? Metadata { get; set; }

    /// <summary>
    /// Whether to stop the queue when this task fails.
    /// </summary>
    public bool StopQueueOnFail { get; set; }

    /// <summary>
    /// Whether to stop the queue when this task is cancelled.
    /// </summary>
    public bool StopQueueOnCancel { get; set; }

    /// <summary>
    /// Internal token for unsubscribing from EventBus events when the task completes or is cancelled or failed if applicable.
    /// </summary>
    internal EventSubscriptionToken? EventSubscriptionToken { get; set; }

    /// <summary>
    /// Creates a new queued task.
    /// </summary>
    /// <param name="customId">Optional user-defined identifier.</param>
    /// <param name="isBlocking">Whether this task blocks subsequent tasks.</param>
    public QueuedTask(string? customId = null, bool isBlocking = true)
    {
        SystemId = Guid.NewGuid();
        CustomId = customId;
        IsBlocking = isBlocking;
        Status = TaskStatus.Queued;
        QueuedAtTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Gets the total time this task has been in the queue for or executing.
    /// </summary>
    /// <returns>The total time as a TimeSpan.</returns>
    public TimeSpan GetTotalTime()
    {
        var endTicks = FinishedAtTicks ?? Environment.TickCount64;
        return TimeSpan.FromMilliseconds(endTicks - QueuedAtTicks);
    }

    /// <summary>
    /// Gets the execution time of this task if it has started.
    /// </summary>
    /// <returns>The execution time as a TimeSpan, or null if the task has not started yet.</returns>
    public TimeSpan? GetExecutionTime()
    {
        if (StartedAtTicks == null)
            return null;

        var endTicks = FinishedAtTicks ?? Environment.TickCount64;
        return TimeSpan.FromMilliseconds(endTicks - StartedAtTicks.Value);
    }

    /// <summary>
    /// Checks if the task has timed out.
    /// </summary>
    /// <returns>True if the task has exceeded its timeout; otherwise, false.</returns>
    public bool HasTimedOut()
    {
        if (Timeout == null || StartedAtTicks == null)
            return false;

        long elapsedMs;

        if (TimeoutPausedAtTicks.HasValue)
            elapsedMs = AccumulatedTimeoutMillis;
        else
            elapsedMs = AccumulatedTimeoutMillis + (Environment.TickCount64 - StartedAtTicks.Value);

        return elapsedMs > Timeout.Value.TotalMilliseconds;
    }

    /// <summary>
    /// Pauses the timeout tracking for this task.
    /// </summary>
    internal void PauseTimeout()
    {
        if (Timeout == null || StartedAtTicks == null || TimeoutPausedAtTicks.HasValue)
            return;

        AccumulatedTimeoutMillis += Environment.TickCount64 - StartedAtTicks.Value;
        TimeoutPausedAtTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Resumes the timeout tracking for this task.
    /// </summary>
    internal void ResumeTimeout()
    {
        if (Timeout == null || !TimeoutPausedAtTicks.HasValue)
            return;

        StartedAtTicks = Environment.TickCount64;
        TimeoutPausedAtTicks = null;
    }

    /// <summary>
    /// Clones this task, creating a new instance with the same properties.<br/>
    /// Used for immutable statistics.
    /// </summary>
    /// <returns>A copy of the QueuedTask.</returns>
    public QueuedTask Clone()
    {
        return new QueuedTask(CustomId, IsBlocking)
        {
            ExecuteAction = ExecuteAction,
            CompletionCondition = CompletionCondition,
            OnCompleted = OnCompleted,
            OnCancelled = OnCancelled,
            OnFailed = OnFailed,
            Timeout = Timeout,
            Metadata = Metadata,
            StopQueueOnFail = StopQueueOnFail,
            StopQueueOnCancel = StopQueueOnCancel
        };
    }

    /// <summary>
    /// Returns a string representation of this task, including the ID, the Status and whether it is a Blocking task.
    /// </summary>
    /// <returns>The string representation of the task.</returns>
    public override string ToString()
    {
        var id = string.IsNullOrEmpty(CustomId) ? SystemId.ToString() : CustomId;
        return $"Task '{id}' - Status: {Status}, Blocking: {IsBlocking}";
    }
}
