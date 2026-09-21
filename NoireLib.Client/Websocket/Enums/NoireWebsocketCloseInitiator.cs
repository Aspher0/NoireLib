namespace NoireLib.Websocket;

/// <summary>
/// Which side ended a connection.
/// </summary>
public enum NoireWebsocketCloseInitiator
{
    /// <summary>This side asked for the close.</summary>
    Local,

    /// <summary>The peer sent the close frame.</summary>
    Remote,

    /// <summary>Neither side closed cleanly. The transport went away underneath.</summary>
    Transport,
}
