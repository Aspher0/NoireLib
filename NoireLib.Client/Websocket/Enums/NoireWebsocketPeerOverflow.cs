namespace NoireLib.Websocket;

/// <summary>
/// What the server does when a connection's outbound queue fills up. A stopped-reading peer looks like this.
/// </summary>
public enum NoireWebsocketPeerOverflow
{
    /// <summary>Close the connection with <see cref="NoireWebsocketCloseCode.TryAgainLater"/>. The default.</summary>
    CloseConnection,

    /// <summary>Discard that connection's oldest queued message and keep it open.</summary>
    DropOldest,

    /// <summary>Discard the message being sent to that connection and keep it open.</summary>
    DropNewest,
}
