using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.UI;

// The queue behind NoireUI.RunOnDraw: work posted from any thread, run on the draw thread at the start of the next
// frame. Bounded with drop-oldest; when NoireLib is not initialized there is no draw thread and actions run inline.
internal sealed class UiDrawPump
{
    private readonly ConcurrentQueue<Action> queue = new();

    private int queuedCount;
    private int capacity = 512;

    // How many actions the queue holds before the oldest are dropped. Values below one are raised to one.
    public int Capacity
    {
        get => capacity;
        set => capacity = Math.Max(1, value);
    }

    public int Count => Volatile.Read(ref queuedCount);

    // Counted since startup, never reset.
    public int DroppedCount { get; private set; }

    // Test seam: forces posts through the queue even when NoireLib is not initialized, leaving Drain as the only way
    // to run them.
    internal bool ForceQueuedDelivery { get; init; }

    // Actions run on the posting thread because there is no draw thread to marshal onto.
    public bool InlineMode => !NoireService.IsInitialized() && !ForceQueuedDelivery;

    // Queues an action for the next frame, or runs it inline when there is no draw thread.
    public void Post(Action action)
    {
        if (InlineMode)
        {
            Run(action);
            return;
        }

        if (Interlocked.Increment(ref queuedCount) > capacity)
        {
            if (queue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref queuedCount);
                DroppedCount++;

                NoireUI.Diagnostics.ReportFault(
                    nameof(NoireUI.RunOnDraw),
                    "The draw queue is full; the oldest queued action was dropped. This means work is being posted faster than the UI draws.",
                    null);
            }
        }

        queue.Enqueue(action);
    }

    // Runs the actions queued as of entry, on the calling thread.
    public int Drain()
    {
        // Drain only what was queued at entry, so an action that posts more work cannot stretch the frame indefinitely.
        var toDrain = Volatile.Read(ref queuedCount);
        var ran = 0;

        while (toDrain-- > 0 && queue.TryDequeue(out var action))
        {
            Interlocked.Decrement(ref queuedCount);
            Run(action);
            ran++;
        }

        return ran;
    }

    public void Clear()
    {
        queue.Clear();
        Volatile.Write(ref queuedCount, 0);
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            NoireUI.Diagnostics.ReportFault(nameof(NoireUI.RunOnDraw), "A queued draw-thread action threw.", ex);
        }
    }
}
