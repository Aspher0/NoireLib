using System;
using System.Collections.Concurrent;

namespace NoireLib.Helpers;

/// <summary>
/// Keyed frame throttling: each key gets its own independent <see cref="FrameThrottler"/> instance. NoireLib must
/// be initialized before use. The frame twin of <see cref="ThrottleHelper"/>.
/// </summary>
public static class FrameThrottleHelper
{
    private static readonly ConcurrentDictionary<string, FrameThrottler> _throttlers = new();

    /// <summary>
    /// Throws an exception if the NoireLib is not initialized.
    /// </summary>
    static FrameThrottleHelper()
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib is not initialized. Please initialize NoireLib before using FrameThrottleHelper.");

        NoireLibMain.RegisterOnDispose("NoireLib_Internal_FrameThrottleHelper", Dispose);
    }

    /// <summary>
    /// Gets or creates a frame throttler for the specified key with the given interval.
    /// </summary>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="interval">The interval in game frames between executions for this key.</param>
    /// <returns>The throttler instance for the specified key.</returns>
    private static FrameThrottler GetOrCreateThrottler(string key, long interval)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (interval < 1)
            throw new ArgumentException("Interval must be at least one frame.", nameof(interval));

        var throttler = _throttlers.GetOrAdd(key, _ => new FrameThrottler(interval));

        if (throttler.GetInterval() != interval)
            throttler.SetInterval(interval);

        return throttler;
    }

    /// <summary>
    /// Throttles the specified function for a given key.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="interval">The interval in game frames between executions for this key.</param>
    /// <param name="func">The function to execute if the throttle interval has passed.</param>
    /// <param name="defaultValue">The default value to return if the function is throttled.</param>
    /// <returns>The function result if executed, or the default value if throttled.</returns>
    public static T? Throttle<T>(string key, long interval, Func<T> func, T? defaultValue = default)
    {
        var throttler = GetOrCreateThrottler(key, interval);
        return throttler.Throttle(func, defaultValue);
    }

    /// <summary>
    /// Throttles the specified action for a given key.
    /// </summary>
    /// <param name="key">The key to identify this throttle instance.</param>
    /// <param name="interval">The interval in game frames between executions for this key.</param>
    /// <param name="action">The action to execute if the throttle interval has passed.</param>
    /// <returns>True if the action was executed, false if it was throttled.</returns>
    public static bool Throttle(string key, long interval, Action action)
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
    /// <param name="interval">The new interval in game frames to check against.</param>
    /// <returns>True if the throttle interval has passed, false otherwise.</returns>
    public static bool IsAvailable(string key, long interval)
    {
        var throttler = GetOrCreateThrottler(key, interval);
        return throttler.IsAvailable();
    }

    /// <summary>
    /// Gets how many game frames are left before the throttler for the specified key will be available again.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="allowNegative">If true, allows negative values indicating how long ago the throttler became available.</param>
    /// <returns>The remaining frames, or 0 if the throttler is already available.</returns>
    public static double GetRemainingFrames(string key, bool allowNegative = false)
    {
        if (_throttlers.TryGetValue(key, out var throttler))
            return throttler.GetRemainingFrames(allowNegative);
        return 0;
    }

    /// <summary>
    /// Gets how many game frames are left before the throttler for the specified key will be available again.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="interval">The new interval in game frames to check against.</param>
    /// <param name="allowNegative">If true, allows negative values indicating how long ago the throttler became available.</param>
    /// <returns>The remaining frames, or 0 if the throttler is already available.</returns>
    public static double GetRemainingFrames(string key, long interval, bool allowNegative = false)
    {
        var throttler = GetOrCreateThrottler(key, interval);
        return throttler.GetRemainingFrames(allowNegative);
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
    /// Resets all frame throttlers, allowing all actions to execute immediately.
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
    /// Clears all frame throttler states and disposes them.
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
    /// Disposes the FrameThrottleHelper by clearing all throttler states.
    /// </summary>
    internal static void Dispose()
    {
        Clear();
    }
}
