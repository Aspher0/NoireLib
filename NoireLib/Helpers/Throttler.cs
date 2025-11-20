using NoireLib.Helpers.ObjectExtensions;
using NoireLib.Internal.Helpers;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NoireLib.Helpers;

/// <summary>
/// Provides throttle functionality to limit the rate at which an action can be executed.
/// </summary>
public class Throttler : TimingHelperBase
{
    private long _lastExecutionMs = 0;

    /// <summary>
    /// Creates a new throttler with the specified interval.
    /// </summary>
    /// <param name="intervalMilliseconds">The minimum interval in milliseconds between action executions.</param>
    public Throttler(int intervalMilliseconds) : base(intervalMilliseconds) { }

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

        bool shouldExecute = false;

        _lock.Wait();
        try
        {
            var now = Environment.TickCount64;
            var timeSinceLastExecution = now - _lastExecutionMs;

            if (timeSinceLastExecution >= _delayMilliseconds)
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
        ThrowIfDisposed();

        func.ThrowIfNull(nameof(func));

        bool shouldExecute = false;

        _lock.Wait();
        try
        {
            var now = Environment.TickCount64;
            var timeSinceLastExecution = now - _lastExecutionMs;

            if (timeSinceLastExecution >= _delayMilliseconds)
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
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            var now = Environment.TickCount64;
            var timeSinceLastExecution = now - _lastExecutionMs;
            var remaining = _delayMilliseconds - timeSinceLastExecution;
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
        return GetDelay();
    }

    /// <summary>
    /// Sets a new interval for the throttler.
    /// </summary>
    /// <param name="intervalMilliseconds">The new interval in milliseconds.</param>
    /// <exception cref="ArgumentException">Thrown when interval is less than or equal to zero.</exception>
    public void SetInterval(int intervalMilliseconds)
    {
        SetDelay(intervalMilliseconds);
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
            _lastExecutionMs = 0;
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

        // Throttler doesn't need additional cleanup beyond marking as disposed
        // Do not dispose the SemaphoreSlim to avoid racing with in-flight Release calls.
    }
}

/// <summary>
/// Provides keyed throttling functionality, allowing independent throttling for different operations.
/// Each key maintains its own Throttler instance, preventing interference between different operations.
/// </summary>
public static class KeyedThrottler
{
    private static readonly ConcurrentDictionary<string, Throttler> _throttlers = new();

    /// <summary>
    /// Gets or creates a throttler for the specified key with the given interval.
    /// </summary>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="intervalMilliseconds">The interval in milliseconds between executions for this key.</param>
    /// <returns>The throttler instance for the specified key.</returns>
    private static Throttler GetOrCreateThrottler(string key, int intervalMilliseconds)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (intervalMilliseconds <= 0)
            throw new ArgumentException("Interval must be greater than zero.", nameof(intervalMilliseconds));

        var throttler = _throttlers.GetOrAdd(key, _ => new Throttler(intervalMilliseconds));
        
        // Update interval if it's different (allows dynamic interval changes per key)
        if (throttler.GetInterval() != intervalMilliseconds)
            throttler.SetInterval(intervalMilliseconds);

        return throttler;
    }

    /// <summary>
    /// Throttles the specified function for a given key. Each key has independent throttling.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="func">The function to execute if the throttle interval has passed.</param>
    /// <param name="intervalMilliseconds">The interval in milliseconds between executions for this key.</param>
    /// <param name="defaultValue">The default value to return if the function is throttled.</param>
    /// <returns>The function result if executed, or the default value if throttled.</returns>
    public static T? Throttle<T>(string key, Func<T> func, int intervalMilliseconds, T? defaultValue = default)
    {
        var throttler = GetOrCreateThrottler(key, intervalMilliseconds);
        return throttler.Throttle(func, defaultValue);
    }

    /// <summary>
    /// Throttles the specified action for a given key. Each key has independent throttling.
    /// </summary>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="action">The action to execute if the throttle interval has passed.</param>
    /// <param name="intervalMilliseconds">The interval in milliseconds between executions for this key.</param>
    /// <returns>True if the action was executed, false if it was throttled.</returns>
    public static bool Throttle(string key, Action action, int intervalMilliseconds)
    {
        var throttler = GetOrCreateThrottler(key, intervalMilliseconds);
        return throttler.Throttle(action);
    }

    /// <summary>
    /// Checks if the throttler for the specified key is available to execute an action.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="intervalMilliseconds">The interval in milliseconds to check against.</param>
    /// <returns>True if the throttle interval has passed, false otherwise.</returns>
    public static bool IsAvailable(string key, int intervalMilliseconds)
    {
        var throttler = GetOrCreateThrottler(key, intervalMilliseconds);
        return throttler.IsAvailable();
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the throttler for the specified key will be available again.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="intervalMilliseconds">The interval in milliseconds to check against.</param>
    /// <param name="allowNegative">If true, allows negative values indicating how long ago the throttler became available.</param>
    /// <returns>The remaining time in milliseconds, or 0 if the throttler is already available.</returns>
    public static double GetRemainingTime(string key, int intervalMilliseconds, bool allowNegative = false)
    {
        var throttler = GetOrCreateThrottler(key, intervalMilliseconds);
        return throttler.GetRemainingTime(allowNegative);
    }

    /// <summary>
    /// Resets the throttler for the specified key, allowing the next action to execute immediately.
    /// </summary>
    /// <param name="key">The key to reset.</param>
    public static void Reset(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_throttlers.TryGetValue(key, out var throttler))
        {
            throttler.Reset();
        }
    }

    /// <summary>
    /// Resets all throttlers, allowing all actions to execute immediately.
    /// </summary>
    public static void ResetAll()
    {
        foreach (var kvp in _throttlers)
        {
            kvp.Value.Reset();
        }
    }

    /// <summary>
    /// Removes the throttler state for the specified key and disposes it.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    public static void Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_throttlers.TryRemove(key, out var throttler))
        {
            throttler.Dispose();
        }
    }

    /// <summary>
    /// Clears all throttler states and disposes them.
    /// </summary>
    public static void Clear()
    {
        foreach (var kvp in _throttlers)
        {
            kvp.Value.Dispose();
        }
        _throttlers.Clear();
    }
}
