using Dalamud.Plugin.Services;
using NoireLib.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Internal.Helpers;

/// <summary>
/// The shared machinery of the delayers: the pending list, the game-update countdown, the cancel conditions and
/// the disposal rules. The unit a delay is counted in belongs to the derived class, so a tick here is a
/// millisecond for <see cref="NoireLib.Helpers.Delayer"/> and a game frame for
/// <see cref="NoireLib.Helpers.FrameDelayer"/>.
/// </summary>
/// <typeparam name="TTrigger">The trigger type this delayer hands out.</typeparam>
public abstract class DelayerBase<TTrigger> : IDelayerHost, IDisposable
    where TTrigger : DelayedTriggerBase, new()
{
    private readonly List<TTrigger> _executions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isFrameworkUpdateAttached = false;
    private bool _disposed = false;

    /// <summary>
    /// The current tick, in this delayer's own unit.
    /// </summary>
    protected abstract long CurrentTick { get; }

    /// <summary>
    /// Schedules a trigger and starts the countdown.
    /// </summary>
    /// <param name="ticks">How far ahead the trigger is due, in this delayer's own unit.</param>
    /// <param name="action">The action to run, or null when the trigger runs an asynchronous one.</param>
    /// <param name="asyncAction">The asynchronous action to run, or null when the trigger runs a plain one.</param>
    /// <param name="condition">A predicate that cancels the trigger when it answers true, or null for none.</param>
    /// <param name="asyncCondition">An asynchronous predicate that cancels the trigger, or null for none.</param>
    /// <param name="checkConditionImmediately">Whether the condition is checked every frame rather than only once the delay is out.</param>
    /// <returns>The scheduled trigger.</returns>
    protected TTrigger Schedule(
        long ticks, Action? action, Func<Task>? asyncAction, Func<bool>? condition,
        Func<Task<bool>>? asyncCondition, bool checkConditionImmediately)
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            var execution = new TTrigger
            {
                Action = action,
                AsyncAction = asyncAction,
                Condition = condition,
                AsyncCondition = asyncCondition,
                CheckConditionImmediately = checkConditionImmediately,
                ScheduledTick = CurrentTick + ticks,
                Host = this,
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
            var now = CurrentTick;

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

                if (now >= execution.ScheduledTick)
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

    private async Task CheckAsyncConditionAndCancel(TTrigger execution, Task<bool> conditionTask)
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

    private async Task ExecuteWithAsyncCondition(TTrigger execution)
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
    /// Cancels a specific trigger.
    /// </summary>
    /// <param name="trigger">The trigger to cancel.</param>
    /// <returns>True if the trigger was found and cancelled, false otherwise.</returns>
    public bool Cancel(TTrigger? trigger)
    {
        if (trigger == null)
            return false;

        return Cancel(trigger.UniqueId);
    }

    /// <summary>
    /// Cancels a specific trigger by its ID.
    /// </summary>
    /// <param name="triggerId">The unique identifier of the trigger to cancel.</param>
    /// <returns>True if the trigger was found and cancelled, false otherwise.</returns>
    bool IDelayerHost.Cancel(Guid triggerId) => Cancel(triggerId);

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
                if (_executions[i].UniqueId == triggerId)
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
    /// <param name="trigger">The trigger to check.</param>
    /// <returns>True if the trigger is still pending, false otherwise.</returns>
    public bool IsRunning(TTrigger? trigger)
    {
        if (trigger == null)
            return false;

        return IsRunning(trigger.UniqueId);
    }

    /// <summary>
    /// Checks if a specific trigger is still running.
    /// </summary>
    /// <param name="triggerId">The unique identifier of the trigger to check.</param>
    /// <returns>True if the trigger is still pending, false otherwise.</returns>
    bool IDelayerHost.IsRunning(Guid triggerId) => IsRunning(triggerId);

    internal bool IsRunning(Guid triggerId)
    {
        ThrowIfDisposed();

        if (triggerId == Guid.Empty)
            return false;

        _lock.Wait();
        try
        {
            return _executions.Exists(e => e.UniqueId == triggerId && !e.Cts.IsCancellationRequested);
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
    /// Gets how much of the delay is left for a specific trigger, in this delayer's own unit.
    /// </summary>
    /// <param name="triggerId">The unique identifier of the trigger.</param>
    /// <param name="allowNegative">If true, allows negative values when the scheduled tick has passed; otherwise returns 0.</param>
    /// <returns>The remaining amount, or 0 if the trigger is not found.</returns>
    double IDelayerHost.GetRemaining(Guid triggerId, bool allowNegative) => GetRemaining(triggerId, allowNegative);

    internal double GetRemaining(Guid triggerId, bool allowNegative = false)
    {
        ThrowIfDisposed();

        if (triggerId == Guid.Empty)
            return 0;

        _lock.Wait();
        try
        {
            var execution = _executions.Find(e => e.UniqueId == triggerId);
            if (execution == null)
                return 0;

            var remaining = execution.ScheduledTick - CurrentTick;
            return allowNegative ? remaining : Math.Max(0, remaining);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets how much of the delay is left before the next trigger executes, in this delayer's own unit.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled tick has passed; otherwise returns 0.</param>
    /// <returns>The remaining amount, or 0 if no trigger is pending (when allowNegative is false).</returns>
    protected double GetNextRemaining(bool allowNegative = false)
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
                if (execution.ScheduledTick < nextExecution.ScheduledTick)
                    nextExecution = execution;
            }

            var remaining = nextExecution.ScheduledTick - CurrentTick;
            return allowNegative ? remaining : Math.Max(0, remaining);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if this instance has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Disposes the delayer and cancels any pending triggers.
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

        GC.SuppressFinalize(this);
    }
}
