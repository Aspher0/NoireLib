using System;

namespace NoireLib.TaskQueue;

/// <summary>
/// Exception thrown when a task exceeds its maximum retry attempts.
/// </summary>
public class MaxRetryAttemptsExceededException : Exception
{
    /// <summary>
    /// Creates a new MaxRetryAttemptsExceededException.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public MaxRetryAttemptsExceededException(string message) : base(message) { }

    /// <summary>
    /// Creates a new MaxRetryAttemptsExceededException with an inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public MaxRetryAttemptsExceededException(string message, Exception innerException) : base(message, innerException) { }
}
