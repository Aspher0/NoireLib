using NoireLib.Models;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Provides keyed delayed trigger functionality, allowing independent delayed triggers for different operations.<br/>
/// Each key maintains its own Delayer instance, preventing interference between different operations.<br/>
/// Provides easy-to-use static methods to start, cancel, and manage delayed triggers based on unique keys.<br/>
/// NoireLib must be initialized before using this helper.<br/>
/// See <see cref="Delayer"/> for more details.
/// </summary>
public static class DelayerHelper
{
    private static readonly ConcurrentDictionary<string, Delayer> _delayers = new();

    /// <summary>
    /// Throws an exception if the NoireLib is not initialized.
    /// </summary>
    private static void EnsureInitialized()
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib is not initialized. Please initialize NoireLib before using ThrottleHelper.");

        NoireLibMain.RegisterOnDispose("NoireLib_Internal_DelayerHelper", Dispose);
    }

    /// <summary>
    /// Gets or creates a task delayer for the specified key.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <returns>The Delayer instance for the specified key.</returns>
    private static Delayer GetOrCreateDelayer(string key)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        return _delayers.GetOrAdd(key, _ => new Delayer());
    }

    /// <summary>
    /// Starts a delayed trigger for a given key that will execute the action after the specified delay unless cancelled.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    public static DelayedTrigger Start(string key, Action action, int delayMilliseconds)
    {
        var delayer = GetOrCreateDelayer(key);
        return delayer.Start(action, delayMilliseconds);
    }

    /// <summary>
    /// Starts a delayed trigger for a given key that will execute the asynchronous action after the specified delay unless cancelled.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    public static DelayedTrigger StartAsync(string key, Func<Task> action, int delayMilliseconds)
    {
        var delayer = GetOrCreateDelayer(key);
        return delayer.StartAsync(action, delayMilliseconds);
    }

    /// <summary>
    /// Starts a delayed trigger for a given key with a condition that will be checked before execution.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <param name="cancelCondition">A callback that determines if the action should cancel.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    public static DelayedTrigger? Start(string key, Action action, int delayMilliseconds, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        var delayer = GetOrCreateDelayer(key);
        return delayer.Start(action, delayMilliseconds, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Starts a delayed trigger for a given key with an asynchronous condition that will be checked before execution.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <param name="cancelCondition">An asynchronous function that determines if the action should execute.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    public static async Task<DelayedTrigger?> StartAsync(string key, Func<Task> action, int delayMilliseconds, Func<Task<bool>> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        var delayer = GetOrCreateDelayer(key);
        return await delayer.StartAsync(action, delayMilliseconds, cancelCondition, immediatelyCancelOnConditionMet);
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
    /// Gets the remaining time in milliseconds before the next trigger for the specified key will execute.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if no trigger is pending for this key.</returns>
    public static double GetNextRemainingTime(string key, bool allowNegative = false)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        if (_delayers.TryGetValue(key, out var delayer))
        {
            return delayer.GetNextRemainingTime(allowNegative);
        }

        return 0;
    }

    /// <summary>
    /// Removes the task delayer for the specified key and disposes it.
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
    /// Clears all task delayer states and disposes them.
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
    /// Disposes all task delayer states and clears them.
    /// </summary>
    internal static void Dispose()
    {
        Clear();
    }
}
