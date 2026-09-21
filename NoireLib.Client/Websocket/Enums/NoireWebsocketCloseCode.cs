namespace NoireLib.Websocket;

/// <summary>
/// The close codes RFC 6455 registers. A peer may send a code outside this set.
/// <see cref="NoireWebsocketClose.RawCode"/> is always readable.
/// </summary>
public enum NoireWebsocketCloseCode
{
    /// <summary>The purpose the connection was opened for is finished.</summary>
    Normal = 1000,

    /// <summary>The endpoint is going away, such as a server shutting down.</summary>
    GoingAway = 1001,

    /// <summary>A protocol error.</summary>
    ProtocolError = 1002,

    /// <summary>A message of a kind this endpoint cannot accept.</summary>
    UnsupportedData = 1003,

    /// <summary>No code was present in the close frame. Never sent, only reported.</summary>
    NoStatusReceived = 1005,

    /// <summary>The connection ended without a close frame at all. Never sent, only reported.</summary>
    AbnormalClosure = 1006,

    /// <summary>A text message whose payload was not valid UTF-8.</summary>
    InvalidPayload = 1007,

    /// <summary>A message the endpoint refuses on policy grounds.</summary>
    PolicyViolation = 1008,

    /// <summary>A message past the size this endpoint accepts.</summary>
    MessageTooBig = 1009,

    /// <summary>The client needed an extension the server did not negotiate.</summary>
    MandatoryExtension = 1010,

    /// <summary>The endpoint hit a condition it could not complete the request under.</summary>
    InternalError = 1011,

    /// <summary>The service is restarting.</summary>
    ServiceRestart = 1012,

    /// <summary>The service is overloaded, or the client should back off for another reason.</summary>
    TryAgainLater = 1013,

    /// <summary>The TLS handshake failed. Never sent, only reported.</summary>
    TlsHandshake = 1015,
}
