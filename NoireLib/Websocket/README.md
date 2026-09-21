# NoireLib Documentation - NoireWebsocket

You are reading the documentation for the `NoireWebsocket` system.

Four client transports and a server, beside NoireRemote. The clients live in `NoireLib.Client` and work without Dalamud. `NoireLib` adds a static facade over them.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [The raw WebSocket client](#the-raw-websocket-client)
- [Server-sent events](#server-sent-events)
- [Long-polling](#long-polling)
- [Socket.IO](#socketio)
- [Shared hosting, LiteSpeed and the polling lever](#shared-hosting-litespeed-and-the-polling-lever)
- [The server](#the-server)
- [Rooms and broadcast](#rooms-and-broadcast)
- [Talking to a plugin from outside the game](#talking-to-a-plugin-from-outside-the-game)
- [Threading](#threading)
- [Reconnection](#reconnection)
- [Options reference](#options-reference)
- [Honest limits](#honest-limits)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

- **Four independent transports**: a raw RFC 6455 client, server-sent events, long-polling and Socket.IO.
- **A server on the existing listener**, upgrading on the NoireRemote socket, with rooms, broadcast and per-connection state.
- **One options object per transport**, usable as `new XOptions()`.
- **Escape hatches**: `ConfigureHandler` reaches the `SocketsHttpHandler`, `ConfigureSocket` reaches `ClientWebSocketOptions`, and `NoireSocketIOClient.Underlying` is the protocol package's own client.
- **Dalamud-free**: everything but the facade is in `NoireLib.Client`. It references only the base class library, Newtonsoft.Json and the Socket.IO packages.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Connect to something

```csharp
using NoireLib.Websocket;

var socket = NoireWebsocket.Connect("wss://example.tld/hub", c => c.OnMessage(m => Handle(m.Text)));
```

The `configure` callback runs before the first byte moves. Handlers attached later miss what arrives first, and `OnOpen` could fire before they register.

### 2. Send

```csharp
await socket.SendAsync("hello");
await socket.SendJsonAsync(new { kind = "greet", name = "Aspher" });
```

### 3. Publish a socket of your own

```csharp
var chat = NoireWebsocket.Publish("chat");

chat.OnMessage((client, message) => chat.Broadcast(message.Text, except: client));
```

A caller reaches it at `ws://127.0.0.1:<port>/noire/ws/chat`. `NoireWebsocket.LocalUrl("chat")` builds that.

---

## The raw WebSocket client

```csharp
var socket = NoireWebsocket.Connect("wss://example.tld/hub", c =>
{
    c.OnOpen(() => Log("open"));
    c.OnMessage(m => Handle(m.Text));
    c.OnClose(close => Log($"closed {close.RawCode} {close.Reason}"));
    c.OnError(ex => Log(ex.Message));
});
```

Every `On` has a `Func<..., Task>` overload, awaited before the next message. Each returns a `NoireSocketSubscription` to dispose. `UnsubscribeAll(owner)` drops everything under an owner.

The primitive underneath is public:

```csharp
var client = new NoireWebsocketClient(url, options);
client.OnMessage(Handle);
await client.ConnectAsync();
```

`message.Bytes` points into a pooled buffer, **valid only during the callback**. `message.ToArray()` keeps the payload. `message.Json<T>()` reads it.

---

## Server-sent events

Reads `text/event-stream`.

```csharp
var events = NoireWebsocket.ConnectSse("https://example.tld/events", c =>
{
    c.OnEvent(e => Log($"{e.Type}: {e.Data}"));
});
```

`Last-Event-ID` is sent on every reconnect once an id has been seen. A `retry:` field overrides the initial delay. `OnComment` reports the keep-alive comment lines.

---

## Long-polling

```csharp
var poll = NoireWebsocket.ConnectLongPoll("https://example.tld/poll", c =>
{
    c.OnResponse(r => Handle(r.Json<Update>()));
});
```

Each answer's cursor goes into the next request, as a query parameter or a header named on the options. A 4xx stops the loop. 408, 429 and 5xx retry. `Body` with `Method = Get` throws at connect.

---

## Socket.IO

Built on the `SocketIOClient` package, with every payload going through Newtonsoft.

```csharp
var io = NoireWebsocket.ConnectSocketIO("https://ws.example.tld/", c =>
{
    c.On("welcome", e => Log(e.Get<string>(0) ?? string.Empty));
    c.On("echo", e => Log(e.Get<string>(0) ?? string.Empty));
});

await io.EmitAsync("message", "hello");
```

An acknowledgement in either direction:

```csharp
var reply = await io.EmitAndWaitAsync("ask", ["how many"], TimeSpan.FromSeconds(5));

c.On("ping", async e => await e.ReplyAsync("pong"));
```

`Get<T>(index)` returns null past the last argument. `Underlying` is the package's own client.

---

## Shared hosting, LiteSpeed and the polling lever

Some shared hosts run a LiteSpeed proxy that wraps WebSocket frames in chunked transfer-encoding. A conforming client rejects that with **"received a frame with one or more reserved bits set"**. Long-polling is plain HTTP and passes.

```csharp
var io = NoireWebsocket.ConnectSocketIO(url, new NoireSocketIOOptions
{
    Transport = NoireSocketIOTransport.PollingOnly,
});
```

| Value | Behaviour |
| --- | --- |
| `PollingThenUpgrade` | The default. Starts on polling, upgrades when the server offers it |
| `PollingOnly` | Never upgrades. This is the setting for the host described above |
| `WebSocketOnly` | No polling handshake at all |

**`PollingOnly` clears `AutoUpgrade`.** The package's `AutoUpgrade` defaults to `true` and upgrades whenever the handshake offers WebSocket, including on a later reconnect.

Two more constraints on such hosts, not enforced for you:

- **No custom ports.** Connect on 443 over `https://`. A `ws://` or `wss://` URL is warned about in `UrlWarning`.
- **Send a User-Agent.** `NoireSocketHttpOptions.Headers` carries one by default. Some firewalls reject requests without one.

Socket.IO's reconnection is additive: a small fixed step per attempt up to `MaxDelay`. `NoireSocketIOReconnect` maps onto it without a second loop. Two loops would open two sockets.

---

## The server

```csharp
var chat = NoireWebsocket.Publish("chat");

chat.OnOpen(client => client.Send("welcome"));
chat.OnMessage((client, message) => chat.Broadcast(message.Text, except: client));
chat.OnClose((client, close) => Log($"{client.Id} left"));
```

Or declaratively, discovered during `NoireLibMain.Initialize`:

```csharp
[NoireWebsocketEndpoint("chat")]
public sealed class ChatSocket
{
    [NoireWebsocketOn]
    public void OnMessage(NoireWebsocketConnection client, NoireWebsocketMessage message)
        => client.Endpoint.Broadcast(message.Text, except: client);
}
```

The attribute is sugar over `Publish`. A method's role follows from its signature: connection and message, connection and close, connection alone, or an exception.

A connection carries `Id`, `RemoteAddress`, `IsLoopback`, `SubProtocol`, `OpenedUtc`, an `Items` bag for your own state, and `QueuedMessages` for throttling before the overflow policy fires.

**The security gate is unchanged.** An upgrade passes the same checks as a call. A browser page sends `Origin`, refused by the gate, and cannot set `Authorization`.

---

## Rooms and broadcast

```csharp
client.Join("lobby");
chat.Broadcast("someone arrived", room: "lobby", except: client);
```

Rooms are a convenience over a predicate. Both are available:

```csharp
chat.Broadcast("ping", where: c => c.IsLoopback);
chat.Broadcast("ping", room: "lobby", where: c => c.Items.ContainsKey("ready"));
```

A room exists while it has members. `MaxRooms`, `MaxRoomsPerConnection` and `MaxRoomNameLength` are enforced on `Join`. A peer's message can drive it.

`Clients` and `Rooms` are snapshots, safe to enumerate while connections change.

---

## Talking to a plugin from outside the game

A plugin's own surface is published and called through [NoireRemote](../Remote/README.md), with discovery, credentials, reconnection and events. Use these clients for servers that are not NoireLib listeners.

## Threading

Callbacks go through a pump that hops **once per batch**. `OnOpen` comes before the first message, and a handler returning a `Task` is awaited before the next item.

`Thread` takes `NoireRemoteThread.Inherit` (the default), `Framework` or `Background`, resolved from the options, then the host's default, then the framework thread. Outside a plugin, handlers run inline.

`Host` is null by default and resolved when the connection opens. An options object built in a field initializer would otherwise capture the standalone host before NoireLib installs the plugin one.

---

## Reconnection

`NoireRetryPolicy` is an immutable record shared by the raw, SSE and long-poll clients:

```csharp
var options = new NoireWebsocketClientOptions
{
    Retry = NoireRetryPolicy.Default with { MaxDelay = TimeSpan.FromMinutes(2), Jitter = 0.5 },
};
```

Defaults: one second, doubling, capped at thirty, a quarter of jitter, no attempt limit, and a clean close ends it. `NoireRetryPolicy.None` never reconnects. Jitter keeps a fleet from reconnecting in lockstep.

---

## Options reference

Every options type works as `new XOptions()` and has `Clone()`.

**`NoireWebsocketClientOptions`**: `SubProtocols`, `Http`, `KeepAliveInterval`, `IdleTimeout`,
`MaxMessageSize`, `ReceiveBufferSize`, `Retry`, `SendQueueCapacity`, `Overflow`, `Thread`, `Host`,
`ConfigureSocket`, `ConfigureHandler`.

**`NoireSseOptions`**: `Http`, `Retry`, `Thread`, `Host`, `MaxEventSize`, `ConfigureHandler`.

**`NoireLongPollOptions`**: `Http`, `Method`, `Body`, `Retry`, `Thread`, `Host`, `PollTimeout`,
`EmptyPollDelay`, `CursorParameter`, `CursorHeader`, `CursorField`, `ReadCursor`, `InitialCursor`,
`ConfigureHandler`.

**`NoireSocketIOOptions`**: `Namespace`, `Transport`, `Path`, `Auth`, `Query`, `Http`, `Reconnect`,
`Thread`, `Host`, `ConfigureOptions`.

**`NoireSocketHttpOptions`** (on all four): `Headers`, `Proxy`, `UseSystemProxy`, `Cookies`,
`ClientCertificates`, `Credentials`, `RemoteCertificateValidationCallback`, `ConnectionTimeout`,
`PooledConnectionLifetime`, `Credential`.

**`NoireWebsocketServerOptions`**: `MaxSockets` (64), `MaxSocketsPerAddress` (32), `MaxRooms` (256),
`MaxRoomsPerConnection` (16), `MaxRoomNameLength` (64), `MaxFrameSize` (1 MB), `MaxMessageSize` (4 MB),
`MaxMessageFragments` (256), `PeerQueueCapacity` (256), `Overflow`, `IdleTimeout` (120s),
`HeartbeatInterval` (30s), `CloseTimeout` (5s), `Thread`.

**`NoireWebsocketEndpointOptions`**: `SubProtocols`, `RequireSubProtocol`, `Access`, `Requires`, `Thread`.

`MaxSocketsPerAddress` counts loopback too. Every loopback connection shares `127.0.0.1`.

---

## Honest limits

- **The keep-alive is not always answered.** Before .NET 9 the managed keep-alive sent unsolicited Pongs. `IdleTimeout` detects a dead peer. `KeepAliveTimeout` exists at runtime but not in the framework `NoireLib.Client` compiles against. Reach it through `ConfigureSocket`.
- **`permessage-deflate` is not implemented.** The handshake never echoes `Sec-WebSocket-Extensions`. A client offering compression connects and must not compress.
- **A browser cannot reach the API port.** The browser console has its own port and gate.
- **Outbound Socket.IO binary attachments are untested.** Inbound ones are read.

---

## Troubleshooting

### "received a frame with one or more reserved bits set"

- A host is wrapping WebSocket frames in chunked transfer-encoding. Set `Transport = NoireSocketIOTransport.PollingOnly`.
- Connect over `https://` on 443, not `wss://` or a custom port.
- A handshake showing `"upgrades":["websocket"]` means the server does not advertise polling.

### The connection opens and immediately closes

- Check `LastClose.RawCode`. 1009: past `MaxMessageSize`. 1007: a text frame that was not UTF-8. 1002: a protocol error, a compressed frame included.
- 1006: no close frame arrived. A transport drop.

### Callbacks run on the wrong thread

- Leave `Options.Host` null.
- A connection opened before `NoireLibMain.Initialize` delivers inline until the host is installed.

### A caller outside the game gets 401 or 403

- 401: a missing or wrong credential. Pass `NoireWebsocket.ConnectTo` a directory record.
- 403 off loopback: the endpoint's `Access` is `Local`, or the request carried a browser header.

### Nothing arrives, and no error either

- Handlers attached after the connect call miss what arrived first. Attach inside `configure`.
- Check `QueuedMessages`. A full queue under `DropNewest` discards by design.

Check `/xllog` for the listener's lines, and report it.

---

## See Also

- [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [NoireRemote](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Remote/README.md)
- [NoireNetworker](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/Networker/README.md)
