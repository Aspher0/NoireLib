using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.UI;

/// <summary>
/// The queue behind <see cref="NoireUI.RunOnDraw"/>: work posted from any thread, run on the draw thread at the start of
/// the next frame.<br/>
/// Bounded with drop-oldest, so a UI that has stopped drawing costs a bounded amount of memory instead of growing until
/// the game dies. When NoireLib is not initialized there is no draw thread to marshal onto and actions run inline.
/// </summary>
/// <remarks>
/// This mirrors the delivery queue the networker uses on the framework thread; the two cannot be shared because they
/// drain on different threads.
/// </remarks>
internal sealed class UiDrawPump
{
    private readonly ConcurrentQueue<Action> queue = new();

    private int queuedCount;
    private int capacity = 512;

    /// <summary>
    /// How many actions the queue holds before the oldest are dropped. Values below one are raised to one.
    /// </summary>
    public int Capacity
    {
        get => capacity;
        set => capacity = Math.Max(1, value);
    }

    /// <summary>
    /// How many actions are waiting to run.
    /// </summary>
    public int Count => Volatile.Read(ref queuedCount);

    /// <summary>
    /// How many actions have been dropped because the queue was full, since startup.
    /// </summary>
    public int DroppedCount { get; private set; }

    /// <summary>
    /// Forces posts through the queue even when NoireLib is not initialized, leaving <see cref="Drain"/> as the only way
    /// to run them.<br/>
    /// This is the seam for exercising queueing, ordering and the drop-oldest policy without a running game.
    /// </summary>
    internal bool ForceQueuedDelivery { get; init; }

    /// <summary>
    /// Whether actions run on the posting thread because there is no draw thread to marshal onto.
    /// </summary>
    public bool InlineMode => !NoireService.IsInitialized() && !ForceQueuedDelivery;

    /// <summary>
    /// Queues an action for the next frame, or runs it inline when there is no draw thread.
    /// </summary>
    /// <param name="action">The action to run.</param>
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

    /// <summary>
    /// Runs the actions queued as of entry, on the calling thread.
    /// </summary>
    /// <returns>How many actions ran.</returns>
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

    /// <summary>
    /// Drops every queued action.
    /// </summary>
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
