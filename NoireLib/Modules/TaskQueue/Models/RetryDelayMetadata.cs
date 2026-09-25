namespace NoireLib.TaskQueue;

internal class RetryDelayMetadata
{
    public long DelayUntilTicks { get; set; }

    public object? OriginalMetadata { get; set; }
}
