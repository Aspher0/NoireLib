using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Keyed frame debouncing: each key gets its own independent <see cref="FrameDebouncer"/> instance. NoireLib must
/// be initialized before use. The frame twin of <see cref="DebounceHelper"/>.
/// </summary>
public static class FrameDebounceHelper
{
    private static readonly ConcurrentDictionary<string, FrameDebouncer> _debouncers = new();

    /// <summary>
    /// Throws an exception if the NoireLib is not initialized.
    /// </summary>
    static FrameDebounceHelper()
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib is not initialized. Please initialize NoireLib before using FrameDebounceHelper.");

        NoireLibMain.RegisterOnDispose("NoireLib_Internal_FrameDebounceHelper", Dispose);
    }

    /// <summary>
    /// Gets or creates a frame debouncer for the specified key with the given delay.
    /// </summary>
    /// <param name="key">The key to identify this debounce instance.</param>
    /// <param name="frames">The number of game frames to wait before executing the action.</param>
    /// <returns>The debouncer instance for the specified key.</returns>
    private static FrameDebouncer GetOrCreateDebouncer(string key, long frames)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (frames < 1)
            throw new ArgumentException("Frame count must be at least one.", nameof(frames));

        var debouncer = _debouncers.GetOrAdd(key, _ => new FrameDebouncer(frames));

        if (debouncer.GetFrames() != frames)
            debouncer.SetFrames(frames);

        return debouncer;
    }

    /// <summary>
    /// Debounces the specified action for a given key. If called multiple times, only the last call executes
    /// after the delay period.
    /// </summary>
    /// <param name="key">The key to identify this debounce instance.</param>
    /// <param name="frames">The number of game frames to wait before executing the action.</param>
    /// <param name="action">The action to execute after the debounce delay.</param>
    public static async Task DebounceAsync(string key, long frames, Action action)
    {
        var debouncer = GetOrCreateDebouncer(key, frames);
        await debouncer.DebounceAsync(action);
    }

    /// <summary>
    /// Debounces the specified asynchronous function for a given key. If called multiple times, only the last call
    /// executes after the delay period.
    /// </summary>
    /// <param name="key">The key to identify this debounce instance.</param>
    /// <param name="frames">The number of game frames to wait before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the debounce delay.</param>
    public static async Task DebounceAsync(string key, long frames, Func<Task> action)
    {
        var debouncer = GetOrCreateDebouncer(key, frames);
        await debouncer.DebounceAsync(action);
    }

    /// <summary>
    /// Checks if there is a pending debounced action for the specified key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="frames">The frame count to check against.</param>
    /// <returns>True if an action is currently waiting to be executed, false otherwise.</returns>
    public static bool IsPending(string key, long frames)
    {
        var debouncer = GetOrCreateDebouncer(key, frames);
        return debouncer.IsPending();
    }

    /// <summary>
    /// Gets how many game frames are left before the debounced action for the specified key will execute.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled frame has passed; otherwise returns 0.</param>
    /// <returns>The remaining frames, or 0 if no action is pending (when allowNegative is false).</returns>
    public static double GetRemainingFrames(string key, bool allowNegative = false)
    {
        if (_debouncers.TryGetValue(key, out var debouncer))
            return debouncer.GetRemainingFrames(allowNegative);
        return 0;
    }

    /// <summary>
    /// Gets how many game frames are left before the debounced action for the specified key will execute.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="frames">The new frame count to check against.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled frame has passed; otherwise returns 0.</param>
    /// <returns>The remaining frames, or 0 if no action is pending (when allowNegative is false).</returns>
    public static double GetRemainingFrames(string key, long frames, bool allowNegative = false)
    {
        var debouncer = GetOrCreateDebouncer(key, frames);
        return debouncer.GetRemainingFrames(allowNegative);
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
    /// Clears all frame debouncer states and disposes them.
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
    /// Disposes all frame debouncer states and clears them.
    /// </summary>
    internal static void Dispose()
    {
        Clear();
    }
}
