using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Internal wrapper that allows tasks and batches to be stored in the same queue
/// while preserving insertion order.
/// </summary>
internal class QueueItemWrapper
{
    private readonly object item;

    public Guid SystemId { get; }
    public string? CustomId { get; }
    public bool IsBlocking { get; }
    public QueueItemType ItemType { get; }
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

    public static QueueItemWrapper FromTask(QueuedTask task)
        => new(task, task.SystemId, task.CustomId, task.IsBlocking, QueueItemType.Task, Environment.TickCount64);

    public static QueueItemWrapper FromBatch(TaskBatch batch)
        => new(batch, batch.SystemId, batch.CustomId, batch.IsBlocking, QueueItemType.Batch, batch.QueuedAtTicks);

    public object GetUnderlyingItem() => item;

    public QueuedTask AsTask() => (QueuedTask)item;

    public TaskBatch AsBatch() => (TaskBatch)item;

    public bool IsTask => ItemType == QueueItemType.Task;

    public bool IsBatch => ItemType == QueueItemType.Batch;
}
