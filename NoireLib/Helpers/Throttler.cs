using System;
using System.Threading;

namespace NoireLib.Helpers;

/// <summary>
/// Provides throttle functionality to limit the rate at which an action can be executed.
/// </summary>
public class Throttler
{
    private int _intervalMilliseconds;
    private long _lastExecutionMs = 0;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a new throttler with the specified interval.
    /// </summary>
    /// <param name="intervalMilliseconds">The minimum interval in milliseconds between action executions.</param>
    public Throttler(int intervalMilliseconds)
    {
        if (intervalMilliseconds <= 0)
            throw new ArgumentException("Interval must be greater than zero.", nameof(intervalMilliseconds));

        _intervalMilliseconds = intervalMilliseconds;
    }

    /// <summary>
    /// Throttles the specified action. If called multiple times within the interval, only the first call executes.
    /// </summary>
    /// <param name="action">The action to execute if the throttle interval has passed.</param>
    /// <returns>True if the action was executed, false if it was throttled.</returns>
    public bool Throttle(Action action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        bool shouldExecute = false;

        _lock.Wait();
        try
        {
            var now = Environment.TickCount64;
            var timeSinceLastExecution = now - _lastExecutionMs;

            if (timeSinceLastExecution >= _intervalMilliseconds)
            {
                _lastExecutionMs = now;
                shouldExecute = true;
            }
        }
        finally
        {
            _lock.Release();
        }

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
        if (func == null)
            throw new ArgumentNullException(nameof(func));

        bool shouldExecute = false;

        _lock.Wait();
        try
        {
            var now = Environment.TickCount64;
            var timeSinceLastExecution = now - _lastExecutionMs;

            if (timeSinceLastExecution >= _intervalMilliseconds)
            {
                _lastExecutionMs = now;
                shouldExecute = true;
            }
        }
        finally
        {
            _lock.Release();
        }

        return shouldExecute ? func() : defaultValue;
    }

    /// <summary>
    /// Checks if the throttler is available to execute an action.
    /// </summary>
    /// <returns>True if the throttle interval has passed and an action can be executed, false otherwise.</returns>
    public bool IsAvailable()
    {
        return GetRemainingTime() <= 0;
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the throttler will be available again.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values indicating how long ago the throttler became available.</param>
    /// <returns>The remaining time in milliseconds, or 0 if the throttler is already available.</returns>
    public double GetRemainingTime(bool allowNegative = false)
    {
        _lock.Wait();
        try
        {
            var now = Environment.TickCount64;
            var timeSinceLastExecution = now - _lastExecutionMs;
            var remaining = _intervalMilliseconds - timeSinceLastExecution;
            return allowNegative ? remaining : Math.Max(0, remaining);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the current interval in milliseconds.
    /// </summary>
    /// <returns>The current throttle interval in milliseconds.</returns>
    public int GetInterval()
    {
        _lock.Wait();
        try
        {
            return _intervalMilliseconds;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sets a new interval for the throttler.
    /// </summary>
    /// <param name="intervalMilliseconds">The new interval in milliseconds.</param>
    /// <exception cref="ArgumentException">Thrown when interval is less than or equal to zero.</exception>
    public void SetInterval(int intervalMilliseconds)
    {
        if (intervalMilliseconds <= 0)
            throw new ArgumentException("Interval must be greater than zero.", nameof(intervalMilliseconds));

        _lock.Wait();
        try
        {
            _intervalMilliseconds = intervalMilliseconds;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Resets the throttler, allowing the next action to execute immediately.
    /// </summary>
    public void Reset()
    {
        _lock.Wait();
        try
        {
            _lastExecutionMs = 0;
        }
        finally
        {
            _lock.Release();
        }
    }
}
