using NoireLib.EventBus;
using System;
using System.Linq;

namespace NoireLib.TaskQueue;

/// <summary>
/// EventBus integration partial class for <see cref="NoireTaskQueue"/>.
/// </summary>
public partial class NoireTaskQueue
{
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

        var subscribeMethod = typeof(NoireEventBus).GetMethods()
            .FirstOrDefault(m => m.Name == nameof(NoireEventBus.Subscribe)
                              && m.IsGenericMethod
                              && m.GetParameters().Length == 4
                              && m.GetParameters()[0].ParameterType.Name != "String");
        if (subscribeMethod == null)
        {
            if (EnableLogging)
                NoireLogger.LogError(this, "Could not find Subscribe method on EventBus");
            return;
        }

        var genericSubscribeMethod = subscribeMethod.MakeGenericMethod(eventType);

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

        Action<object> wrapper = (evt) =>
        {
            bool canCapture = task.CompletionCondition?.AllowEventCaptureWhileQueued == true
                ? (task.Status == TaskStatus.Queued ||
                   task.Status == TaskStatus.Executing ||
                   task.Status == TaskStatus.WaitingForCompletion)
                : (task.Status == TaskStatus.Executing ||
                   task.Status == TaskStatus.WaitingForCompletion);

            if (!canCapture)
                return;

            var boundaryType = task.CompletionCondition?.EventCaptureBoundaryType ?? ContextDefinition.CrossContext;
            if (boundaryType != ContextDefinition.CrossContext)
            {
                if (!AreTasksInSameContext(task, boundaryType))
                {
                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Event not captured for task {task}: different context (boundaryType = {boundaryType})");
                    return;
                }
            }

            if (task.CompletionCondition?.AllowEventCaptureWhileQueued == true &&
                task.CompletionCondition.EventCaptureDepth.HasValue)
            {
                var depth = GetTaskDepth(task, boundaryType);

                if (depth == null || depth > task.CompletionCondition.EventCaptureDepth.Value)
                {
                    if (EnableLogging)
                        NoireLogger.LogDebug(this, $"Event not captured for task {task}: depth {depth} exceeds limit {task.CompletionCondition.EventCaptureDepth.Value}");
                    return;
                }
            }

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
    /// Checks if a target task is in the same context as the currently executing task based on the boundary type.
    /// </summary>
    /// <param name="targetTask">The task to check.</param>
    /// <param name="boundaryType">The boundary type to use for context checking.</param>
    /// <returns>True if tasks are in the same context; false otherwise.</returns>
    private bool AreTasksInSameContext(QueuedTask targetTask, ContextDefinition boundaryType)
    {
        return boundaryType switch
        {
            ContextDefinition.CrossContext => true,
            ContextDefinition.SameContext => AreTasksInSameContextFlexible(targetTask),
            ContextDefinition.StrictWithBoundaryCheck => AreTasksInSameContextStrict(targetTask),
            _ => true
        };
    }

    /// <summary>
    /// Checks if tasks are in the same context with flexible batch boundaries (SameContext).
    /// </summary>
    private bool AreTasksInSameContextFlexible(QueuedTask targetTask)
    {
        if (currentBatch != null)
        {
            return ReferenceEquals(targetTask.ParentBatch, currentBatch);
        }
        else if (currentTask != null)
        {
            return targetTask.ParentBatch == null;
        }
        else
        {
            return true;
        }
    }

    /// <summary>
    /// Checks if tasks are in the same context with strict boundary checking (StrictWithBoundaryCheck).
    /// </summary>
    private bool AreTasksInSameContextStrict(QueuedTask targetTask)
    {
        if (currentBatch != null)
        {
            return ReferenceEquals(targetTask.ParentBatch, currentBatch);
        }
        else if (currentTask != null)
        {
            if (targetTask.ParentBatch != null)
                return false;

            bool foundCurrent = false;
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask && ReferenceEquals(item.AsTask(), currentTask))
                {
                    foundCurrent = true;
                    continue;
                }

                if (!foundCurrent)
                    continue;

                if (item.IsTask && ReferenceEquals(item.AsTask(), targetTask))
                    return true;

                if (item.IsBatch)
                    return false;
            }

            return false;
        }
        else
        {
            if (targetTask.ParentBatch != null)
                return false;

            foreach (var item in unifiedQueue)
            {
                if (item.IsTask && ReferenceEquals(item.AsTask(), targetTask))
                    return true;

                if (item.IsBatch)
                    return false;
            }

            return false;
        }
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
            foreach (var item in unifiedQueue)
            {
                if (item.IsTask)
                {
                    var task = item.AsTask();
                    if (task.EventSubscriptionToken != null)
                        UnsubscribeTask(task);
                }
                else if (item.IsBatch)
                {
                    var batch = item.AsBatch();
                    foreach (var task in batch.Tasks)
                        if (task.EventSubscriptionToken != null)
                            UnsubscribeTask(task);
                }
            }
        }
    }
}
