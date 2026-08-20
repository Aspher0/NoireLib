using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Internal.Helpers;

/// <summary>
/// The shared substrate of the timing helpers: one lock, one scheduled execution, and the disposal rules. The unit
/// the interval is counted in is the derived class's business, so a tick here is a millisecond for
/// <see cref="TimeTimingHelperBase"/> and a game frame for <see cref="FrameTimingHelperBase"/>.
/// </summary>
public abstract class TimingHelperBase : IDisposable
{
    /// <summary>
    /// A semaphore used for thread-safe operations.
    /// </summary>
    protected readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// A flag indicating whether the instance has been disposed.
    /// </summary>
    protected bool _disposed;

    /// <summary>
    /// A CancellationTokenSource used for managing scheduled executions.
    /// </summary>
    protected CancellationTokenSource? _cts;

    /// <summary>
    /// The tick the scheduled execution is due at, in this helper's own unit. Zero when nothing is scheduled.
    /// </summary>
    protected long _scheduledExecution = 0;

    /// <summary>
    /// The current tick, in this helper's own unit.
    /// </summary>
    protected abstract long CurrentTick { get; }

    /// <summary>
    /// The configured interval, in this helper's own unit.
    /// </summary>
    protected abstract long IntervalTicks { get; }

    /// <summary>
    /// Waits out one interval, resolving false when the wait was cancelled.
    /// </summary>
    /// <param name="cts">The CancellationTokenSource to use for cancellation.</param>
    /// <returns>True if the wait completed without cancellation, false if cancelled.</returns>
    protected abstract Task<bool> TryWaitAsync(CancellationTokenSource cts);

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if this instance has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Creates a new CancellationTokenSource and schedules execution. Must be called within a lock.
    /// </summary>
    /// <returns>The newly created CancellationTokenSource.</returns>
    protected CancellationTokenSource CreateNewScheduledExecution()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        _cts = new CancellationTokenSource();
        _scheduledExecution = CurrentTick + IntervalTicks;
        return _cts;
    }

    /// <summary>
    /// Cancels and disposes the current CancellationTokenSource. Must be called within a lock.
    /// </summary>
    protected void CancelCurrentExecution()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _scheduledExecution = 0;
    }

    /// <summary>
    /// Clears the scheduled execution tick. Must be called within a lock.
    /// </summary>
    protected void ClearScheduledExecution()
    {
        _scheduledExecution = 0;
    }

    /// <summary>
    /// Checks if the given CancellationTokenSource is still valid (matches current and not cancelled). Must be called within a lock.
    /// </summary>
    /// <param name="cts">The CancellationTokenSource to check.</param>
    /// <returns>True when it is still the scheduled execution.</returns>
    protected bool IsCurrentExecution(CancellationTokenSource cts)
    {
        return cts == _cts && !cts.IsCancellationRequested;
    }

    /// <summary>
    /// Gets how much of the interval is left before the scheduled execution, in this helper's own unit.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled tick has passed; otherwise returns 0.</param>
    /// <returns>The remaining amount.</returns>
    protected double GetRemainingCore(bool allowNegative = false)
    {
        if (_cts == null || _cts.IsCancellationRequested || _scheduledExecution == 0)
            return 0;

        var remaining = _scheduledExecution - CurrentTick;
        return allowNegative ? remaining : Math.Max(0, remaining);
    }

    /// <summary>
    /// Disposes the timing helper and releases resources.
    /// </summary>
    public abstract void Dispose();
}
