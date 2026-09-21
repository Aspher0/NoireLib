namespace NoireLib.Websocket;

/// <summary>
/// Whether a message arrived as text or as bytes.
/// </summary>
public enum NoireWebsocketMessageKind
{
    /// <summary>A text message. Its payload was validated as UTF-8 when it was read.</summary>
    Text,

    /// <summary>A binary message. Nothing is assumed about its payload.</summary>
    Binary,
}
