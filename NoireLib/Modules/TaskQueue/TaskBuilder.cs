using NoireLib.EventBus;
using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Fluent builder for creating queued tasks with a clean API, also allowing to enqueue the task directly.<br/>
/// See <see cref="QueuedTask"/> for more information on each property. <br/>
/// Based on and made for creating tasks for the NoireTaskQueue module. For information on how to manage the task queue, see <see cref="NoireTaskQueue"/>.
/// </summary>
public class TaskBuilder
{
    private readonly QueuedTask task;

    /// <summary>
    /// Creates a new task builder.<br/>
    /// Same as calling <see cref="Create(string?)"/>.<br/>
    /// The <see cref="QueuedTask"/> will be created as non blocking by default when <see cref="Build()"/> is called, or when <see cref="EnqueueTo(NoireTaskQueue)"/> is called.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the task.</param>
    public TaskBuilder(string? customId = null)
    {
        task = new QueuedTask(customId, false);
    }

    /// <summary>
    /// Sets the custom ID for the task. Used to identify the task in logs and callbacks, or for future retrieval.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithCustomId(string customId)
    {
        task.CustomId = customId;
        return this;
    }

    /// <summary>
    /// Sets whether the task is blocking.<br/>
    /// When a task is blocking, the queue will wait for it to complete before starting the next task.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder AsBlocking(bool isBlocking = true)
    {
        task.IsBlocking = isBlocking;
        return this;
    }

    /// <summary>
    /// Sets whether the task is non-blocking.<br/>
    /// By default, tasks are non-blocking unless specified otherwise.<br/>
    /// When a task is non-blocking, the queue will start the next task immediately after starting this one, without waiting for it to complete.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder AsNonBlocking()
    {
        task.IsBlocking = false;
        return this;
    }

    /// <summary>
    /// Sets the action to execute when the task starts.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithAction(Action action)
    {
        task.ExecuteAction = action;
        return this;
    }

    /// <summary>
    /// Sets a predicate-based completion condition.<br/>
    /// This condition will be evaluated periodically to determine if the task is complete.<br/>
    /// For example, you may want the condition to be "Is the character in map X?". Whenever the condition returns true, the task will be marked as complete.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithCondition(Func<bool> condition)
    {
        task.CompletionCondition = TaskCompletionCondition.FromPredicate(condition);
        return this;
    }

    /// <summary>
    /// Sets an event-based completion condition using the <see cref="NoireEventBus"/>.<br/>
    /// The task will complete when an event of type <typeparamref name="TEvent"/> is published to the event bus and if that event filter matches.<br/>
    /// For example, you may want the task to complete when a "PlayerEnterMap(int mapId)" is published, and only if the mapId matches your condition.<br/>
    /// You can also use parameterless events and you can also omit the filter.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WaitForEvent<TEvent>(Func<TEvent, bool>? filter = null)
    {
        task.CompletionCondition = TaskCompletionCondition.FromEvent(filter);
        return this;
    }

    /// <summary>
    /// Sets a delay-based completion condition.<br/>
    /// The task will complete after the specified delay has elapsed since the task started.<br/>
    /// This is a condition on its own, meaning it is not combinable with other conditions.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithDelay(TimeSpan delay)
    {
        task.CompletionCondition = TaskCompletionCondition.FromDelay(delay);
        return this;
    }

    /// <summary>
    /// Sets immediate completion, meaning the task completes as soon as action finishes.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithImmediateCompletion()
    {
        task.CompletionCondition = TaskCompletionCondition.Immediate();
        return this;
    }

    /// <summary>
    /// Sets a custom completion condition.<br/>
    /// This is an advanced option for when you need more control over the completion logic.<br/>
    /// See <see cref="TaskCompletionCondition"/> for more information.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithCompletionCondition(TaskCompletionCondition condition)
    {
        task.CompletionCondition = condition;
        return this;
    }

    /// <summary>
    /// Sets the callback for when the task completes.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task completes.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnCompleted(Action<QueuedTask> callback)
    {
        task.OnCompleted = callback;
        return this;
    }

