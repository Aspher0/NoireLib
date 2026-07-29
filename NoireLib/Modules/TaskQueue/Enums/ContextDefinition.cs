namespace NoireLib.TaskQueue;

/// <summary>
/// Defines how context boundaries are checked when determining if two tasks are in the same context.
/// Used for event capture depth checking and context-limited operations.
/// </summary>
public enum ContextDefinition
{
    /// <summary>
    /// No boundary checks; tasks are always in the same context, regardless of batches between them.
    /// </summary>
    CrossContext = 0,

    /// <summary>
    /// Same context if both tasks are in the same batch, or both are standalone with any batches between them; a batch/standalone split is never the same context.
    /// </summary>
    SameContext = 1,

    /// <summary>
    /// Same context if both tasks are in the same batch, or both are standalone with no batch between them; any batch boundary breaks it for standalone tasks.
    /// </summary>
    SameContextStrict = 2
}
