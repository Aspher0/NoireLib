using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Internal.Helpers;

/// <summary>
/// Base class for timing-related helpers that provides common functionality for delay management and thread safety.
/// </summary>
public abstract class TimingHelperBase : IDisposable
{
    /// <summary>
    /// The delay in milliseconds associated with the timing helper.
    /// </summary>
    protected int _delayMilliseconds;

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
    /// The scheduled execution time in milliseconds since epoch.
    /// </summary>
    protected long _scheduledExecutionMs = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimingHelperBase"/> class.
    /// </summary>
    /// <param name="delayMilliseconds">The delay in milliseconds.</param>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    protected TimingHelperBase(int delayMilliseconds)
    {
        if (delayMilliseconds <= 0)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delayMilliseconds));

        _delayMilliseconds = delayMilliseconds;
    }

    /// <summary>
    /// Gets the current delay in milliseconds.
    /// </summary>
    /// <returns>The current delay in milliseconds.</returns>
    public int GetDelay()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return _delayMilliseconds;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets a new delay. This does not affect any currently running operation.
    /// </summary>
    /// <param name="delayMilliseconds">The new delay in milliseconds.</param>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public void SetDelay(int delayMilliseconds)
    {
        ThrowIfDisposed();

        if (delayMilliseconds <= 0)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delayMilliseconds));

        _lock.Wait();
        try
        {
            _delayMilliseconds = delayMilliseconds;
        }
        finally
        {
            _lock.Release();
        }
    }

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
        _scheduledExecutionMs = Environment.TickCount64 + _delayMilliseconds;
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
        _scheduledExecutionMs = 0;
    }

    /// <summary>
    /// Clears the scheduled execution time. Must be called within a lock.
    /// </summary>
    protected void ClearScheduledExecution()
    {
        _scheduledExecutionMs = 0;
    }

    /// <summary>
    /// Checks if the given CancellationTokenSource is still valid (matches current and not cancelled). Must be called within a lock.
    /// </summary>
    protected bool IsCurrentExecution(CancellationTokenSource cts)
    {
        return cts == _cts && !cts.IsCancellationRequested;
    }

    /// <summary>
    /// Awaits a delay with cancellation support and handles the OperationCanceledException.
    /// </summary>
    /// <param name="cts">The CancellationTokenSource to use for cancellation.</param>
    /// <returns>True if the delay completed without cancellation, false if cancelled.</returns>
    protected async Task<bool> TryDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_delayMilliseconds, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before scheduled execution.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds.</returns>
    protected double GetRemainingTimeCore(bool allowNegative = false)
    {
        if (_cts == null || _cts.IsCancellationRequested || _scheduledExecutionMs == 0)
            return 0;

        var now = Environment.TickCount64;
        var remaining = _scheduledExecutionMs - now;
        return allowNegative ? remaining : Math.Max(0, remaining);
    }

    /// <summary>
    /// Disposes the timing helper and releases resources.
    /// </summary>
    public abstract void Dispose();
}
