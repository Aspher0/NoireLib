using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Provides safe threading, scheduling, and asynchronous execution.
/// Helps with work in background threads and framework thread without freezing the game.
/// </summary>
public static class AsyncHelper
{
    private const string Prefix = $"[{nameof(AsyncHelper)}] ";

    #region Background Execution

    /// <summary>
    /// Runs an asynchronous action on a background thread with exception logging.
    /// </summary>
    /// <param name="action">The asynchronous action to execute on a background thread.</param>
    /// <param name="operationName">An optional name for the operation, used in log messages for diagnostics.</param>
    /// <returns>A <see cref="Task"/> representing the background operation.</returns>
    public static Task RunInBackgroundAsync(Func<Task> action, string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Background operation '{operationName ?? "unnamed"}' failed.", Prefix);
            }
        });
    }

    /// <summary>
    /// Runs a synchronous action on a background thread with exception logging.
    /// </summary>
    /// <param name="action">The action to execute on a background thread.</param>
    /// <param name="operationName">An optional name for the operation, used in log messages for diagnostics.</param>
    /// <returns>A <see cref="Task"/> representing the background operation.</returns>
    public static Task RunInBackgroundAsync(Action action, string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Background operation '{operationName ?? "unnamed"}' failed.", Prefix);
            }
        });
    }

    /// <summary>
    /// Runs an asynchronous function on a background thread with exception logging and returns the result.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The asynchronous function to execute on a background thread.</param>
    /// <param name="defaultValue">The default value to return if an exception occurs.</param>
    /// <param name="operationName">An optional name for the operation, used in log messages for diagnostics.</param>
    /// <returns>The result of the function, or <paramref name="defaultValue"/> if an exception occurs.</returns>
    public static Task<T?> RunInBackgroundAsync<T>(Func<Task<T>> func, T? defaultValue = default, string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(func);

        return Task.Run(async () =>
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Background operation '{operationName ?? "unnamed"}' failed.", Prefix);
                return defaultValue;
            }
        });
    }

    /// <summary>
    /// Runs a synchronous function on a background thread with exception logging and returns the result.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute on a background thread.</param>
    /// <param name="defaultValue">The default value to return if an exception occurs.</param>
    /// <param name="operationName">An optional name for the operation, used in log messages for diagnostics.</param>
    /// <returns>The result of the function, or <paramref name="defaultValue"/> if an exception occurs.</returns>
    public static Task<T?> RunInBackgroundAsync<T>(Func<T> func, T? defaultValue = default, string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(func);

        return Task.Run(() =>
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Background operation '{operationName ?? "unnamed"}' failed.", Prefix);
                return defaultValue;
            }
        });
    }

    #endregion

    #region Framework Thread Execution

    /// <summary>
    /// Executes a synchronous action on the framework thread.
    /// If already on the framework thread, the action executes immediately.
    /// </summary>
    /// <param name="action">The action to execute on the framework thread.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has finished executing on the framework thread.</returns>
    public static Task RunOnFrameworkThreadAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        NoireService.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Executes a synchronous function on the framework thread and returns its result.
    /// If already on the framework thread, the function executes immediately.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute on the framework thread.</param>
    /// <returns>A <see cref="Task{T}"/> containing the result of the function.</returns>
    public static Task<T> RunOnFrameworkThreadAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        NoireService.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Starts an asynchronous action on the framework thread and awaits its completion.
    /// The async work begins on the framework thread; continuations may run on a thread pool thread.
    /// </summary>
    /// <param name="asyncAction">The asynchronous action to start on the framework thread.</param>
    /// <returns>A <see cref="Task"/> that completes when the asynchronous action has finished.</returns>
    public static Task StartOnFrameworkThreadAsync(Func<Task> asyncAction)
    {
        ArgumentNullException.ThrowIfNull(asyncAction);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        NoireService.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                asyncAction().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        tcs.TrySetException(t.Exception!.InnerExceptions);
                    else if (t.IsCanceled)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetResult();
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Starts an asynchronous function on the framework thread and returns its result.
    /// The async work begins on the framework thread; continuations may run on a thread pool thread.
    /// </summary>
    /// <typeparam name="T">The return type of the asynchronous function.</typeparam>
    /// <param name="asyncFunc">The asynchronous function to start on the framework thread.</param>
    /// <returns>A <see cref="Task{T}"/> containing the result of the asynchronous function.</returns>
    public static Task<T> StartOnFrameworkThreadAsync<T>(Func<Task<T>> asyncFunc)
    {
        ArgumentNullException.ThrowIfNull(asyncFunc);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        NoireService.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                asyncFunc().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        tcs.TrySetException(t.Exception!.InnerExceptions);
                    else if (t.IsCanceled)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetResult(t.Result);
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    #endregion

    #region Frame Delays

    /// <summary>
    /// Asynchronously waits for the specified number of framework update frames before continuing.
    /// </summary>
    /// <param name="frameCount">The number of frames to wait. Must be greater than zero.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the wait early.</param>
    /// <returns>A <see cref="Task"/> that completes after the specified number of frames have elapsed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="frameCount"/> is less than or equal to zero.</exception>
    public static Task DelayFramesAsync(int frameCount, CancellationToken cancellationToken = default)
    {
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be greater than zero.");

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = frameCount;

        CancellationTokenRegistration? ctr = null;

        if (cancellationToken.CanBeCanceled)
        {
            ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        void OnUpdate(IFramework framework)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                NoireService.Framework.Update -= OnUpdate;
                ctr?.Dispose();
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            remaining--;

            if (remaining <= 0)
            {
                NoireService.Framework.Update -= OnUpdate;
                ctr?.Dispose();
                tcs.TrySetResult();
            }
        }

        NoireService.Framework.Update += OnUpdate;
        return tcs.Task;
    }

    #endregion

    #region Timeout Wrappers

    /// <summary>
    /// Executes an asynchronous function with a timeout. If the operation does not complete within the
    /// specified duration, a <see cref="TimeoutException"/> is thrown.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="timeout">The maximum time allowed for the operation to complete.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>The result of the operation if it completes within the timeout.</returns>
    /// <exception cref="TimeoutException">Thrown when the operation exceeds the specified timeout.</exception>
    public static async Task<T> WithTimeoutAsync<T>(Func<Task<T>> operation, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await operation().WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The operation did not complete within the allowed timeout of {timeout.TotalSeconds:F1}s.");
        }
    }

    /// <summary>
    /// Executes an asynchronous action with a timeout. If the operation does not complete within the
    /// specified duration, a <see cref="TimeoutException"/> is thrown.
    /// </summary>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="timeout">The maximum time allowed for the operation to complete.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the operation finishes within the timeout.</returns>
    /// <exception cref="TimeoutException">Thrown when the operation exceeds the specified timeout.</exception>
    public static async Task WithTimeoutAsync(Func<Task> operation, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await operation().WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The operation did not complete within the allowed timeout of {timeout.TotalSeconds:F1}s.");
        }
    }

    /// <summary>
    /// Executes an asynchronous function with a timeout, returning a default value instead of throwing
    /// if the operation does not complete in time.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="timeout">The maximum time allowed for the operation to complete.</param>
    /// <param name="defaultValue">The value to return if the operation times out.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>The result of the operation, or <paramref name="defaultValue"/> if it times out.</returns>
    public static async Task<T?> WithTimeoutOrDefaultAsync<T>(
        Func<Task<T>> operation,
        TimeSpan timeout,
        T? defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithTimeoutAsync(operation, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return defaultValue;
        }
    }

    #endregion

    #region Retry

    /// <summary>
    /// Retries an asynchronous function up to the specified number of times with an optional delay between attempts.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="maxRetries">The maximum number of retry attempts. Must be greater than zero.</param>
    /// <param name="delay">The delay between retries. Defaults to 500ms if not specified.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>The result of the operation on success.</returns>
    /// <exception cref="AggregateException">Thrown when all retry attempts fail, containing all collected exceptions.</exception>
    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (maxRetries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries must be greater than zero.");

        var retryDelay = delay ?? TimeSpan.FromMilliseconds(500);
        var exceptions = new List<Exception>();

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                exceptions.Add(ex);
                NoireLogger.LogWarning($"Retry attempt {attempt}/{maxRetries} failed: {ex.Message}", Prefix);

                if (retryDelay > TimeSpan.Zero)
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException($"All {maxRetries} retry attempts failed.", exceptions);
    }

    /// <summary>
    /// Retries an asynchronous action up to the specified number of times with an optional delay between attempts.
    /// </summary>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="maxRetries">The maximum number of retry attempts. Must be greater than zero.</param>
    /// <param name="delay">The delay between retries. Defaults to 500ms if not specified.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the operation succeeds.</returns>
    /// <exception cref="AggregateException">Thrown when all retry attempts fail, containing all collected exceptions.</exception>
    public static async Task RetryAsync(
        Func<Task> operation,
        int maxRetries = 3,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await RetryAsync<bool>(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, maxRetries, delay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retries an asynchronous function with exponential backoff between attempts.
    /// The delay doubles after each failed attempt starting from <paramref name="initialDelay"/>.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="maxRetries">The maximum number of retry attempts. Must be greater than zero.</param>
    /// <param name="initialDelay">The initial delay before the first retry. Subsequent retries double this value.</param>
    /// <param name="maxDelay">The maximum delay between retries. Prevents unbounded backoff growth.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>The result of the operation on success.</returns>
    /// <exception cref="AggregateException">Thrown when all retry attempts fail, containing all collected exceptions.</exception>
    public static async Task<T> RetryWithBackoffAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (maxRetries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries must be greater than zero.");

        var currentDelay = initialDelay ?? TimeSpan.FromMilliseconds(200);
        var maximumDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        var exceptions = new List<Exception>();

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                exceptions.Add(ex);
                NoireLogger.LogWarning($"Retry with backoff attempt {attempt}/{maxRetries} failed: {ex.Message}", Prefix);

                await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);

                currentDelay = TimeSpan.FromTicks(Math.Min(currentDelay.Ticks * 2, maximumDelay.Ticks));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw new AggregateException($"All {maxRetries} retry attempts with backoff failed.", exceptions);
    }

    /// <summary>
    /// Retries an asynchronous action with exponential backoff between attempts.
    /// The delay doubles after each failed attempt starting from <paramref name="initialDelay"/>.
    /// </summary>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="maxRetries">The maximum number of retry attempts. Must be greater than zero.</param>
    /// <param name="initialDelay">The initial delay before the first retry. Subsequent retries double this value.</param>
    /// <param name="maxDelay">The maximum delay between retries. Prevents unbounded backoff growth.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the operation succeeds.</returns>
    /// <exception cref="AggregateException">Thrown when all retry attempts fail, containing all collected exceptions.</exception>
    public static async Task RetryWithBackoffAsync(
        Func<Task> operation,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await RetryWithBackoffAsync<bool>(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, maxRetries, initialDelay, maxDelay, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Batch Execution

    /// <summary>
    /// Processes a collection of items asynchronously with controlled concurrency, returning results for each item.
    /// Results are returned in the same order as the input items.
    /// </summary>
    /// <typeparam name="TItem">The type of the input items.</typeparam>
    /// <typeparam name="TResult">The type of the results.</typeparam>
    /// <param name="items">The items to process.</param>
    /// <param name="processor">The asynchronous function to apply to each item.</param>
    /// <param name="maxConcurrency">The maximum number of items to process in parallel. Must be greater than zero.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>An array of results in the same order as the input items.</returns>
    public static async Task<TResult[]> RunBatchAsync<TItem, TResult>(
        IEnumerable<TItem> items,
        Func<TItem, Task<TResult>> processor,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(processor);

        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than zero.");

        var itemList = items as IList<TItem> ?? items.ToList();
        var results = new TResult[itemList.Count];

        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = itemList.Select(async (item, index) =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                results[index] = await processor(item).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    /// <summary>
    /// Processes a collection of items asynchronously with controlled concurrency.
    /// </summary>
    /// <typeparam name="TItem">The type of the input items.</typeparam>
    /// <param name="items">The items to process.</param>
    /// <param name="processor">The asynchronous action to apply to each item.</param>
    /// <param name="maxConcurrency">The maximum number of items to process in parallel. Must be greater than zero.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when all items have been processed.</returns>
    public static async Task RunBatchAsync<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, Task> processor,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processor);

        await RunBatchAsync<TItem, bool>(items, async item =>
        {
            await processor(item).ConfigureAwait(false);
            return true;
        }, maxConcurrency, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a collection of items asynchronously with controlled concurrency and collects exceptions
    /// rather than failing on the first error. All items are attempted even if some fail.
    /// </summary>
    /// <typeparam name="TItem">The type of the input items.</typeparam>
    /// <param name="items">The items to process.</param>
    /// <param name="processor">The asynchronous action to apply to each item.</param>
    /// <param name="maxConcurrency">The maximum number of items to process in parallel. Must be greater than zero.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    /// <returns>A list of exceptions that occurred during processing. Empty if all items succeeded.</returns>
    public static async Task<List<Exception>> RunBatchSafeAsync<TItem>(
        IEnumerable<TItem> items,
        Func<TItem, Task> processor,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(processor);

        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than zero.");

        var exceptions = new List<Exception>();
        var lockObj = new object();

        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var itemList = items as IList<TItem> ?? items.ToList();

        var tasks = itemList.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await processor(item).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (lockObj)
                {
                    exceptions.Add(ex);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return exceptions;
    }

    #endregion

    #region Cancellation Helpers

    /// <summary>
    /// Creates a linked <see cref="CancellationTokenSource"/> that cancels when either the provided token is
    /// cancelled or the specified timeout elapses.
    /// The caller is responsible for disposing the returned token source.
    /// </summary>
    /// <param name="token">The cancellation token to link with.</param>
    /// <param name="timeout">The timeout after which cancellation is requested automatically.</param>
    /// <returns>A new <see cref="CancellationTokenSource"/> with linked cancellation and timeout.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is negative.</exception>
    public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token, TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must not be negative.");

        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout);
        return cts;
    }

    /// <summary>
    /// Creates a <see cref="CancellationTokenSource"/> that automatically cancels after the specified timeout.
    /// The caller is responsible for disposing the returned token source.
    /// </summary>
    /// <param name="timeout">The timeout after which cancellation is requested automatically.</param>
    /// <returns>A new <see cref="CancellationTokenSource"/> with the specified timeout.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is negative.</exception>
    public static CancellationTokenSource CreateTimeoutTokenSource(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must not be negative.");

        return new CancellationTokenSource(timeout);
    }

    /// <summary>
    /// Creates a linked <see cref="CancellationTokenSource"/> that cancels when any of the provided tokens is cancelled.
    /// The caller is responsible for disposing the returned token source.
    /// </summary>
    /// <param name="tokens">The cancellation tokens to link together.</param>
    /// <returns>A new <see cref="CancellationTokenSource"/> linked to all provided tokens.</returns>
    public static CancellationTokenSource CreateLinkedTokenSource(params CancellationToken[] tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return CancellationTokenSource.CreateLinkedTokenSource(tokens);
    }

    #endregion

    #region Condition Waiting

    /// <summary>
    /// Waits asynchronously until a condition becomes true, polling on each framework update frame.
    /// </summary>
    /// <param name="condition">The condition predicate to evaluate each frame.</param>
    /// <param name="timeout">An optional timeout. If <see langword="null"/>, waits indefinitely.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the wait.</param>
    /// <returns>A <see cref="Task"/> that completes when the condition becomes true.</returns>
    /// <exception cref="TimeoutException">Thrown when the timeout elapses before the condition is met.</exception>
    public static Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startTime = Environment.TickCount64;
        var timeoutMs = timeout.HasValue ? (long)timeout.Value.TotalMilliseconds : long.MaxValue;

        CancellationTokenRegistration? ctr = null;

        if (cancellationToken.CanBeCanceled)
        {
            ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        void OnUpdate(IFramework framework)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Detach();
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            if (Environment.TickCount64 - startTime > timeoutMs)
            {
                Detach();
                tcs.TrySetException(new TimeoutException("The condition was not met within the specified timeout."));
                return;
            }

            try
            {
                if (condition())
                {
                    Detach();
                    tcs.TrySetResult();
                }
            }
            catch (Exception ex)
            {
                Detach();
                tcs.TrySetException(ex);
            }
        }

        void Detach()
        {
            NoireService.Framework.Update -= OnUpdate;
            ctr?.Dispose();
        }

        NoireService.Framework.Update += OnUpdate;
        return tcs.Task;
    }

    /// <summary>
    /// Waits asynchronously until a condition becomes true, polling at a specified interval on a background thread.
    /// Use this for conditions that do not require framework-thread evaluation.
    /// </summary>
    /// <param name="condition">The condition predicate to evaluate at each polling interval.</param>
    /// <param name="pollInterval">The interval between condition checks.</param>
    /// <param name="timeout">An optional timeout. If <see langword="null"/>, waits indefinitely.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the wait.</param>
    /// <returns>A <see cref="Task"/> that completes when the condition becomes true.</returns>
    /// <exception cref="TimeoutException">Thrown when the timeout elapses before the condition is met.</exception>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan pollInterval,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be greater than zero.");

        var startTime = Environment.TickCount64;
        var timeoutMs = timeout.HasValue ? (long)timeout.Value.TotalMilliseconds : long.MaxValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (Environment.TickCount64 - startTime > timeoutMs)
                throw new TimeoutException("The condition was not met within the specified timeout.");

            if (condition())
                return;

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Waits asynchronously until an asynchronous condition becomes true, polling at a specified interval.
    /// </summary>
    /// <param name="condition">The asynchronous condition predicate to evaluate at each polling interval.</param>
    /// <param name="pollInterval">The interval between condition checks.</param>
    /// <param name="timeout">An optional timeout. If <see langword="null"/>, waits indefinitely.</param>
    /// <param name="cancellationToken">An optional cancellation token to cancel the wait.</param>
    /// <returns>A <see cref="Task"/> that completes when the condition becomes true.</returns>
    /// <exception cref="TimeoutException">Thrown when the timeout elapses before the condition is met.</exception>
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan pollInterval,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be greater than zero.");

        var startTime = Environment.TickCount64;
        var timeoutMs = timeout.HasValue ? (long)timeout.Value.TotalMilliseconds : long.MaxValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (Environment.TickCount64 - startTime > timeoutMs)
                throw new TimeoutException("The condition was not met within the specified timeout.");

            if (await condition().ConfigureAwait(false))
                return;

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    #endregion

    #region Background-to-Framework Switch Pattern

    /// <summary>
    /// Runs asynchronous work on a background thread, then applies the result on the framework thread.
    /// </summary>
    /// <typeparam name="T">The type of the computed result.</typeparam>
    /// <param name="backgroundWork">The async function to run on a background thread.</param>
    /// <param name="frameworkApply">The action to run on the framework thread with the computed result.</param>
    /// <param name="operationName">An optional name for the operation, used in log messages for diagnostics.</param>
    /// <returns>A <see cref="Task"/> that completes when the framework action has finished.</returns>
    public static async Task RunBackgroundThenFrameworkAsync<T>(
        Func<Task<T>> backgroundWork,
        Action<T> frameworkApply,
        string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(backgroundWork);
        ArgumentNullException.ThrowIfNull(frameworkApply);

        try
        {
            var result = await Task.Run(async () => await backgroundWork().ConfigureAwait(false)).ConfigureAwait(false);
            await RunOnFrameworkThreadAsync(() => frameworkApply(result)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Background-then-framework operation '{operationName ?? "unnamed"}' failed.", Prefix);
            throw;
        }
    }

    /// <summary>
    /// Runs synchronous work on a background thread, then applies the result on the framework thread.
    /// </summary>
    /// <typeparam name="T">The type of the computed result.</typeparam>
    /// <param name="backgroundWork">The function to run on a background thread.</param>
    /// <param name="frameworkApply">The action to run on the framework thread with the computed result.</param>
    /// <param name="operationName">An optional name for the operation, used in log messages for diagnostics.</param>
    /// <returns>A <see cref="Task"/> that completes when the framework action has finished.</returns>
    public static Task RunBackgroundThenFrameworkAsync<T>(
        Func<T> backgroundWork,
        Action<T> frameworkApply,
        string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(backgroundWork);

        return RunBackgroundThenFrameworkAsync(
            () => Task.FromResult(backgroundWork()),
            frameworkApply,
            operationName);
    }

    /// <summary>
    /// Runs asynchronous work on a background thread, then applies the result on the framework thread.
    /// Exceptions are caught and logged instead of thrown.
    /// </summary>
    /// <typeparam name="T">The type of the computed result.</typeparam>
    /// <param name="backgroundWork">The async function to run on a background thread.</param>
    /// <param name="frameworkApply">The action to run on the framework thread with the computed result.</param>
    /// <param name="onException">An optional callback invoked when an exception occurs.</param>
    /// <param name="operationName">An optional name for the operation, used in log messages for diagnostics.</param>
    /// <returns>A <see cref="Task"/> that completes when the operation finishes, regardless of exceptions.</returns>
    public static async Task RunBackgroundThenFrameworkSafeAsync<T>(
        Func<Task<T>> backgroundWork,
        Action<T> frameworkApply,
        Action<Exception>? onException = null,
        string? operationName = null)
    {
        try
        {
            await RunBackgroundThenFrameworkAsync(backgroundWork, frameworkApply, operationName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            onException?.Invoke(ex);
        }
    }

    #endregion
}
