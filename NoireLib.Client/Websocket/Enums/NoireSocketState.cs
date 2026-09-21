namespace NoireLib.Websocket;

/// <summary>
/// Where a connection is in its lifecycle.
/// </summary>
public enum NoireSocketState
{
    /// <summary>Nothing is open and nothing is being attempted.</summary>
    Disconnected,

    /// <summary>The first attempt is in flight.</summary>
    Connecting,

    /// <summary>Open, and carrying messages.</summary>
    Connected,

    /// <summary>Closed unexpectedly, waiting out the retry delay before attempting again.</summary>
    Reconnecting,

    /// <summary>A close was started and the handshake has not finished.</summary>
    Closing,

    /// <summary>Stopped for good. Only reached when the retry policy sets a limit and that limit ran out.</summary>
    Faulted,
}
