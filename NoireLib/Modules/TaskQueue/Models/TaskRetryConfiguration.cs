using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Configuration for task retry behavior when the completion condition stalls, for use with <see cref="NoireTaskQueue"/>.
/// Use <see cref="TaskBuilder"/> to create and enqueue tasks.
/// </summary>
public class TaskRetryConfiguration
{
    /// <summary>
    /// Maximum number of retry attempts; if null, retries are unlimited.
    /// </summary>
    public int? MaxAttempts { get; set; }

    /// <summary>
    /// Duration the completion condition can remain false before triggering a retry; if null, stall detection is disabled.
    /// </summary>
    public TimeSpan? StallTimeout { get; set; }

    /// <summary>
    /// Delay to wait between retry attempts; if null, retries happen immediately after stall detection.
    /// </summary>
    public TimeSpan? RetryDelay { get; set; }

    /// <summary>
    /// Optional override action to execute on retry instead of the original ExecuteAction, receiving the task and the 1-based retry attempt number; if null, ExecuteAction is re-executed.
    /// </summary>
    public Action<QueuedTask, int>? OverrideRetryAction { get; set; }

    /// <summary>
    /// Optional callback invoked before each retry attempt, receiving the task and the 1-based retry attempt number.
    /// </summary>
    public Action<QueuedTask, int>? OnBeforeRetry { get; set; }

    /// <summary>
    /// Optional callback invoked when max retry attempts are exhausted.
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
