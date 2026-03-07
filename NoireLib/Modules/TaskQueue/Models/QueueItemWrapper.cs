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
    /// The system ID given to the task or batch when it was created. This is a unique identifier that can be used to track the item throughout its lifecycle.
    /// </summary>
    public Guid SystemId { get; }

    /// <summary>
    /// The custom ID assigned to the task or batch, if any. This can be used for user-defined tracking or categorization.
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

    private QueueItemWrapper(object item, Guid systemId, string? customId, bool isBlocking, QueueItemType itemType, long queuedAtTicks)
    {
        this.item = item;
        SystemId = systemId;
        CustomId = customId;
        IsBlocking = isBlocking;
        ItemType = itemType;
        QueuedAtTicks = queuedAtTicks;
    }

    /// <summary>
    /// Creates a new instance of the QueueItemWrapper class that encapsulates the properties of the specified QueuedTask.
    /// </summary>
    /// <param name="task">The QueuedTask instance containing the data to initialize the QueueItemWrapper. Must not be null.</param>
    /// <returns>A QueueItemWrapper that represents the provided QueuedTask, including its identifiers and state information.</returns>
    public static QueueItemWrapper FromTask(QueuedTask task)
        => new(task, task.SystemId, task.CustomId, task.IsBlocking, QueueItemType.Task, Environment.TickCount64);

    /// <summary>
    /// Creates a new instance of the QueueItemWrapper class that encapsulates the properties of the specified
    /// TaskBatch.
    /// </summary>
    /// <param name="batch">The TaskBatch instance containing the data to initialize the QueueItemWrapper. Must not be null.</param>
    /// <returns>A QueueItemWrapper that represents the provided TaskBatch, including its identifiers and state information.</returns>
    public static QueueItemWrapper FromBatch(TaskBatch batch)
        => new(batch, batch.SystemId, batch.CustomId, batch.IsBlocking, QueueItemType.Batch, batch.QueuedAtTicks);

    /// <summary>
    /// Gets the underlying item associated with this instance.
    /// </summary>
    /// <returns>The underlying item, which can be of any type, representing the data encapsulated by this instance.</returns>
    public object GetUnderlyingItem() => item;

    /// <summary>
    /// Gets the wrapped item as a QueuedTask. Should only be called if IsTask is true, otherwise an InvalidCastException will be thrown.
    /// </summary>
    /// <returns>The wrapped item as a QueuedTask.</returns>
    public QueuedTask AsTask() => (QueuedTask)item;

    /// <summary>
    /// Gets the wrapped item as a TaskBatch. Should only be called if IsBatch is true, otherwise an InvalidCastException will be thrown.
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
    /// <param name="showTaskIdentifierIfBatch">If true and the item is a batch with an executing task, returns that task's identifier; otherwise returns the batch identifier.</param>
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
