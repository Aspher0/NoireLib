using NoireLib.Models;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Keyed frame-delayed triggers: each key gets its own independent <see cref="FrameDelayer"/> instance. NoireLib
/// must be initialized before use. The frame twin of <see cref="DelayerHelper"/>.
/// </summary>
public static class FrameDelayerHelper
{
    private static readonly ConcurrentDictionary<string, FrameDelayer> _delayers = new();

    /// <summary>
    /// Throws an exception if the NoireLib is not initialized.
    /// </summary>
    static FrameDelayerHelper()
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib is not initialized. Please initialize NoireLib before using FrameDelayerHelper.");

        NoireLibMain.RegisterOnDispose("NoireLib_Internal_FrameDelayerHelper", Dispose);
    }

    /// <summary>
    /// Gets or creates a frame delayer for the specified key.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <returns>The FrameDelayer instance for the specified key.</returns>
    private static FrameDelayer GetOrCreateDelayer(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        return _delayers.GetOrAdd(key, _ => new FrameDelayer());
    }

    /// <summary>
    /// Starts a delayed trigger for a given key that will execute the action after the specified number of frames unless cancelled.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    public static FrameDelayedTrigger Start(string key, long frames, Action action)
    {
        var delayer = GetOrCreateDelayer(key);
        return delayer.Start(frames, action);
    }

    /// <summary>
    /// Starts a delayed trigger for a given key that will execute the asynchronous action after the specified number of frames unless cancelled.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    public static FrameDelayedTrigger StartAsync(string key, long frames, Func<Task> action)
    {
        var delayer = GetOrCreateDelayer(key);
        return delayer.StartAsync(frames, action);
    }

    /// <summary>
    /// Starts a delayed trigger for a given key with a condition that will be checked before execution.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="cancelCondition">A callback that determines if the action should cancel.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    public static FrameDelayedTrigger? Start(string key, long frames, Action action, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        var delayer = GetOrCreateDelayer(key);
        return delayer.Start(frames, action, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Starts a delayed trigger for a given key with an asynchronous condition that will be checked before execution.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="frames">The number of game frames before executing the action.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="cancelCondition">An asynchronous function that determines if the action should execute.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A FrameDelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    public static async Task<FrameDelayedTrigger?> StartAsync(string key, long frames, Func<Task> action, Func<Task<bool>> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        var delayer = GetOrCreateDelayer(key);
        return await delayer.StartAsync(frames, action, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Cancels all pending triggers for the specified key.
    /// </summary>
    /// <param name="key">The key to cancel all triggers for.</param>
    public static void CancelAll(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_delayers.TryGetValue(key, out var delayer))
        {
            delayer.CancelAll();
        }
    }

    /// <summary>
    /// Cancels all pending triggers for all keys.
    /// </summary>
    public static void CancelAll()
    {
        foreach (var kvp in _delayers)
        {
            kvp.Value.CancelAll();
        }
    }

    /// <summary>
    /// Checks if there are any triggers currently running for the specified key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if any trigger is pending for this key, false otherwise.</returns>
    public static bool IsAnyRunning(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_delayers.TryGetValue(key, out var delayer))
        {
            return delayer.IsAnyRunning();
        }

        return false;
    }

    /// <summary>
    /// Gets the number of triggers currently pending for the specified key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>The number of pending triggers for this key.</returns>
    public static int GetPendingCount(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_delayers.TryGetValue(key, out var delayer))
        {
            return delayer.GetPendingCount();
        }

        return 0;
    }

    /// <summary>
    /// Gets how many game frames are left before the next trigger for the specified key will execute.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled frame has passed; otherwise returns 0.</param>
    /// <returns>The remaining frames, or 0 if no trigger is pending for this key.</returns>
    public static double GetNextRemainingFrames(string key, bool allowNegative = false)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_delayers.TryGetValue(key, out var delayer))
        {
            return delayer.GetNextRemainingFrames(allowNegative);
        }

        return 0;
    }

    /// <summary>
    /// Removes the frame delayer for the specified key and disposes it.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    public static void Remove(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_delayers.TryRemove(key, out var delayer))
        {
            delayer.Dispose();
        }
    }

    /// <summary>
    /// Clears all frame delayer states and disposes them.
    /// </summary>
    public static void Clear()
    {
        foreach (var kvp in _delayers)
        {
            kvp.Value.Dispose();
        }
        _delayers.Clear();
    }

    /// <summary>
    /// Disposes all frame delayer states and clears them.
    /// </summary>
    internal static void Dispose()
    {
        Clear();
    }
}
