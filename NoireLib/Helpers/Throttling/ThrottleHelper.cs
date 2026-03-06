using System;
using System.Collections.Concurrent;

namespace NoireLib.Helpers;

/// <summary>
/// Provides keyed throttling functionality, allowing independent throttling for different operations.<br/>
/// Each key maintains its own Throttler instance, preventing interference between different operations.<br/>
/// Provides easy-to-use static methods to throttle actions based on unique keys.<br/>
/// NoireLib must be initialized before using this helper.<br/>
/// See <see cref="Throttler"/> for more details.
/// </summary>
public static class ThrottleHelper
{
    private static readonly ConcurrentDictionary<string, Throttler> _throttlers = new();

    /// <summary>
    /// Throws an exception if the NoireLib is not initialized.
    /// </summary>
    static ThrottleHelper()
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib is not initialized. Please initialize NoireLib before using ThrottleHelper.");

        NoireLibMain.RegisterOnDispose("NoireLib_Internal_ThrottleHelper", Dispose);
    }

    /// <summary>
    /// Gets or creates a throttler for the specified key with the given interval.
    /// </summary>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="interval">The interval between executions for this key.</param>
    /// <returns>The throttler instance for the specified key.</returns>
    private static Throttler GetOrCreateThrottler(string key, TimeSpan interval)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (interval <= TimeSpan.Zero)
            throw new ArgumentException("Interval must be greater than zero.", nameof(interval));

        var throttler = _throttlers.GetOrAdd(key, _ => new Throttler(interval));

        if (throttler.GetInterval() != interval)
            throttler.SetInterval(interval);

        return throttler;
    }

    /// <summary>
    /// Throttles the specified function for a given key. Each key has independent throttling.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="interval">The interval between executions for this key.</param>
    /// <param name="func">The function to execute if the throttle interval has passed.</param>
    /// <param name="defaultValue">The default value to return if the function is throttled.</param>
    /// <returns>The function result if executed, or the default value if throttled.</returns>
    public static T? Throttle<T>(string key, TimeSpan interval, Func<T> func, T? defaultValue = default)
    {
        var throttler = GetOrCreateThrottler(key, interval);
        return throttler.Throttle(func, defaultValue);
    }

    /// <summary>
    /// Throttles the specified action for a given key. Each key has independent throttling.
    /// </summary>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="interval">The interval between executions for this key.</param>
    /// <param name="action">The action to execute if the throttle interval has passed.</param>
    /// <returns>True if the action was executed, false if it was throttled.</returns>
    public static bool Throttle(string key, TimeSpan interval, Action action)
    {
        var throttler = GetOrCreateThrottler(key, interval);
        return throttler.Throttle(action);
    }

    /// <summary>
    /// Checks if the throttler for the specified key is available to execute an action.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the throttle interval has passed, false otherwise.</returns>
    public static bool IsAvailable(string key)
    {
        if (_throttlers.TryGetValue(key, out var throttler))
            return throttler.IsAvailable();
        return true;
    }

    /// <summary>
    /// Checks if the throttler for the specified key is available to execute an action.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="interval">The new interval to check against.</param>
    /// <returns>True if the throttle interval has passed, false otherwise.</returns>
    public static bool IsAvailable(string key, TimeSpan interval)
    {
        var throttler = GetOrCreateThrottler(key, interval);
        return throttler.IsAvailable();
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the throttler for the specified key will be available again.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="allowNegative">If true, allows negative values indicating how long ago the throttler became available.</param>
    /// <returns>The remaining time in milliseconds, or 0 if the throttler is already available.</returns>
    public static double GetRemainingTime(string key, bool allowNegative = false)
    {
        if (_throttlers.TryGetValue(key, out var throttler))
            return throttler.GetRemainingTime(allowNegative);
        return 0;
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the throttler for the specified key will be available again.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="interval">The new interval to check against.</param>
    /// <param name="allowNegative">If true, allows negative values indicating how long ago the throttler became available.</param>
    /// <returns>The remaining time in milliseconds, or 0 if the throttler is already available.</returns>
    public static double GetRemainingTime(string key, TimeSpan interval, bool allowNegative = false)
    {
        var throttler = GetOrCreateThrottler(key, interval);
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

    /// <summary>
    /// Disposes the ThrottleHelper by clearing all throttler states.
    /// </summary>
    internal static void Dispose()
    {
        Clear();
    }
}
