namespace NoireLib.Websocket;

/// <summary>
/// What a caller's own send queue does once it is full.
/// </summary>
public enum NoireSocketSendOverflow
{
    /// <summary>Throw <see cref="NoireSocketQueueFullException"/>. The default.</summary>
    Fail,

    /// <summary>Discard the oldest queued message to make room.</summary>
    DropOldest,

    /// <summary>Discard the message being sent.</summary>
    DropNewest,

    /// <summary>Wait for room. Only safe to await. Never block a send on the host thread.</summary>
    Block,
}
