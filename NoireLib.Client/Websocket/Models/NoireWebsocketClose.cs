namespace NoireLib.Websocket;

/// <summary>
/// How a connection ended.
/// </summary>
public sealed class NoireWebsocketClose
{
    internal NoireWebsocketClose(int rawCode, string? reason, NoireWebsocketCloseInitiator initiator)
    {
        RawCode = rawCode;
        Reason = reason;
        Initiator = initiator;
    }

    /// <summary>
    /// Gets the close code as a registered value, or null when the peer sent one outside the registry.
    /// </summary>
    public NoireWebsocketCloseCode? Code
        => System.Enum.IsDefined(typeof(NoireWebsocketCloseCode), RawCode) ? (NoireWebsocketCloseCode)RawCode : null;

    /// <summary>
    /// Gets the close code as it arrived. 1005 means a close frame with no code. 1006 means no close frame at all.
    /// </summary>
    public int RawCode { get; }

    /// <summary>
    /// Gets the reason text, when one was sent.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets whether the close handshake completed cleanly.
    /// </summary>
    public bool WasClean => RawCode != (int)NoireWebsocketCloseCode.AbnormalClosure;

    /// <summary>
    /// Gets which side ended it.
    /// </summary>
    public NoireWebsocketCloseInitiator Initiator { get; }
}
