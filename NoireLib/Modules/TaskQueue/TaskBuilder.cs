using NoireLib.EventBus;
using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Base class for <see cref="TaskBuilder"/> and <see cref="TaskBuilder{TModule}"/>.<br/>
/// Uses the Curiously Recurring Template Pattern (CRTP) so that every fluent method returns
/// the concrete derived type, preserving the full API regardless of how deep the chain goes.
/// </summary>
/// <typeparam name="TSelf">The concrete builder type (e.g. <see cref="TaskBuilder"/> or <see cref="TaskBuilder{TModule}"/>).</typeparam>
public class TaskBuilderBase<TSelf> where TSelf : TaskBuilderBase<TSelf>
{
    /// <summary>The underlying task being configured by this builder.</summary>
    protected readonly QueuedTask task;

    /// <summary>
    /// Initialises a new builder instance.<br/>
    /// The <see cref="QueuedTask"/> will be created as blocking by default.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the task.</param>
    protected TaskBuilderBase(string? customId = null)
    {
        task = new QueuedTask(customId, true);
    }

    // ── Fluent configuration ─────────────────────────────────────────────────

    /// <summary>
    /// Sets the custom ID for the task. Used to identify the task in logs and callbacks, or for future retrieval.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithCustomId(string customId)
    {
        task.CustomId = customId;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets whether the task is blocking.<br/>
    /// When a task is blocking, the queue will wait for it to complete before starting the next task.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AsBlocking()
    {
        task.IsBlocking = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets whether the task is non-blocking.<br/>
    /// By default, tasks are non-blocking unless specified otherwise.<br/>
    /// When a task is non-blocking, the queue will start the next task immediately after starting this one, without waiting for it to complete.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AsNonBlocking()
    {
        task.IsBlocking = false;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the action to execute when the task starts.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithAction(Action action)
    {
        task.ExecuteAction = action;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the action to execute when the task starts, with access to the current task instance.<br/>
    /// </summary>
    /// <param name="action">The action that receives the current task as a parameter.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithAction(Action<QueuedTask> action)
    {
        task.ExecuteAction = () => action(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a predicate-based completion condition.<br/>
    /// This condition will be evaluated periodically to determine if the task is complete.<br/>
    /// For example, you may want the condition to be "Is the character in map X?". Whenever the condition returns true, the task will be marked as complete.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithCondition(Func<bool> condition)
    {
        task.CompletionCondition = TaskCompletionCondition.FromPredicate(condition);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a predicate-based completion condition with access to the current task instance.<br/>
    /// </summary>
    /// <param name="condition">The condition function that receives the current task as a parameter.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithCondition(Func<QueuedTask, bool> condition)
    {
        task.CompletionCondition = TaskCompletionCondition.FromPredicate(() => condition(task));
        return (TSelf)this;
    }

    /// <summary>
    /// Sets an event-based completion condition using the <see cref="NoireEventBus"/>.<br/>
    /// The task will complete when an event of type <typeparamref name="TEvent"/> is published to the event bus and if that event filter matches.<br/>
    /// For example, you may want the task to complete when a "PlayerEnterMap(int mapId)" is published, and only if the mapId matches your condition.<br/>
    /// You can also use parameterless events and you can also omit the filter.
    /// </summary>
    /// <param name="filter">Optional filter to conditionally accept events.</param>
    /// <param name="allowCaptureWhileQueued">If true, events can be captured while the task is still queued. If false (default), events can only be captured when the task is executing or waiting for completion.</param>
    /// <param name="eventCaptureDepth">
    /// Maximum depth (maximum number of tasks allowed between current and target tasks) from the current executing task where events can be captured.<br/>
    /// Only applies when allowCaptureWhileQueued is true. Null (default) means no depth limit.<br/>
    /// A value of 0 means only the current executing task can capture events.<br/>
    /// A value of 5 means a maximum of 5 tasks can be between the current task and the target task (capturing events).
    /// </param>
    /// <param name="boundaryType">
    /// Defines how context boundaries are checked for depth calculation. <br/>
    /// CrossContext (default): fully cross-context, SameContext: same batch or both standalone, StrictWithBoundaryCheck: no batch separation allowed.
    /// </param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WaitForEvent<TEvent>(
        Func<TEvent, bool>? filter = null,
        bool allowCaptureWhileQueued = false,
        int? eventCaptureDepth = null,
        ContextDefinition boundaryType = ContextDefinition.CrossContext)
    {
        task.CompletionCondition = TaskCompletionCondition.FromEvent(filter, allowCaptureWhileQueued, eventCaptureDepth, boundaryType);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a post-completion delay.<br/>
    /// Once the task completes, the queue will wait this amount of time before proceeding with the rest of the tasks.
    /// </summary>
    /// <param name="delay">The delay duration.</param>
    /// <param name="applyOnFailure">Whether to apply the delay when the task fails (default: false).</param>
    /// <param name="applyOnCancellation">Whether to apply the delay when the task is cancelled (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithDelay(TimeSpan? delay, bool applyOnFailure = false, bool applyOnCancellation = false)
    {
        task.PostCompletionDelay = delay;
        task.ApplyPostDelayOnFailure = applyOnFailure;
        task.ApplyPostDelayOnCancellation = applyOnCancellation;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a post-completion delay using a predicate function.<br/>
    /// The delay will be evaluated at the moment the post-completion delay is about to start (not at task creation), allowing for dynamic delay calculation.
    /// </summary>
    /// <param name="delayPredicate">A function that returns the delay duration.</param>
    /// <param name="applyOnFailure">Whether to apply the delay when the task fails (default: false).</param>
    /// <param name="applyOnCancellation">Whether to apply the delay when the task is cancelled (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithDelay(Func<TimeSpan?> delayPredicate, bool applyOnFailure = false, bool applyOnCancellation = false)
    {
        task.PostCompletionDelayProvider = _ => delayPredicate();
        task.ApplyPostDelayOnFailure = applyOnFailure;
        task.ApplyPostDelayOnCancellation = applyOnCancellation;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a post-completion delay using a predicate function with access to the task.<br/>
    /// The delay will be evaluated at the moment the post-completion delay is about to start (not at task creation), allowing for dynamic delay calculation based on task state.
    /// </summary>
    /// <param name="delayPredicate">A function that receives the task and returns the delay duration.</param>
    /// <param name="applyOnFailure">Whether to apply the delay when the task fails (default: false).</param>
    /// <param name="applyOnCancellation">Whether to apply the delay when the task is cancelled (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithDelay(Func<QueuedTask, TimeSpan?> delayPredicate, bool applyOnFailure = false, bool applyOnCancellation = false)
    {
        task.PostCompletionDelayProvider = delayPredicate;
        task.ApplyPostDelayOnFailure = applyOnFailure;
        task.ApplyPostDelayOnCancellation = applyOnCancellation;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets immediate completion, meaning the task completes as soon as action finishes.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithImmediateCompletion()
    {
        task.CompletionCondition = TaskCompletionCondition.Immediate();
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a custom completion condition.<br/>
    /// This is an advanced option for when you need more control over the completion logic.<br/>
    /// See <see cref="TaskCompletionCondition"/> for more information.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithCompletionCondition(TaskCompletionCondition condition)
    {
        task.CompletionCondition = condition;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the task completes.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task completes.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnCompleted(Action<QueuedTask> callback)
    {
        task.OnCompleted = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the task is cancelled.<br/>
    /// See <see cref="OnCancelled(Action{QueuedTask}, bool)"/> for an overload that also allows stopping the queue on cancelled.<br/>
    /// For Failure handling, see <see cref="OnFailed(Action{QueuedTask, Exception})"/> or <see cref="OnFailed(Action{QueuedTask, Exception}, bool)"/>.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task is cancelled.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnCancelled(Action<QueuedTask> callback)
    {
        task.OnCancelled = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the task fails.<br/>
    /// This callback will also be called when the task fails due maximum retry attempts being exhausted.<br/>
    /// See <see cref="OnFailed(Action{QueuedTask, Exception}, bool)"/> for an overload that also allows stopping the queue on failure.<br/>
    /// For Cancellation handling, see <see cref="OnCancelled(Action{QueuedTask})"/> or <see cref="OnCancelled(Action{QueuedTask}, bool)"/>.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnFailed(Action<QueuedTask, Exception> callback)
    {
        task.OnFailed = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the queue to stop when this task fails.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopQueueOnFail()
    {
        task.StopQueueOnFail = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the queue to stop when this task is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopQueueOnCancel()
    {
        task.StopQueueOnCancel = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the queue to stop when this task is failed or cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopQueueOnFailOrCancel()
    {
        task.StopQueueOnCancel = true;
        task.StopQueueOnFail = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to fail when this task fails.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf FailParentBatchOnFail()
    {
        task.FailParentBatchOnFail = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to be cancelled when this task fails.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf CancelParentBatchOnFail()
    {
        task.CancelParentBatchOnFail = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to fail when this task is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf FailParentBatchOnCancel()
    {
        task.FailParentBatchOnCancel = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to be cancelled when this task is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf CancelParentBatchOnCancel()
    {
        task.CancelParentBatchOnCancel = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to fail when this task fails or is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf FailParentBatchOnFailOrCancel()
    {
        task.FailParentBatchOnFail = true;
        task.FailParentBatchOnCancel = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to be cancelled when this task fails or is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf CancelParentBatchOnFailOrCancel()
    {
        task.CancelParentBatchOnFail = true;
        task.CancelParentBatchOnCancel = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to fail when this task exceeds max retry attempts.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf FailParentBatchOnMaxRetries()
    {
        task.FailParentBatchOnMaxRetries = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the parent batch (if exists) to be cancelled when this task exceeds max retry attempts.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf CancelParentBatchOnMaxRetries()
    {
        task.CancelParentBatchOnMaxRetries = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the task fails and optionally stops the queue.
    /// This callback will also be called when the task fails due maximum retry attempts being exhausted.<br/>
    /// For Cancellation handling, see <see cref="OnCancelled(Action{QueuedTask})"/> or <see cref="OnCancelled(Action{QueuedTask}, bool)"/>.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails.</param>
    /// <param name="stopQueue">Whether to stop the queue on task failure.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnFailed(Action<QueuedTask, Exception> callback, bool stopQueue)
    {
        task.OnFailed = callback;
        task.StopQueueOnFail = stopQueue;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the task is cancelled and optionally stops the queue.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task is cancelled.</param>
    /// <param name="stopQueue">Whether to stop the queue on task cancellation.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnCancelled(Action<QueuedTask> callback, bool stopQueue)
    {
        task.OnCancelled = callback;
        task.StopQueueOnCancel = stopQueue;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the task fails or is cancelled.<br/>
    /// This callback will also be called when the task fails due maximum retry attempts being exhausted.
    /// This is a convenience method for handling both failure and cancellation with the same callback.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails or is cancelled.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnFailedOrCancelled(Action<QueuedTask, Exception?> callback)
    {
        task.OnFailed = (t, ex) => callback(t, ex);
        task.OnCancelled = (t) => callback(t, null);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the task fails or is cancelled, and optionally stops the queue.<br/>
    /// This callback will also be called when the task fails due maximum retry attempts being exhausted.
    /// This is a convenience method for handling both failure and cancellation with the same callback.
    /// </summary>
    /// <param name="callback">The callback to invoke when the task fails or is cancelled.</param>
    /// <param name="stopQueue">Whether to stop the queue on task failure or cancellation.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnFailedOrCancelled(Action<QueuedTask, Exception?> callback, bool stopQueue)
    {
        task.OnFailed = (t, ex) => callback(t, ex);
        task.OnCancelled = (t) => callback(t, null);
        task.StopQueueOnFail = stopQueue;
        task.StopQueueOnCancel = stopQueue;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a timeout for the task.<br/>
    /// When the timeout is reached, the task will be marked as failed.<br/>
    /// Timeouts are based on processing time, meaning when you pause the queue, the timeout timer is also paused.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithTimeout(TimeSpan timeout)
    {
        task.Timeout = timeout;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures unlimited retry attempts when the completion condition stalls.
    /// </summary>
    /// <param name="stallTimeout">Duration before considering the condition stalled.</param>
    /// <param name="retryDelay">Optional delay between retry attempts.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithUnlimitedRetries(TimeSpan stallTimeout, TimeSpan? retryDelay = null)
    {
        task.RetryConfiguration = TaskRetryConfiguration.Unlimited(stallTimeout, retryDelay);
        return (TSelf)this;
    }

    /// <summary>
    /// Configures retry behavior with a maximum number of attempts when the completion condition stalls.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of retry attempts (does not include the initial attempt).</param>
    /// <param name="stallTimeout">Duration before considering the condition stalled.</param>
    /// <param name="retryDelay">Optional delay between retry attempts.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithRetries(int maxAttempts, TimeSpan stallTimeout, TimeSpan? retryDelay = null)
    {
        task.RetryConfiguration = TaskRetryConfiguration.WithMaxAttempts(maxAttempts, stallTimeout, retryDelay);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets an override action to execute on retry instead of the original action.
    /// </summary>
    /// <param name="retryAction">The action to execute on retry.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithRetryAction(Action retryAction)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OverrideRetryAction = (task, attempt) => retryAction();
        return (TSelf)this;
    }

    /// <summary>
    /// Sets an override action to execute on retry with access to the task and retry attempt number.
    /// </summary>
    /// <param name="retryAction">The action to execute on retry (receives task and attempt number).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithRetryAction(Action<QueuedTask, int> retryAction)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OverrideRetryAction = retryAction;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a callback to invoke before each retry attempt.
    /// </summary>
    /// <param name="callback">The callback to invoke before retry (receives task and retry attempt number).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnBeforeRetry(Action<QueuedTask, int> callback)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OnBeforeRetry = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a custom retry configuration.
    /// </summary>
    /// <param name="configuration">The retry configuration.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithRetryConfiguration(TaskRetryConfiguration configuration)
    {
        task.RetryConfiguration = configuration;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets custom metadata for the task.<br/>
    /// Retrieving the task later, for example with <see cref="OnCompleted(Action{QueuedTask})"/>, will allow you to access this metadata.
    /// </summary>
    /// <param name="metadata">The metadata generic object to associate with the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithMetadata(object metadata)
    {
        task.Metadata = metadata;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a callback to invoke when max retry attempts are exhausted.<br/>
    /// If <see cref="OnFailed(Action{QueuedTask, Exception})"/> or <see cref="OnFailedOrCancelled(Action{QueuedTask, Exception?})"/> callbacks are set, this callback will be invoked **before them** when max retries are exceeded, allowing you to handle max retry exhaustion separately from other types of failure if desired.
    /// </summary>
    /// <param name="callback">The callback to invoke when retries are exhausted.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnMaxRetriesExceeded(Action<QueuedTask> callback)
    {
        if (task.RetryConfiguration == null)
            task.RetryConfiguration = new TaskRetryConfiguration();

        task.RetryConfiguration.OnMaxRetriesExceeded = callback;
        return (TSelf)this;
    }

    // ── Terminal methods ──────────────────────────────────────────────────────

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

    // ── Static utilities ──────────────────────────────────────────────────────

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
    /// Retrieves a task from a specific batch in the queue by their custom IDs.
    /// </summary>
    /// <param name="queue">The task queue containing the batch and task.</param>
    /// <param name="batchCustomId">The custom ID of the batch.</param>
    /// <param name="taskCustomId">The custom ID of the task.</param>
    /// <returns>The task if found; otherwise, null.</returns>
    public static QueuedTask? GetTaskFromBatch(NoireTaskQueue queue, string batchCustomId, string taskCustomId)
    {
        return queue.GetTaskFromBatch(batchCustomId, taskCustomId);
    }

    /// <summary>
    /// Retrieves a task from a specific batch in the queue by their system IDs.
    /// </summary>
    /// <param name="queue">The task queue containing the batch and task.</param>
    /// <param name="batchSystemId">The system ID of the batch.</param>
    /// <param name="taskSystemId">The system ID of the task.</param>
    /// <returns>The task if found; otherwise, null.</returns>
    public static QueuedTask? GetTaskFromBatch(NoireTaskQueue queue, Guid batchSystemId, Guid taskSystemId)
    {
        return queue.GetTaskFromBatch(batchSystemId, taskSystemId);
    }

    /// <summary>
    /// Retrieves metadata from a task within a batch by their custom IDs.<br/>
    /// This method should be called within <see cref="WithAction(Action{QueuedTask})"/> or <see cref="WithCondition(Func{QueuedTask, bool})"/>.
    /// </summary>
    /// <typeparam name="T">The type of metadata to retrieve.</typeparam>
    /// <param name="queue">The task queue containing the batch and task.</param>
    /// <param name="batchCustomId">The custom ID of the batch.</param>
    /// <param name="taskCustomId">The custom ID of the task.</param>
    /// <returns>The metadata from the task, or default(T) if not found.</returns>
    public static T? GetMetadataFromBatchTask<T>(NoireTaskQueue queue, string batchCustomId, string taskCustomId)
    {
        var task = queue.GetTaskFromBatch(batchCustomId, taskCustomId);

        if (task?.Metadata is T metadata)
            return metadata;

        return default;
    }

    /// <summary>
    /// Retrieves a pointer from a task's metadata within a batch by their custom IDs.<br/>
    /// This is a type-safe wrapper for retrieving pointers stored as <see cref="PointerMetadata{T}"/>.
    /// </summary>
    /// <typeparam name="T">The unmanaged pointer type.</typeparam>
    /// <param name="queue">The task queue containing the batch and task.</param>
    /// <param name="batchCustomId">The custom ID of the batch.</param>
    /// <param name="taskCustomId">The custom ID of the task.</param>
    /// <returns>The pointer from the task's metadata.</returns>
    public static unsafe T* GetPointerMetadataFromBatchTask<T>(NoireTaskQueue queue, string batchCustomId, string taskCustomId) where T : unmanaged
    {
        var task = queue.GetTaskFromBatch(batchCustomId, taskCustomId);

        if (task?.Metadata is PointerMetadata<T> pointerMetadata)
            return pointerMetadata.GetPointer();

        // Fallback: check if it's stored as IntPtr directly
        if (task?.Metadata is IntPtr intPtr)
            return (T*)intPtr;

        return null;
    }
}

/// <summary>
/// Fluent builder for creating queued tasks with a clean API, also allowing to enqueue the task directly.<br/>
/// See <see cref="QueuedTask"/> for more information on each property. <br/>
/// Based on and made for creating tasks for the NoireTaskQueue module. For information on how to manage the task queue, see <see cref="NoireTaskQueue"/>.<br/>
/// For a queue-bound variant where <see cref="TaskBuilder{TModule}.Enqueue"/> is directly available, use <see cref="TaskBuilder{TModule}"/>.
/// </summary>
public class TaskBuilder : TaskBuilderBase<TaskBuilder>
{
    /// <summary>
    /// Creates a new task builder.<br/>
    /// Same as calling <see cref="Create(string?)"/>.<br/>
    /// The <see cref="QueuedTask"/> will be created as blocking by default when <see cref="TaskBuilderBase{TSelf}.Build()"/> is called, or when <see cref="TaskBuilderBase{TSelf}.EnqueueTo(NoireTaskQueue)"/> is called.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the task.</param>
    public TaskBuilder(string? customId = null) : base(customId) { }

    /// <summary>
    /// Creates a new task builder which can be further configured.<br/>
    /// Same as calling the constructor <see cref="TaskBuilder(string?)"/>.<br/>
    /// The <see cref="QueuedTask"/> will be created as blocking by default when <see cref="TaskBuilderBase{TSelf}.Build()"/> is called, or when <see cref="TaskBuilderBase{TSelf}.EnqueueTo(NoireTaskQueue)"/> is called.
    /// </summary>
    /// <returns>The TaskBuilder instance for chaining.</returns>
    public static TaskBuilder Create(string? customId = null) => new(customId);

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

/// <summary>
/// Queue-bound variant of <see cref="TaskBuilder"/> that carries a typed <typeparamref name="TModule"/> reference.<br/>
/// All fluent methods return <see cref="TaskBuilder{TModule}"/>, so <see cref="Enqueue"/> is always reachable
/// at the end of any chain without casting.
/// </summary>
/// <typeparam name="TModule">The concrete <see cref="NoireTaskQueue"/> type this builder is bound to.</typeparam>
public class TaskBuilder<TModule> : TaskBuilderBase<TaskBuilder<TModule>> where TModule : NoireTaskQueue
{
    private readonly TModule taskQueue;

    /// <summary>
    /// Creates a new queue-bound builder for <paramref name="taskQueue"/>.<br/>
    /// The <see cref="QueuedTask"/> will be created as blocking by default.
    /// </summary>
    /// <param name="taskQueue">The queue this builder is bound to.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    public TaskBuilder(TModule taskQueue, string? customId = null) : base(customId)
    {
        this.taskQueue = taskQueue;
    }

    /// <summary>
    /// Creates a new queue-bound task builder for <paramref name="taskQueue"/>.
    /// </summary>
    public static TaskBuilder<TModule> Create(TModule taskQueue, string? customId = null)
        => new(taskQueue, customId);

    /// <summary>
    /// Will build the task and enqueue it to the associated <typeparamref name="TModule"/> instance provided in the constructor.<br/>
    /// </summary>
    public QueuedTask Enqueue()
    {
        task.OwningQueue = taskQueue;
        var builtTask = Build();
        taskQueue.EnqueueTask(builtTask);
        return builtTask;
    }
}
