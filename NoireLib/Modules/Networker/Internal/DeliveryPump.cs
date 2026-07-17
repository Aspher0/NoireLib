using Dalamud.Plugin.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.Networker.Internal;

/// <summary>
/// The single ordered delivery queue of a networker: peer mutations, handler invocations, and request completions
/// all pass through it, giving the "everything user-visible happens on the framework thread" guarantee.<br/>
/// Bounded - a long-frozen instance drops its oldest deliveries with an error report instead of growing memory.<br/>
/// When NoireLib is not initialized (unit tests), actions run inline on the posting thread.
/// </summary>
internal sealed class DeliveryPump : IDisposable
{
    private readonly ConcurrentQueue<Action> queue = new();
    private readonly int capacity;
    private readonly Action<Exception, string> onError;
    private int queuedCount;
    private int attached;
    private int disposed;

    public DeliveryPump(int capacity, Action<Exception, string> onError)
    {
        this.capacity = capacity;
        this.onError = onError;
    }

    /// <summary>
    /// Forces deliveries through the framework thread queue even when NoireLib is not initialized, leaving
    /// <see cref="Drain"/> as the only way to run them.<br/>
    /// This is the seam for exercising queueing, ordering and the discard-on-disposal policy without a running game.
    /// </summary>
    internal bool ForceQueuedDelivery { get; init; }

    /// <summary>
    /// Whether deliveries run on the posting thread.<br/>
    /// Without an initialized NoireLib there is no framework thread to marshal onto, so inline is the only option.
    /// </summary>
    public bool InlineMode => !NoireService.IsInitialized() && !ForceQueuedDelivery;

    /// <summary>
    /// Queues a delivery for the framework thread, or runs it inline when there is no framework thread to marshal
    /// onto. Deliveries posted after disposal are discarded, and so is anything still queued when disposal runs.
    /// </summary>
    /// <param name="action">The delivery to run.</param>
    public void Post(Action action)
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        if (InlineMode)
        {
            RunSafe(action);
            return;
        }

        if (Interlocked.Increment(ref queuedCount) > capacity)
        {
            // Drop the oldest delivery to make room; reliability's documented bounded-queue limit.
            if (queue.TryDequeue(out _))
                Interlocked.Decrement(ref queuedCount);

            onError(new InvalidOperationException("Delivery queue overflow."), "Delivery queue overflow; the oldest delivery was dropped.");
        }

        queue.Enqueue(action);

        if (NoireService.IsInitialized() && Interlocked.CompareExchange(ref attached, 1, 0) == 0)
            NoireService.Framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework) => Drain();

    /// <summary>
    /// Runs the deliveries queued as of entry, on the calling thread.
    /// </summary>
    internal void Drain()
    {
        // Drain only what was queued at entry so a handler that posts new work cannot starve the frame.
        var toDrain = Volatile.Read(ref queuedCount);

        while (toDrain-- > 0 && queue.TryDequeue(out var action))
        {
            Interlocked.Decrement(ref queuedCount);
            RunSafe(action);
        }
    }

    private void RunSafe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            onError(ex, "Unhandled exception in a networker delivery.");
        }
    }

    /// <summary>
    /// Detaches from the framework update and discards every delivery still queued.<br/>
    /// The backlog is dropped rather than drained: disposal happens while the networker is being torn down, and the
    /// queued deliveries describe a network it has already left. A delivery that must reach a consumer therefore
    /// cannot be posted here and then left to disposal; it has to be run before the pump is disposed.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        if (Interlocked.Exchange(ref attached, 0) == 1)
            NoireService.Framework.Update -= OnFrameworkUpdate;

        queue.Clear();
        queuedCount = 0;
    }
}
