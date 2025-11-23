

# Module Documentation : NoireTaskQueue

You are reading the documentation for the `NoireTaskQueue` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Creating Tasks](#creating-tasks)
  - [Using TaskBuilder](#using-taskbuilder)
  - [Task Completion Conditions](#task-completion-conditions)
  - [Add Delay to Tasks](#add-delay-to-tasks)
  - [Blocking vs Non-Blocking Tasks](#blocking-vs-non-blocking-tasks)
- [Queue Control](#queue-control)
- [Task Management](#task-management)
- [Retry Configuration](#retry-configuration)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
  - [Task Metadata](#task-metadata)
  - [Task Callbacks](#task-callbacks)
  - [Timeouts](#timeouts)
  - [Queue Statistics](#queue-statistics)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireTaskQueue` is a powerful module that manages task queuing and processing with support for:
- **Blocking and non-blocking tasks** for flexible execution flow
- **Multiple completion conditions** (predicate, event-based, immediate)
- **Automatic retry logic** with configurable stall detection
- **Timeout support** for cancelling tasks that takes too long to complete
- **EventBus integration** for event-driven task completion
- **Task metadata** for passing data between tasks
- **Comprehensive callbacks** for task lifecycle events
- **Queue state management** (start, pause, resume, stop, skip n tasks, goto ...)

This module is ideal for scenarios where you need to:
- Execute tasks sequentially with specific completion conditions
- Wait for game state changes before proceeding
- Implement complex automation workflows
- Handle asynchronous operations with retry logic
- And a lot more scenarios

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create and Enqueue Your First Task

Using the powerful `TaskBuilder` API:

```csharp
// Those are very simple ways to create tasks
// However, there are way more methods allowing you to create complex, highly flexible tasks

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

That's it! Your tasks are now queued and will be processed when the queue will start, either automatically if `ShouldProcessQueueAutomatically` is `true` or manually by calling `TaskQueue.StartQueue()`.

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

`TaskBuilder` is a fluent API that makes creating and configuring tasks simple and intuitive.

#### Basic Usage

Those are basic example of the TaskBuilder fluent API:

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

TaskBuilder also provides simpler methods for common scenarios, if you want a faster one-liner:

```csharp
// Add a simple action
TaskBuilder.AddAction(
    action: () => NoireLogger.LogInfo("Action executed!"),
    queue: taskQueue
);

// Add a delay
TaskBuilder.AddDelaySeconds(3.0, taskQueue);
TaskBuilder.AddDelayMilliseconds(500, taskQueue);
TaskBuilder.AddDelay(TimeSpan.FromMinutes(1), taskQueue);

// Add a condition wait
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

### Task Completion Conditions

Tasks can complete based on different conditions:

#### 1. Immediate Completion

Task completes as soon as the action finishes (default):

```csharp
TaskBuilder.Create()
    .WithAction(() => DoWork())
    .WithImmediateCompletion() // Can omit since this is the default behavior
    .EnqueueTo(queue);
```

#### 2. Predicate-Based Condition

Task completes when a condition returns true:

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

Task completes when an EventBus event is published:

```csharp
// Wait for any event of this type
TaskBuilder.Create()
    .WithAction(() => InitiateTeleport())
    .WaitForEvent<TeleportCompleteEvent>()
    .EnqueueTo(queue);

// Wait for filtered event
TaskBuilder.Create()
    .WithAction(() => InitiateTeleport())
    .WaitForEvent<TeleportCompleteEvent>(
        filter: evt => evt.Destination == "Limsa Lominsa"
    )
    .EnqueueTo(queue);
```

### Add Delay to Tasks

You can also add tasks that will either act as a delay on their own, or you can add a post-completion delay.
In either cases, the process is the same:

```csharp
// Enqueue a 5 seconds delay task
TaskBuilder.Create()
    .WithDelay(TimeSpan.FromSeconds(5))
    .EnqueueTo(queue);

// Enqueue a task that will wait for completion
// then after condition completes, will wait 1 second
TaskBuilder.Create()
    .WithAction(() => StartMoving())
    .WithCondition(() => HasReachedDestination())
    .WithDelay(TimeSpan.FromSeconds(1))
    .EnqueueTo(queue);

// Add a predicate based delay
TaskBuilder.Create()
    .WithDelay(() => NoireService.ClientState.TerritoryType == 123 ?
      TimeSpan.FromSeconds(1) : null)
    .EnqueueTo(queue);

// Or accessing the task
TaskBuilder.Create()
    .WithAction(task => 
    {
       if (NoireService.ClientState.TerritoryType == 123)
       {
          task.Metadata = null;
          MoveToDestination();
          return;
       }
       
       task.Metadata = TimeSpan.FromMilliseconds(500);
       TeleportToTerritory();       
    })
    .WithDelay(task => task.Metadata)
    .EnqueueTo(queue);
```

### Blocking vs Non-Blocking Tasks

Control whether subsequent tasks wait for completion.
By default, tasks are set as blocking:

```csharp
// Blocking task (default) - queue waits for completion
TaskBuilder.Create()
    .WithAction(() => CriticalOperation())
    .WithCondition(() => IsOperationComplete())
    .EnqueueTo(queue);

// Non-blocking task - queue starts this task and then start the next one immediately
TaskBuilder.Create()
    .AsNonBlocking()
    .WithAction(() => BackgroundTask())
    .WithCondition(() => IsBackgroundTaskComplete())
    .EnqueueTo(queue);
```

**Use cases:**
- **Blocking**: Sequential operations where order matters (e.g., teleport → wait for load → interact)
- **Non-blocking**: Parallel operations that can run independently (e.g., logging, monitoring)

### Inserting a Task after another one

The fluent `TaskBuilder` API also lets you insert a task after another task down the queue:

```csharp
// Insert after a Task's system ID or custom ID
TaskBuilder.Create()
    .WithDelay(TimeSpan.FromSeconds(5))
    .EnqueueToAfterTask(queue, systemOrCustomTaskId); // Will insert after task with id in variable `systemOrCustomTaskId` in the specified `queue`
```

### Skipping N amount of Tasks

You may want to skip upcoming tasks based on some conditions, you can do it like so:

```csharp
TaskBuilder.Create()
    .WithAction(task =>
    {
        if (SomeCondition())
        {
            int numberOfTasksToSkip = 3;
            bool alsoSkipCurrent = true;
            queue.SkipNextTasks(numberOfTasksToSkip , alsoSkipCurrent);
            // Will skip current task, AND next 3 tasks.
            // If alsoSkipCurrent == false, will only skip next 3 tasks
        }
    })
    .EnqueueTo(queue);
```

### Jumping to a Task (Goto)

```csharp
// Will jump to the target task with system or custom ID
// Cancelling/Skipping all tasks preceding the target
queue.JumpToTask(targetTaskSystemOrCustomId);
```

---

## Queue Control

### Starting the Queue

```csharp
// Manual start
taskQueue.StartQueue();

// Or enable auto-start
taskQueue.SetAutoProcessing(true);
// Queue will start automatically when tasks are added
```

### Pausing and Resuming

```csharp
// Pause processing (preserves task state, timeouts pause)
taskQueue.PauseQueue();

// Resume processing
taskQueue.ResumeQueue();
```

**Note:** When paused:
- Delay-based conditions pause their timers
- Task timeouts pause
- Retry stall tracking pauses
- No tasks are processed

### Stopping the Queue

```csharp
// Stop and clear all tasks
taskQueue.StopQueue();
```

### Queue States

The queue can be in one of these states:

- `Idle`: Queue created but never started
- `Running`: Actively processing tasks
- `Paused`: Temporarily suspended
- `Stopped`: Stopped and cleared

Check the current state:

```csharp
if (taskQueue.QueueState == QueueState.Running)
{
    // Queue is processing
}
```

---

## Task Management

### Retrieving Tasks

```csharp
// Get current task
var current = taskQueue.GetCurrentTask();

// Get all tasks
var allTasks = taskQueue.GetAllTasks();

// Get by system ID
var task = taskQueue.GetTaskBySystemId(guid);

// Get by custom ID
var task = taskQueue.GetTaskByCustomId("my-task");
var tasks = taskQueue.GetTasksByCustomId("my-task"); // Multiple tasks
```

### Cancelling Tasks

```csharp
// Cancel by system ID
taskQueue.CancelTask(systemId);

// Cancel by custom ID
taskQueue.CancelTask("my-task");

// Cancel all with custom ID
taskQueue.CancelAllTasks("my-task");

// Or, cancelling the task directly
var task = taskQueue.GetTaskByCustomId("my-task");
task.Cancel(); // The task must have its property OwningQueue set to a NoireTaskQueue, automatically set with TaskBuilder.EnqueueTo(TaskQueue)
```

### Clearing the Queue

```csharp
// Clear all tasks
int cleared = taskQueue.ClearQueue();

// Clear only completed/cancelled/failed tasks
int removed = taskQueue.ClearCompletedTasks();
```

---

## Retry Configuration

Automatically retry tasks when completion conditions stalls:

### Unlimited Retries

```csharp
TaskBuilder.Create()
    .WithAction(() => AttemptConnection())
    .WithCondition(() => IsConnected())
    .WithUnlimitedRetries(
        stallTimeout: TimeSpan.FromSeconds(30),  // Consider stalled after 30s
        retryDelay: TimeSpan.FromSeconds(5)   // Wait 5s between retries
    )
    .EnqueueTo(queue);
```

### Limited Retries

```csharp
TaskBuilder.Create()
    .WithAction(() => AttemptOperation())
    .WithCondition(() => IsOperationComplete())
    .WithRetries(
        maxAttempts: 3,       // Max 3 retries
        stallTimeout: TimeSpan.FromSeconds(10),  // Stall after 10s
        retryDelay: TimeSpan.FromSeconds(2)      // 2s between retries
    )
    .EnqueueTo(queue);
```

### Retry Actions and Callbacks

```csharp
TaskBuilder.Create()
    .WithAction(() => FirstAttempt())
    .WithCondition(() => IsComplete())
    .WithRetries(3, TimeSpan.FromSeconds(10))
    // Custom retry action (instead of original action)
    .WithRetryAction((task, attempt) => {
        NoireLogger.LogInfo($"Retry attempt {attempt}");
        RetryLogic();
    })
    // Before each retry
    .OnBeforeRetry((task, attempt) => {
        NoireLogger.LogWarning($"Retrying task {task.CustomId}, attempt {attempt}");
    })
    // When max retries exceeded
    .OnMaxRetriesExceeded(task => {
        NoireLogger.LogError($"Task {task.CustomId} exhausted all retries");
    })
    .EnqueueTo(queue);
```

### Retry Configuration Object

For advanced scenarios, create a custom configuration:

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

---

## EventBus Integration

The `NoireTaskQueue` integrates with `NoireEventBus` for event-driven functionality.

### Setup

```csharp
// Create EventBus
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus");

// Assign to queue
taskQueue.EventBus = eventBus;
```

### Event-Driven Task Completion

```csharp
// Define your events
public record PlayerEnterMapEvent(int MapId);
public record CombatStartedEvent();

// Wait for event
TaskBuilder.Create("wait-for-map")
    .WaitForEvent<PlayerEnterMapEvent>(
        filter: evt => evt.MapId == 129
    )
    .EnqueueTo(queue);

// Later in your code, publish the event
eventBus.Publish(new PlayerEnterMapEvent(129));
// Task will complete automatically
```

### Queue Events

Subscribe to queue lifecycle events:

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
    NoireLogger.LogInfo($"Queue cleared {evt.TasksCleared} tasks"));
```

### Task Events

Subscribe to individual task events:

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

---

## Advanced Features

### Task Metadata

Pass data between tasks using metadata:

```csharp
// Store metadata in a task
TaskBuilder.Create("task1")
    .WithAction(() => {
        var result = ComputeSomething();
        // Metadata will be accessible later
    })
    .WithMetadata(new { Result = 42, Message = "Success" })
    .WithImmediateCompletion()
    .EnqueueTo(queue);

// Retrieve metadata in a later task
TaskBuilder.Create("task2")
    .WithAction(task => {
        var metadata = TaskBuilder.GetMetadataFromTask<object>(queue, "task1");
        if (metadata != null)
        {
            // Use the data
        }
    })
    .WithImmediateCompletion()
    .EnqueueTo(queue);
```

#### Pointer Metadata

For unsafe pointer scenarios:

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

### Task Callbacks

Respond to task lifecycle events:

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
    // Or handle both failure and cancellation
    .OnFailedOrCancelled((task, exception) => {
        if (exception != null)
            NoireLogger.LogError($"Task failed: {exception.Message}");
        else
            NoireLogger.LogWarning("Task was cancelled");
    })
    .EnqueueTo(queue);
```

### Stopping Queue on Task Events

Control queue behavior when tasks fail or are cancelled:

```csharp
// Stop on failure
TaskBuilder.Create()
    .WithAction(() => CriticalOperation())
    .WithCondition(() => IsComplete())
    .StopQueueOnFail()
    .EnqueueTo(queue);

// Stop on cancellation
TaskBuilder.Create()
  .WithAction(() => ImportantTask())
    .WithCondition(() => IsComplete())
    .StopQueueOnCancel()
    .EnqueueTo(queue);

// Stop on either
TaskBuilder.Create()
    .WithAction(() => MustSucceed())
    .WithCondition(() => IsComplete())
    .StopQueueOnFailOrCancel()
    .EnqueueTo(queue);

// Or use overloads with callbacks
TaskBuilder.Create()
    .WithAction(() => DoWork())
    .WithCondition(() => IsComplete())
    .StopQueueOnFailOrCancel((task, ex) => {
        NoireLogger.LogError($"Critical failure: {ex.Message}");
    }, stopQueue: true)
    .EnqueueTo(queue);
```

### Timeouts

Set timeouts for task completion:

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

**Note:** Timeouts respect pause/resume - the timer pauses when the queue is paused.

### Queue Statistics

Get detailed queue statistics:

```csharp
var stats = taskQueue.GetStatistics(getCopyOfCurrentTask: true);

NoireLogger.LogInfo($"Total tasks queued: {stats.TotalTasksQueued}");
NoireLogger.LogInfo($"Completed: {stats.TasksCompleted}");
NoireLogger.LogInfo($"Cancelled: {stats.TasksCancelled}");
NoireLogger.LogInfo($"Failed: {stats.TasksFailed}");
NoireLogger.LogInfo($"Current queue size: {stats.CurrentQueueSize}");
NoireLogger.LogInfo($"Queue state: {stats.QueueState}");
NoireLogger.LogInfo($"Total processing time: {stats.TotalProcessingTime}");
NoireLogger.LogInfo($"Current task: {stats.CurrentTask}");

// Get specific counts
int pending = taskQueue.GetPendingTaskCount();
int remaining = taskQueue.GetRemainingTaskCount();
int size = taskQueue.GetQueueSize();

// Get progress (0.0 to 1.0)
double progress = taskQueue.GetQueueProgress();
NoireLogger.LogInfo($"Queue is {progress * 100:F1}% complete");
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
- Verify completion condition is achievable
- Check if EventBus is properly configured for event-based conditions
- Add timeout to detect stalled tasks: `.WithTimeout(TimeSpan.FromSeconds(30))`
- Use retry configuration to handle stalled conditions
- Enable logging to see what's happening: `taskQueue.EnableLogging = true`

### Event-Based Conditions Not Working

**Problem**: Tasks waiting for events never complete.

**Solutions**:
- Ensure EventBus is assigned: `taskQueue.EventBus = myEventBus`
- Verify the event is being published
- Check event filter logic
- Make sure EventBus is active

### Retries Not Triggering

**Problem**: Retry configuration doesn't seem to work.

**Solutions**:
- Verify retry configuration is set on the task
- Ensure `StallTimeout` is configured
- Check that the completion condition is predicate-based (retries don't work with event-based or immediate conditions)
- Enable logging to see retry attempts

### Queue Pausing Unexpectedly

**Problem**: Queue stops processing without manual pause.

**Solutions**:
- Check if tasks have `StopQueueOnFail` or `StopQueueOnCancel` set
- Look for task failures or cancellations in logs
- Verify no external code is calling `PauseQueue()` or `StopQueue()`

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
