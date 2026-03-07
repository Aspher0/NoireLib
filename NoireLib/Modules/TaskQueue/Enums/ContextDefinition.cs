namespace NoireLib.TaskQueue;

/// <summary>
/// Defines how context boundaries are checked when determining if two tasks are in the same context.
/// Used for event capture depth checking and context-limited operations.
/// </summary>
public enum ContextDefinition
{
    /// <summary>
    /// No boundary checks - fully cross-context.<br/>
    /// Tasks are always considered in the same context regardless of batch boundaries.<br/>
    /// There can be any number of batches and tasks between two tasks.
    /// </summary>
    CrossContext = 0,

    /// <summary>
    /// Same context with flexible batch boundaries.<br/>
    /// Two tasks are considered in the same context if:<br/>
    /// - They are both within the same batch, OR<br/>
    /// - They are both standalone tasks in the queue (with possible batches in between).<br/>
    /// Tasks in different contexts (one in batch, one standalone) are not in the same context.
    /// </summary>
    SameContext = 1,

    /// <summary>
    /// Strict boundary check with no batch separation allowed.<br/>
    /// Two tasks are considered in the same context if:<br/>
    /// - They are both within the same batch, OR<br/>
    /// - They are both standalone tasks in the queue with NO batch separating them.<br/>
    /// Any batch boundary breaks the context for standalone tasks.
    /// </summary>
    SameContextStrict = 2
}
