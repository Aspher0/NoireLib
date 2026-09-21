using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Websocket.Internal;

// The completion only exists when a caller asked for it. A fire-and-forget send would leave a faulted task nobody observes.
internal sealed class PeerSendItem
{
    private TaskCompletionSource? completion;

    public PeerSendItem(byte[] frame, bool isClose = false)
    {
        Frame = frame;
        IsClose = isClose;
    }

    public byte[] Frame { get; }

    public bool IsClose { get; }

    // Set before the item is queued.
    public Task Awaited
        => (completion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public void Complete()
        => completion?.TrySetResult();

    public void Fail(Exception error)
        => completion?.TrySetException(error);
}

internal enum PeerEnqueueResult
{
    Queued,
    DroppedOldest,
    DroppedNewest,
    Overflowed,
    Closed,
}

// Bounded. The overflow policy applies when a message would pass the capacity. A peer that stopped reading is noticed while the queue still fits in memory.
internal sealed class PeerSendQueue
{
    private readonly object gate = new();
    private readonly LinkedList<PeerSendItem> items = new();
    private readonly SemaphoreSlim pending = new(0);
    private readonly int capacity;
    private readonly NoireWebsocketPeerOverflow overflow;

    private bool closed;

    public PeerSendQueue(int capacity, NoireWebsocketPeerOverflow overflow)
    {
        this.capacity = capacity < 1 ? 1 : capacity;
        this.overflow = overflow;
    }

    public int Capacity => capacity;

    public bool IsClosed
    {
        get
        {
            lock (gate)
                return closed;
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
                return items.Count;
        }
    }

    // The connection decides what a dropped or refused message's task does.
    public PeerEnqueueResult Enqueue(PeerSendItem item)
    {
        PeerSendItem? dropped = null;
        PeerEnqueueResult result;

        lock (gate)
        {
            if (closed)
                return PeerEnqueueResult.Closed;

            if (items.Count < capacity)
            {
                items.AddLast(item);
                result = PeerEnqueueResult.Queued;
            }
            else
            {
                switch (overflow)
                {
                    case NoireWebsocketPeerOverflow.DropNewest:
                        dropped = item;
                        result = PeerEnqueueResult.DroppedNewest;
                        break;

                    case NoireWebsocketPeerOverflow.DropOldest:
                        dropped = items.First!.Value;
                        items.RemoveFirst();
                        items.AddLast(item);
                        result = PeerEnqueueResult.DroppedOldest;
                        break;

                    default:
                        result = PeerEnqueueResult.Overflowed;
                        break;
                }
            }
        }

        if (result == PeerEnqueueResult.Overflowed)
            return result;

        if (dropped != null)
        {
            dropped.Complete();

            // Either policy leaves the length unchanged.
            return result;
        }

        pending.Release();
        return result;
    }

    // A close or a pong goes ahead of the backlog.
    public void EnqueueFirst(PeerSendItem item)
    {
        lock (gate)
        {
            if (closed)
            {
                item.Fail(new NoireSocketClosedException("The connection stopped before the message was written."));
                return;
            }

            items.AddFirst(item);
        }

        pending.Release();
    }

    // Null on a heartbeat tick or a closed queue.
    public async Task<PeerSendItem?> DequeueAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await pending.WaitAsync(wait, cancellationToken).ConfigureAwait(false))
                return null;

            lock (gate)
            {
                if (items.First == null)
                {
                    if (closed)
                        return null;

                    continue;
                }

                var item = items.First.Value;
                items.RemoveFirst();

                return item;
            }
        }

        return null;
    }

    public void Close(Exception error)
    {
        PeerSendItem[] waiting;

        lock (gate)
        {
            if (closed)
                return;

            closed = true;
            waiting = new PeerSendItem[items.Count];
            items.CopyTo(waiting, 0);
            items.Clear();
        }

        foreach (var item in waiting)
            item.Fail(error);

        pending.Release();
    }
}
