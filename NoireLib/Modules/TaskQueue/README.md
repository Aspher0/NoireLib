# Module Documentation : NoireTaskQueue

You are reading the documentation for the `NoireTaskQueue` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Creating Tasks](#creating-tasks)
- [Batch Processing](#batch-processing)
- [Queue Control](#queue-control)
- [Task Management](#task-management)
- [Context Boundaries](#context-boundaries)
- [Retry Configuration](#retry-configuration)
- [EventBus Integration](#eventbus-integration)
- [Task Callbacks](#task-callbacks)
- [Timeouts](#timeouts)
- [Task Metadata](#task-metadata)
- [Queue Statistics](#queue-statistics)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireTaskQueue` is a module that manages task queuing and processing with support for:
- **Blocking and non-blocking tasks** for flexible execution flow
- **Batch processing** to group related tasks with shared failure/cancellation handling
- **Multiple completion conditions** (predicate, event-based, immediate)
- **Automatic retry logic** with configurable stall detection
- **Timeout support** for cancelling tasks that take too long to complete
- **Post-completion delays** on both tasks and batches, with dynamic delay providers
- **EventBus integration** for event-driven task completion, with configurable event capture depth and context boundaries
- **Context boundary checking** (`CrossContext`, `SameContext`, `SameContextStrict`) for scoping operations like skip, jump, and retrieval
- **Task metadata** for passing data between tasks, including unsafe pointer support
- **Comprehensive callbacks** for task and batch lifecycle events
- **Queue state management** (start, pause, resume, stop, skip, jump/goto)
- **Queue-bound builders** (`TaskBuilder<TModule>`, `BatchBuilder<TModule>`) for streamlined enqueuing

This module is ideal for scenarios where you need to:
- Execute tasks sequentially with specific completion conditions
- Wait for game state changes before proceeding
- Group related tasks into batches with shared error handling
- Handle asynchronous operations with retry logic

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create and Enqueue Your First Task

Using the `TaskBuilder` API:

```csharp
// Simple action with immediate completion
TaskBuilder.AddAction(
    action: () => NoireLogger.LogInfo("Hello from task queue!"),
    queue: taskQueue,
    customId: "greet-task"
);

// Task with a delay
TaskBuilder.AddDelaySeconds(
    seconds: 2.0,
    queue: taskQueue,
    customId: "wait-2s"
);

// Simple task with a condition
TaskBuilder.AddCondition(
    condition: () => IsPlayerInZone(123),
    queue: taskQueue,
    customId: "wait-for-zone"
);
```

Your tasks are now queued and will be processed when the queue starts, either automatically if `ShouldProcessQueueAutomatically` is `true` or manually by calling `taskQueue.StartQueue()`.

---

## Configuration

### Module Parameters

Configure the queue behavior using constructor parameters:

```csharp
var taskQueue = new NoireTaskQueue(
    moduleId: "MyQueue",                    // Optional identifier for multiple queues
    active: true,                           // Enable/disable the module
    enableLogging: true,                    // Log queue events
    shouldProcessQueueAutomatically: false, // Auto-start when tasks are added
    shouldStopQueueOnComplete: true,        // Auto-stop when all tasks complete
    eventBus: eventBus                      // Optional EventBus for events
);
```

### Runtime Configuration

Modify queue behavior at runtime:

```csharp
var queue = NoireLibMain.GetModule<NoireTaskQueue>();

// Configure automatic processing
queue?.SetAutoProcessing(true);

// Configure auto-stop behavior
queue?.SetAutoStopQueueOnComplete(true);

// Set EventBus
queue.EventBus = myEventBus;

// Chain configuration
queue?.SetAutoProcessing(true)
      .SetAutoStopQueueOnComplete(false);
```

---

## Creating Tasks

### Using TaskBuilder

`TaskBuilder` is a fluent API for creating and configuring tasks.

#### Basic Usage

```csharp
// Create a task with the Build() method
var task = TaskBuilder.Create("my-task")
    .WithAction(() => DoSomething())
    .WithCondition(() => localPlayer != null)
    .Build();

// Or create and enqueue in one step
TaskBuilder.Create("my-task")
    .WithAction(() => DoSomething())
    .WithCondition(() => localPlayer != null)
    .EnqueueTo(taskQueue);
```

#### Quick Helpers

`TaskBuilder` provides convenience methods for common scenarios:

```csharp
// Add a simple action (immediate completion)
TaskBuilder.AddAction(
    action: () => NoireLogger.LogInfo("Action executed!"),
    queue: taskQueue
);

// Add a delay
TaskBuilder.AddDelaySeconds(3.0, taskQueue);
TaskBuilder.AddDelayMilliseconds(500, taskQueue);
TaskBuilder.AddDelay(TimeSpan.FromMinutes(1), taskQueue);

// Add a condition wait (no action, just waits)
TaskBuilder.AddCondition(
    condition: () => IsReady(),
    queue: taskQueue
);

// Add an event wait
TaskBuilder.AddEventWait<PlayerMovedEvent>(
    queue: taskQueue,
    filter: evt => evt.MapId == 123
);
```

#### Actions with Task Access

Actions can receive the current `QueuedTask` instance for accessing metadata, status, or other task properties:

```csharp
TaskBuilder.Create("my-task")
    .WithAction(task => {
        NoireLogger.LogInfo($"Running task: {task.CustomId}");
        task.Metadata = ComputeResult();
    })
    .EnqueueTo(queue);
```

### Queue-Bound TaskBuilder

If you want to avoid passing the queue reference to every `EnqueueTo` call, use `TaskBuilder<TModule>` or the `CreateTask` helper directly on the queue:

```csharp
// Using the queue's CreateTask helper
queue.CreateTask("my-task")
    .WithAction(() => DoSomething())
    .WithCondition(() => IsReady())
    .Enqueue(); // No need to pass the queue

// Or create a queue-bound builder explicitly
var builder = TaskBuilder<NoireTaskQueue>.Create(queue, "my-task");
builder.WithAction(() => DoSomething())
       .Enqueue();
```

### Task Completion Conditions

Tasks can complete based on different conditions:

#### 1. Immediate Completion

Task completes as soon as the action finishes. This is the default when no condition is set:

```csharp
TaskBuilder.Create()
    .WithAction(() => DoWork())
    .WithImmediateCompletion() // Can omit, this is the default
    .EnqueueTo(queue);
```

#### 2. Predicate-Based Condition

Task completes when a condition function returns true. The condition is evaluated every frame:

```csharp
TaskBuilder.Create()
    .WithAction(() => StartMoving())
    .WithCondition(() => HasReachedDestination())
    .EnqueueTo(queue);

// With access to the task instance
TaskBuilder.Create()
    .WithCondition(task => {
        var metadata = task.Metadata as MyMetadata;
        return metadata?.IsComplete ?? false;
    })
    .EnqueueTo(queue);
```

#### 3. Event-Based Condition

Task completes when an EventBus event is published. Requires `EventBus` to be assigned on the queue:

```csharp
// Wait for any event of this type
TaskBuilder.Create()
    .WithAction(() => InitiateTeleport())
    .WaitForEvent<TeleportCompleteEvent>()
    .EnqueueTo(queue);

// Wait for a filtered event
TaskBuilder.Create()
    .WithAction(() => InitiateTeleport())
    .WaitForEvent<TeleportCompleteEvent>(
        filter: evt => evt.Destination == "Limsa Lominsa"
    )
    .EnqueueTo(queue);
```

See [Event Capture While Queued](#event-capture-while-queued) for advanced event capture options.

### Post-Completion Delays

You can add a delay that runs after a task's completion condition is met. This can be a standalone delay task or a post-completion pause before the queue proceeds.

```csharp
// Standalone delay task (5 seconds)
TaskBuilder.Create()
    .WithDelay(TimeSpan.FromSeconds(5))
    .EnqueueTo(queue);

// Task that waits for a condition, then delays 1 second after completion
TaskBuilder.Create()
    .WithAction(() => StartMoving())
    .WithCondition(() => HasReachedDestination())
    .WithDelay(TimeSpan.FromSeconds(1))
    .EnqueueTo(queue);

// Dynamic delay using a predicate (evaluated when the delay starts, not at creation time)
TaskBuilder.Create()
    .WithDelay(() => NoireService.ClientState.TerritoryType == 123
        ? TimeSpan.FromSeconds(1)
        : null)
    .EnqueueTo(queue);

// Dynamic delay with access to the task
TaskBuilder.Create()
    .WithAction(task => {
        if (NoireService.ClientState.TerritoryType == 123)
        {
            task.Metadata = null;
            MoveToDestination();
            return;
        }
        task.Metadata = TimeSpan.FromMilliseconds(500);
        TeleportToTerritory();
    })
    .WithDelay(task => task.Metadata as TimeSpan?)
    .EnqueueTo(queue);
```

#### Applying Delays on Failure or Cancellation

By default, post-completion delays only run on successful completion. You can opt in to applying them on failure or cancellation:

```csharp
TaskBuilder.Create()
    .WithAction(() => DoWork())
    .WithCondition(() => IsComplete())
    .WithDelay(TimeSpan.FromSeconds(2), applyOnFailure: true, applyOnCancellation: true)
    .EnqueueTo(queue);
```

### Blocking vs Non-Blocking Tasks

Control whether subsequent tasks wait for completion.
By default, tasks are blocking:

```csharp
// Blocking task (default) - queue waits for completion
TaskBuilder.Create()
    .WithAction(() => CriticalOperation())
    .WithCondition(() => IsOperationComplete())
    .EnqueueTo(queue);

// Non-blocking task - queue starts this and immediately moves to the next task
TaskBuilder.Create()
    .AsNonBlocking()
    .WithAction(() => BackgroundTask())
    .WithCondition(() => IsBackgroundTaskComplete())
    .EnqueueTo(queue);
```

**Use cases:**
- **Blocking**: Sequential operations where order matters (e.g., teleport, wait for load, interact)
- **Non-blocking**: Parallel operations that can run independently (e.g., logging, monitoring)

---

## Batch Processing

Batches let you group related tasks into a single unit with shared lifecycle callbacks, failure/cancellation handling, and optional post-completion delays. A batch is treated as a single item in the queue.

### Using BatchBuilder

```csharp
// Create and enqueue a batch
BatchBuilder.Create("my-batch")
    .AddAction(() => Step1())
    .AddCondition(() => IsStep1Complete())
    .AddAction(() => Step2())
    .AddDelaySeconds(1.0)
    .OnCompleted(batch => NoireLogger.LogInfo($"Batch done! {batch.CompletedTaskCount}/{batch.TaskCount}"))
    .EnqueueTo(queue);
```

#### Adding Tasks with Full Configuration

Use the `AddTask` overload that takes a `TaskBuilder` configurator for full control over each task inside the batch:

```csharp
BatchBuilder.Create("complex-batch")
    .AddTask(tb => tb
        .WithCustomId("step-1")
        .WithAction(() => DoStep1())
        .WithCondition(() => IsStep1Complete())
        .WithTimeout(TimeSpan.FromSeconds(10))
        .FailParentBatchOnFail()
    )
    .AddTask(tb => tb
        .WithCustomId("step-2")
        .WithAction(() => DoStep2())
        .WithRetries(3, TimeSpan.FromSeconds(5))
    )
    .OnFailed((batch, ex) => NoireLogger.LogError($"Batch failed: {ex?.Message}"))
    .EnqueueTo(queue);
```

#### Adding Pre-Built Tasks

You can also add pre-built `QueuedTask` objects:

```csharp
var task1 = TaskBuilder.Create("t1").WithAction(() => DoWork()).Build();
var task2 = TaskBuilder.Create("t2").WithAction(() => MoreWork()).Build();

BatchBuilder.Create("batch")
    .AddTask(task1)
    .AddTasks(task2)
    .EnqueueTo(queue);
```

### Queue-Bound BatchBuilder

Similar to `TaskBuilder<TModule>`, you can use `BatchBuilder<TModule>` or the `CreateBatch` helper on the queue:

```csharp
queue.CreateBatch("my-batch")
    .AddAction(() => Step1())
    .AddCondition(() => IsStep1Complete())
    .Enqueue(); // No need to pass the queue
```

### Inline Task Creation with BatchTaskConfigurator

The `AddTasks(Action<BatchTaskConfigurator>)` overload provides a `BatchTaskConfigurator` for creating multiple tasks inline with a fluent API:

```csharp
BatchBuilder.Create("batch-with-configurator")
    .AddTasks(cfg => {
        cfg.Create("task-a")
            .WithAction(() => DoA())
            .WithCondition(() => IsAComplete())
            .Enqueue();

        cfg.Create("task-b")
            .WithAction(() => DoB())
            .Enqueue();
    })
    .EnqueueTo(queue);
```

The `BatchTaskConfigurator` also provides helper methods like `AddTask`, `AddTasks`, `FailBatch()`, and `CancelBatch()`.

### Batch Failure and Cancellation Modes

Control how the batch reacts when individual tasks fail or are cancelled:

#### Failure Modes (`BatchTaskFailureMode`)

- `ContinueRemaining` (default): Continue processing remaining tasks in the batch even if one fails.
- `FailBatch`: Fail the entire batch immediately and stop processing remaining tasks.
- `FailBatchAndStopQueue`: Fail the batch and stop the entire queue.

```csharp
BatchBuilder.Create("strict-batch")
    .StopBatchOnTaskFailure()   // FailBatch mode
    .AddAction(() => CriticalStep())
    .EnqueueTo(queue);

BatchBuilder.Create("lenient-batch")
    .ContinueOnTaskFailure()    // ContinueRemaining mode
    .AddAction(() => OptionalStep())
    .EnqueueTo(queue);

BatchBuilder.Create("critical-batch")
    .StopQueueOnTaskFailure()   // FailBatchAndStopQueue mode
    .AddAction(() => MustSucceed())
    .EnqueueTo(queue);
```

#### Cancellation Modes (`BatchTaskCancellationMode`)

- `ContinueRemaining` (default): Continue processing remaining tasks if one is cancelled.
- `CancelBatch`: Cancel the entire batch immediately.
- `CancelBatchAndQueue`: Cancel the batch and stop the entire queue.

```csharp
BatchBuilder.Create("cancel-safe-batch")
    .CancelBatchOnTaskCancellation()
    .AddAction(() => DoWork())
    .EnqueueTo(queue);

BatchBuilder.Create("flexible-batch")
    .ContinueOnTaskCancellation()
    .AddAction(() => DoWork())
    .EnqueueTo(queue);
```

You can also use the generic setters:

```csharp
BatchBuilder.Create()
    .WithTaskFailureMode(BatchTaskFailureMode.FailBatch)
    .WithTaskCancellationMode(BatchTaskCancellationMode.CancelBatch)
    .EnqueueTo(queue);
```

### Batch Delays

Just like tasks, batches support post-completion delays:

```csharp
// Fixed delay after batch completes
BatchBuilder.Create("delayed-batch")
    .AddAction(() => DoWork())
    .WithDelay(TimeSpan.FromSeconds(2))
    .EnqueueTo(queue);

// Dynamic delay based on batch state
BatchBuilder.Create("dynamic-delay-batch")
    .AddAction(() => DoWork())
    .WithDelay(batch => batch.FailedTaskCount > 0
        ? TimeSpan.FromSeconds(5)
        : TimeSpan.FromSeconds(1))
    .EnqueueTo(queue);

// Apply delay even on failure or cancellation
BatchBuilder.Create()
    .AddAction(() => DoWork())
    .WithDelay(TimeSpan.FromSeconds(2), applyOnFailure: true, applyOnCancellation: true)
    .EnqueueTo(queue);
```

### Batch Callbacks

```csharp
BatchBuilder.Create("my-batch")
    .OnStarted(batch => NoireLogger.LogInfo($"Batch started: {batch.CustomId}"))
    .OnCompleted(batch => NoireLogger.LogInfo($"Batch completed: {batch.CompletedTaskCount}/{batch.TaskCount}"))
    .OnCancelled(batch => NoireLogger.LogWarning($"Batch cancelled"))
    .OnFailed((batch, ex) => NoireLogger.LogError($"Batch failed: {ex?.Message}"))
    // Or handle both failure and cancellation
    .OnFailedOrCancelled((batch, ex) => {
        if (ex != null)
            NoireLogger.LogError($"Batch failed: {ex.Message}");
        else
            NoireLogger.LogWarning("Batch cancelled");
    })
    .EnqueueTo(queue);
```

#### Stopping the Queue from Batch Events

```csharp
BatchBuilder.Create()
    .StopQueueOnFail()
    .StopQueueOnCancel()
    // Or both at once
    .StopQueueOnFailOrCancel()
    .EnqueueTo(queue);

// With callbacks
BatchBuilder.Create()
    .OnCancelled(() => NoireLogger.LogWarning("Cancelled!"), stopQueue: true)
    .OnFailed(() => NoireLogger.LogError("Failed!"), stopQueue: true)
    .EnqueueTo(queue);
```

### Per-Task Batch Propagation

Individual tasks inside a batch can be configured to propagate failure or cancellation up to the parent batch, independent of the batch's global failure/cancellation mode:

```csharp
TaskBuilder.Create("critical-task")
    .WithAction(() => CriticalWork())
    .FailParentBatchOnFail()          // Fail the batch if this specific task fails
    .CancelParentBatchOnCancel()      // Cancel the batch if this specific task is cancelled
    .FailParentBatchOnMaxRetries()    // Fail the batch if this task exhausts retries
    .Build();

// Convenience methods for both directions
TaskBuilder.Create()
    .FailParentBatchOnFailOrCancel()     // Fail batch on either failure or cancellation
    .CancelParentBatchOnFailOrCancel()   // Cancel batch on either failure or cancellation
    .Build();
```

### Batch Properties

Each `TaskBatch` exposes useful properties and methods:

```csharp
batch.TaskCount;            // Total number of tasks
batch.CompletedTaskCount;   // Number of completed tasks
batch.FailedTaskCount;      // Number of failed tasks
batch.CancelledTaskCount;   // Number of cancelled tasks
batch.GetProgress();        // 0.0 to 1.0
batch.GetExecutionTime();   // TimeSpan of execution
batch.GetTotalTime();       // TimeSpan including queue wait
batch.Metadata;             // Custom metadata
```

---

## Queue Control

### Starting the Queue

```csharp
// Manual start
taskQueue.StartQueue();

// Or enable auto-start (queue starts automatically when tasks/batches are added)
taskQueue.SetAutoProcessing(true);
```

### Pausing and Resuming

```csharp
// Pause processing (preserves task state)
taskQueue.PauseQueue();

// Resume processing
taskQueue.ResumeQueue();
```

When paused:
- Post-completion delay timers pause
- Task timeout timers pause
- Retry stall tracking pauses
- Batch post-delay timers pause
- No tasks are processed

### Stopping the Queue

```csharp
// Stop and clear all tasks and batches
taskQueue.StopQueue();
```

### Queue States

The queue can be in one of these states (`QueueState` enum):

- `Idle` - Queue created but never started
- `Running` - Actively processing tasks
- `Paused` - Temporarily suspended
- `Stopped` - Stopped and cleared

```csharp
if (taskQueue.QueueState == QueueState.Running)
{
    // Queue is processing
}
```

### Checking Queue Status

```csharp
// True only when Running
bool isRunning = taskQueue.IsQueueRunning();

// True when Running or Paused (queue is engaged in processing)
bool isProcessing = taskQueue.IsQueueProcessing();
```

---

## Task Management

### Retrieving Tasks

All retrieval methods support an optional `ContextDefinition` parameter. See [Context Boundaries](#context-boundaries) for details.

```csharp
// Get current task being processed
var current = taskQueue.GetCurrentTask();

// Get current queue item (task or batch wrapper)
var currentItem = taskQueue.GetCurrentQueueItem();

// Get all tasks (includes tasks inside batches)
var allTasks = taskQueue.GetAllTasks();
var allTasksSameContext = taskQueue.GetAllTasks(ContextDefinition.SameContext);

// Get by system ID
var task = taskQueue.GetTaskBySystemId(guid);

// Get by custom ID
var task = taskQueue.GetTaskByCustomId("my-task");

// Get all tasks with a custom ID
var tasks = taskQueue.GetTasksByCustomId("my-task");

// Get tasks matching a predicate
var tasks = taskQueue.GetTasksByPredicate(t => t.IsBlocking);
```

### Retrieving Batches

```csharp
// Get current batch
var currentBatch = taskQueue.GetCurrentBatch();

// Get by system ID
var batch = taskQueue.GetBatchBySystemId(guid);

// Get by custom ID
var batch = taskQueue.GetBatchByCustomId("my-batch");

// Get all batches
var allBatches = taskQueue.GetAllBatches();
```

### Retrieving Tasks from Batches

```csharp
// From the queue level
var task = taskQueue.GetTaskFromBatch("my-batch", "my-task");          // by custom IDs
var task = taskQueue.GetTaskFromBatch(batchSystemId, taskSystemId);    // by system IDs

// From a task within a batch (sibling access)
var batch = taskQueue.GetBatchByCustomId("my-batch");
var task = batch?.GetTaskByCustomId("task-1");
var tasks = batch?.GetTasksByCustomId("task-1");

// Using TaskBuilder static helpers
var task = TaskBuilder.GetTaskFromBatch(queue, "my-batch", "my-task");
```

### Sibling Tasks

Tasks inside a batch can access other tasks in the same batch:

```csharp
TaskBuilder.Create("step-2")
    .WithAction(task => {
        // Get a sibling by custom ID
        var step1 = task.GetSiblingTaskByCustomId("step-1");
        var data = step1?.Metadata;

        // Get sibling by system ID
        var sibling = task.GetSiblingTaskBySystemId(someGuid);

        // Get all siblings with a custom ID
        var siblings = task.GetSiblingTasksByCustomId("repeated-task");

        // Get a task from a different batch
        var otherTask = task.GetTaskFromBatch("other-batch", "other-task");
    })
    .Build();
```

### Cancelling Tasks

```csharp
// Cancel by system ID
taskQueue.CancelTask(systemId);

// Cancel by custom ID
taskQueue.CancelTask("my-task");

// Cancel all tasks with a custom ID
taskQueue.CancelAllTasks("my-task");

// Cancel with context boundary
taskQueue.CancelTask("my-task", ContextDefinition.SameContext);
taskQueue.CancelAllTasks("my-task", ContextDefinition.SameContextStrict);

// Cancel directly from the task instance
var task = taskQueue.GetTaskByCustomId("my-task");
task?.Cancel();
```

### Cancelling and Failing Batches

```csharp
// Cancel a batch by system ID (cancels all its remaining tasks)
taskQueue.CancelBatch(systemId);

// Cancel a batch by custom ID
taskQueue.CancelBatch("my-batch");

// Cancel all batches with a custom ID
taskQueue.CancelAllBatches("my-batch");

// Cancel directly from the batch instance
var batch = taskQueue.GetBatchByCustomId("my-batch");
batch?.Cancel();

// Fail a batch with an exception
taskQueue.FailBatch(systemId, new Exception("Something went wrong"));
taskQueue.FailBatch("my-batch", new Exception("Something went wrong"));

// Fail directly from the batch instance
batch?.Fail(new Exception("Manual failure"));
batch?.Fail(); // Uses a default exception message
```

### Clearing the Queue

```csharp
// Clear all items (tasks and batches)
int cleared = taskQueue.ClearQueue();

// Clear only completed/cancelled/failed tasks (standalone tasks only)
int removed = taskQueue.ClearCompletedTasks();

// Clear only completed/cancelled/failed batches
int removed = taskQueue.ClearCompletedBatches();
```

### Inserting Tasks

Insert a task after another task already in the queue:

```csharp
// Insert after a task by system ID
TaskBuilder.Create("inserted-task")
    .WithDelay(TimeSpan.FromSeconds(5))
    .EnqueueToAfterTask(queue, afterTaskSystemId);

// Insert after a task by custom ID
TaskBuilder.Create("inserted-task")
    .WithDelay(TimeSpan.FromSeconds(5))
    .EnqueueToAfterTask(queue, "target-task-id");

// Using the queue method directly (also supports ContextDefinition)
queue.InsertTaskAfter(myTask, "target-task-id", ContextDefinition.SameContext);
```

Insertion works across both standalone tasks and tasks inside batches. When inserting after a task inside a batch, the new task is added to the same batch. Insertion before the currently executing task is not allowed.

### Skipping Tasks

```csharp
// Skip the next 3 queued tasks (cancels them)
int skipped = taskQueue.SkipNextTasks(3);

// Skip including the current task
int skipped = taskQueue.SkipNextTasks(3, includeCurrentTask: true);

// Skip with context boundary
int skipped = taskQueue.SkipNextTasks(3, boundaryType: ContextDefinition.SameContext);

// Skip the current task only
bool skipped = taskQueue.SkipCurrentTask();
```

Skipping inside an action:

```csharp
TaskBuilder.Create()
    .WithAction(task => {
        if (SomeCondition())
        {
            queue.SkipNextTasks(3, includeCurrentTask: true);
            // Skips this task and the next 3
        }
    })
    .EnqueueTo(queue);
```

### Skipping Batches

```csharp
// Skip the next 2 queued batches
int skipped = taskQueue.SkipNextBatches(2);

// Skip including the current batch
int skipped = taskQueue.SkipNextBatches(2, includeCurrentBatch: true);

// Skip the current batch only
bool skipped = taskQueue.SkipCurrentBatch();
```

### Jumping to a Task (Goto)

Jump to a specific task, cancelling all items before it:

```csharp
// Jump by system ID
taskQueue.JumpToTask(targetSystemId);

// Jump by custom ID
taskQueue.JumpToTask("target-task");

// Jump with context boundary checking
taskQueue.JumpToTask("target-task", ContextDefinition.SameContext);
```

The target task must be in `Queued` status. All queued/executing items before the target are cancelled.

### Jumping to a Task Inside a Batch

Jump to a specific task within a batch, cancelling all items before it (including items before the batch and tasks before the target within the batch):

```csharp
// By system IDs
taskQueue.JumpToTaskInBatch(batchSystemId, taskSystemId);

// By custom IDs
taskQueue.JumpToTaskInBatch("my-batch", "my-task");
```

---

## Context Boundaries

Many queue operations accept a `ContextDefinition` parameter that controls how context boundaries are checked. This determines the scope of operations like task retrieval, skipping, jumping, and cancellation.

### `ContextDefinition.CrossContext` (default)

No boundary checks. Operations span the entire queue, crossing batch boundaries freely. Two tasks are always considered in the same context.

### `ContextDefinition.SameContext`

Two tasks are in the same context if:
- They are both within the same batch, OR
- They are both standalone tasks in the queue (batches between them are allowed)

Tasks in different contexts (one in a batch, one standalone) are not considered to be in the same context.

### `ContextDefinition.SameContextStrict`

Two tasks are in the same context if:
- They are both within the same batch, OR
- They are both standalone tasks with NO batch separating them

Any batch boundary between standalone tasks breaks the context.

### Example

```csharp
// Get only tasks in the same context
var tasks = queue.GetAllTasks(ContextDefinition.SameContext);

// Skip tasks only within the same strict context
queue.SkipNextTasks(2, boundaryType: ContextDefinition.SameContextStrict);

// Jump only within the same context
queue.JumpToTask("target", ContextDefinition.SameContext);
```

---

## Retry Configuration

Automatically retry tasks when their completion condition stalls (remains false for too long).

### Unlimited Retries

```csharp
TaskBuilder.Create()
    .WithAction(() => AttemptConnection())
    .WithCondition(() => IsConnected())
    .WithUnlimitedRetries(
        stallTimeout: TimeSpan.FromSeconds(30),
        retryDelay: TimeSpan.FromSeconds(5)
    )
    .EnqueueTo(queue);
```

### Limited Retries

```csharp
TaskBuilder.Create()
    .WithAction(() => AttemptOperation())
    .WithCondition(() => IsOperationComplete())
    .WithRetries(
        maxAttempts: 3,
        stallTimeout: TimeSpan.FromSeconds(10),
        retryDelay: TimeSpan.FromSeconds(2)
    )
    .EnqueueTo(queue);
```

When max retries are exceeded, the task fails with a `MaxRetryAttemptsExceededException`.

### Retry Actions and Callbacks

```csharp
TaskBuilder.Create()
    .WithAction(() => FirstAttempt())
    .WithCondition(() => IsComplete())
    .WithRetries(3, TimeSpan.FromSeconds(10))
    // Custom action to execute on retry (instead of the original action)
    .WithRetryAction((task, attempt) => {
        NoireLogger.LogInfo($"Retry attempt {attempt}");
        RetryLogic();
    })
    // Callback before each retry
    .OnBeforeRetry((task, attempt) => {
        NoireLogger.LogWarning($"Retrying task {task.CustomId}, attempt {attempt}");
    })
    // Callback when max retries are exceeded (called before OnFailed)
    .OnMaxRetriesExceeded(task => {
        NoireLogger.LogError($"Task {task.CustomId} exhausted all retries");
    })
    .EnqueueTo(queue);
```

### Retry Configuration Object

For advanced scenarios, create a reusable configuration:

```csharp
var retryConfig = new TaskRetryConfiguration
{
    MaxAttempts = 5,
    StallTimeout = TimeSpan.FromSeconds(30),
    RetryDelay = TimeSpan.FromSeconds(5),
    OverrideRetryAction = (task, attempt) => CustomRetryLogic(task, attempt),
    OnBeforeRetry = (task, attempt) => LogRetry(task, attempt),
    OnMaxRetriesExceeded = task => HandleFailure(task)
};

TaskBuilder.Create()
    .WithAction(() => DoWork())
    .WithCondition(() => IsComplete())
    .WithRetryConfiguration(retryConfig)
    .EnqueueTo(queue);
```

You can also use the factory methods:

```csharp
var unlimited = TaskRetryConfiguration.Unlimited(
    stallTimeout: TimeSpan.FromSeconds(30),
    retryDelay: TimeSpan.FromSeconds(5));

var limited = TaskRetryConfiguration.WithMaxAttempts(
    maxAttempts: 3,
    stallTimeout: TimeSpan.FromSeconds(10),
    retryDelay: TimeSpan.FromSeconds(2));
```

---

## EventBus Integration

The `NoireTaskQueue` integrates with `NoireEventBus` for event-driven task completion and queue lifecycle events.

### Setup

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus");
taskQueue.EventBus = eventBus;
```

### Event-Driven Task Completion

```csharp
// Define your events
public record PlayerEnterMapEvent(int MapId);
public record CombatStartedEvent();

// Wait for a filtered event
TaskBuilder.Create("wait-for-map")
    .WaitForEvent<PlayerEnterMapEvent>(
        filter: evt => evt.MapId == 129
    )
    .EnqueueTo(queue);

// Later in your code, publish the event
eventBus.Publish(new PlayerEnterMapEvent(129));
// Task completes automatically
```

### Event Capture While Queued

By default, event-based conditions only capture events when the task is in `Executing` or `WaitingForCompletion` status. You can enable early event capture while the task is still queued:

```csharp
TaskBuilder.Create()
    .WaitForEvent<SomeEvent>(
        allowCaptureWhileQueued: true,   // Capture events even while queued
        eventCaptureDepth: 5,            // Max 5 tasks between current and this task
        boundaryType: ContextDefinition.SameContext  // Only within same context
    )
    .EnqueueTo(queue);
```

**Parameters:**
- `allowCaptureWhileQueued` - If `true`, events can be captured in `Queued`, `Executing`, or `WaitingForCompletion` status. Default: `false`.
- `eventCaptureDepth` - Maximum number of tasks allowed between the currently executing task and this task for event capture. `null` means no depth limit. Only applies when `allowCaptureWhileQueued` is `true`.
- `boundaryType` - Defines how context boundaries are checked for depth calculation. Default: `CrossContext`.

### Queue Events

```csharp
eventBus.Subscribe<QueueStartedEvent>(evt =>
    NoireLogger.LogInfo("Queue started"));

eventBus.Subscribe<QueuePausedEvent>(evt =>
    NoireLogger.LogInfo("Queue paused"));

eventBus.Subscribe<QueueResumedEvent>(evt =>
    NoireLogger.LogInfo("Queue resumed"));

eventBus.Subscribe<QueueStoppedEvent>(evt =>
    NoireLogger.LogInfo("Queue stopped"));

eventBus.Subscribe<QueueCompletedEvent>(evt =>
    NoireLogger.LogInfo($"Queue completed {evt.TasksCompleted} tasks"));

eventBus.Subscribe<QueueClearedEvent>(evt =>
    NoireLogger.LogInfo($"Queue cleared {evt.TasksCleared} items"));
```

### Task Events

```csharp
eventBus.Subscribe<TaskQueuedEvent>(evt =>
    NoireLogger.LogInfo($"Task queued: {evt.Task}"));

eventBus.Subscribe<TaskStartedEvent>(evt =>
    NoireLogger.LogInfo($"Task started: {evt.Task}"));

eventBus.Subscribe<TaskCompletedEvent>(evt =>
    NoireLogger.LogInfo($"Task completed: {evt.Task}"));

eventBus.Subscribe<TaskCancelledEvent>(evt =>
    NoireLogger.LogWarning($"Task cancelled: {evt.Task}"));

eventBus.Subscribe<TaskFailedEvent>(evt =>
    NoireLogger.LogError($"Task failed: {evt.Task}, Error: {evt.Exception.Message}"));

eventBus.Subscribe<TaskRetryingEvent>(evt =>
    NoireLogger.LogInfo($"Task retrying: {evt.Task}, Attempt: {evt.RetryAttempt}"));
```

### Batch Events

```csharp
eventBus.Subscribe<BatchQueuedEvent>(evt =>
    NoireLogger.LogInfo($"Batch queued: {evt.Batch}"));

eventBus.Subscribe<BatchStartedEvent>(evt =>
    NoireLogger.LogInfo($"Batch started: {evt.Batch}"));

eventBus.Subscribe<BatchCompletedEvent>(evt =>
    NoireLogger.LogInfo($"Batch completed: {evt.Batch}"));

eventBus.Subscribe<BatchCancelledEvent>(evt =>
    NoireLogger.LogWarning($"Batch cancelled: {evt.Batch}"));

eventBus.Subscribe<BatchFailedEvent>(evt =>
    NoireLogger.LogError($"Batch failed: {evt.Batch}, Error: {evt.Exception.Message}"));
```

---

## Task Callbacks

### Lifecycle Callbacks

```csharp
TaskBuilder.Create()
    .WithAction(() => DoWork())
    .WithCondition(() => IsComplete())
    // On successful completion
    .OnCompleted(task => {
        NoireLogger.LogInfo($"Task {task.CustomId} completed!");
        NoireLogger.LogInfo($"Execution time: {task.GetExecutionTime()}");
    })
    // On cancellation
    .OnCancelled(task => {
        NoireLogger.LogWarning($"Task {task.CustomId} was cancelled");
    })
    // On failure
    .OnFailed((task, exception) => {
        NoireLogger.LogError($"Task {task.CustomId} failed: {exception.Message}");
    })
    // Or handle both failure and cancellation with a single callback
    .OnFailedOrCancelled((task, exception) => {
        if (exception != null)
            NoireLogger.LogError($"Task failed: {exception.Message}");
        else
            NoireLogger.LogWarning("Task was cancelled");
    })
    .EnqueueTo(queue);
```

All callback methods also accept simpler overloads (e.g., `OnCompleted(Action)`, `OnFailed(Action)`, `OnFailed(Action<Exception>)`).

### Stopping Queue on Task Events

```csharp
// Stop the queue when this task fails
TaskBuilder.Create()
    .WithAction(() => CriticalOperation())
    .StopQueueOnFail()
    .EnqueueTo(queue);

// Stop the queue when this task is cancelled
TaskBuilder.Create()
    .WithAction(() => ImportantTask())
    .StopQueueOnCancel()
    .EnqueueTo(queue);

// Stop on either failure or cancellation
TaskBuilder.Create()
    .WithAction(() => MustSucceed())
    .StopQueueOnFailOrCancel()
    .EnqueueTo(queue);

// Combine with callbacks using the optional stopQueue parameter
TaskBuilder.Create()
    .OnFailed((task, ex) => {
        NoireLogger.LogError($"Critical failure: {ex.Message}");
    }, stopQueue: true)
    .OnCancelled(task => {
        NoireLogger.LogWarning("Cancelled!");
    }, stopQueue: true)
    .EnqueueTo(queue);
```

---

## Timeouts

Set a maximum time for task completion. If the timeout is exceeded, the task fails with a `TimeoutException`:

```csharp
TaskBuilder.Create()
    .WithAction(() => LongRunningOperation())
    .WithCondition(() => IsComplete())
    .WithTimeout(TimeSpan.FromSeconds(30))
    .OnFailed((task, exception) => {
        if (exception is TimeoutException)
            NoireLogger.LogWarning("Task timed out!");
    })
    .EnqueueTo(queue);
```

Timeouts are based on active processing time. When you pause the queue, timeout timers pause as well.

---

## Task Metadata

### Basic Metadata

Pass data between tasks using the `Metadata` property:

```csharp
// Store metadata in a task action
TaskBuilder.Create("task1")
    .WithAction(task => {
        var result = ComputeSomething();
        task.Metadata = result;
    })
    .EnqueueTo(queue);

// Or set metadata at creation time
TaskBuilder.Create("task1bis")
    .WithMetadata(new { Result = 42, Message = "Success" })
    .EnqueueTo(queue);

// Retrieve metadata in a later task
TaskBuilder.Create("task2")
    .WithAction(task => {
        var metadata = TaskBuilder.GetMetadataFromTask<MyResult>(queue, "task1");
        if (metadata != null)
        {
            // Use the data
        }
    })
    .EnqueueTo(queue);
```

### Pointer Metadata

For unsafe pointer scenarios, use `PointerMetadata<T>`:

```csharp
unsafe
{
    MyStruct* ptr = GetPointer();

    // Store pointer
    TaskBuilder.Create("task-with-pointer")
        .WithMetadata(new PointerMetadata<MyStruct>(ptr))
        .WithAction(() => UsePointer())
        .EnqueueTo(queue);

    // Retrieve pointer later
    TaskBuilder.Create("task-using-pointer")
        .WithAction(task => {
            var ptr = TaskBuilder.GetPointerMetadataFromTask<MyStruct>(queue, "task-with-pointer");
            if (ptr != null)
            {
                // Use pointer
            }
        })
        .EnqueueTo(queue);
}
```

### Batch Task Metadata

Retrieve metadata from tasks inside batches:

```csharp
// Get metadata from a task within a batch (by custom IDs)
var data = TaskBuilder.GetMetadataFromBatchTask<MyResult>(queue, "my-batch", "my-task");

// Get pointer metadata from a task within a batch
unsafe
{
    var ptr = TaskBuilder.GetPointerMetadataFromBatchTask<MyStruct>(queue, "my-batch", "my-task");
}
```

---

## Queue Statistics

Get detailed queue statistics:

```csharp
var stats = taskQueue.GetStatistics(
    getCopyOfCurrentTask: true,
    getCopyOfCurrentBatch: true
);

NoireLogger.LogInfo($"Total tasks: {stats.TotalTasks}");
NoireLogger.LogInfo($"Queued: {stats.QueuedTasks}");
NoireLogger.LogInfo($"Executing: {stats.ExecutingTasks}");
NoireLogger.LogInfo($"Completed: {stats.CompletedTasks}");
NoireLogger.LogInfo($"Cancelled: {stats.CancelledTasks}");
NoireLogger.LogInfo($"Failed: {stats.FailedTasks}");
NoireLogger.LogInfo($"Queue state: {stats.QueueState}");
NoireLogger.LogInfo($"Current queue size: {stats.CurrentQueueSize}");
NoireLogger.LogInfo($"Progress: {stats.ProgressPercentage:F1}%");
NoireLogger.LogInfo($"Total processing time: {stats.TotalProcessingTime}");
NoireLogger.LogInfo($"Current task: {stats.CurrentTaskDescription}");
NoireLogger.LogInfo($"Total batches queued: {stats.TotalBatchesQueued}");
NoireLogger.LogInfo($"Batches completed: {stats.BatchesCompleted}");
NoireLogger.LogInfo($"Batches cancelled: {stats.BatchesCancelled}");
NoireLogger.LogInfo($"Batches failed: {stats.BatchesFailed}");
```

### Individual Counts

```csharp
// Pending tasks (supports ContextDefinition)
int pending = taskQueue.GetPendingTaskCount();
int pendingSameContext = taskQueue.GetPendingTaskCount(ContextDefinition.SameContext);

// Pending batches
int pendingBatches = taskQueue.GetPendingBatchCount();

// Remaining tasks (not yet completed/failed/cancelled)
int remaining = taskQueue.GetRemainingTaskCount();

// Queue sizes
int totalItems = taskQueue.GetQueueSize();         // Total items (tasks + batches)
int taskCount = taskQueue.GetTaskQueueSize();       // Standalone tasks only
int batchCount = taskQueue.GetBatchQueueSize();     // Batches only

// Progress percentage (0 to 100)
double progress = taskQueue.GetQueueProgressPercentage(decimals: 1);

// Total active processing time in milliseconds (excludes paused time)
long processingMs = taskQueue.GetTotalProcessingTime();
```

---

## Troubleshooting

### Tasks Not Executing

**Problem**: Tasks are added but don't execute.

**Solutions**:
- Ensure the module is active: `taskQueue.IsActive == true`
- Check if the queue is started: `taskQueue.QueueState == QueueState.Running`
- Enable automatic processing: `taskQueue.SetAutoProcessing(true)`
- Or manually start: `taskQueue.StartQueue()`

### Tasks Stuck in WaitingForCompletion

**Problem**: Tasks never complete.

**Solutions**:
- Verify the completion condition is achievable
- Check if EventBus is properly configured for event-based conditions
- Add a timeout to detect stalled tasks: `.WithTimeout(TimeSpan.FromSeconds(30))`
- Use retry configuration to handle stalled conditions
- Enable logging: `taskQueue.EnableLogging = true`

### Event-Based Conditions Not Working

**Problem**: Tasks waiting for events never complete.

**Solutions**:
- Ensure EventBus is assigned: `taskQueue.EventBus = myEventBus`
- Verify the event is being published
- Check event filter logic
- Make sure EventBus is active
- If using `allowCaptureWhileQueued`, check that `eventCaptureDepth` and `boundaryType` are configured correctly

### Retries Not Triggering

**Problem**: Retry configuration doesn't seem to work.

**Solutions**:
- Verify retry configuration is set on the task
- Ensure `StallTimeout` is configured
- Check that the completion condition is predicate-based (retries work with predicate conditions, not event-based or immediate)
- Enable logging to see retry attempts

### Queue Stopping Unexpectedly

**Problem**: Queue stops processing without manual stop.

**Solutions**:
- Check if tasks have `StopQueueOnFail` or `StopQueueOnCancel` set
- Check if batches have `StopQueueOnFail`, `StopQueueOnCancel`, or `StopQueueOnTaskFailure()` configured
- Check for `BatchTaskFailureMode.FailBatchAndStopQueue` or `BatchTaskCancellationMode.CancelBatchAndQueue`
- Look for task failures or cancellations in logs
- Verify no external code is calling `PauseQueue()` or `StopQueue()`

### Batch Not Completing

**Problem**: A batch seems stuck.

**Solutions**:
- Check if a task inside the batch is stuck (same solutions as "Tasks Stuck in WaitingForCompletion")
- Verify the batch's `TaskFailureMode` and `TaskCancellationMode` are appropriate
- Check if a blocking task inside the batch is preventing progress
- Enable logging to see batch processing details

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
