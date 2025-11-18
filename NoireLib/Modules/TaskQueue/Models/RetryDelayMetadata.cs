namespace NoireLib.TaskQueue;

/// <summary>
/// Internal metadata for tracking retry delays.
/// </summary>
internal class RetryDelayMetadata
{
    /// <summary>
    /// The tick count when the delay will be complete.
    /// </summary>
    public long DelayUntilTicks { get; set; }

    /// <summary>
    /// The original metadata that was stored before the retry delay.
    /// </summary>
    public object? OriginalMetadata { get; set; }
}
