using NoireLib.Internal.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Provides debounce functionality to delay action execution until a specified time has passed without new calls.
/// </summary>
public class Debouncer : TimingHelperBase
{
    /// <summary>
    /// Creates a new debouncer with the specified delay.
    /// </summary>
    /// <param name="delayMilliseconds">The delay in milliseconds to wait before executing the action.</param>
    public Debouncer(int delayMilliseconds) : base(delayMilliseconds) { }

    /// <summary>
    /// Debounces the specified action. If called multiple times, only the last call will execute after the delay period.
    /// </summary>
    /// <param name="action">The action to execute after the debounce delay.</param>
    public async Task DebounceAsync(Action action)
    {
        ThrowIfDisposed();

        if (action == null)
            throw new ArgumentNullException(nameof(action));

        CancellationTokenSource currentCts;

        await _lock.WaitAsync();
        try
        {
            currentCts = CreateNewScheduledExecution();
        }
        finally
        {
            _lock.Release();
        }

        if (!await TryDelayAsync(currentCts))
            return;

        await _lock.WaitAsync();
        try
        {
            if (!IsCurrentExecution(currentCts))
                return;

            ClearScheduledExecution();
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
        ThrowIfDisposed();

        if (action == null)
            throw new ArgumentNullException(nameof(action));

        CancellationTokenSource currentCts;

        await _lock.WaitAsync();
        try
        {
            currentCts = CreateNewScheduledExecution();
        }
        finally
        {
            _lock.Release();
        }

        if (!await TryDelayAsync(currentCts))
            return;

        await _lock.WaitAsync();
        try
        {
            if (!IsCurrentExecution(currentCts))
                return;

            ClearScheduledExecution();
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
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return GetRemainingTimeCore(allowNegative);
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
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            CancelCurrentExecution();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Disposes the debouncer and cancels any pending actions.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _lock.Wait();
        try
        {
            CancelCurrentExecution();
        }
        finally
        {
            _lock.Release();
        }
    }
}
