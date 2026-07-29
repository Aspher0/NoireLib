using Dalamud.Plugin.Services;
using NoireLib.Core.Modules;
using NoireLib.EventBus;
using System;
using System.Collections.Generic;

namespace NoireLib.TaskQueue;

/// <summary>
/// A module providing task queuing and processing, blocking or non-blocking, based on conditions and callbacks.
/// See <see cref="QueuedTask"/> and <see cref="TaskCompletionCondition"/> for task definitions, <see cref="TaskBuilder"/> for building and enqueuing tasks, and <see cref="TaskBatch"/>/<see cref="BatchBuilder"/> for batch operations.
/// </summary>
public partial class NoireTaskQueue : NoireModuleBase<NoireTaskQueue>
{
    private readonly List<QueueItemWrapper> unifiedQueue = new();
    private readonly object queueLock = new();

    private QueuedTask? currentTask;
    private TaskBatch? currentBatch;
    private QueueItemWrapper? currentItem;

    private int totalTasksQueued;
    private int tasksCompleted;
    private int tasksCancelled;
    private int tasksFailed;
    private int totalBatchesQueued;
    private int batchesCompleted;
    private int batchesCancelled;
    private int batchesFailed;
    private long processingStartTimeTicks;
    private long accumulatedProcessingMillis;

    /// <summary>
    /// The associated EventBus for publishing queue events and subscribing to event-based completion conditions; if null, neither functions.
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
    /// If true, the queue will automatically start processing when a task or batch is added.
    /// </summary>
    public bool ShouldProcessQueueAutomatically
    {
        get => shouldProcessQueueAutomatically;
        set => shouldProcessQueueAutomatically = value;
    }

    private bool shouldStopQueueOnComplete = true;
    /// <summary>
    /// If true, the queue will automatically stop when all items are completed.
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
    /// <param name="shouldStopQueueOnComplete">Whether to stop the queue automatically when completed.</param>
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
    /// Constructor for use with <see cref="NoireLibMain.AddModule{T}(string?)"/> with <paramref name="moduleId"/>, for internal module management only.
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
        // Requires NoireLib to be initialized; if activated before that, the module stays inert and must be activated again afterwards to process.
        if (!NoireService.IsInitialized())
        {
            NoireLogger.LogWarning(this, "Task Queue activated before NoireLib was initialized. The queue will not be processed until the module is activated again once NoireLib is initialized.");
            return;
        }

        NoireService.Framework.Update += OnFrameworkUpdate;

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Task Queue activated.");
    }

    /// <summary>
    /// Called when the module is deactivated, specifically going from <see cref="NoireModuleBase{TModule}.IsActive"/> true to false.
    /// </summary>
    protected override void OnDeactivated()
    {
        // Detaching is the only part that needs the service; an activation before NoireLib was initialized never attached the handler.
        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= OnFrameworkUpdate;

        StopQueue();

        if (EnableLogging)
            NoireLogger.LogInfo(this, "Task Queue deactivated.");
    }

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

    /// <summary>
    /// Used to process the queue every frame.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsActive)
            return;

        TickOnce();
    }

    /// <summary>
    /// Runs a single queue processing pass, the same one a framework frame runs.
    /// </summary>
    /// <remarks>
    /// Otherwise reachable only from the framework update, which needs a running game; this lets the queue be
    /// stepped deterministically without one. It deliberately does not test
    /// <see cref="NoireModuleBase{TModule}.IsActive"/>, since that flag is about whether the module is wired to
    /// the frame loop, not about processing; the queue state gate does belong to processing and stays here.
    /// </remarks>
    internal void TickOnce()
    {
        if (QueueState != QueueState.Running)
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

        // In its own try, separate from the one above, so a pass that threw still reconciles what it changed,
        // and a callback throwing here cannot mask a processing error.
        try
        {
            ReconcileConsumerWrittenStatuses();
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, ex, "Error reconciling directly written task or batch statuses.");
        }
    }

    /// <summary>
    /// Internal dispose method called when the module is disposed.
    /// </summary>
    protected override void DisposeInternal()
    {
        if (NoireService.IsInitialized())
            NoireService.Framework.Update -= OnFrameworkUpdate;

        StopQueue();
        UnsubscribeFromAllEvents();

        lock (queueLock)
        {
            unifiedQueue.Clear();
            currentTask = null;
            currentBatch = null;
        }

        if (EnableLogging)
        {
            var stats = GetStatistics();
            NoireLogger.LogInfo(this, $"Task Queue disposed. Total: {stats.TotalTasks}, Completed: {stats.CompletedTasks}, Failed: {stats.FailedTasks}, Batches: {stats.TotalBatchesQueued}");
        }
    }
}
