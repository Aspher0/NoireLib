namespace NoireLib.Websocket;

/// <summary>
/// Which Engine.IO transports a Socket.IO connection is allowed to use.
/// </summary>
public enum NoireSocketIOTransport
{
    /// <summary>Start on long-polling and upgrade to WebSocket when the handshake offers it. The default.</summary>
    PollingThenUpgrade,

    /// <summary>Long-polling only. LiteSpeed on cPanel wraps WebSocket frames in chunked transfer-encoding. A conforming client rejects them.</summary>
    PollingOnly,

    /// <summary>WebSocket only, with no polling handshake at all. Fails outright where an upgrade cannot complete.</summary>
    WebSocketOnly,
}
