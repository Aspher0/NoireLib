namespace NoireLib.Remote;

/// <summary>
/// The state of the listener.
/// </summary>
public enum NoireRemoteListenerState
{
    /// <summary>Not listening, and nothing is being started.</summary>
    Stopped,

    /// <summary>Binding the socket.</summary>
    Starting,

    /// <summary>Bound and accepting connections.</summary>
    Listening,

    /// <summary>The bind failed. The failure is logged and the listener stays down.</summary>
    Failed,
}
