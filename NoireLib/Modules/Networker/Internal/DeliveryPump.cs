using Dalamud.Plugin.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.Networker.Internal;

/// <summary>
/// The single ordered delivery queue of a networker: peer mutations, handler invocations, and request completions
/// all pass through it, giving the "everything user-visible happens on the framework thread" guarantee.<br/>
/// Bounded — a long-frozen instance drops its oldest deliveries with an error report instead of growing memory.<br/>
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

    public bool InlineMode => !NoireService.IsInitialized();

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

        if (Interlocked.CompareExchange(ref attached, 1, 0) == 0)
            NoireService.Framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Drain only what was queued at tick start so a handler that posts new work cannot starve the frame.
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
