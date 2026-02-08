using Dalamud.Plugin.Services;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.TaskQueue;

/// <summary>
/// A module providing task queuing and processing, in a blocking or non-blocking way, based on conditions and callbacks.<br/>
/// Useful for processing tasks one at a time while awaiting certain conditions, or for scheduling tasks to be executed under specific scenarios.<br/>
/// See <see cref="QueuedTask"/> and <see cref="TaskCompletionCondition"/> for task definitions and completion conditions.<br/>
/// See <see cref="TaskBuilder"/> for building and enqueuing tasks comprehensively and easily with chainable methods.
/// </summary>
public class NoireTaskQueue : NoireModuleBase<NoireTaskQueue>
{
    private readonly List<QueuedTask> taskQueue = new();
    private readonly object queueLock = new();

    private QueuedTask? currentTask;

    private int totalTasksQueued;
    private int tasksCompleted;
    private int tasksCancelled;
    private int tasksFailed;
    private long processingStartTimeTicks; // last start/resume tick
    private long accumulatedProcessingMillis; // total active processing time excluding pauses

    /// <summary>
    /// The associated EventBus instance for publishing queue events or subscribing to events for task completion conditions.<br/>
    /// If <see langword="null"/>, no events will be published, and event-based completion conditions will not function.
    /// </summary>
    public NoireEventBus? EventBus { get; set; } = null;

    private QueueState queueState = QueueState.Idle;
    /// <summary>
    /// The current state of the queue.
    /// </summary>
    public QueueState QueueState
    {
        get => queueState;
        private set => queueState = value;
    }

    private bool shouldProcessQueueAutomatically = false;
    /// <summary>
    /// If true, the queue will automatically start processing when a task is added.
    /// </summary>
    public bool ShouldProcessQueueAutomatically
    {
        get => shouldProcessQueueAutomatically;
        set => shouldProcessQueueAutomatically = value;
    }

    private bool shouldStopQueueOnComplete = true;
    /// <summary>
    /// If true, completed tasks will be automatically removed from the queue.
    /// </summary>
    public bool ShouldStopQueueOnComplete
    {
        get => shouldStopQueueOnComplete;
        set => shouldStopQueueOnComplete = value;
    }

    /// <summary>
    /// The default constructor needed for internal purposes.
    /// </summary>
    public NoireTaskQueue() : base() { }

    /// <summary>
    /// Creates a new instance of the <see cref="NoireTaskQueue"/> module.
    /// </summary>
    /// <param name="moduleId">The optional module identifier.</param>
    /// <param name="active">Whether the module should be active upon creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="shouldProcessQueueAutomatically">Whether to automatically start processing when tasks are added.</param>
    /// <param name="shouldStopQueueOnComplete">Whether to clear completed tasks automatically.</param>
    /// <param name="eventBus">Optional EventBus instance to publish queue events.</param>
    public NoireTaskQueue(
        string? moduleId = null,
        bool active = true,
        bool enableLogging = true,
        bool shouldProcessQueueAutomatically = false,
        bool shouldStopQueueOnComplete = true,
        NoireEventBus? eventBus = null)
        : base(moduleId, active, enableLogging, shouldProcessQueueAutomatically, shouldStopQueueOnComplete, eventBus) { }

    /// <summary>
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>.<br/>
    /// Only used for internal module management.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    internal NoireTaskQueue(ModuleId? moduleId, bool active = true, bool enableLogging = true)
    : base(moduleId, active, enableLogging) { }

    /// <summary>
    /// Initializes the module with optional initialization parameters.
    /// </summary>
    /// <param name="args">The initialization parameters</param>
    protected override void InitializeModule(params object?[] args)
    {
        if (args.Length > 0 && args[0] is bool autoProcess)
            shouldProcessQueueAutomatically = autoProcess;

        if (args.Length > 1 && args[1] is bool stopOnComplete)
            shouldStopQueueOnComplete = stopOnComplete;

        if (args.Length > 2 && args[2] is NoireEventBus eventBus)
            EventBus = eventBus;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Task Queue initialized.");
    }

    /// <summary>
    /// Called when the module is activated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> false to true.
    /// </summary>
    protected override void OnActivated()
    {
        NoireService.Framework.Update += OnFrameworkUpdate;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Task Queue activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        NoireService.Framework.Update -= OnFrameworkUpdate;
        StopQueue();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Task Queue deactivated.");
    }

    #region Framework Update

    /// <summary>
    /// Used to process the queue every frame.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsActive || QueueState != QueueState.Running)
            return;

