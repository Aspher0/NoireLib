using System;
using System.Collections.Generic;

namespace NoireLib.TaskQueue;

/// <summary>
/// Base class for <see cref="BatchBuilder"/> and <see cref="BatchBuilder{TModule}"/>.<br/>
/// Uses the Curiously Recurring Template Pattern (CRTP) so that every fluent method returns
/// the concrete derived type, preserving the full API regardless of how deep the chain goes.
/// </summary>
/// <typeparam name="TSelf">The concrete builder type.</typeparam>
public class BatchBuilderBase<TSelf> where TSelf : BatchBuilderBase<TSelf>
{
    /// <summary>The underlying batch being configured by this builder.</summary>
    protected readonly TaskBatch batch;

    /// <summary>
    /// Initializes a new builder instance.<br/>
    /// The <see cref="TaskBatch"/> will be created as blocking by default.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the batch.</param>
    protected BatchBuilderBase(string? customId = null)
    {
        batch = new TaskBatch(customId, true);
    }

    // ── Fluent configuration ─────────────────────────────────────────────────

    /// <summary>
    /// Sets the custom ID for this batch.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithCustomId(string customId)
    {
        batch.CustomId = customId;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets whether the batch is blocking.<br/>
    /// When a batch is blocking, the queue will wait for it to complete before starting the next item.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AsBlocking()
    {
        batch.IsBlocking = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets whether the batch is non-blocking.<br/>
    /// When a batch is non-blocking, the queue will start the next item immediately after starting this batch.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AsNonBlocking()
    {
        batch.IsBlocking = false;
        return (TSelf)this;
    }

    /// <summary>
    /// Adds a task to this batch.
    /// </summary>
    /// <param name="task">The task to add.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddTask(QueuedTask task)
    {
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Adds multiple tasks to this batch.
    /// </summary>
    /// <param name="tasks">The tasks to add.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddTasks(params QueuedTask[] tasks)
    {
        foreach (var task in tasks)
        {
            task.ParentBatch = batch;
            batch.AddTask(task);
        }
        return (TSelf)this;
    }

    /// <summary>
    /// Adds multiple tasks to this batch.
    /// </summary>
    /// <param name="tasks">The tasks to add.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddTasks(IEnumerable<QueuedTask> tasks)
    {
        foreach (var task in tasks)
        {
            task.ParentBatch = batch;
            batch.AddTask(task);
        }
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the batch starts processing.
    /// </summary>
    /// <param name="callback">The callback to invoke when the batch starts.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnStarted(Action<TaskBatch> callback)
    {
        batch.OnStarted = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the batch completes successfully.
    /// </summary>
    /// <param name="callback">The callback to invoke when the batch completes.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnCompleted(Action<TaskBatch> callback)
    {
        batch.OnCompleted = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the batch is cancelled.
    /// </summary>
    /// <param name="callback">The callback to invoke when the batch is cancelled.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnCancelled(Action<TaskBatch> callback)
    {
        batch.OnCancelled = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the batch fails.
    /// </summary>
    /// <param name="callback">The callback to invoke when the batch fails.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnFailed(Action<TaskBatch, Exception?> callback)
    {
        batch.OnFailed = callback;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the batch fails or is cancelled.<br/>
    /// This is a convenience method for handling both failure and cancellation with the same callback.
    /// </summary>
    /// <param name="callback">The callback to invoke when the batch fails or is cancelled.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnFailedOrCancelled(Action<TaskBatch, Exception?> callback)
    {
        batch.OnFailed = callback;
        batch.OnCancelled = (b) => callback(b, null);
        return (TSelf)this;
    }

    /// <summary>
    /// Sets the callback for when the batch fails or is cancelled, and optionally stops the queue.<br/>
    /// This is a convenience method for handling both failure and cancellation with the same callback.
    /// </summary>
    /// <param name="callback">The callback to invoke when the batch fails or is cancelled.</param>
    /// <param name="stopQueue">Whether to stop the queue on batch failure or cancellation.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf OnFailedOrCancelled(Action<TaskBatch, Exception?> callback, bool stopQueue)
    {
        batch.OnFailed = callback;
        batch.OnCancelled = (b) => callback(b, null);
        batch.StopQueueOnFail = stopQueue;
        batch.StopQueueOnCancel = stopQueue;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the queue to stop when this batch fails.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopQueueOnFail()
    {
        batch.StopQueueOnFail = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the queue to stop when this batch is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopQueueOnCancel()
    {
        batch.StopQueueOnCancel = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the queue to stop when this batch fails or is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopQueueOnFailOrCancel()
    {
        batch.StopQueueOnFail = true;
        batch.StopQueueOnCancel = true;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets how the batch should handle task failures.
    /// </summary>
    /// <param name="mode">The failure handling mode.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithTaskFailureMode(BatchTaskFailureMode mode)
    {
        batch.TaskFailureMode = mode;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets how the batch should handle task cancellations.
    /// </summary>
    /// <param name="mode">The cancellation handling mode.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithTaskCancellationMode(BatchTaskCancellationMode mode)
    {
        batch.TaskCancellationMode = mode;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the batch to stop immediately when any task fails.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopBatchOnTaskFailure()
    {
        batch.TaskFailureMode = BatchTaskFailureMode.FailBatch;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the batch to continue processing remaining tasks even if one fails.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf ContinueOnTaskFailure()
    {
        batch.TaskFailureMode = BatchTaskFailureMode.ContinueRemaining;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the batch to stop the entire queue when any task fails.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf StopQueueOnTaskFailure()
    {
        batch.TaskFailureMode = BatchTaskFailureMode.FailBatchAndStopQueue;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the batch to cancel immediately when any task is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf CancelBatchOnTaskCancellation()
    {
        batch.TaskCancellationMode = BatchTaskCancellationMode.CancelBatch;
        return (TSelf)this;
    }

    /// <summary>
    /// Configures the batch to continue processing remaining tasks even if one is cancelled.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf ContinueOnTaskCancellation()
    {
        batch.TaskCancellationMode = BatchTaskCancellationMode.ContinueRemaining;
        return (TSelf)this;
    }

    /// <summary>
    /// Attaches custom metadata to this batch.
    /// </summary>
    /// <param name="metadata">The metadata object to attach.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithMetadata(object metadata)
    {
        batch.Metadata = metadata;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a post-completion delay for the batch.<br/>
    /// Once the batch completes, the queue will wait this amount of time before proceeding with the rest of the queue.
    /// </summary>
    /// <param name="delay">The delay duration.</param>
    /// <param name="applyOnFailure">Whether to apply the delay when the batch fails (default: false).</param>
    /// <param name="applyOnCancellation">Whether to apply the delay when the batch is cancelled (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithDelay(TimeSpan? delay, bool applyOnFailure = false, bool applyOnCancellation = false)
    {
        batch.PostCompletionDelay = delay;
        batch.ApplyPostDelayOnFailure = applyOnFailure;
        batch.ApplyPostDelayOnCancellation = applyOnCancellation;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a post-completion delay for the batch using a predicate function.<br/>
    /// The delay will be evaluated at the moment the post-completion delay is about to start (not at batch creation), allowing for dynamic delay calculation.
    /// </summary>
    /// <param name="delayPredicate">A function that returns the delay duration.</param>
    /// <param name="applyOnFailure">Whether to apply the delay when the batch fails (default: false).</param>
    /// <param name="applyOnCancellation">Whether to apply the delay when the batch is cancelled (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithDelay(Func<TimeSpan?> delayPredicate, bool applyOnFailure = false, bool applyOnCancellation = false)
    {
        batch.PostCompletionDelayProvider = _ => delayPredicate();
        batch.ApplyPostDelayOnFailure = applyOnFailure;
        batch.ApplyPostDelayOnCancellation = applyOnCancellation;
        return (TSelf)this;
    }

    /// <summary>
    /// Sets a post-completion delay for the batch using a predicate function with access to the batch.<br/>
    /// The delay will be evaluated at the moment the post-completion delay is about to start (not at batch creation), allowing for dynamic delay calculation based on batch state.
    /// </summary>
    /// <param name="delayPredicate">A function that receives the batch and returns the delay duration.</param>
    /// <param name="applyOnFailure">Whether to apply the delay when the batch fails (default: false).</param>
    /// <param name="applyOnCancellation">Whether to apply the delay when the batch is cancelled (default: false).</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf WithDelay(Func<TaskBatch, TimeSpan?> delayPredicate, bool applyOnFailure = false, bool applyOnCancellation = false)
    {
        batch.PostCompletionDelayProvider = delayPredicate;
        batch.ApplyPostDelayOnFailure = applyOnFailure;
        batch.ApplyPostDelayOnCancellation = applyOnCancellation;
        return (TSelf)this;
    }

    /// <summary>
    /// Creates and adds a task to the batch using a fluent task builder configurator.
    /// </summary>
    /// <param name="configurator">Action that configures the task builder.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddTask(Action<TaskBuilder> configurator)
    {
        var taskBuilder = TaskBuilder.Create();
        configurator(taskBuilder);
        var task = taskBuilder.Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Creates and adds a simple action-only task with immediate completion to the batch.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddAction(Action action, string? customId = null)
    {
        var task = TaskBuilder.Create(customId)
            .WithAction(action)
            .WithImmediateCompletion()
            .Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Creates and adds a simple action-only task with immediate completion to the batch.
    /// </summary>
    /// <param name="action">The action to execute (receives the task as parameter).</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddAction(Action<QueuedTask> action, string? customId = null)
    {
        var task = TaskBuilder.Create(customId)
            .WithAction(action)
            .WithImmediateCompletion()
            .Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Creates and adds a condition-only task (no action, just waits for condition) to the batch.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddCondition(Func<bool> condition, string? customId = null)
    {
        var task = TaskBuilder.Create(customId)
            .WithCondition(condition)
            .Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Creates and adds a condition-only task (no action, just waits for condition) to the batch.
    /// </summary>
    /// <param name="condition">The condition to wait for (receives the task as parameter).</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddCondition(Func<QueuedTask, bool> condition, string? customId = null)
    {
        var task = TaskBuilder.Create(customId)
            .WithCondition(condition)
            .Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Creates and adds a delay-only task to the batch.
    /// </summary>
    /// <param name="delay">The delay duration.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddDelay(TimeSpan delay, string? customId = null)
    {
        var task = TaskBuilder.Create(customId)
            .WithDelay(delay)
            .Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Creates and adds a delay-only task to the batch.
    /// </summary>
    /// <param name="seconds">The delay in seconds.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddDelaySeconds(double seconds, string? customId = null)
    {
        return AddDelay(TimeSpan.FromSeconds(seconds), customId);
    }

    /// <summary>
    /// Creates and adds a delay-only task to the batch.
    /// </summary>
    /// <param name="milliseconds">The delay in milliseconds.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddDelayMilliseconds(int milliseconds, string? customId = null)
    {
        return AddDelay(TimeSpan.FromMilliseconds(milliseconds), customId);
    }

    /// <summary>
    /// Creates and adds an event-waiting task to the batch.
    /// </summary>
    /// <typeparam name="TEvent">The event type to wait for.</typeparam>
    /// <param name="filter">Optional filter for the event.</param>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddEventWait<TEvent>(Func<TEvent, bool>? filter = null, string? customId = null)
    {
        var task = TaskBuilder.Create(customId)
            .WaitForEvent(filter)
            .Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return (TSelf)this;
    }

    /// <summary>
    /// Adds multiple tasks to the batch using a configurator pattern.<br/>
    /// This allows creating and enqueueing multiple tasks in a fluent manner.
    /// </summary>
    /// <param name="configurator">Action that receives a configurator for creating and enqueueing tasks.</param>
    /// <returns>The builder instance for chaining.</returns>
    public TSelf AddTasks(Action<BatchTaskConfigurator> configurator)
    {
        var taskConfigurator = new BatchTaskConfigurator(batch);
        configurator(taskConfigurator);
        return (TSelf)this;
    }

    // ── Terminal methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds and returns the configured batch.
    /// </summary>
    /// <returns>The TaskBatch.</returns>
    public TaskBatch Build()
    {
        return batch;
    }

    /// <summary>
    /// Builds the batch and adds it to the specified <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="queue">The <see cref="NoireTaskQueue"/> to add the batch to.</param>
    /// <returns>The built batch.</returns>
    public TaskBatch EnqueueTo(NoireTaskQueue queue)
    {
        var builtBatch = Build();
        queue.EnqueueBatch(builtBatch);
        return builtBatch;
    }
}

/// <summary>
/// Fluent builder for creating task batches with a clean API.<br/>
/// See <see cref="TaskBatch"/> for more information on each property.<br/>
/// For a queue-bound variant where <see cref="BatchBuilder{TModule}.Enqueue"/> is directly available, use <see cref="BatchBuilder{TModule}"/>.
/// </summary>
public class BatchBuilder : BatchBuilderBase<BatchBuilder>
{
    /// <summary>
    /// Creates a new batch builder.<br/>
    /// The <see cref="TaskBatch"/> will be created as blocking by default.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the batch.</param>
    public BatchBuilder(string? customId = null) : base(customId) { }

    /// <summary>
    /// Creates a new batch builder which can be further configured.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the batch.</param>
    /// <returns>The BatchBuilder instance for chaining.</returns>
    public static BatchBuilder Create(string? customId = null) => new(customId);
}

/// <summary>
/// Queue-bound variant of <see cref="BatchBuilder"/> that carries a typed <typeparamref name="TModule"/> reference.<br/>
/// All fluent methods return <see cref="BatchBuilder{TModule}"/>, so <see cref="Enqueue"/> is always reachable
/// at the end of any chain without casting.
/// </summary>
/// <typeparam name="TModule">The concrete <see cref="NoireTaskQueue"/> type this builder is bound to.</typeparam>
public class BatchBuilder<TModule> : BatchBuilderBase<BatchBuilder<TModule>> where TModule : NoireTaskQueue
{
    private readonly TModule taskQueue;

    /// <summary>
    /// Creates a new queue-bound builder for <paramref name="taskQueue"/>.<br/>
    /// The <see cref="TaskBatch"/> will be created as blocking by default.
    /// </summary>
    /// <param name="taskQueue">The queue this builder is bound to.</param>
    /// <param name="customId">Optional custom identifier for the batch.</param>
    public BatchBuilder(TModule taskQueue, string? customId = null) : base(customId)
    {
        this.taskQueue = taskQueue;
    }

    /// <summary>
    /// Creates a new queue-bound batch builder for <paramref name="taskQueue"/>.
    /// </summary>
    /// <param name="taskQueue">The queue this builder is bound to.</param>
    /// <param name="customId">Optional custom identifier for the batch.</param>
    /// <returns>The BatchBuilder instance for chaining.</returns>
    public static BatchBuilder<TModule> Create(TModule taskQueue, string? customId = null)
        => new(taskQueue, customId);

    /// <summary>
    /// Builds the batch and enqueues it to the associated <typeparamref name="TModule"/> instance provided in the constructor.
    /// </summary>
    /// <returns>The built batch.</returns>
    public TaskBatch Enqueue()
    {
        var builtBatch = Build();
        taskQueue.EnqueueBatch(builtBatch);
        return builtBatch;
    }

    /// <summary>
    /// Creates and adds a task to the batch using a task builder configurator.
    /// </summary>
    /// <param name="configurator">Action that configures the task builder.</param>
    /// <returns>The builder instance for chaining.</returns>
    public BatchBuilder<TModule> AddTask(Action<TaskBuilder<TModule>> configurator)
    {
        var taskBuilder = TaskBuilder<TModule>.Create(taskQueue);
        configurator(taskBuilder);
        var task = taskBuilder.Build();
        task.ParentBatch = batch;
        batch.AddTask(task);
        return this;
    }
}

/// <summary>
/// Configurator for creating tasks within a batch context.<br/>
/// Provides a fluent API for creating multiple tasks and adding them to a batch.
/// </summary>
public class BatchTaskConfigurator
{
    private readonly TaskBatch batch;

    internal BatchTaskConfigurator(TaskBatch batch)
    {
        this.batch = batch;
    }

    /// <summary>
    /// Creates a new task builder bound to the parent batch.<br/>
    /// Call <see cref="BatchTaskBuilder.Enqueue"/> to add the task to the batch.
    /// </summary>
    /// <param name="customId">Optional custom identifier for the task.</param>
    /// <returns>A batch-bound task builder.</returns>
    public BatchTaskBuilder Create(string? customId = null)
        => new(batch, customId);
}

/// <summary>
/// Task builder bound to a batch, allowing fluent task creation and enqueueing.<br/>
/// All fluent methods from <see cref="TaskBuilderBase{TSelf}"/> are available.
/// </summary>
public class BatchTaskBuilder : TaskBuilderBase<BatchTaskBuilder>
{
    private readonly TaskBatch parentBatch;

    internal BatchTaskBuilder(TaskBatch parentBatch, string? customId = null) : base(customId)
    {
        this.parentBatch = parentBatch;
    }

    /// <summary>
    /// Builds the task and adds it to the parent batch.
    /// </summary>
    /// <returns>The built and enqueued task.</returns>
    public QueuedTask Enqueue()
    {
        var builtTask = Build();
        builtTask.ParentBatch = parentBatch;
        parentBatch.AddTask(builtTask);
        return builtTask;
    }
}
