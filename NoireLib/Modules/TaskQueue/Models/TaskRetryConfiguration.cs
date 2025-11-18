using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Configuration for task retry behavior when the completion condition stalls.<br/>
/// Allows automatic retry of task execution if the completion condition doesn't progress.<br/>
/// Meant to be used with the <see cref="NoireTaskQueue"/> module.<br/>
/// For ease of use, consider using the <see cref="TaskBuilder"/> to create tasks and enqueue them.
/// </summary>
public class TaskRetryConfiguration
{
    /// <summary>
    /// Maximum number of retry attempts. If null, retries are unlimited.
    /// </summary>
    public int? MaxAttempts { get; set; }

    /// <summary>
    /// Duration that the completion condition can remain false before triggering a retry.
    /// If null, stall detection is disabled.
    /// </summary>
    public TimeSpan? StallTimeout { get; set; }

    /// <summary>
    /// Delay to wait between retry attempts.
    /// If null, retries happen immediately after stall detection.
    /// </summary>
    public TimeSpan? RetryDelay { get; set; }

    /// <summary>
    /// Optional override action to execute on retry instead of the original ExecuteAction.
    /// Receives the task and the retry attempt number (1-based: 1 = first retry, 2 = second retry, etc).
    /// If null, the original ExecuteAction will be re-executed.
    /// </summary>
    public Action<QueuedTask, int>? OverrideRetryAction { get; set; }

    /// <summary>
    /// Optional callback invoked before each retry attempt.
    /// Receives the task and the retry attempt number (1-based: 1 = first retry, 2 = second retry, etc).
    /// </summary>
    public Action<QueuedTask, int>? OnBeforeRetry { get; set; }

    /// <summary>
    /// Optional callback invoked when max retry attempts are exhausted.
    /// Receives the task instance.
    /// </summary>
    public Action<QueuedTask>? OnMaxRetriesExceeded { get; set; }

    /// <summary>
    /// Creates a retry configuration with unlimited attempts.
    /// </summary>
    /// <param name="stallTimeout">Duration before considering the condition stalled.</param>
    /// <param name="retryDelay">Optional delay between retries.</param>
    /// <returns>A new retry configuration.</returns>
    public static TaskRetryConfiguration Unlimited(TimeSpan stallTimeout, TimeSpan? retryDelay = null)
    {
        return new TaskRetryConfiguration
        {
            MaxAttempts = null,
            StallTimeout = stallTimeout,
            RetryDelay = retryDelay
        };
    }

    /// <summary>
    /// Creates a retry configuration with a maximum number of attempts.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of retry attempts (does not include the initial attempt).</param>
    /// <param name="stallTimeout">Duration before considering the condition stalled.</param>
    /// <param name="retryDelay">Optional delay between retries.</param>
    /// <returns>A new retry configuration.</returns>
    public static TaskRetryConfiguration WithMaxAttempts(int maxAttempts, TimeSpan stallTimeout, TimeSpan? retryDelay = null)
    {
        return new TaskRetryConfiguration
        {
            MaxAttempts = maxAttempts,
            StallTimeout = stallTimeout,
            RetryDelay = retryDelay
        };
    }
}