        try
        {
            ProcessQueue();
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Error in queue processing.");
        }
    }

    #endregion

    #region EventBus Integration

    /// <summary>
    /// Publishes a queue event to the EventBus if available.
    /// </summary>
    private void PublishEvent<TEvent>(TEvent eventData)
    {
        EventBus?.Publish(eventData);
    }

    /// <summary>
    /// Subscribes to an event for a specific task's completion condition.
    /// </summary>
    private void SubscribeToEventForTask(QueuedTask task)
    {
        if (EventBus == null || task.CompletionCondition?.EventType == null)
            return;

        var eventType = task.CompletionCondition.EventType;

        // Use reflection to call the generic Subscribe method with the correct event type
        var subscribeMethod = typeof(NoireEventBus).GetMethod(nameof(NoireEventBus.Subscribe));
        if (subscribeMethod == null)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, "Could not find Subscribe method on EventBus");
            return;
        }

        var genericSubscribeMethod = subscribeMethod.MakeGenericMethod(eventType);

        // Create a wrapper that captures the task context
        var wrapperDelegate = CreateEventHandlerWrapper(eventType, task);

        try
        {
            var token = genericSubscribeMethod.Invoke(EventBus, [wrapperDelegate, 0, null, this]);
            if (token != null)
            {
                task.EventSubscriptionToken = (EventSubscriptionToken)token;
                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Subscribed task {task} to event {eventType.Name} with token {token}");
            }
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"Failed to subscribe task {task} to event {eventType.Name}");
        }
    }

    /// <summary>
    /// Creates a wrapper delegate for event handling that captures the task context.
    /// </summary>
    private Delegate CreateEventHandlerWrapper(Type eventType, QueuedTask task)
    {
        var handlerType = typeof(Action<>).MakeGenericType(eventType);

        // Create a dynamic method that checks the filter of the Event and sets the condition met flag
        Action<object> wrapper = (evt) =>
        {
            if (task.Status != TaskStatus.WaitingForCompletion)
                return;

            if (task.CompletionCondition?.EventFilter == null || task.CompletionCondition.EventFilter(evt))
            {
                if (task.CompletionCondition != null)
                    task.CompletionCondition.EventConditionMet = true;

                if (EnableLogging)
                    NoireLogger.LogDebug(this, $"Event condition met for task: {task}");
            }
        };

        return Delegate.CreateDelegate(handlerType, wrapper.Target, wrapper.Method);
    }

    /// <summary>
    /// Unsubscribes a specific task from its EventBus event.
    /// </summary>
    private void UnsubscribeTask(QueuedTask task)
    {
        if (EventBus == null || task.EventSubscriptionToken == null)
            return;

        try
        {
            EventBus.Unsubscribe(task.EventSubscriptionToken.Value);
            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Unsubscribed task {task} from EventBus event");
        }
        finally
        {
            task.EventSubscriptionToken = null;
        }
    }

    /// <summary>
    /// Unsubscribes from all events by unsubscribing all tasks.
    /// </summary>
    private void UnsubscribeFromAllEvents()
    {
        if (EventBus == null)
            return;

        lock (queueLock)
        {
            foreach (var task in taskQueue)
                if (task.EventSubscriptionToken != null)
                    UnsubscribeTask(task);
        }
    }

    #endregion

    #region Queue Management

    /// <summary>
    /// Adds a task to the queue.
    /// </summary>
    /// <param name="task">The <see cref="QueuedTask"/> to add.</param>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue EnqueueTask(QueuedTask task)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot enqueue task - module is not active.");
            return this;
        }

        bool subscribed = false;
        lock (queueLock)
        {
            taskQueue.Add(task);
            totalTasksQueued++;
            if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent && task.CompletionCondition.EventType != null)
            {
                SubscribeToEventForTask(task);
                subscribed = true;
            }
        }

        PublishEvent(new TaskQueuedEvent(task));

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Task enqueued: {task}{(subscribed ? " (event subscribed)" : "")}");

        if (ShouldProcessQueueAutomatically && (QueueState == QueueState.Idle || QueueState == QueueState.Stopped))
            StartQueue();

        return this;
    }

    /// <summary>
    /// Inserts a task after another task in the queue.
    /// </summary>
    /// <param name="task">The task to insert.</param>
    /// <param name="afterTaskSystemId">The system ID of the task to insert after.</param>
    /// <returns>True if the task was successfully inserted; false if the target task was not found.</returns>
    public bool InsertTaskAfter(QueuedTask task, Guid afterTaskSystemId)
    {
        return InsertTaskAfterInternal(task, t => t.SystemId == afterTaskSystemId, afterTaskSystemId.ToString());
    }

    /// <summary>
    /// Inserts a task after another task in the queue by custom ID.
    /// </summary>
    /// <param name="task">The task to insert.</param>
    /// <param name="afterTaskCustomId">The custom ID of the task to insert after.</param>
    /// <returns>True if the task was successfully inserted; false if the target task was not found.</returns>
    public bool InsertTaskAfter(QueuedTask task, string afterTaskCustomId)
    {
        return InsertTaskAfterInternal(task, t => t.CustomId == afterTaskCustomId, afterTaskCustomId);
    }

    /// <summary>
    /// Internal method to insert a task after another task matching a predicate.
    /// </summary>
    private bool InsertTaskAfterInternal(QueuedTask task, Func<QueuedTask, bool> predicate, string targetDescription)
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot insert task - module is not active.");
            return false;
        }

        bool subscribed = false;
        bool inserted = false;
        lock (queueLock)
        {
            var targetIndex = taskQueue.FindIndex(t => predicate(t));
            if (targetIndex == -1)
            {
                if (EnableLogging)
                    NoireLogger.LogWarning(this, $"Cannot insert task - target task with ID '{targetDescription}' not found.");
                return false;
            }

            var currentExecutingTaskIndex = currentTask != null ? taskQueue.IndexOf(currentTask) : -1;

            if (targetIndex < currentExecutingTaskIndex)
            {
                if (EnableLogging)
                    NoireLogger.LogWarning(this, $"Cannot insert task - target task '{targetDescription}' is already executed. Can only insert after queued or current tasks.");
                return false;
            }

            taskQueue.Insert(targetIndex + 1, task);
            totalTasksQueued++;
            inserted = true;

            if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent && task.CompletionCondition.EventType != null)
            {
                SubscribeToEventForTask(task);
                subscribed = true;
            }
        }

        if (inserted)
        {
            PublishEvent(new TaskQueuedEvent(task));

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Task inserted after {targetDescription}: {task}{(subscribed ? " (event subscribed)" : "")}");

            if (ShouldProcessQueueAutomatically && (QueueState == QueueState.Idle || QueueState == QueueState.Stopped))
                StartQueue();
        }

        return inserted;
    }

    /// <summary>
    /// Skips the next N tasks in the queue by cancelling them.
    /// </summary>
    /// <param name="count">The number of queued tasks to skip. Needs to be greater than 0.</param>
    /// <param name="includeCurrentTask">If true, will mark the currently executing task for skipping if it exists.<br/>
    /// If true, argument <paramref name="count"/> will include the current task in the skip count.</param>
    /// <returns>The number of tasks that were actually skipped.</returns>
    public int SkipNextTasks(int count, bool includeCurrentTask = false)
    {
        if (count <= 0)
            return 0;

        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                int skipped = 0;

                if (includeCurrentTask && currentTask != null)
                {
                    if (CancelTaskInternal(currentTask))
                    {
                        skipped++;
                        if (EnableLogging)
                            NoireLogger.LogInfo(this, $"Skipped current task: {currentTask}");
                    }
                }

                var queuedTasks = count > skipped
                    ? taskQueue.Where(t => t.Status == TaskStatus.Queued).Take(count - skipped).ToList()
                    : [];

                foreach (var task in queuedTasks)
                {
                    if (CancelTaskInternal(task))
                        skipped++;
                }

                if (EnableLogging && skipped > 0)
                    NoireLogger.LogInfo(this, $"Skipped {skipped} task(s){(includeCurrentTask ? " (including current task)" : "")}.");

                return skipped;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }


    /// <summary>
    /// Skips the current task regardless of its status.
    /// </summary>
    /// <returns>true if the current task was skipped; otherwise, false.</returns>
    public bool SkipCurrentTask()
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                if (currentTask == null)
                    return false;

                if (!CancelTaskInternal(currentTask))
                    return false;

                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Skipped current task: {currentTask}");

                return true;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Jumps to a specific task by system ID, cancelling all queued tasks before it.
    /// </summary>
    /// <param name="targetSystemId">The system ID of the task to jump to.</param>
    /// <returns>True if the jump was successful; false if the target task was not found or not in Queued status.</returns>
    public bool JumpToTask(Guid targetSystemId)
    {
        return JumpToTaskInternal(t => t.SystemId == targetSystemId, targetSystemId.ToString());
    }

    /// <summary>
    /// Jumps to a specific task by custom ID, cancelling all queued tasks before it.
    /// </summary>
    /// <param name="targetCustomId">The custom ID of the task to jump to.</param>
    /// <returns>True if the jump was successful; false if the target task was not found or not in Queued status.</returns>
    public bool JumpToTask(string targetCustomId)
    {
        return JumpToTaskInternal(t => t.CustomId == targetCustomId, targetCustomId);
    }

    /// <summary>
    /// Internal method to jump to a task matching a predicate.
    /// </summary>
    private bool JumpToTaskInternal(Func<QueuedTask, bool> predicate, string targetDescription)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                var targetTask = taskQueue.FirstOrDefault(predicate);
                if (targetTask == null)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - task with ID {targetDescription} not found.");
                    return false;
                }

                if (targetTask.Status != TaskStatus.Queued)
                {
                    if (EnableLogging)
                        NoireLogger.LogWarning(this, $"Cannot jump to task - task {targetTask} is not in Queued status (Status: {targetTask.Status}).");
                    return false;
                }

                var targetIndex = taskQueue.IndexOf(targetTask);
                var tasksToCancel = taskQueue.Take(targetIndex).Where(t =>
                    t.Status == TaskStatus.Queued ||
                    t.Status == TaskStatus.Executing ||
                    t.Status == TaskStatus.WaitingForCompletion ||
                    t.Status == TaskStatus.WaitingForPostDelay).ToList();
                int cancelled = 0;

                foreach (var task in tasksToCancel)
                {
                    if (CancelTaskInternal(task))
                        cancelled++;
                }

                if (EnableLogging)
                    NoireLogger.LogInfo(this, $"Jumped to task {targetTask}, cancelled {cancelled} task(s) before it.");

                return true;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Creates and enqueues a simple task.
    /// </summary>
    /// <param name="customId">Optional custom identifier.</param>
    /// <param name="isBlocking">Whether the task blocks subsequent tasks.</param>
    /// <param name="executeAction">The action to execute.</param>
    /// <param name="completionCondition">The completion condition.</param>
    /// <param name="onCompleted">Optional completion callback.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <returns>The created task.</returns>
    public QueuedTask EnqueueTask(
        string? customId,
        bool isBlocking,
        Action? executeAction,
        TaskCompletionCondition? completionCondition,
        Action<QueuedTask>? onCompleted = null,
        TimeSpan? timeout = null)
    {
        var task = new QueuedTask(customId, isBlocking)
        {
            ExecuteAction = executeAction,
            CompletionCondition = completionCondition ?? TaskCompletionCondition.Immediate(),
            OnCompleted = onCompleted,
            Timeout = timeout
        };

        EnqueueTask(task);
        return task;
    }

    /// <summary>
    /// Gets a task by its system ID.
    /// </summary>
    public QueuedTask? GetTaskBySystemId(Guid systemId)
    {
        lock (queueLock)
        {
            return taskQueue.FirstOrDefault(t => t.SystemId == systemId);
        }
    }

    /// <summary>
    /// Gets a task by its custom ID.
    /// </summary>
    public QueuedTask? GetTaskByCustomId(string customId)
    {
        lock (queueLock)
        {
            return taskQueue.FirstOrDefault(t => t.CustomId == customId);
        }
    }

    /// <summary>
    /// Gets all tasks with a specific custom ID.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetTasksByCustomId(string customId)
    {
        lock (queueLock)
        {
            return taskQueue.Where(t => t.CustomId == customId).ToList();
        }
    }

    /// <summary>
    /// Cancels a task by its system ID.
    /// </summary>
    public bool CancelTask(Guid systemId)
    {
        lock (queueLock)
        {
            var task = taskQueue.FirstOrDefault(t => t.SystemId == systemId);
            if (task == null)
                return false;

            return CancelTaskInternal(task);
        }
    }

    /// <summary>
    /// Cancels a task by its custom ID.
    /// </summary>
    public bool CancelTask(string customId)
    {
        lock (queueLock)
        {
            var task = taskQueue.FirstOrDefault(t => t.CustomId == customId);
            if (task == null)
                return false;

            return CancelTaskInternal(task);
        }
    }

    /// <summary>
    /// Cancels all tasks with a specific custom ID.
    /// </summary>
    public int CancelAllTasks(string customId)
    {
        var wasRunning = QueueState == QueueState.Running;

        if (wasRunning)
            PauseQueue();

        try
        {
            lock (queueLock)
            {
                var tasks = taskQueue.Where(t => t.CustomId == customId).ToList();
                int cancelled = 0;

                foreach (var task in tasks)
                    if (CancelTaskInternal(task))
                        cancelled++;

                return cancelled;
            }
        }
        finally
        {
            if (wasRunning)
                ResumeQueue();
        }
    }

    /// <summary>
    /// Internal method to cancel a task.
    /// </summary>
    private bool CancelTaskInternal(QueuedTask task)
    {
        if (task.Status == TaskStatus.Completed || task.Status == TaskStatus.Cancelled || task.Status == TaskStatus.Failed)
            return false;

        UnsubscribeTask(task);

        task.Status = TaskStatus.Cancelled;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCancelled++;

        try
        {
            task.OnCancelled?.Invoke(task);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnCancelled callback threw an exception.");
        }

        PublishEvent(new TaskCancelledEvent(task));

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

        if (task.StopQueueOnCancel)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to task cancellation: {task}");
            StopQueue();
        }

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Task cancelled: {task}");

        return true;
    }

    /// <summary>
    /// Clears all tasks from the queue.
    /// </summary>
    /// <returns>The number of tasks cleared.</returns>
    public int ClearQueue()
    {
        var previousState = QueueState;

        QueueState = QueueState.Paused;

        List<QueuedTask> removed = new();
        try
        {
            lock (queueLock)
            {
                if (currentTask != null)
                    CancelTaskInternal(currentTask);

                removed = taskQueue.ToList();
                taskQueue.Clear();
                currentTask = null;
            }
        }
        finally
        {
            foreach (var t in removed)
                UnsubscribeTask(t);

            PublishEvent(new QueueClearedEvent(removed.Count));

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Queue cleared: {removed.Count} tasks removed.");

            QueueState = previousState;
        }

        return removed.Count;
    }

    /// <summary>
    /// Removes completed/cancelled/failed tasks from the queue.
    /// </summary>
    /// <returns>The number of tasks removed.</returns>
    public int ClearCompletedTasks()
    {
        List<QueuedTask> toRemove;
        lock (queueLock)
        {
            toRemove = taskQueue.Where(t =>
            t.Status == TaskStatus.Completed ||
            t.Status == TaskStatus.Cancelled ||
            t.Status == TaskStatus.Failed).ToList();

            foreach (var task in toRemove)
                taskQueue.Remove(task);

            if (currentTask != null && toRemove.Contains(currentTask))
                currentTask = null;
        }

        foreach (var task in toRemove)
            UnsubscribeTask(task);

        if (EnableLogging && toRemove.Count > 0)
            NoireLogger.LogDebug(this, $"Cleared {toRemove.Count} completed tasks.");

        return toRemove.Count;
    }

    #endregion

    #region Queue Control

    /// <summary>
    /// Starts processing the queue.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue StartQueue()
    {
        if (!IsActive)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot start queue - module is not active.");
            return this;
        }

        if (QueueState == QueueState.Running)
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, "Queue is already running.");
            return this;
        }

        if (taskQueue.Count == 0)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot start queue - no tasks in the queue.");
            return this;
        }

        // If starting from paused state, resume delay-based tasks and timeouts
        if (QueueState == QueueState.Paused)
        {
            lock (queueLock)
            {
                foreach (var task in taskQueue)
                {
                    if (task.Status == TaskStatus.WaitingForPostDelay && task.PostCompletionDelay.HasValue)
                        task.PausePostDelay();

                    if ((task.Status == TaskStatus.Executing || task.Status == TaskStatus.WaitingForCompletion) && task.Timeout.HasValue)
                        task.ResumeTimeout();
                }
            }
        }

        QueueState = QueueState.Running;
        processingStartTimeTicks = Environment.TickCount64;

        PublishEvent(new QueueStartedEvent());

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Queue started.");

        return this;
    }

    /// <summary>
    /// Pauses the queue processing.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue PauseQueue()
    {
        if (QueueState != QueueState.Running)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot pause queue - it is not running.");
            return this;
        }

        accumulatedProcessingMillis += Environment.TickCount64 - processingStartTimeTicks;
        processingStartTimeTicks = 0;

        lock (queueLock)
        {
            foreach (var task in taskQueue)
            {
                if ((task.Status == TaskStatus.Executing || task.Status == TaskStatus.WaitingForCompletion || task.Status == TaskStatus.WaitingForPostDelay) && task.Timeout.HasValue)
                    task.PauseTimeout();

                if (task.Status == TaskStatus.WaitingForCompletion && task.RetryConfiguration != null)
                    task.PauseStallTracking();

                if (task.Status == TaskStatus.WaitingForPostDelay && task.PostCompletionDelay.HasValue)
                    task.PausePostDelay();
            }
        }

        QueueState = QueueState.Paused;
        PublishEvent(new QueuePausedEvent());

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Queue paused.");

        return this;
    }

    /// <summary>
    /// Resumes the queue processing from pause.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue ResumeQueue()
    {
        if (QueueState != QueueState.Paused)
        {
            if (EnableLogging)
                NoireLogger.LogWarning(this, "Cannot resume queue - it is not paused.");
            return this;
        }

        QueueState = QueueState.Running;
        processingStartTimeTicks = Environment.TickCount64;

        lock (queueLock)
        {
            foreach (var task in taskQueue)
            {
                if ((task.Status == TaskStatus.Executing || task.Status == TaskStatus.WaitingForCompletion) && task.Timeout.HasValue)
                    task.ResumeTimeout();

                if (task.Status == TaskStatus.WaitingForCompletion && task.RetryConfiguration != null)
                    task.ResumeStallTracking();

                if (task.Status == TaskStatus.WaitingForPostDelay && task.PostCompletionDelay.HasValue)
                    task.ResumePostDelay();
            }
        }

        PublishEvent(new QueueResumedEvent());

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Queue resumed.");

        return this;
    }

    /// <summary>
    /// Stops the queue processing and clears any remaining tasks.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public NoireTaskQueue StopQueue()
    {
        ClearQueue();

        if (QueueState == QueueState.Idle || QueueState == QueueState.Stopped)
        {
            if (EnableLogging)
                NoireLogger.LogDebug(this, "Queue is already stopped or idle.");

            return this;
        }

        if (QueueState == QueueState.Running && processingStartTimeTicks > 0)
        {
            accumulatedProcessingMillis += Environment.TickCount64 - processingStartTimeTicks;
            processingStartTimeTicks = 0;
        }

        QueueState = QueueState.Stopped;

        PublishEvent(new QueueStoppedEvent());

        if (EnableLogging)
        {
            NoireLogger.LogInfo(this, "Queue stopped. " +
                $"Total Tasks Queued: {totalTasksQueued}, " +
                $"Completed: {tasksCompleted}, " +
                $"Cancelled: {tasksCancelled}, " +
                $"Failed: {tasksFailed}, " +
                $"Total Active Processing Time: {accumulatedProcessingMillis} ms.");
        }

        return this;
    }

    #endregion

    #region Queue Processing

    /// <summary>
    /// Main queue processing method called every frame.
    /// </summary>
    private void ProcessQueue()
    {
        QueuedTask? taskToProcess = null;
        bool shouldWaitForBlocking = false;
        bool shouldCheckCompletion = false;
        bool earlyReturn = false;

        // Newly added lists for non-current waiting tasks completion/timeout checks.
        List<QueuedTask> waitingTasksToComplete = new();
        List<QueuedTask> waitingTasksToFail = new();

        lock (queueLock)
        {
            if (currentTask != null)
            {
                // Special handling for retry-delayed tasks in Queued status
                if (currentTask.Status == TaskStatus.Queued && currentTask.Metadata is RetryDelayMetadata)
                {
                    // This is a task waiting for retry delay - we need to process it
                    taskToProcess = currentTask;
                    taskToProcess.Status = TaskStatus.Executing;
                }
                else if (currentTask.Status == TaskStatus.WaitingForCompletion)
                {
                    bool conditionMet = currentTask.CompletionCondition?.IsMet() == true;

                    if (conditionMet)
                    {
                        // Check if we need to start post-completion delay
                        if (currentTask.PostCompletionDelay.HasValue && !currentTask.PostDelayStartTicks.HasValue)
                        {
                            currentTask.PostDelayStartTicks = Environment.TickCount64;
                            currentTask.Status = TaskStatus.WaitingForPostDelay;

                            if (currentTask.Timeout.HasValue)
                                currentTask.PauseTimeout();

                            if (EnableLogging)
                                NoireLogger.LogDebug(this, $"Task entering post-completion delay: {currentTask}");
                        }
                        else
                        {
                            CompleteTask(currentTask);
                            earlyReturn = true;
                        }
                    }
                    else if (currentTask.HasTimedOut())
                    {
                        FailTask(currentTask, new TimeoutException("Task timed out."));
                        earlyReturn = true;
                    }
                    else if (currentTask.HasConditionStalled())
                    {
                        // Condition has stalled - attempt retry
                        if (!earlyReturn && TryRetryTask(currentTask))
                        {
                            // Retry was initiated, continue processing
                        }
                        else if (!currentTask.RetryConfiguration!.MaxAttempts.HasValue ||
                            currentTask.CurrentRetryAttempt < currentTask.RetryConfiguration.MaxAttempts.Value)
                        {
                            // Still have retries left or unlimited, reset stall tracking
                            currentTask.ResetStallTracking();
                        }
                        else
                        {
                            // Max retries exceeded
                            try
                            {
                                currentTask.RetryConfiguration?.OnMaxRetriesExceeded?.Invoke(currentTask);
                            }
                            catch (Exception ex)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogError(this, ex, "OnMaxRetriesExceeded callback threw an exception.");
                            }

                            FailTask(currentTask, new MaxRetryAttemptsExceededException(
                                $"Task exceeded maximum retry attempts ({currentTask.RetryConfiguration?.MaxAttempts.ToString() ?? "Unknown"})"));
                            earlyReturn = true;
                        }
                    }
                    else
                    {
                        // Update stall tracking
                        if (currentTask.RetryConfiguration != null && currentTask.CompletionCondition?.Type == CompletionConditionType.Predicate)
                        {
                            if (!currentTask.LastConditionCheckTicks.HasValue)
                                currentTask.ResetStallTracking();
                        }
                    }
                }
                else if (currentTask.Status == TaskStatus.WaitingForPostDelay)
                {
                    // Check if post-completion delay has elapsed
                    if (currentTask.HasPostDelayCompleted())
                    {
                        CompleteTask(currentTask);
                        earlyReturn = true;
                    }
                    else if (currentTask.HasTimedOut())
                    {
                        FailTask(currentTask, new TimeoutException("Task timed out during post-completion delay."));
                        earlyReturn = true;
                    }
                }

                if (!earlyReturn && taskToProcess == null && currentTask.IsBlocking &&
                    currentTask.Status != TaskStatus.Completed &&
                    currentTask.Status != TaskStatus.Cancelled &&
                    currentTask.Status != TaskStatus.Failed)
                {
                    shouldWaitForBlocking = true;
                }
            }

            // Iterate all other tasks waiting for completion (previously started tasks that are non-blocking)
            foreach (var wt in taskQueue.Where(t =>
                (t.Status == TaskStatus.WaitingForCompletion || t.Status == TaskStatus.WaitingForPostDelay) &&
                !ReferenceEquals(t, currentTask)))
            {
                if (wt.Status == TaskStatus.WaitingForPostDelay)
                {
                    if (wt.HasPostDelayCompleted())
                    {
                        waitingTasksToComplete.Add(wt);
                    }
                    else if (wt.HasTimedOut())
                    {
                        waitingTasksToFail.Add(wt);
                    }
                }
                else
                {
                    bool conditionMet = wt.CompletionCondition?.IsMet() == true;

                    if (conditionMet)
                    {
                        // Check if we need to start post-completion delay
                        if (wt.PostCompletionDelay.HasValue && !wt.PostDelayStartTicks.HasValue)
                        {
                            wt.PostDelayStartTicks = Environment.TickCount64;
                            wt.Status = TaskStatus.WaitingForPostDelay;

                            if (wt.Timeout.HasValue)
                                wt.PauseTimeout();

                            if (EnableLogging)
                                NoireLogger.LogDebug(this, $"Task entering post-completion delay: {wt}");
                        }
                        else
                        {
                            waitingTasksToComplete.Add(wt);
                        }
                    }
                    else if (wt.HasTimedOut())
                    {
                        waitingTasksToFail.Add(wt);
                    }
                    else if (wt.HasConditionStalled())
                    {
                        // Non-blocking task stalled - attempt retry
                        if (TryRetryTask(wt))
                        {
                            // Retry initiated
                        }
                        else if (!wt.RetryConfiguration!.MaxAttempts.HasValue ||
                                 wt.CurrentRetryAttempt < wt.RetryConfiguration.MaxAttempts.Value)
                        {
                            wt.ResetStallTracking();
                        }
                        else
                        {
                            try
                            {
                                wt.RetryConfiguration?.OnMaxRetriesExceeded?.Invoke(wt);
                            }
                            catch (Exception ex)
                            {
                                if (EnableLogging)
                                    NoireLogger.LogError(this, ex, "OnMaxRetriesExceeded callback threw an exception.");
                            }

                            waitingTasksToFail.Add(wt);
                        }
                    }
                    else
                    {
                        // Update stall tracking
                        if (wt.RetryConfiguration != null && wt.CompletionCondition?.Type == CompletionConditionType.Predicate)
                        {
                            if (!wt.LastConditionCheckTicks.HasValue)
                                wt.ResetStallTracking();
                        }
                    }
                }
            }

            if (!shouldWaitForBlocking && !earlyReturn && taskToProcess == null)
            {
                taskToProcess = taskQueue.FirstOrDefault(t => t.Status == TaskStatus.Queued);

                if (taskToProcess != null)
                {
                    currentTask = taskToProcess;
                    taskToProcess.Status = TaskStatus.Executing;
                    taskToProcess.StartedAtTicks = Environment.TickCount64;
                }
                else
                {
                    // If no tasks queued (tasks not yet started) but currentTask was just completed/failed and there are still waiting tasks (example, non blocking ones),
                    // set currentTask to the first non-blocking waiting task to maintain visibility
                    if (currentTask == null)
                    {
                        var firstWaitingTask = taskQueue.FirstOrDefault(t =>
                            t.Status == TaskStatus.WaitingForCompletion ||
                            t.Status == TaskStatus.WaitingForPostDelay);
                        if (firstWaitingTask != null)
                            currentTask = firstWaitingTask;
                        else
                            shouldCheckCompletion = true;
                    }
                    else
                        shouldCheckCompletion = true;
                }
            }
        }

        if (earlyReturn)
            return;

        // Complete / fail any non-current waiting tasks outside lock to avoid long holds
        foreach (var wt in waitingTasksToComplete)
            CompleteTask(wt);
        foreach (var wt in waitingTasksToFail)
        {
            Exception exception;
            if (wt.RetryConfiguration != null && wt.CurrentRetryAttempt >= (wt.RetryConfiguration.MaxAttempts ?? int.MaxValue))
                exception = new MaxRetryAttemptsExceededException($"Task exceeded maximum retry attempts ({wt.RetryConfiguration.MaxAttempts})");
            else
                exception = new TimeoutException("Task timed out.");

            FailTask(wt, exception);
        }

        if (taskToProcess != null)
        {
            ExecuteTask(taskToProcess);
        }
        else if (shouldCheckCompletion)
        {
            CheckQueueCompletion();
        }
    }

    /// <summary>
    /// Attempts to retry a stalled task by re-executing its action or retry override.
    /// </summary>
    /// <param name="task">The task to retry.</param>
    /// <returns>True if retry was initiated, false if max retries exceeded.</returns>
    private bool TryRetryTask(QueuedTask task)
    {
        if (task.RetryConfiguration == null)
            return false;

        // Check if we've exceeded max attempts
        if (task.RetryConfiguration.MaxAttempts.HasValue &&
            task.CurrentRetryAttempt >= task.RetryConfiguration.MaxAttempts.Value)
            return false;

        task.CurrentRetryAttempt++;

        if (EnableLogging)
            NoireLogger.LogInfo(this, $"Retrying task {task} (Attempt {task.CurrentRetryAttempt}/{task.RetryConfiguration.MaxAttempts?.ToString() ?? "∞"})");

        try
        {
            task.RetryConfiguration.OnBeforeRetry?.Invoke(task, task.CurrentRetryAttempt);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnBeforeRetry callback threw an exception.");
        }

        task.ResetStallTracking();

        if (task.RetryConfiguration.RetryDelay.HasValue)
        {
            // Set task back to queued and insert delay logic
            task.Status = TaskStatus.Queued;

            // We'll handle the delay by setting a special flag and checking it in ExecuteTask
            task.Metadata = new RetryDelayMetadata
            {
                DelayUntilTicks = Environment.TickCount64 + (long)task.RetryConfiguration.RetryDelay.Value.TotalMilliseconds,
                OriginalMetadata = task.Metadata is RetryDelayMetadata rdm ? rdm.OriginalMetadata : task.Metadata
            };

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Task retry scheduled after delay: {task}");

            return true;
        }

        return ExecuteRetryAction(task);
    }

    /// <summary>
    /// Executes the retry action for a task.
    /// </summary>
    /// <param name="task">The task to execute retry action for.</param>
    /// <returns>True if retry action executed successfully, false if it failed.</returns>
    private bool ExecuteRetryAction(QueuedTask task)
    {
        try
        {
            PublishEvent(new TaskRetryingEvent(task, task.CurrentRetryAttempt));

            // Execute override action or fall back to original action
            if (task.RetryConfiguration?.OverrideRetryAction != null)
            {
                task.RetryConfiguration.OverrideRetryAction(task, task.CurrentRetryAttempt);
            }
            else if (task.ExecuteAction != null)
            {
                task.ExecuteAction();
            }

            if (task.CompletionCondition?.Type == CompletionConditionType.EventBusEvent)
            {
                task.CompletionCondition.EventConditionMet = false;
            }
            else if (task.PostCompletionDelay.HasValue)
            {
                task.PostDelayStartTicks = Environment.TickCount64;
                task.AccumulatedPostDelayMillis = 0;
                task.PostDelayPausedAtTicks = null;
            }

            task.Status = TaskStatus.WaitingForCompletion;
            task.ResetStallTracking();

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Task retry executed: {task}");

            return true;
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, $"Retry action failed for task {task}");

            FailTask(task, ex);
            return false;
        }
    }

    /// <summary>
    /// Executes a task.
    /// </summary>
    private void ExecuteTask(QueuedTask task)
    {
        if (task.Status != TaskStatus.Executing)
        {
            if (ReferenceEquals(currentTask, task))
                lock (queueLock)
                    if (ReferenceEquals(currentTask, task))
                        currentTask = null;

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Skipping execution for task no longer executing: {task} (Status: {task.Status})");

            return;
        }

        // Check if this is a delayed retry
        if (task.Metadata is RetryDelayMetadata delayMetadata)
        {
            if (Environment.TickCount64 < delayMetadata.DelayUntilTicks)
            {
                // Delay not elapsed yet, set back to queued
                task.Status = TaskStatus.Queued;
                return;
            }

            // Delay elapsed, restore original metadata and execute retry action
            task.Metadata = delayMetadata.OriginalMetadata;

            if (!ExecuteRetryAction(task))
            {
                // Retry action failed, task will be failed by ExecuteRetryAction
                return;
            }

            // Retry action succeeded, task is now waiting for completion
            return;
        }

        try
        {
            PublishEvent(new TaskStartedEvent(task));

            if (EnableLogging)
                NoireLogger.LogDebug(this, $"Executing task: {task}");

            task.ExecuteAction?.Invoke();

            lock (queueLock)
            {
                if (task.CompletionCondition?.Type == CompletionConditionType.Immediate)
                {
                    // Check if we need to start post-completion delay
                    if (task.PostCompletionDelay.HasValue)
                    {
                        task.PostDelayStartTicks = Environment.TickCount64;
                        task.Status = TaskStatus.WaitingForPostDelay;

                        if (task.Timeout.HasValue)
                            task.PauseTimeout();

                        if (EnableLogging)
                            NoireLogger.LogDebug(this, $"Task entering post-completion delay: {task}");
                    }
                    else
                    {
                        CompleteTask(task);
                    }
                }
                else
                {
                    // Only transition to waiting if still executing (could have been externally cancelled during ExecuteAction)
                    if (task.Status == TaskStatus.Executing)
                    {
                        task.Status = TaskStatus.WaitingForCompletion;

                        // Initialize stall tracking if retry is configured
                        if (task.RetryConfiguration != null)
                            task.ResetStallTracking();

                        if (EnableLogging)
                            NoireLogger.LogDebug(this, $"Task waiting for completion: {task}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (queueLock)
                FailTask(task, ex);
        }
    }

    /// <summary>
    /// Checks if the queue has completed all tasks.
    /// </summary>
    private void CheckQueueCompletion()
    {
        bool queueCompleted = false;
        int completedCount = 0;

        lock (queueLock)
        {
            var hasUnfinishedTasks = taskQueue.Any(t =>
                t.Status == TaskStatus.Queued ||
                t.Status == TaskStatus.Executing ||
                t.Status == TaskStatus.WaitingForCompletion ||
                t.Status == TaskStatus.WaitingForPostDelay);

            if (!hasUnfinishedTasks)
            {
                queueCompleted = true;
                completedCount = taskQueue.Count(t => t.Status == TaskStatus.Completed);
            }
        }

        if (queueCompleted)
        {
            PublishEvent(new QueueCompletedEvent(completedCount));

            if (ShouldStopQueueOnComplete)
                StopQueue();

            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Queue completed. Tasks: {completedCount}");
        }
    }

    /// <summary>
    /// Completes a task successfully.
    /// </summary>
    private void CompleteTask(QueuedTask task)
    {
        UnsubscribeTask(task);

        task.Status = TaskStatus.Completed;
        task.FinishedAtTicks = Environment.TickCount64;
        tasksCompleted++;

        try
        {
            task.OnCompleted?.Invoke(task);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnCompleted callback threw an exception.");
        }

        PublishEvent(new TaskCompletedEvent(task));

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

        if (EnableLogging)
            NoireLogger.LogDebug(this, $"Task completed: {task} (Duration: {task.GetExecutionTime()})");
    }

    /// <summary>
    /// Marks a task as failed.
    /// </summary>
    private void FailTask(QueuedTask task, Exception exception)
    {
        UnsubscribeTask(task);

        task.Status = TaskStatus.Failed;
        task.FinishedAtTicks = Environment.TickCount64;
        task.FailureException = exception;
        tasksFailed++;

        try
        {
            task.OnFailed?.Invoke(task, exception);
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "OnFailed callback threw an exception.");
        }

        PublishEvent(new TaskFailedEvent(task, exception));

        if (ReferenceEquals(currentTask, task))
            currentTask = null;

        if (task.StopQueueOnFail)
        {
            if (EnableLogging)
                NoireLogger.LogInfo(this, $"Stopping queue due to task failure: {task}");

            StopQueue();
        }

        if (EnableLogging)
            NoireLogger.LogError(this, exception, $"Task failed: {task}");
    }

    #endregion

    #region Statistics and Info

    /// <summary>
    /// Determines whether the queue is currently in the running state.
    /// </summary>
    /// <returns>true if the queue is running; otherwise, false.</returns>
    public bool IsQueueRunning()
    {
        lock (queueLock)
        {
            return QueueState switch
            {
                QueueState.Running => true,
                _ => false,
            };
        }
    }

    /// <summary>
    /// Determines whether the queue is currently processing items, including both running and paused states.
    /// </summary>
    /// <remarks>This method considers the queue to be processing if it is either actively running or
    /// temporarily paused. Use this method to check if the queue is engaged in processing, regardless of whether it is
    /// momentarily paused.</remarks>
    /// <returns>true if the queue is in a running or paused state; otherwise, false.</returns>
    public bool IsQueueProcessing()
    {
        lock (queueLock)
        {
            return QueueState switch
            {
                QueueState.Running => true,
                QueueState.Paused => true,
                _ => false,
            };
        }
    }

    /// <summary>
    /// Gets statistics about the task queue.
    /// </summary>
    /// <param name="getCopyOfCurrentTask">If true, returns a copy of the task being processed at this exact moment instead of the reference.</param>
    public TaskQueueStatistics GetStatistics(bool getCopyOfCurrentTask = false)
    {
        lock (queueLock)
        {
            long activeMillis = accumulatedProcessingMillis;
            if (QueueState == QueueState.Running && processingStartTimeTicks > 0)
                activeMillis += Environment.TickCount64 - processingStartTimeTicks;

            var totalTime = TimeSpan.FromMilliseconds(activeMillis);


            QueuedTask? task = null;
            if (currentTask != null)
                task = getCopyOfCurrentTask ? currentTask.Clone() : currentTask;

            return new TaskQueueStatistics(
                TotalTasksQueued: totalTasksQueued,
                TasksCompleted: tasksCompleted,
                TasksCancelled: tasksCancelled,
                TasksFailed: tasksFailed,
                CurrentQueueSize: taskQueue.Count,
                QueueState: QueueState,
                CurrentTask: task,
                TotalProcessingTime: totalTime);
        }
    }

    /// <summary>
    /// Gets the current queue size.
    /// </summary>
    public int GetQueueSize()
    {
        lock (queueLock)
        {
            return taskQueue.Count;
        }
    }

    /// <summary>
    /// Gets the number of pending (queued) tasks.
    /// </summary>
    public int GetPendingTaskCount()
    {
        lock (queueLock)
        {
            return taskQueue.Count(t => t.Status == TaskStatus.Queued);
        }
    }

    /// <summary>
    /// Gets the number of remaining tasks (queued, executing, waiting).
    /// </summary>
    /// <returns></returns>
    public int GetRemainingTaskCount()
    {
        lock (queueLock)
        {
            return taskQueue.Count(t =>
            t.Status == TaskStatus.Queued ||
            t.Status == TaskStatus.Executing ||
            t.Status == TaskStatus.WaitingForCompletion);
        }
    }

    /// <summary>
    /// Gets the currently executing task.
    /// </summary>
    public QueuedTask? GetCurrentTask()
    {
        lock (queueLock)
        {
            return currentTask;
        }
    }

    /// <summary>
    /// Gets all tasks in the queue.
    /// </summary>
    public IReadOnlyList<QueuedTask> GetAllTasks()
    {
        lock (queueLock)
        {
            return taskQueue.ToList();
        }
    }

    /// <summary>
    /// Gets the progress of the queue (0.0 to 1.0), useful for percentage display.
    /// </summary>
    public double GetQueueProgress()
    {
        lock (queueLock)
        {
            if (taskQueue.Count == 0)
                return 1.0;

            var finishedTasks = taskQueue.Count(t =>
            t.Status == TaskStatus.Completed ||
            t.Status == TaskStatus.Cancelled ||
            t.Status == TaskStatus.Failed);

            return (double)finishedTasks / taskQueue.Count;
        }
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Sets whether to automatically process the queue when tasks are added.
    /// </summary>
    public NoireTaskQueue SetAutoProcessing(bool autoProcess)
    {
        ShouldProcessQueueAutomatically = autoProcess;
        return this;
    }

    /// <summary>
    /// Sets whether to automatically stop the queue when all tasks are completed.
    /// </summary>
    public NoireTaskQueue SetAutoStopQueueOnComplete(bool autoClear)
    {
        ShouldStopQueueOnComplete = autoClear;
        return this;
    }

    #endregion

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
        NoireService.Framework.Update -= OnFrameworkUpdate;
        StopQueue();
        UnsubscribeFromAllEvents();

        lock (queueLock)
        {
            taskQueue.Clear();
            currentTask = null;
        }

        if (EnableLogging)
        {
            var stats = GetStatistics();
            NoireLogger.LogInfo(this, $"Task Queue disposed. Total: {stats.TotalTasksQueued}, Completed: {stats.TasksCompleted}, Failed: {stats.TasksFailed}");
        }
    }
}
