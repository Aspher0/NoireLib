using NoireLib.Internal.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Delays action execution until a number of game frames has passed without new calls.<br/>
/// Prefer <see cref="FrameDebounceHelper"/> unless you need this directly; if you use it, remember to call
/// <see cref="Dispose"/>. The frame twin of <see cref="Debouncer"/>.
/// </summary>
public class FrameDebouncer : FrameTimingHelperBase
{
    /// <summary>
    /// Creates a new frame debouncer with the specified delay.
    /// </summary>
    /// <param name="frames">The number of game frames to wait before executing the action.</param>
    public FrameDebouncer(long frames) : base(frames) { }

    /// <summary>
    /// Debounces the specified action. If called multiple times, only the last call will execute after the delay period.
    /// </summary>
    /// <param name="action">The action to execute after the debounce delay.</param>
    public async Task DebounceAsync(Action action)
    {
        ThrowIfDisposed();

        if (action == null)
            throw new ArgumentNullException(nameof(action));

        if (!await WaitOutAsync().ConfigureAwait(false))
            return;

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

        if (!await WaitOutAsync().ConfigureAwait(false))
            return;

        await action();
    }

    /// <summary>
    /// Checks if there is a pending debounced action.
    /// </summary>
    /// <returns>True if an action is currently waiting to be executed, false otherwise.</returns>
    public bool IsPending()
    {
        return GetRemainingFrames() > 0;
    }

    /// <summary>
    /// Gets how many game frames are left before the debounced action will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled frame has passed; otherwise returns 0.</param>
    /// <returns>The remaining frames, or 0 if no action is pending (when allowNegative is false).</returns>
    public double GetRemainingFrames(bool allowNegative = false)
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return GetRemainingCore(allowNegative);
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

    /// <summary>
    /// Schedules this call, waits the interval out, and reports whether it is still the call that should run.
    /// </summary>
    /// <returns>True when no later call superseded this one.</returns>
    private async Task<bool> WaitOutAsync()
    {
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

        if (!await TryWaitAsync(currentCts))
            return false;

        await _lock.WaitAsync();
        try
        {
            if (!IsCurrentExecution(currentCts))
                return false;

            ClearScheduledExecution();
        }
        finally
        {
            _lock.Release();
        }

        return true;
    }
}
