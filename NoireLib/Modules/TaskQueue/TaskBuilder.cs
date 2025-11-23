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
    /// The <see cref="QueuedTask"/> will be created as blocking by default when <see cref="Build()"/> is called, or when <see cref="EnqueueTo(NoireTaskQueue)"/> is called.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the task.</param>
    public TaskBuilder(string? customId = null)
    {
        task = new QueuedTask(customId, true);
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
    public TaskBuilder AsBlocking()
    {
        task.IsBlocking = true;
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
    /// Sets the action to execute when the task starts, with access to the current task instance.<br/>
    /// </summary>
    /// <param name="action">The action that receives the current task as a parameter.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithAction(Action<QueuedTask> action)
    {
        task.ExecuteAction = () => action(task);
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
    /// Sets a predicate-based completion condition with access to the current task instance.<br/>
    /// </summary>
    /// <param name="condition">The condition function that receives the current task as a parameter.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithCondition(Func<QueuedTask, bool> condition)
    {
        task.CompletionCondition = TaskCompletionCondition.FromPredicate(() => condition(task));
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
    /// Sets a post-completion delay.<br/>
    /// Once the task completes, the queue will wait this amount of time before proceeding with the rest of the tasks.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithDelay(TimeSpan? delay)
    {
        task.PostCompletionDelay = delay;
        return this;
    }

    /// <summary>
    /// Sets a post-completion delay using a predicate function.<br/>
    /// The delay will be evaluated when the task is built, allowing for dynamic delay calculation.
    /// </summary>
    /// <param name="delayPredicate">A function that returns the delay duration.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithDelay(Func<TimeSpan?> delayPredicate)
    {
        task.PostCompletionDelay = delayPredicate();
        return this;
    }

    /// <summary>
    /// Sets a post-completion delay using a predicate function with access to the task.<br/>
    /// The delay will be evaluated when the task is built, allowing for dynamic delay calculation based on task state.
    /// </summary>
    /// <param name="delayPredicate">A function that receives the task and returns the delay duration.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithDelay(Func<QueuedTask, TimeSpan?> delayPredicate)
    {
        task.PostCompletionDelay = delayPredicate(task);
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
    /// Sets the callback for when the task fails or is cancelled.<br/>
    /// This is a convenience method for handling both failure and cancellation with the same callback.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails or is cancelled.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnFailedOrCancelled(Action<QueuedTask, Exception?> callback)
    {
        task.OnFailed = (t, ex) => callback(t, ex);
        task.OnCancelled = (t) => callback(t, null);
        return this;
    }

    /// <summary>
    /// Sets the callback for when the task fails or is cancelled, and optionally stops the queue.<br/>
    /// This is a convenience method for handling both failure and cancellation with the same callback.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails or is cancelled.</param>
    /// <param name="stopQueue">Whether to stop the queue on task failure or cancellation.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnFailedOrCancelled(Action<QueuedTask, Exception?> callback, bool stopQueue)
    {
        task.OnFailed = (t, ex) => callback(t, ex);
        task.OnCancelled = (t) => callback(t, null);
        task.StopQueueOnFail = stopQueue;
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
    /// Configures unlimited retry attempts when the completion condition stalls.
    /// </summary>
    /// <param name="stallTimeout">Duration before considering the condition stalled.</param>
    /// <param name="retryDelay">Optional delay between retry attempts.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithUnlimitedRetries(TimeSpan stallTimeout, TimeSpan? retryDelay = null)
    {
        task.RetryConfiguration = TaskRetryConfiguration.Unlimited(stallTimeout, retryDelay);
        return this;
    }

    /// <summary>
    /// Configures retry behavior with a maximum number of attempts when the completion condition stalls.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of retry attempts (does not include the initial attempt).</param>
    /// <param name="stallTimeout">Duration before considering the condition stalled.</param>
    /// <param name="retryDelay">Optional delay between retry attempts.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithRetries(int maxAttempts, TimeSpan stallTimeout, TimeSpan? retryDelay = null)
    {
        task.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(maxAttempts, stallTimeout, retryDelay);
        return this;
    }

    /// <summary>
    /// Sets an override action to execute on retry instead of the original action.
    /// </summary>
    /// <param name="retryAction">The action to execute on retry.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithRetryAction(Action retryAction)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OverrideRetryAction = (task, attempt) => retryAction();
        return this;
    }

    /// <summary>
    /// Sets an override action to execute on retry with access to the task and retry attempt number.
    /// </summary>
    /// <param name="retryAction">The action to execute on retry (receives task and attempt number).</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithRetryAction(Action<QueuedTask, int> retryAction)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OverrideRetryAction = retryAction;
        return this;
    }

    /// <summary>
    /// Sets a callback to invoke before each retry attempt.
    /// </summary>
    /// <param name="callback">The callback to invoke before retry (receives task and retry attempt number).</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnBeforeRetry(Action<QueuedTask, int> callback)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OnBeforeRetry = callback;
        return this;
    }

    /// <summary>
    /// Sets a callback to invoke when max retry attempts are exhausted.
    /// </summary>
    /// <param name="callback">The callback to invoke when retries are exhausted.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder OnMaxRetriesExceeded(Action<QueuedTask> callback)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OnMaxRetriesExceeded = callback;
        return this;
    }

    /// <summary>
    /// Sets a custom retry configuration.
    /// </summary>
    /// <param name="configuration">The retry configuration.</param>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public TaskBuilder WithRetryConfiguration(TaskRetryConfiguration configuration)
    {
        task.RetryConfiguration = configuration;
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
    /// Retrieves metadata from a previous task in the queue by custom ID.<br/>
    /// This method should be called within <see cref="WithAction(Action{QueuedTask})"/> or <see cref="WithCondition(Func{QueuedTask, bool})"/>.
    /// </summary>
    /// <typeparam name="T">The type of metadata to retrieve.</typeparam>
    /// <param name="queue">The task queue containing the previous task.</param>
    /// <param name="customId">The custom ID of the previous task.</param>
    /// <returns>The metadata from the previous task, or default(T) if not found.</returns>
    public static T? GetMetadataFromTask<T>(NoireTaskQueue queue, string customId)
    {
        var previousTask = queue.GetTaskByCustomId(customId);

        if (previousTask?.Metadata is T metadata)
            return metadata;

        return default;
    }

    /// <summary>
    /// Retrieves a pointer from a previous task's metadata by custom ID.<br/>
    /// This is a type-safe wrapper for retrieving pointers stored as <see cref="PointerMetadata{T}"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged pointer type.</typeparam>
    /// <param name="queue">The task queue containing the previous task.</param>
    /// <param name="customId">The custom ID of the previous task.</param>
    /// <returns>The pointer from the previous task's metadata.</returns>
    public static unsafe T* GetPointerMetadataFromTask<T>(NoireTaskQueue queue, string customId) where T : unmanaged
    {
        var previousTask = queue.GetTaskByCustomId(customId);

        if (previousTask?.Metadata is PointerMetadata<T> pointerMetadata)
            return pointerMetadata.GetPointer();

        // Fallback: check if it's stored as IntPtr directly
        if (previousTask?.Metadata is IntPtr intPtr)
            return (T*)intPtr;

        return null;
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
        task.OwningQueue = queue;
        var builtTask = Build();
        queue.EnqueueTask(builtTask);
        return builtTask;
    }

    /// <summary>
    /// Builds the task and inserts it after another task in the specified <see cref="NoireTaskQueue"/> by system ID.
    /// </summary>
    /// <param name="queue">The <see cref="NoireTaskQueue"/> to add the task to.</param>
    /// <param name="afterTaskSystemId">The system ID of the task to insert after.</param>
    /// <returns>The built task if insertion was successful; otherwise, null.</returns>
    public QueuedTask? EnqueueToAfterTask(NoireTaskQueue queue, Guid afterTaskSystemId)
    {
        task.OwningQueue = queue;
        var builtTask = Build();
        var success = queue.InsertTaskAfter(builtTask, afterTaskSystemId);
        return success ? builtTask : null;
    }

    /// <summary>
    /// Builds the task and inserts it after another task in the specified <see cref="NoireTaskQueue"/> by custom ID.
    /// </summary>
    /// <param name="queue">The <see cref="NoireTaskQueue"/> to add the task to.</param>
    /// <param name="afterTaskCustomId">The custom ID of the task to insert after.</param>
    /// <returns>The built task if insertion was successful; otherwise, null.</returns>
    public QueuedTask? EnqueueToAfterTask(NoireTaskQueue queue, string afterTaskCustomId)
    {
        task.OwningQueue = queue;
        var builtTask = Build();
        var success = queue.InsertTaskAfter(builtTask, afterTaskCustomId);
        return success ? builtTask : null;
    }

    /// <summary>
    /// Creates a new task builder which can be further configured.<br/>
    /// Same as calling the constructor <see cref="TaskBuilder(string?)"/>.<br/>
    /// The <see cref="QueuedTask"/> will be created as blocking by default when <see cref="Build()"/> is called, or when <see cref="EnqueueTo(NoireTaskQueue)"/> is called.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public static TaskBuilder Create(string? customId = null)
    {
        return new TaskBuilder(customId);
    }

    /// <summary>
    /// Quickly creates and enqueues a delay-only task to the specified queue.<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create().WithDelay(delay).EnqueueTo(queue)</code>
    /// </summary>
    /// <param name="delay">The delay duration.</param>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddDelay(TimeSpan delay, NoireTaskQueue queue, string? customId = null)
    {
        return Create(customId)
            .WithDelay(delay)
            .EnqueueTo(queue);
    }

    /// <summary>
    /// Quickly creates and enqueues a delay-only task to the specified queue.<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create().WithDelay(TimeSpan.FromSeconds(seconds)).EnqueueTo(queue)</code>
    /// </summary>
    /// <param name="seconds">The delay in seconds.</param>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddDelaySeconds(double seconds, NoireTaskQueue queue, string? customId = null)
    {
        return AddDelay(TimeSpan.FromSeconds(seconds), queue, customId);
    }

    /// <summary>
    /// Quickly creates and enqueues a delay-only task to the specified queue.<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create().WithDelay(TimeSpan.FromMilliseconds(milliseconds)).EnqueueTo(queue)</code>
    /// </summary>
    /// <param name="milliseconds">The delay in milliseconds.</param>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddDelayMilliseconds(int milliseconds, NoireTaskQueue queue, string? customId = null)
    {
        return AddDelay(TimeSpan.FromMilliseconds(milliseconds), queue, customId);
    }

    /// <summary>
    /// Quickly creates and enqueues a simple action-only task with immediate completion.<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create(customId).WithAction(action).WithImmediateCompletion().EnqueueTo(queue)</code>
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddAction(Action action, NoireTaskQueue queue, string? customId = null)
    {
        return Create(customId)
            .WithAction(action)
            .WithImmediateCompletion()
            .EnqueueTo(queue);
    }

    /// <summary>
    /// Quickly creates and enqueues a simple action-only task with immediate completion.<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create(customId).WithAction(action).WithImmediateCompletion().EnqueueTo(queue)</code>
    /// </summary>
    /// <param name="action">The action to execute (receives the task as parameter).</param>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddAction(Action<QueuedTask> action, NoireTaskQueue queue, string? customId = null)
    {
        return Create(customId)
        .WithAction(action)
        .WithImmediateCompletion()
        .EnqueueTo(queue);
    }

    /// <summary>
    /// Quickly creates and enqueues a condition-only task (no action, just waits for condition).<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create(customId).WithCondition(condition).EnqueueTo(queue)</code>
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddCondition(Func<bool> condition, NoireTaskQueue queue, string? customId = null)
    {
        return Create(customId)
            .WithCondition(condition)
            .EnqueueTo(queue);
    }

    /// <summary>
    /// Quickly creates and enqueues a condition-only task (no action, just waits for condition).<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create(customId).WithCondition(condition).EnqueueTo(queue)</code>
    /// </summary>
    /// <param name="condition">The condition to wait for (receives the task as parameter).</param>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddCondition(Func<QueuedTask, bool> condition, NoireTaskQueue queue, string? customId = null)
    {
        return Create(customId)
            .WithCondition(condition)
            .EnqueueTo(queue);
    }

    /// <summary>
    /// Quickly creates and enqueues an event-waiting task.<br/>
    /// This is a convenience method equivalent to:<br/>
    /// <code>TaskBuilder.Create(customId).WaitForEvent&lt;TEvent&gt;(filter).EnqueueTo(queue)</code>
    /// </summary>
    /// <typeparam name="TEvent">The event type to wait for.</typeparam>
    /// <param name="queue">The queue to add the task to.</param>
    /// <param name="filter">Optional filter for the event.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The created and enqueued task.</returns>
    public static QueuedTask AddEventWait<TEvent>(NoireTaskQueue queue, Func<TEvent, bool>? filter = null, string? customId = null)
    {
        return Create(customId)
            .WaitForEvent(filter)
            .EnqueueTo(queue);
    }
}
