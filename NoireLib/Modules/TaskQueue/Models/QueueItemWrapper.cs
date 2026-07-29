using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Internal wrapper that allows tasks and batches to be stored in the same queue
/// while preserving insertion order.
/// </summary>
public class QueueItemWrapper
{
    private readonly object item;

    /// <summary>
    /// The queue that owns this batch.
    /// </summary>
    public NoireTaskQueue? OwningQueue { get; internal set; }

    /// <summary>
    /// The system ID given to the task or batch when it was created.
    /// </summary>
    public Guid SystemId { get; }

    /// <summary>
    /// The custom ID assigned to the task or batch, if any.
    /// </summary>
    public string? CustomId { get; }

    /// <summary>
    /// Indicates whether the task or batch is blocking other items in the queue.
    /// </summary>
    public bool IsBlocking { get; }

    /// <summary>
    /// The type of the item (task or batch).
    /// </summary>
    public QueueItemType ItemType { get; }

    /// <summary>
    /// The timestamp when the item was queued, in ticks.
    /// </summary>
    public long QueuedAtTicks { get; }

    private QueueItemWrapper(object item, NoireTaskQueue? owningQueue, Guid systemId, string? customId, bool isBlocking, QueueItemType itemType, long queuedAtTicks)
    {
        this.item = item;
        OwningQueue = owningQueue;
        SystemId = systemId;
        CustomId = customId;
        IsBlocking = isBlocking;
        ItemType = itemType;
        QueuedAtTicks = queuedAtTicks;
    }

    /// <summary>
    /// Creates a QueueItemWrapper for a <see cref="QueuedTask"/>.
    /// </summary>
    /// <param name="task">The task to wrap.</param>
    /// <returns>The wrapping QueueItemWrapper.</returns>
    public static QueueItemWrapper FromTask(QueuedTask task)
        => new(task, task.OwningQueue, task.SystemId, task.CustomId, task.IsBlocking, QueueItemType.Task, Environment.TickCount64);

    /// <summary>
    /// Creates a QueueItemWrapper for a <see cref="TaskBatch"/>.
    /// </summary>
    /// <param name="batch">The batch to wrap.</param>
    /// <returns>The wrapping QueueItemWrapper.</returns>
    public static QueueItemWrapper FromBatch(TaskBatch batch)
        => new(batch, batch.OwningQueue, batch.SystemId, batch.CustomId, batch.IsBlocking, QueueItemType.Batch, batch.QueuedAtTicks);

    /// <summary>
    /// Gets the underlying item associated with this instance.
    /// </summary>
    /// <returns>The underlying item.</returns>
    public object GetUnderlyingItem() => item;

    /// <summary>
    /// Gets the wrapped item as a QueuedTask; throws InvalidCastException unless IsTask is true.
    /// </summary>
    /// <returns>The wrapped item as a QueuedTask.</returns>
    public QueuedTask AsTask() => (QueuedTask)item;

    /// <summary>
    /// Gets the wrapped item as a TaskBatch; throws InvalidCastException unless IsBatch is true.
    /// </summary>
    /// <returns>The wrapped item as a TaskBatch.</returns>
    public TaskBatch AsBatch() => (TaskBatch)item;

    /// <summary>
    /// Determines whether the wrapped item is a task.
    /// </summary>
    public bool IsTask => ItemType == QueueItemType.Task;

    /// <summary>
    /// Determines whether the wrapped item is a batch.
    /// </summary>
    public bool IsBatch => ItemType == QueueItemType.Batch;

    /// <inheritdoc/>
    public override string ToString() => GetIdentifier();

    /// <summary>
    /// Gets the string representation of the wrapped item, optionally showing the currently executing task if the item is a batch.
    /// </summary>
    /// <param name="showTaskIdentifierIfBatch">Whether to show the executing task's identifier when the item is a batch.</param>
    /// <returns>The identifier string of the item or its currently executing task.</returns>
    public string GetIdentifier(bool showTaskIdentifierIfBatch = true)
    {
        if (!showTaskIdentifierIfBatch || IsTask)
            return item.ToString() ?? string.Empty;

        return AsBatch().GetCurrentIdentifier();
    }

    /// <summary>
    /// Gets the display name (CustomId if available, otherwise SystemId) of the wrapped item.
    /// </summary>
    /// <returns>The CustomId if not empty, otherwise the SystemId as a string.</returns>
    public string GetDisplayName()
    {
        return !string.IsNullOrEmpty(CustomId) ? CustomId : SystemId.ToString();
    }

    /// <summary>
    /// Gets the status of the wrapped item as a string.
    /// </summary>
    /// <returns>The status of the underlying task or batch.</returns>
    public string GetStatus()
    {
        return IsTask ? AsTask().Status.ToString() : AsBatch().Status.ToString();
    }
}
