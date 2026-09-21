using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

internal sealed class SendItem
{
    public SendItem(byte[] payload, NoireWebsocketMessageKind kind)
    {
        Payload = payload;
        Kind = kind;
    }

    public byte[] Payload { get; }

    public NoireWebsocketMessageKind Kind { get; }

    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// Bounded, first in first out across reconnects. The room counter only exists for the Block policy.
internal sealed class SendQueue
{
    private readonly object gate = new();
    private readonly LinkedList<SendItem> items = new();
    private readonly SemaphoreSlim pending = new(0);
    private readonly SemaphoreSlim? room;
    private readonly int capacity;
    private readonly NoireSocketSendOverflow overflow;

    private bool closed;

    public SendQueue(int capacity, NoireSocketSendOverflow overflow)
    {
        this.capacity = capacity < 1 ? 1 : capacity;
        this.overflow = overflow;

        room = overflow == NoireSocketSendOverflow.Block
            ? new SemaphoreSlim(this.capacity, this.capacity)
            : null;
    }

    public int Capacity => capacity;

    public int Count
    {
        get
        {
            lock (gate)
                return items.Count;
        }
    }

    public async Task EnqueueAsync(SendItem item, CancellationToken cancellationToken)
    {
        if (room != null)
        {
            await room.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (gate)
            {
                if (closed)
                {
                    room.Release();
                    throw new NoireSocketClosedException("The connection stopped before the message could be queued.");
                }

                items.AddLast(item);
            }

            pending.Release();
            return;
        }

        SendItem? dropped = null;

        lock (gate)
        {
            if (closed)
                throw new NoireSocketClosedException("The connection stopped before the message could be queued.");

            if (items.Count < capacity)
            {
                items.AddLast(item);
            }
            else
            {
                switch (overflow)
                {
                    case NoireSocketSendOverflow.DropNewest:
                        dropped = item;
                        break;

                    case NoireSocketSendOverflow.DropOldest:
                        dropped = items.First!.Value;
                        items.RemoveFirst();
                        items.AddLast(item);
                        break;

                    default:
                        throw new NoireSocketQueueFullException(capacity);
                }
            }
        }

        // Either policy leaves the length unchanged.
        if (dropped != null)
        {
            Settle(dropped, null);
            return;
        }

        pending.Release();
    }

    public async Task<SendItem?> DequeueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (gate)
            {
                if (items.First == null)
                    continue;

                var item = items.First.Value;
                items.RemoveFirst();
                return item;
            }
        }

        return null;
    }

    // A message whose write failed goes out first on the next connection.
    public void Requeue(SendItem item)
    {
        lock (gate)
        {
            if (closed)
            {
                item.Completion.TrySetException(
                    new NoireSocketClosedException("The connection stopped before the message was written."));
                return;
            }

            items.AddFirst(item);
        }

        pending.Release();
    }

    public void Settle(SendItem item, Exception? error)
    {
        if (error == null)
            item.Completion.TrySetResult();
        else
            item.Completion.TrySetException(error);

        room?.Release();
    }

    public void Close(Exception error)
    {
        SendItem[] waiting;

        lock (gate)
        {
            if (closed)
                return;

            closed = true;
            waiting = new SendItem[items.Count];
            items.CopyTo(waiting, 0);
            items.Clear();
        }

        foreach (var item in waiting)
            Settle(item, error);
    }
}
