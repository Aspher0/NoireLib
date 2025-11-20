using Dalamud.Plugin.Services;
using NoireLib.Helpers.ObjectExtensions;
using NoireLib.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Provides delayed trigger functionality that executes an action only if it hasn't been cancelled before the specified delay.
/// Useful for showing loading indicators, timeout handlers, or any deferred action that should be cancelled if the primary operation completes quickly.<br/>
/// For example, you might want to show a loading spinner only if a data fetch takes longer than 500ms. You can use this class to schedule the spinner display after 500ms, and cancel it if the data fetch completes sooner.<br/>
/// Each trigger is independent with its own delay, allowing multiple triggers to be started and managed individually.
/// </summary>
public class TaskDelayer : IDisposable
{
    private readonly List<DelayedTrigger> _executions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isFrameworkUpdateAttached = false;
    private bool _disposed = false;

    /// <summary>
    /// Creates a new delayed trigger instance.
    /// </summary>
    public TaskDelayer() { }

    /// <summary>
    /// Starts a delayed trigger that will execute the action after the specified delay unless cancelled.
    /// Each trigger is independent and will execute after its own delay.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger StartAsync(Action action, int delayMilliseconds)
    {
        ThrowIfDisposed();
        action.ThrowIfNull(nameof(action));

        if (delayMilliseconds <= 0)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delayMilliseconds));

        _lock.Wait();
        try
        {
            var execution = new DelayedTrigger
            {
                Action = action,
                ScheduledExecutionMs = Environment.TickCount64 + delayMilliseconds,
                ParentTrigger = this
            };

            _executions.Add(execution);
            EnsureFrameworkUpdateAttached();
            return execution;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Starts a delayed trigger that will execute the asynchronous action after the specified delay unless cancelled.
    /// Each trigger is independent and will execute after its own delay.
    /// </summary>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger StartAsync(Func<Task> action, int delayMilliseconds)
    {
        ThrowIfDisposed();
        action.ThrowIfNull(nameof(action));

        if (delayMilliseconds <= 0)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delayMilliseconds));

        _lock.Wait();
        try
        {
            var execution = new DelayedTrigger
            {
                AsyncAction = action,
                ScheduledExecutionMs = Environment.TickCount64 + delayMilliseconds,
                ParentTrigger = this
            };

            _executions.Add(execution);
            EnsureFrameworkUpdateAttached();
            return execution;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Starts a delayed trigger with a condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// Each trigger is independent and will execute after its own delay.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <param name="cancelCondition">A callback that determines if the action should cancel.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger? StartAsync(Action action, int delayMilliseconds, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        ThrowIfDisposed();
        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));

        if (delayMilliseconds <= 0)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delayMilliseconds));

        if (immediatelyCancelOnConditionMet && cancelCondition())
            return null;

        _lock.Wait();
        try
        {
            var execution = new DelayedTrigger
            {
                Action = action,
                Condition = cancelCondition,
                CheckConditionImmediately = immediatelyCancelOnConditionMet,
                ScheduledExecutionMs = Environment.TickCount64 + delayMilliseconds,
                ParentTrigger = this
            };

            _executions.Add(execution);
            EnsureFrameworkUpdateAttached();
            return execution;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Starts a delayed trigger with an asynchronous condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// Each trigger is independent and will execute after its own delay.
    /// </summary>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <param name="cancelCondition">An asynchronous function that determines if the action should execute. Called after the delay period.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public async Task<DelayedTrigger?> StartAsync(Func<Task> action, int delayMilliseconds, Func<Task<bool>> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        ThrowIfDisposed();
        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));

        if (delayMilliseconds <= 0)
            throw new ArgumentException("Delay must be greater than zero.", nameof(delayMilliseconds));

        if (immediatelyCancelOnConditionMet && await cancelCondition())
            return null;

        _lock.Wait();
        try
        {
            var execution = new DelayedTrigger
            {
                AsyncAction = action,
                AsyncCondition = cancelCondition,
                CheckConditionImmediately = immediatelyCancelOnConditionMet,
                ScheduledExecutionMs = Environment.TickCount64 + delayMilliseconds,
                ParentTrigger = this
            };

            _executions.Add(execution);
            EnsureFrameworkUpdateAttached();
            return execution;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureFrameworkUpdateAttached()
    {
        if (!_isFrameworkUpdateAttached)
        {
            NoireService.Framework.Update += OnFrameworkUpdate;
            _isFrameworkUpdateAttached = true;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        _lock.Wait();
        try
        {
            var now = Environment.TickCount64;

            for (int i = _executions.Count - 1; i >= 0; i--)
            {
                var execution = _executions[i];

                if (execution.Cts.IsCancellationRequested)
                {
                    execution.Cts.Dispose();
                    _executions.RemoveAt(i);
                    continue;
                }

                if (execution.CheckConditionImmediately)
                {
                    bool shouldCancel = false;

                    if (execution.Condition != null)
                        shouldCancel = execution.Condition();
                    else if (execution.AsyncCondition != null)
                    {
                        var task = execution.AsyncCondition();
                        if (task.IsCompleted)
                            shouldCancel = task.Result;
                        else
                            _ = CheckAsyncConditionAndCancel(execution, task);
                    }

                    if (shouldCancel)
                    {
                        execution.Cts.Cancel();
                        execution.Cts.Dispose();
                        _executions.RemoveAt(i);
                        continue;
                    }
                }

                if (now >= execution.ScheduledExecutionMs)
                {
                    bool conditionIndicatesCancel = false;

                    if (execution.Condition != null)
                        conditionIndicatesCancel = execution.Condition();
                    else if (execution.AsyncCondition != null)
                    {
                        _ = ExecuteWithAsyncCondition(execution);
                        _executions.RemoveAt(i);
                        continue;
                    }

                    if (!conditionIndicatesCancel)
                    {
                        if (execution.Action != null)
                            execution.Action();
                        else if (execution.AsyncAction != null)
                            _ = execution.AsyncAction();
                    }

                    execution.Cts.Dispose();
                    _executions.RemoveAt(i);
                }
            }

            if (_executions.Count == 0)
            {
                NoireService.Framework.Update -= OnFrameworkUpdate;
                _isFrameworkUpdateAttached = false;
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Error in framework update handler");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CheckAsyncConditionAndCancel(DelayedTrigger execution, Task<bool> conditionTask)
    {
        try
        {
            var shouldCancel = await conditionTask;

            if (shouldCancel)
            {
                _lock.Wait();
                try
                {
                    execution.Cts.Cancel();
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Error checking async condition for execution");
        }
    }

    private async Task ExecuteWithAsyncCondition(DelayedTrigger execution)
    {
        try
        {
            bool shouldCancel = false;

            if (execution.AsyncCondition != null)
                shouldCancel = await execution.AsyncCondition();

            if (!shouldCancel && execution.AsyncAction != null)
                await execution.AsyncAction();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Error executing async action with condition");
        }
        finally
        {
            execution.Cts.Dispose();
        }
    }

    /// <summary>
    /// Starts a delayed trigger without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// Each trigger is independent and will execute after its own delay.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger Start(Action action, int delayMilliseconds)
    {
        return StartAsync(action, delayMilliseconds);
    }

    /// <summary>
    /// Starts a delayed trigger with a condition without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// Each trigger is independent and will execute after its own delay.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    /// <param name="cancelCondition">A function that determines if the action should execute.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A DelayedTrigger instance that can be used to cancel or check the status of this trigger, or null if cancelled immediately.</returns>
    /// <exception cref="ArgumentException">Thrown when delay is less than or equal to zero.</exception>
    public DelayedTrigger? Start(Action action, int delayMilliseconds, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        return StartAsync(action, delayMilliseconds, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Cancels a specific trigger by its DelayedTrigger instance.
    /// </summary>
    /// <param name="trigger">The DelayedTrigger instance to cancel.</param>
    /// <returns>True if the trigger was found and cancelled, false otherwise.</returns>
    public bool Cancel(DelayedTrigger? trigger)
    {
        if (trigger == null)
            return false;

        return Cancel(trigger.Id);
    }

    /// <summary>
    /// Cancels a specific trigger by its ID.
    /// </summary>
    /// <param name="triggerId">The unique identifier of the trigger to cancel.</param>
    /// <returns>True if the trigger was found and cancelled, false otherwise.</returns>
    internal bool Cancel(Guid triggerId)
    {
        ThrowIfDisposed();

        if (triggerId == Guid.Empty)
            return false;

        _lock.Wait();
        try
        {
            for (int i = 0; i < _executions.Count; i++)
            {
                if (_executions[i].Id == triggerId)
                {
                    _executions[i].Cts.Cancel();
                    _executions[i].Cts.Dispose();
                    _executions.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Cancels all pending triggers.
    /// </summary>
    public void CancelAll()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            foreach (var execution in _executions)
            {
                execution.Cts.Cancel();
                execution.Cts.Dispose();
            }
            _executions.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks if a specific trigger is still running.
    /// </summary>
    /// <param name="trigger">The DelayedTrigger instance to check.</param>
    /// <returns>True if the trigger is still pending, false otherwise.</returns>
    public bool IsRunning(DelayedTrigger? trigger)
    {
        if (trigger == null)
            return false;

        return IsRunning(trigger.Id);
    }

    /// <summary>
    /// Checks if a specific trigger is still running.
    /// </summary>
    /// <param name="triggerId">The unique identifier of the trigger to check.</param>
    /// <returns>True if the trigger is still pending, false otherwise.</returns>
    internal bool IsRunning(Guid triggerId)
    {
        ThrowIfDisposed();

        if (triggerId == Guid.Empty)
            return false;

        _lock.Wait();
        try
        {
            return _executions.Exists(e => e.Id == triggerId && !e.Cts.IsCancellationRequested);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks if there are any triggers currently running (waiting to execute).
    /// </summary>
    /// <returns>True if any trigger is pending, false otherwise.</returns>
    public bool IsAnyRunning()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return _executions.Count > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the number of triggers currently pending.
    /// </summary>
    /// <returns>The number of pending triggers.</returns>
    public int GetPendingCount()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return _executions.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before a specific trigger will execute.
    /// </summary>
    /// <param name="trigger">The DelayedTrigger instance.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if the trigger is not found or has no time remaining (when allowNegative is false).</returns>
    public double GetRemainingTime(DelayedTrigger? trigger, bool allowNegative = false)
    {
        if (trigger == null)
            return 0;

        return GetRemainingTime(trigger.Id, allowNegative);
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before a specific trigger will execute.
    /// </summary>
    /// <param name="triggerId">The unique identifier of the trigger.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if the trigger is not found or has no time remaining (when allowNegative is false).</returns>
    internal double GetRemainingTime(Guid triggerId, bool allowNegative = false)
    {
        ThrowIfDisposed();

        if (triggerId == Guid.Empty)
            return 0;

        _lock.Wait();
        try
        {
            var execution = _executions.Find(e => e.Id == triggerId);
            if (execution == null)
                return 0;

            var remaining = execution.ScheduledExecutionMs - Environment.TickCount64;
            return allowNegative ? remaining : Math.Max(0, remaining);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the next trigger will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if no trigger is pending (when allowNegative is false).</returns>
    public double GetNextRemainingTime(bool allowNegative = false)
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            if (_executions.Count == 0)
                return 0;

            var nextExecution = _executions[0];
            foreach (var execution in _executions)
            {
                if (execution.ScheduledExecutionMs < nextExecution.ScheduledExecutionMs)
                    nextExecution = execution;
            }

            var remaining = nextExecution.ScheduledExecutionMs - Environment.TickCount64;
            return allowNegative ? remaining : Math.Max(0, remaining);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Disposes the delayed trigger and cancels any pending triggers.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _lock.Wait();
        try
        {
            if (_isFrameworkUpdateAttached)
            {
                NoireService.Framework.Update -= OnFrameworkUpdate;
                _isFrameworkUpdateAttached = false;
            }

            foreach (var execution in _executions)
            {
                execution.Cts.Cancel();
                execution.Cts.Dispose();
            }
            _executions.Clear();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}

/// <summary>
/// Provides keyed delayed trigger functionality, allowing independent delayed triggers for different operations.
/// Each key maintains its own TaskDelayer instance, preventing interference between different operations.
/// </summary>
public static class KeyedTaskDelayer
{
    private static readonly ConcurrentDictionary<string, TaskDelayer> _delayers = new();

    /// <summary>
    /// Gets or creates a task delayer for the specified key.
    /// </summary>
    /// <param name="key">The key to identify this delayer instance.</param>
    /// <returns>The TaskDelayer instance for the specified key.</returns>
    private static TaskDelayer GetOrCreateDelayer(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));

        return _delayers.GetOrAdd(key, _ => new TaskDelayer());
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
}
