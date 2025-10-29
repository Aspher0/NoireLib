using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Provides debounce functionality to delay action execution until a specified time has passed without new calls.
/// </summary>
public class Debouncer : IDisposable
{
    private int _delayMilliseconds;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;
    private long _scheduledExecutionMs = 0;

    /// <summary>
    /// Creates a new debouncer with the specified delay.
    /// </summary>
    /// <param name="delayMilliseconds">The delay in milliseconds to wait before executing the action.</param>
    public Debouncer(int delayMilliseconds)
    {
        if (delayMilliseconds <= 0)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delayMilliseconds));

        _delayMilliseconds = delayMilliseconds;
    }

    /// <summary>
    /// Debounces the specified action. If called multiple times, only the last call will execute after the delay period.
    /// </summary>
    /// <param name="action">The action to execute after the debounce delay.</param>
    public async Task DebounceAsync(Action action)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Debouncer));

        if (action == null)
            throw new ArgumentNullException(nameof(action));

        CancellationTokenSource currentCts;

        await _lock.WaitAsync();
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();
            currentCts = _cts;
            _scheduledExecutionMs = Environment.TickCount64 + _delayMilliseconds;
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            await Task.Delay(_delayMilliseconds, currentCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when debounce is cancelled or superseded
            return;
        }

        await _lock.WaitAsync();
        try
        {
            if (currentCts != _cts || currentCts.IsCancellationRequested)
                return;

            _scheduledExecutionMs = 0;
        }
        finally
        {
            _lock.Release();
        }

        action();
    }

    /// <summary>
    /// Debounces the specified asynchronous function.
    /// </summary>
    /// <param name="action">The asynchronous action to execute after the debounce delay.</param>
    public async Task DebounceAsync(Func<Task> action)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Debouncer));

        if (action == null)
            throw new ArgumentNullException(nameof(action));

        CancellationTokenSource currentCts;

        await _lock.WaitAsync();
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();
            currentCts = _cts;
            _scheduledExecutionMs = Environment.TickCount64 + _delayMilliseconds;
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            await Task.Delay(_delayMilliseconds, currentCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when debounce is cancelled or superseded
            return;
        }

        await _lock.WaitAsync();
        try
        {
            if (currentCts != _cts || currentCts.IsCancellationRequested)
                return;

            _scheduledExecutionMs = 0;
        }
        finally
        {
            _lock.Release();
        }

        await action();
    }

    /// <summary>
    /// Checks if there is a pending debounced action.
    /// </summary>
    /// <returns>True if an action is currently waiting to be executed, false otherwise.</returns>
    public bool IsPending()
    {
        return GetRemainingTime() > 0;
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the debounced action will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if no action is pending (when allowNegative is false).</returns>
    public double GetRemainingTime(bool allowNegative = false)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Debouncer));

        _lock.Wait();
        try
        {
            if (_cts == null || _cts.IsCancellationRequested || _scheduledExecutionMs == 0)
                return 0;

            var now = Environment.TickCount64;
            var remaining = _scheduledExecutionMs - now;
            return allowNegative ? remaining : Math.Max(0, remaining);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the current delay in milliseconds.
    /// </summary>
    /// <returns>The current debounce delay in milliseconds.</returns>
    public int GetDelay()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Debouncer));

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
    /// Sets a new delay for the debouncer. This does not affect any currently pending action.
    /// </summary>
    /// <param name="delayMilliseconds">The new delay in milliseconds.</param>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public void SetDelay(int delayMilliseconds)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Debouncer));

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
    /// Cancels any pending debounced action.
    /// </summary>
    public void Cancel()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Debouncer));

        _lock.Wait();
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _scheduledExecutionMs = 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Disposes the debouncer and cancels any pending actions.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Clean up under the lock to avoid racing with in-flight operations.
        _lock.Wait();
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _scheduledExecutionMs = 0;
        }
        finally
        {
            _lock.Release();
        }

        // Do not dispose the SemaphoreSlim to avoid racing with in-flight Release calls.
    }
}