    /// <summary>
    /// Sets the callback for when the task is cancelled.<br/>
    /// See <see cref="OnCancelled(Action{QueuedTask}, bool)"/> for an overload that also allows stopping the queue on cancelled.<br/>
    /// For Failure handling, see <see cref="OnFailed(Action{QueuedTask, Exception})"/> or <see cref="OnFailed(Action{QueuedTask, Exception}, bool)"/>.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task is cancelled.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnCancelled(Action<QueuedTask> callback)
    {
        task.OnCancelled = callback;
        return this;
    }

    /// <summary>
    /// Sets the callback for when the task fails.<br/>
    /// See <see cref="OnFailed(Action{QueuedTask, Exception}, bool)"/> for an overload that also allows stopping the queue on failure.<br/>
    /// For Cancellation handling, see <see cref="OnCancelled(Action{QueuedTask})"/> or <see cref="OnCancelled(Action{QueuedTask}, bool)"/>.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnFailed(Action<QueuedTask, Exception> callback)
    {
        task.OnFailed = callback;
        return this;
    }

    /// <summary>
    /// Configures the queue to stop when this task fails.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder StopQueueOnFail()
    {
        task.StopQueueOnFail = true;
        return this;
    }

    /// <summary>
    /// Configures the queue to stop when this task is cancelled.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder StopQueueOnCancel()
    {
        task.StopQueueOnCancel = true;
        return this;
    }

    /// <summary>
    /// Configures the queue to stop when this task is failed or cancelled.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder StopQueueOnFailOrCancel()
    {
        task.StopQueueOnCancel = true;
        task.StopQueueOnFail = true;
        return this;
    }

    /// <summary>
    /// Sets the callback for when the task fails and optionally stops the queue.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails.</param>
    /// <param name="stopQueue">Whether to stop the queue on task failure.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnFailed(Action<QueuedTask, Exception> callback, bool stopQueue)
    {
        task.OnFailed = callback;
        task.StopQueueOnFail = stopQueue;
        return this;
    }

    /// <summary>
    /// Sets the callback for when the task is cancelled and optionally stops the queue.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task is cancelled.</param>
    /// <param name="stopQueue">Whether to stop the queue on task cancellation.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnCancelled(Action<QueuedTask> callback, bool stopQueue)
    {
        task.OnCancelled = callback;
        task.StopQueueOnCancel = stopQueue;
        return this;
    }

    /// <summary>
    /// Sets a timeout for the task.<br/>
    /// When the timeout is reached, the task will be marked as failed.<br/>
    /// Timeouts are based on processing time, meaning when you pause the queue, the timeout timer is also paused.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithTimeout(TimeSpan timeout)
    {
        task.Timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets custom metadata for the task.<br/>
    /// Retrieving the task later, for example with <see cref="OnCompleted(Action{QueuedTask})"/>, will allow you to access this metadata.
    /// </summary>
    /// <param name="metadata">The metadata generic object to associate with the task.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithMetadata(object metadata)
    {
        task.Metadata = metadata;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured task.<br/>
    /// If no completion condition was set, it defaults to immediate completion as per <see cref="WithImmediateCompletion()"/>.
    /// </summary>
    /// <returns>The QueuedTask.</returns>
    public QueuedTask Build()
    {
        if (task.CompletionCondition == null)
            task.CompletionCondition = TaskCompletionCondition.Immediate();

        return task;
    }

    /// <summary>
    /// Builds the task and adds it to the specified <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="queue">The <see cref="NoireTaskQueue"/> to add the task to.</param>
    /// <returns>The built task.</returns>
    /// <returns>The QueuedTask.</returns>
    public QueuedTask EnqueueTo(NoireTaskQueue queue)
    {
        var builtTask = Build();
        queue.EnqueueTask(builtTask);
        return builtTask;
    }

    /// <summary>
    /// Creates a new task builder which can be further configured.<br/>
    /// Same as calling the constructor <see cref="TaskBuilder(string?)"/>.<br/>
    /// The <see cref="QueuedTask"/> will be created as non blocking by default when <see cref="Build()"/> is called, or when <see cref="EnqueueTo(NoireTaskQueue)"/> is called.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public static TaskBuilder Create(string? customId = null)
    {
        return new TaskBuilder(customId);
    }
}
