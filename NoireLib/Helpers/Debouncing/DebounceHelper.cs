using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Provides keyed debouncing functionality, allowing independent debouncing for different operations.<br/>
/// Each key maintains its own Debouncer instance, preventing interference between different operations.<br/>
/// Provides easy-to-use static methods to debounce actions based on unique keys.<br/>
/// NoireLib must be initialized before using this helper.<br/>
/// See <see cref="Debouncer"/> for more details.
/// </summary>
public static class DebounceHelper
{
    private static readonly ConcurrentDictionary<string, Debouncer> _debouncers = new();

    /// <summary>
    /// Throws an exception if the NoireLib is not initialized.
    /// </summary>
    static DebounceHelper()
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib is not initialized. Please initialize NoireLib before using DebounceHelper.");

        NoireLibMain.RegisterOnDispose("NoireLib_Internal_DebounceHelper", Dispose);
    }

    /// <summary>
    /// Gets or creates a debouncer for the specified key with the given delay.
    /// </summary>
    /// <param name="key">The key to identify this debounce instance.</param>
    /// <param name="delay">The delay to wait before executing the action.</param>
    /// <returns>The debouncer instance for the specified key.</returns>
    private static Debouncer GetOrCreateDebouncer(string key, TimeSpan delay)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (delay <= TimeSpan.Zero)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delay));

        var debouncer = _debouncers.GetOrAdd(key, _ => new Debouncer(delay));

        if (debouncer.GetDelay() != delay)
            debouncer.SetDelay(delay);

        return debouncer;
    }

    /// <summary>
    /// Debounces the specified action for a given key. Each key has independent debouncing.
    /// If called multiple times, only the last call will execute after the delay period.
    /// </summary>
    /// <param name="key">The key to identify this debounce instance.</param>
    /// <param name="delay">The delay to wait before executing the action.</param>
    /// <param name="action">The action to execute after the debounce delay.</param>
    public static async Task DebounceAsync(string key, TimeSpan delay, Action action)
    {
        var debouncer = GetOrCreateDebouncer(key, delay);
        await debouncer.DebounceAsync(action);
    }

    /// <summary>
    /// Debounces the specified asynchronous function for a given key. Each key has independent debouncing.
    /// If called multiple times, only the last call will execute after the delay period.
    /// </summary>
    /// <param name="key">The key to identify this debounce instance.</param>
    /// <param name="delay">The delay to wait before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the debounce delay.</param>
    public static async Task DebounceAsync(string key, TimeSpan delay, Func<Task> action)
    {
        var debouncer = GetOrCreateDebouncer(key, delay);
        await debouncer.DebounceAsync(action);
    }

    /// <summary>
    /// Checks if there is a pending debounced action for the specified key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="delay">The delay to check against.</param>
    /// <returns>True if an action is currently waiting to be executed, false otherwise.</returns>
    public static bool IsPending(string key, TimeSpan delay)
    {
        var debouncer = GetOrCreateDebouncer(key, delay);
        return debouncer.IsPending();
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the debounced action for the specified key will execute.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="delay">The delay to check against.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if no action is pending (when allowNegative is false).</returns>
    public static double GetRemainingTime(string key, TimeSpan delay, bool allowNegative = false)
    {
        var debouncer = GetOrCreateDebouncer(key, delay);
        return debouncer.GetRemainingTime(allowNegative);
    }

    /// <summary>
    /// Cancels any pending debounced action for the specified key.
    /// </summary>
    /// <param name="key">The key to cancel.</param>
    public static void Cancel(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_debouncers.TryGetValue(key, out var debouncer))
        {
            debouncer.Cancel();
        }
    }

    /// <summary>
    /// Cancels all pending debounced actions.
    /// </summary>
    public static void CancelAll()
    {
        foreach (var kvp in _debouncers)
        {
            kvp.Value.Cancel();
        }
    }

    /// <summary>
    /// Removes the debouncer state for the specified key and disposes it.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    public static void Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_debouncers.TryRemove(key, out var debouncer))
        {
            debouncer.Dispose();
        }
    }

    /// <summary>
    /// Clears all debouncer states and disposes them.
    /// </summary>
    public static void Clear()
    {
        foreach (var kvp in _debouncers)
        {
            kvp.Value.Dispose();
        }
        _debouncers.Clear();
    }

    /// <summary>
    /// Disposes all debouncer states and clears them.
    /// </summary>
    internal static void Dispose()
    {
        Clear();
    }
}
