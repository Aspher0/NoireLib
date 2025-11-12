using NoireLib.Internal.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace NoireLib.Helpers;

/// <summary>
/// Provides delayed trigger functionality that executes an action only if it hasn't been cancelled before the specified delay.
/// Useful for showing loading indicators, timeout handlers, or any deferred action that should be cancelled if the primary operation completes quickly.<br/>
/// For example, you might want to show a loading spinner only if a data fetch takes longer than 500ms. You can use this class to schedule the spinner display after 500ms, and cancel it if the data fetch completes sooner.
/// </summary>
public class DelayedTrigger : TimingHelperBase
{
    private bool _isRunning = false;
    private Action? _pendingAction;
    private Func<Task>? _pendingAsyncAction;
    private Func<bool>? _pendingCondition;
    private Func<Task<bool>>? _pendingAsyncCondition;
    private bool _checkConditionImmediately;

    /// <summary>
    /// Creates a new delayed trigger with the specified delay.
    /// </summary>
    /// <param name="delayMilliseconds">The delay in milliseconds before executing the action.</param>
    public DelayedTrigger(int delayMilliseconds) : base(delayMilliseconds) { }

    /// <summary>
    /// Starts a delayed trigger that will execute the action after the specified delay unless cancelled.
    /// If a trigger is already running, it will be cancelled and replaced with the new one.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    /// <returns>A task that completes when the trigger is either executed or cancelled.</returns>
    public Task StartAsync(Action action)
    {
        ThrowIfDisposed();

        action.ThrowIfNull(nameof(action));

        _lock.Wait();
        try
        {
            CancelCurrentExecution();
            _cts = new CancellationTokenSource();
            _scheduledExecutionMs = Environment.TickCount64 + _delayMilliseconds;
            _isRunning = true;
            _pendingAction = action;
            _pendingAsyncAction = null;
            _pendingCondition = null;
            _pendingAsyncCondition = null;
            _checkConditionImmediately = false;

            NoireService.Framework.Update += OnFrameworkUpdate;
        }
        finally
        {
            _lock.Release();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts a delayed trigger that will execute the asynchronous action after the specified delay unless cancelled.
    /// If a trigger is already running, it will be cancelled and replaced with the new one.
    /// </summary>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <returns>A task that completes when the trigger is either executed or cancelled.</returns>
    public Task StartAsync(Func<Task> action)
    {
        ThrowIfDisposed();

        action.ThrowIfNull(nameof(action));

        _lock.Wait();
        try
        {
            CancelCurrentExecution();
            _cts = new CancellationTokenSource();
            _scheduledExecutionMs = Environment.TickCount64 + _delayMilliseconds;
            _isRunning = true;
            _pendingAction = null;
            _pendingAsyncAction = action;
            _pendingCondition = null;
            _pendingAsyncCondition = null;
            _checkConditionImmediately = false;

            NoireService.Framework.Update += OnFrameworkUpdate;
        }
        finally
        {
            _lock.Release();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts a delayed trigger with a condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="cancelCondition">A callback that determines if the action should cancel.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A task that completes when the trigger is either executed, cancelled, or skipped due to condition.</returns>
    public Task StartAsync(Action action, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        ThrowIfDisposed();

        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));

        if (immediatelyCancelOnConditionMet && cancelCondition())
            return Task.CompletedTask;

        _lock.Wait();
        try
        {
            CancelCurrentExecution();
            _cts = new CancellationTokenSource();
            _scheduledExecutionMs = Environment.TickCount64 + _delayMilliseconds;
            _isRunning = true;
            _pendingAction = action;
            _pendingAsyncAction = null;
            _pendingCondition = cancelCondition;
            _pendingAsyncCondition = null;
            _checkConditionImmediately = immediatelyCancelOnConditionMet;

            NoireService.Framework.Update += OnFrameworkUpdate;
        }
        finally
        {
            _lock.Release();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts a delayed trigger with an asynchronous condition that will be checked before execution.
    /// The action will be cancelled if the condition returns true after the delay.
    /// </summary>
    /// <param name="action">The asynchronous action to execute after the delay.</param>
    /// <param name="cancelCondition">An asynchronous function that determines if the action should execute. Called after the delay period.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    /// <returns>A task that completes when the trigger is either executed, cancelled, or skipped due to condition.</returns>
    public async Task StartAsync(Func<Task> action, Func<Task<bool>> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        ThrowIfDisposed();

        action.ThrowIfNull(nameof(action));
        cancelCondition.ThrowIfNull(nameof(cancelCondition));

        if (immediatelyCancelOnConditionMet && await cancelCondition())
            return;

        _lock.Wait();
        try
        {
            CancelCurrentExecution();
            _cts = new CancellationTokenSource();
            _scheduledExecutionMs = Environment.TickCount64 + _delayMilliseconds;
            _isRunning = true;
            _pendingAction = null;
            _pendingAsyncAction = action;
            _pendingCondition = null;
            _pendingAsyncCondition = cancelCondition;
            _checkConditionImmediately = immediatelyCancelOnConditionMet;

            NoireService.Framework.Update += OnFrameworkUpdate;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        _lock.Wait();
        try
        {
            if (!_isRunning || _cts == null || _cts.IsCancellationRequested)
            {
                CleanupFrameworkUpdate();
                return;
            }

            if (_checkConditionImmediately)
            {
                bool shouldCancel = false;

                if (_pendingCondition != null)
                    shouldCancel = _pendingCondition();
                else if (_pendingAsyncCondition != null)
                {
                    // For async conditions, we need to handle them carefully
                    // We'll run them synchronously here since we're in a synchronous context
                    var task = _pendingAsyncCondition();

                    if (task.IsCompleted)
                        shouldCancel = task.Result;
                    else
                        _ = CheckAsyncConditionAndCancel(task);
                }

                if (shouldCancel)
                {
                    CleanupFrameworkUpdate();
                    return;
                }
            }

            var now = Environment.TickCount64;
            if (now < _scheduledExecutionMs)
                return;

            // Time has elapsed, check condition (if any) and execute
            bool conditionIndicatesCancel = false;

            if (_pendingCondition != null)
                conditionIndicatesCancel = _pendingCondition();
            else if (_pendingAsyncCondition != null)
            {
                // Schedule async execution
                _ = ExecuteWithAsyncCondition();
                CleanupFrameworkUpdate();
                return;
            }

            var actionToExecute = _pendingAction;
            var asyncActionToExecute = _pendingAsyncAction;
            CleanupFrameworkUpdate();

            if (!conditionIndicatesCancel)
            {
                if (actionToExecute != null)
                    actionToExecute();
                else if (asyncActionToExecute != null)
                    _ = asyncActionToExecute();
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Error in framework update handler");
            CleanupFrameworkUpdate();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CheckAsyncConditionAndCancel(Task<bool> conditionTask)
    {
        try
        {
            var shouldCancel = await conditionTask;

            if (shouldCancel)
                Cancel();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Error checking async condition");
        }
    }

    private async Task ExecuteWithAsyncCondition()
    {
        try
        {
            bool shouldCancel = false;
            Func<Task>? asyncActionToExecute = null;

            // Check condition
            if (_pendingAsyncCondition != null)
                shouldCancel = await _pendingAsyncCondition();

            if (!shouldCancel && _pendingAsyncAction != null)
                asyncActionToExecute = _pendingAsyncAction;

            // Execute if NOT cancelled
            if (asyncActionToExecute != null)
                await asyncActionToExecute();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, "Error executing async action with condition");
        }
    }

    private void CleanupFrameworkUpdate()
    {
        NoireService.Framework.Update -= OnFrameworkUpdate;
        _isRunning = false;
        _scheduledExecutionMs = 0;
        _pendingAction = null;
        _pendingAsyncAction = null;
        _pendingCondition = null;
        _pendingAsyncCondition = null;
        _checkConditionImmediately = false;
    }

    /// <summary>
    /// Starts a delayed trigger without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    public void Start(Action action)
    {
        _ = StartAsync(action);
    }

    /// <summary>
    /// Starts a delayed trigger with a condition without waiting for it to complete.
    /// Useful for fire-and-forget scenarios.
    /// </summary>
    /// <param name="action">The action to execute after the delay.</param>
    /// <param name="cancelCondition">A function that determines if the action should execute.</param>
    /// <param name="immediatelyCancelOnConditionMet">If true, continuously checks the condition and cancels immediately when it becomes true before the delay expires.</param>
    public void Start(Action action, Func<bool> cancelCondition, bool immediatelyCancelOnConditionMet = false)
    {
        _ = StartAsync(action, cancelCondition, immediatelyCancelOnConditionMet);
    }

    /// <summary>
    /// Cancels any pending trigger.
    /// </summary>
    public void Cancel()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            if (_isRunning)
            {
                CleanupFrameworkUpdate();
            }
            CancelCurrentExecution();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Checks if there is a trigger currently running (waiting to execute).
    /// </summary>
    /// <returns>True if a trigger is pending, false otherwise.</returns>
    public bool IsRunning()
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            return _isRunning && _cts != null && !_cts.IsCancellationRequested;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the remaining time in milliseconds before the trigger will execute.
    /// </summary>
    /// <param name="allowNegative">If true, allows negative values when the scheduled time has passed; otherwise returns 0.</param>
    /// <returns>The remaining time in milliseconds, or 0 if no trigger is pending (when allowNegative is false).</returns>
    public double GetRemainingTime(bool allowNegative = false)
    {
        ThrowIfDisposed();

        _lock.Wait();
        try
        {
            if (!_isRunning)
                return 0;

            return GetRemainingTimeCore(allowNegative);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Disposes the delayed trigger and cancels any pending triggers.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _lock.Wait();
        try
        {
            if (_isRunning)
            {
                CleanupFrameworkUpdate();
            }
            CancelCurrentExecution();
        }
        finally
        {
            _lock.Release();
        }
    }
}
