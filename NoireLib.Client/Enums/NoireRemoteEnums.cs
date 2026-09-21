namespace NoireLib.Remote;

/// <summary>
/// Which thread a published member runs on.
/// </summary>
public enum NoireRemoteThread
{
    /// <summary>Takes the value from the class attribute, then from the listener options, then from the host.</summary>
    Inherit,

    /// <summary>The host's own thread. Inside a plugin that is the framework thread, where game state is safe to touch.</summary>
    Framework,

    /// <summary>A thread pool thread, bounded by the concurrent call limit.</summary>
    Background,
}

/// <summary>
/// The character state a published member needs before it runs.
/// </summary>
public enum NoireRemoteReadiness
{
    /// <summary>Takes the value from the class attribute, then from the thread the member runs on.</summary>
    Inherit,

    /// <summary>No gate. The member never reads character state.</summary>
    None,

    /// <summary>Character state is loaded. Required to read a game struct.</summary>
    StateReady,

    /// <summary>The player object exists. Required to call a game function.</summary>
    PlayerLoaded,
}

/// <summary>
/// Whether an endpoint or a member accepts a call from another machine.
/// </summary>
public enum NoireRemoteAccess
{
    /// <summary>Takes the value from the class attribute, then from the listener options.</summary>
    Inherit,

    /// <summary>Loopback connections only.</summary>
    Local,

    /// <summary>Loopback and signed non-loopback connections.</summary>
    Remote,
}

/// <summary>
/// Whether a call answers with its result or with a job to poll.
/// </summary>
public enum NoireRemoteCallMode
{
    /// <summary>Takes the value from the class attribute, then from the listener options.</summary>
    Inherit,

    /// <summary>The response carries the result. The call blocks the connection until the member finishes.</summary>
    Sync,

    /// <summary>The response carries a job id. The result is collected by polling the job route.</summary>
    Job,
}

/// <summary>
/// The state of a job started by a call in job mode.
/// </summary>
public enum NoireRemoteJobState
{
    /// <summary>The member has not finished.</summary>
    Running,

    /// <summary>The member finished and the result is available.</summary>
    Done,

    /// <summary>The member threw and the error is available.</summary>
    Failed,

    /// <summary>The job was cancelled before it finished.</summary>
    Cancelled,

    /// <summary>This listener has no job by that id. It may have been swept, or belonged to a listener that restarted.</summary>
    Unknown,
}
