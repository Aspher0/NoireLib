namespace NoireLib.Websocket.Internal;

// A handler's readiness answer is cached against the drain generation.
internal readonly record struct SocketInbound(NoireWebsocketConnection Connection, NoireWebsocketMessage Message, long Generation);

internal readonly record struct SocketClosed(NoireWebsocketConnection Connection, NoireWebsocketClose Close);
