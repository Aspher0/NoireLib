using NoireLib.Helpers.ObjectExtensions;
using NoireLib.Internal.Helpers;
using System;

namespace NoireLib.Helpers;

/// <summary>
/// Limits how often an action runs, measured in game frames rather than in wall-clock time.<br/>
/// Prefer <see cref="FrameThrottleHelper"/> unless you need this directly; if you use it, remember to call
/// <see cref="Dispose"/>. For a render path that owns its own frame counter and must not take a lock, use the
/// static <see cref="TryPass"/> and <see cref="HasElapsed"/> instead of an instance.
/// </summary>
public class FrameThrottler : FrameTimingHelperBase
{
    /// <summary>
    /// The "has not run yet" value for a last-run field. Passing it always reports elapsed.
    /// </summary>
    /// <remarks>
    /// Give a never-run field this value rather than zero, and never subtract it: <c>currentFrame - long.MinValue</c>
    /// overflows to a large negative number that reads as "still throttled", which wedges the throttled work off for
    /// the whole session. Both static methods here handle it, so a caller that stores <see cref="Never"/> is safe.
    /// </remarks>
    public const long Never = long.MinValue;

    private long _lastExecutionFrame = Never;

    /// <summary>
    /// Creates a new frame throttler with the specified interval.
    /// </summary>
    /// <param name="interval">The minimum number of game frames between action executions.</param>
    public FrameThrottler(long interval) : base(interval) { }

    /// <summary>
    /// Whether enough frames have passed since the last run, against a frame number the caller owns.
    /// </summary>
    /// <param name="currentFrame">The current frame number.</param>
    /// <param name="lastFrame">The frame the work last ran on, or <see cref="Never"/>.</param>
    /// <param name="interval">Minimum frames between runs.</param>
    /// <returns>True when the work is due.</returns>
    public static bool HasElapsed(long currentFrame, long lastFrame, long interval)
        => lastFrame == Never || currentFrame - lastFrame >= interval;

    /// <summary>
    /// Whether the work is due against a frame number the caller owns, advancing the last-run field when it is.
    /// A pure predicate holding no state and needing no initialized library, so it costs an integer compare and
    /// is safe to call from a detour on any thread.
    /// </summary>
    /// <param name="currentFrame">The current frame number.</param>
    /// <param name="lastFrame">The caller's last-run field; set to <paramref name="currentFrame"/> on success.</param>
    /// <param name="interval">Minimum frames between runs.</param>
    /// <returns>True when the work is due.</returns>
    public static bool TryPass(long currentFrame, ref long lastFrame, long interval)
    {
        if (!HasElapsed(currentFrame, lastFrame, interval))
            return false;

        lastFrame = currentFrame;
        return true;
    }

    /// <summary>
    /// Throttles the specified action. If called multiple times within the interval, only the first call executes.
    /// </summary>
    /// <param name="action">The action to execute if the throttle interval has passed.</param>
    /// <returns>True if the action was executed, false if it was throttled.</returns>
    public bool Throttle(Action action)
    {
        ThrowIfDisposed();

        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var shouldExecute = TakeSlot();

        if (shouldExecute)
        {
            // Execute outside the lock to avoid re-entrancy deadlocks and long lock holds
            action();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Throttles the specified function and returns its result. If throttled, returns the default value.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute if the throttle interval has passed.</param>
    /// <param name="defaultValue">The default value to return if the function is throttled.</param>
    /// <returns>The function result if executed, or the default value if throttled.</returns>
    public T? Throttle<T>(Func<T> func, T? defaultValue = default)
    {
        ThrowIfDisposed();

        func.ThrowIfNull(nameof(func));

        return TakeSlot() ? func() : defaultValue;
    }

    /// <summary>
    /// Checks if the throttler is available to execute an action.
    /// </summary>
    /// <returns>True if the throttle interval has passed and an action can be executed, false otherwise.</returns>
    public bool IsAvailable()
    {
        return GetRemainingFrames() <= 0;
    }

    /// <summary>
    /// Gets how many game frames are left before the throttler will be available again.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values indicating how long ago the throttler became available.</param>
    /// <returns>The remaining frames, or 0 if the throttler is already available.</returns>
    public double GetRemainingFrames(bool allowNegative = false)
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            if (_lastExecutionFrame == Never)
                return allowNegative ? -_frames : 0;

            var remaining = _frames - (CurrentTick - _lastExecutionFrame);
            return allowNegative ? remaining : Math.Max(0, remaining);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the current interval in game frames.
    /// </summary>
    /// <returns>The current throttle interval.</returns>
    public long GetInterval()
    {
        return GetFrames();
    }

    /// <summary>
    /// Sets a new interval for the throttler.
    /// </summary>
    /// <param name="interval">The new interval in game frames.</param>
    /// <exception cref="ArgumentException">Thrown when the interval is less than one frame.</exception>
    public void SetInterval(long interval)
    {
        SetFrames(interval);
    }

    /// <summary>
    /// Resets the throttler, allowing the next action to execute immediately.
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            _lastExecutionFrame = Never;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Disposes the throttler and releases resources.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // FrameThrottler doesn't need additional cleanup beyond marking as disposed
        // Do not dispose the SemaphoreSlim to avoid racing with in-flight Release calls.
    }

    private bool TakeSlot()
    {
        _lock.Wait();
        try
        {
            return TryPass(CurrentTick, ref _lastExecutionFrame, _frames);
        }
        finally
        {
            _lock.Release();
        }
    }
}
