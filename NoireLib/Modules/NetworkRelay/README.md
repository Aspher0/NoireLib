# Module Documentation : NoireNetworkRelay

You are reading the documentation for the `NoireNetworkRelay` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Transport Model](#transport-model)
- [Lifecycle and Module Registration](#lifecycle-and-module-registration)
- [Self Registration](#self-registration)
- [Configuration](#configuration)
- [Channels, Messages, and Payloads](#channels-messages-and-payloads)
- [Sending Data](#sending-data)
- [Receiving Data](#receiving-data)
- [Peer Management](#peer-management)
- [EventBus Integration](#eventbus-integration)
- [Statistics and Diagnostics](#statistics-and-diagnostics)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireNetworkRelay` module provides **hybrid local-network communication** for NoireLib modules and plugins.
It keeps **UDP** for lightweight best-effort traffic and adds **TCP** for reliable delivery when you need stronger guarantees.

The module includes:
- **UDP best-effort delivery** for fast, lossy-acceptable traffic
- **TCP reliable delivery** for controlled and reliable traffic
- **Broadcast delivery** for LAN-wide UDP messages
- **Direct peer delivery** for known endpoints
- **Direct endpoint delivery** for arbitrary host/port targets
- **Automatic peer discovery** through presence announcements
- **Peer tracking** with expiration, manual registration, and auto-registration support
- **Reliable peer metadata** through tracked TCP endpoints
- **Self registration** for the local relay instance with activation control
- **Typed payload subscriptions** through `Subscribe<TPayload>(...)`, `On<TPayload>(...)`, and async variants
- **Raw message subscriptions** when sender or transport metadata is required
- **Keyed subscriptions** for named, replaceable handlers
- **Owner-based subscription grouping** for bulk unsubscription
- **EventBus bridge support** for relaying local events outward and republishing remote events locally
- **Awaitable reliable sends** with built-in acknowledgement support
- **Duplicate suppression** to reduce repeated packet processing
- **Peer allow-list** for restricting accepted senders
- **Runtime statistics** for monitoring both UDP and TCP activity
- **Fluent configuration** for transport, discovery, filtering, and serializer settings

In short:
- use **UDP** when you do not care if a packet is occasionally lost
- use **TCP** when you want the message to be delivered through a reliable transport path

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### Important: Self Registration

Before the relay can send any data, the local instance must be **registered** and **active** as a peer.
The relay enforces this through `RegisterSelf()`, which advertises the local instance on the network.

The simplest way is to pass `selfActiveOnStart: true` in the constructor, which automatically registers and activates the local instance.
Alternatively, call `RegisterSelf()` and `ActivateSelf()` manually after creation.

See the [Self Registration](#self-registration) section for full details.

### Minimal Best-Effort Usage

```csharp
var relay = NoireLibMain.AddModule(new NoireNetworkRelay(
    moduleId: "NetworkRelay",
    selfActiveOnStart: true,
    port: 53741,
    enablePeerDiscovery: true));

relay.On<string>(message =>
{
    NoireLogger.PrintToChat($"Received: {message}");
}, channel: "chat");

relay.Send("Hello from another client!", channel: "chat");
```

What this does:
- registers a relay module listening on UDP port `53741`
- registers and activates the local instance as a peer (`selfActiveOnStart: true`)
- subscribes to incoming string payloads on the logical channel `chat`
- broadcasts a best-effort UDP string payload on that same channel

### Minimal Reliable Usage

```csharp
var relay = NoireLibMain.AddModule(new NoireNetworkRelay(
    moduleId: "NetworkRelay",
    selfActiveOnStart: true,
    port: 53741,
    enableReliableTransport: true,
    reliablePort: 53742,
    enablePeerDiscovery: true));

relay.RegisterPeer("client-b", "192.168.1.25", 53741, 53742, "Other Client");

relay.SendReliableToPeer(
    peerId: "client-b",
    payload: "Start the next step",
    channel: "control.start");
```

This uses TCP for the send operation instead of UDP.

### Same-Host Multi-Instance Tip

If you want to test **two game instances on the same PC**, give them different port pairs.

For example:
- instance A: UDP `53741`, TCP `53742`
- instance B: UDP `53751`, TCP `53752`

Then manually register each instance as a peer of the other:

```csharp
relay.RegisterPeer("other-local-instance", "127.0.0.1", 53751, 53752, "Other Local Instance");
```

This matters because:
- two TCP listeners cannot both bind the same port on the same host
- if two instances use different UDP ports, discovery broadcasts alone will not fully connect them
- direct peer registration and direct endpoint sends are the cleanest same-host testing workflow

### Minimal Usage with EventBus

If you intend to relay `EventBus` events between clients, attach the bus when creating the module:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus");

var relay = NoireLibMain.AddModule(new NoireNetworkRelay(
    moduleId: "NetworkRelay",
    selfActiveOnStart: true,
    eventBus: eventBus,
    port: 53741,
    enableReliableTransport: true,
    reliablePort: 53742,
    enablePeerDiscovery: true));
```

This does **not** automatically bridge every event type by itself. It simply gives the relay access to the bus so that:
- integration events can be published into it
- bridge registrations such as `BridgeEvent<TEvent>(...)` can be used later

---

## Transport Model

### Best-Effort vs Reliable

The relay supports two delivery modes.

#### `NetworkRelayDeliveryMode.BestEffort`

This uses **UDP**.

Choose it for:
- presence announcements
- transient state updates
- telemetry
- periodic sync where another update is coming soon anyway
- messages where occasional loss is acceptable

#### `NetworkRelayDeliveryMode.Reliable`

This uses **TCP**.

Choose it for:
- guaranteed delivery
- control flows
- request/response style coordination
- important state transitions
- messages that should not be silently dropped by the transport layer

### Important Broadcast Note

UDP supports broadcast.
TCP does **not**.

Because of that:
- `Broadcast(...)` with best-effort delivery sends one UDP broadcast packet
- `Broadcast(...)` with reliable delivery becomes **direct TCP fan-out to known peers**

That means reliable fan-out requires peers to be known through:
- manual registration via `RegisterPeer(...)`
- discovery plus auto-registration via `SetPeerDiscovery(true, autoRegisterPeers: true)`

### Message Transport Metadata

Every received `NetworkRelayMessage` carries transport information:
- `TransportKind` (`NetworkRelayTransportKind.Udp` or `NetworkRelayTransportKind.Tcp`)
- `IsReliable` (convenience boolean, `true` when `TransportKind` is `Tcp`)

### Peer Transport Metadata

Each `NetworkRelayPeer` carries:
- `EndPoint` for UDP
- `ReliableEndPoint` for TCP (nullable, only present when the peer advertises a reliable port)

Discovery announcements also advertise the reliable TCP port when enabled.

### What Is Separated Between UDP and TCP

The relay separates the following transport-specific settings:
- `Port` vs `ReliablePort`
- `UdpMaxPayloadBytes` vs `ReliableMaxPayloadBytes`
- `UdpReceiveBufferSize` / `UdpSendBufferSize`
- `ReliableReceiveBufferSize` / `ReliableSendBufferSize`
- UDP-only settings like `EnableBroadcast` and `TimeToLive`
- TCP-only settings like `ReliableConnectTimeout`, `ReliableOperationTimeout`, and `ReliableAcknowledgementTimeout`

Compatibility helpers still exist:
- `SetMaxPayloadBytes(...)` sets both UDP and TCP payload limits
- `SetSocketBuffers(...)` sets both UDP and TCP socket buffers

If you need transport-specific tuning, prefer:
- `SetUdpMaxPayloadBytes(...)`
- `SetReliableMaxPayloadBytes(...)`
- `SetTransportPayloadLimits(...)`
- `SetUdpSocketBuffers(...)`
- `SetReliableSocketBuffers(...)`
- `SetTransportSocketBuffers(...)`

---

## Lifecycle and Module Registration

### Constructor Registration

Create the module manually and register it through `NoireLibMain`:

```csharp
var relay = NoireLibMain.AddModule(new NoireNetworkRelay(
    moduleId: "MyRelay",
    active: true,
    enableLogging: true,
    selfActiveOnStart: true,
    instanceId: "client-a",
    displayName: "Client A",
    port: 53741,
    enableReliableTransport: true,
    reliablePort: 53742,
    enableBroadcast: true,
    autoAnnounceOnStart: true,
    enablePeerDiscovery: true,
    allowLoopbackMessages: false,
    defaultChannel: "default",
    exceptionHandling: ExceptionBehavior.LogAndContinue,
    eventBus: eventBus));
```

### Activation and Deactivation

The relay transport is tied to module activation state.

```csharp
relay.Start();
relay.Stop();
```

When activated, the relay:
- starts the UDP socket transport
- starts the TCP listener when reliable transport is enabled
- begins receiving datagrams and reliable framed payloads
- starts periodic peer announcements if discovery is enabled and `AnnouncementInterval > TimeSpan.Zero`
- optionally broadcasts presence immediately if `AutoAnnounceOnStart` is enabled and the local instance is registered and active

When deactivated, the relay:
- stops the UDP transport
- stops the TCP transport
- stops background loops
- disposes internal transport resources

---

## Self Registration

The relay requires the local instance to be **registered** and **active** before it can send data.
This is enforced internally and will throw an `InvalidOperationException` if a send is attempted without it.

### Why Self Registration Exists

Self registration serves two purposes:
1. It ensures the local relay instance has a stable identity (`InstanceId`) and display name (`DisplayName`) for outbound envelopes
2. It controls whether the local instance participates in presence announcements and peer discovery

### Registering on Construction

The simplest approach is to pass `selfActiveOnStart: true` to the constructor:

```csharp
var relay = new NoireNetworkRelay(
    moduleId: "MyRelay",
    selfActiveOnStart: true);
```

This automatically calls `RegisterSelf()` and marks the local instance as active when the module activates.

### Manual Registration

```csharp
relay.RegisterSelf();
relay.ActivateSelf();
```

`RegisterSelf()` accepts optional overrides:

```csharp
relay.RegisterSelf(
    peerId: "client-a",
    displayName: "Client A",
    activateSelf: true);
```

Parameters:
- `peerId`: Optional override for `InstanceId`. If provided, the instance ID is updated.
- `displayName`: Optional override for `DisplayName`. If provided, the display name is updated.
- `activateSelf`: Whether the local instance should also be marked active for self announcements. `null` preserves the current state.

### Activation and Deactivation

```csharp
relay.ActivateSelf();   // Marks the local instance active for sending and announcements
relay.DeactivateSelf();  // Marks the local instance inactive (stops sending and announcements)
```

`ActivateSelf()` requires `RegisterSelf()` to have been called first.

### Unregistering

```csharp
relay.UnregisterSelf();
```

This removes the local instance from the peer table and marks it inactive.

### Properties

- `IsSelfRegistered`: Whether the local relay instance is currently registered as a peer
- `IsSelfActive`: Whether the local relay instance is currently active for sending data

---

## Configuration

### Constructor Parameters

```csharp
var relay = new NoireNetworkRelay(
    moduleId: "MyRelay",
    active: true,
    enableLogging: true,
    selfActiveOnStart: true,
    instanceId: "client-a",
    displayName: "Client A",
    port: 53741,
    enableReliableTransport: true,
    reliablePort: 53742,
    enableBroadcast: true,
    autoAnnounceOnStart: true,
    enablePeerDiscovery: true,
    allowLoopbackMessages: false,
    defaultChannel: "default",
    exceptionHandling: ExceptionBehavior.LogAndContinue,
    eventBus: eventBus);
```

Parameter summary:
- `moduleId`: Optional identifier for multiple relay instances
- `active`: Whether the module starts active immediately (default: `true`)
- `enableLogging`: Enables module logging (default: `true`)
- `selfActiveOnStart`: Whether the local relay instance should start registered and active for self announcements (default: `false`)
- `instanceId`: Optional relay instance identifier to use instead of the generated default
- `displayName`: Optional relay display name to use instead of the default machine or module name
- `port`: UDP port used by the relay (default: `53740`)
- `enableReliableTransport`: Enables the TCP reliable transport listener (default: `true`)
- `reliablePort`: Optional TCP port used for reliable delivery (default: `53741`)
- `enableBroadcast`: Enables LAN broadcast sending (default: `true`)
- `autoAnnounceOnStart`: Sends a presence announcement on activation (default: `true`)
- `enablePeerDiscovery`: Enables peer discovery behavior (default: `true`)
- `allowLoopbackMessages`: Allows processing self-originated packets (default: `false`)
- `defaultChannel`: Default channel used when none is specified (default: `"default"`)
- `exceptionHandling`: Relay exception behavior (default: `ExceptionBehavior.LogAndContinue`)
- `eventBus`: Optional `NoireEventBus` for integration and bridging

### Fluent Configuration API

The relay exposes a fluent configuration surface so the instance can be tuned after creation.
All setter methods return the module instance for chaining.

#### Identity

```csharp
relay.SetInstanceId("client-a")
     .SetDisplayName("Craft Client A");
```

- `InstanceId` is the machine-readable identity used by message metadata and peer routing
- `DisplayName` is a human-readable label primarily useful for discovery, logs, and debugging

#### EventBus

```csharp
relay.SetEventBus(eventBus);
```

Attaches or detaches the `NoireEventBus` used for integration events and bridge registrations. Passing `null` detaches the bus.

#### UDP Transport

```csharp
relay.SetBindAddress(IPAddress.Any)
     .SetPort(53741)
     .SetBroadcast(true)
     .SetUdpSocketBuffers(256 * 1024, 256 * 1024)
     .SetUdpMaxPayloadBytes(60_000)
     .SetTimeToLive(1);
```

`SetBindAddress(...)` also accepts a string:

```csharp
relay.SetBindAddress("192.168.1.10");
```

Relevant properties:
- `BindAddress`: The local IP address to bind the listeners to (default: `IPAddress.Any`)
- `Port`: The UDP port for best-effort traffic (default: `53740`)
- `UdpMaxPayloadBytes`: The maximum payload size accepted and sent through UDP (default: `60000`)
- `EnableBroadcast`: Allows sending UDP broadcast packets to the subnet (default: `true`)
- `UdpReceiveBufferSize`: The UDP receive socket buffer size in bytes (default: `262144`)
- `UdpSendBufferSize`: The UDP send socket buffer size in bytes (default: `262144`)
- `TimeToLive`: The TTL for outgoing UDP packets (default: `1`)
- `MaxPayloadBytes`: Shared compatibility view, returns the smaller of the UDP and TCP payload limits

#### Reliable TCP Transport

```csharp
relay.SetReliableTransport(true, 53742)
     .SetReliableMaxPayloadBytes(256 * 1024)
     .SetReliableSocketBuffers(256 * 1024, 256 * 1024)
     .SetReliableTimeouts(
         connectTimeout: TimeSpan.FromSeconds(5),
         operationTimeout: TimeSpan.FromSeconds(10))
     .SetReliableAcknowledgementTimeout(TimeSpan.FromSeconds(10));
```

Relevant properties:
- `EnableReliableTransport`: Whether the relay starts a TCP listener (default: `true`)
- `ReliablePort`: The TCP listening port used for reliable delivery (default: `53741`)
- `ReliableMaxPayloadBytes`: The maximum payload size accepted and sent through TCP (default: `60000`)
- `ReliableReceiveBufferSize`: The TCP receive socket buffer size in bytes (default: `262144`)
- `ReliableSendBufferSize`: The TCP send socket buffer size in bytes (default: `262144`)
- `ReliableConnectTimeout`: The timeout for outbound TCP connections (default: `5 seconds`)
- `ReliableOperationTimeout`: The timeout for TCP read/write operations (default: `10 seconds`)
- `ReliableAcknowledgementTimeout`: The timeout when waiting for an ACK from the receiver (default: `10 seconds`)

#### Shared Transport Convenience Methods

If you want both transports to use the same limits and buffers:

```csharp
relay.SetMaxPayloadBytes(60_000)
     .SetSocketBuffers(256 * 1024, 256 * 1024);
```

If you want different settings per transport:

```csharp
relay.SetTransportPayloadLimits(
         udpMaxPayloadBytes: 60_000,
         reliableMaxPayloadBytes: 256 * 1024)
     .SetTransportSocketBuffers(
         udpReceiveBufferSize: 256 * 1024,
         udpSendBufferSize: 256 * 1024,
         reliableReceiveBufferSize: 512 * 1024,
         reliableSendBufferSize: 512 * 1024);
```

#### Discovery and Peer Behavior

```csharp
relay.SetPeerDiscovery(true, autoRegisterPeers: true)
     .SetAutoAnnounceOnStart(true)
     .SetPeerExpiration(TimeSpan.FromMinutes(1))
     .SetAnnouncementInterval(TimeSpan.FromSeconds(15));
```

- `EnablePeerDiscovery`: Whether remote peer announcements are processed (default: `true`)
- `AutoRegisterPeers`: Whether unknown discovered peers are automatically registered (default: `true`)
- `AutoAnnounceOnStart`: Whether a presence announcement is sent on activation (default: `true`)
- `PeerExpiration`: How long a dynamic peer remains without activity before being swept (default: `1 minute`). Set to `TimeSpan.Zero` to disable expiration.
- `AnnouncementInterval`: How often periodic presence packets are sent while active (default: `15 seconds`). Set to `TimeSpan.Zero` to disable periodic announcements.

#### Delivery Behavior

```csharp
relay.SetDefaultChannel("chat")
     .SetAllowLoopbackMessages(false)
     .SetAutoActivateOnSend(true)
     .SetAwaitAsyncHandlersOnReceive(true)
     .SetDuplicateSuppression(true, TimeSpan.FromSeconds(10));
```

- `DefaultChannel`: The default logical channel used when none is specified (default: `"default"`)
- `AllowLoopbackMessages`: Whether a client processes its own outgoing messages if they come back (default: `false`)
- `AutoActivateOnSend`: Whether send operations lazily start the relay if inactive (default: `true`)
- `AwaitAsyncHandlersOnReceive`: Whether async receive handlers are awaited or fire-and-forget (default: `true`)
- `SuppressDuplicateMessages`: Whether duplicate message IDs are dropped (default: `true`)
- `DuplicateMessageWindow`: The time window for duplicate suppression (default: `10 seconds`)

#### Peer Allow-List

```csharp
relay.SetAllowedPeerIds([
    "client-a",
    "client-b"
]);
```

If the allow-list is empty (default), all peer IDs are accepted.
Pass `null` or an empty collection to clear the allow-list.

#### Serializer Configuration

```csharp
relay.ConfigureSerializer(settings =>
{
    settings.NullValueHandling = NullValueHandling.Ignore;
    settings.Formatting = Formatting.None;
    settings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
});
```

Or replace the settings object entirely:

```csharp
relay.SetSerializerSettings(new JsonSerializerSettings
{
    NullValueHandling = NullValueHandling.Ignore,
    Formatting = Formatting.None,
});
```

The default serializer settings use:
- `CamelCasePropertyNamesContractResolver`
- `NullValueHandling.Ignore`
- `Formatting.None`
- `DateParseHandling.DateTimeOffset`
- `TypeNameHandling.None`

### Exception Handling

The relay uses `ExceptionBehavior` to control how errors are handled.

```csharp
relay.SetExceptionHandling(ExceptionBehavior.LogAndContinue);
```

Modes:
- `LogAndContinue`: Logs the exception and continues execution
- `LogAndThrow`: Logs the exception and rethrows it
- `Suppress`: Silently suppresses exceptions
- `Throw`: Rethrows the exception without logging

---

## Channels, Messages, and Payloads

### Channels

Relay routing is channel-based. Channels are case-insensitive strings.

Examples:
- `action.start`
- `sync.request`
- `events.player.ready`

When no channel is specified, `DefaultChannel` is used.

### Message Shape

A received `NetworkRelayMessage` exposes:
- `MessageId`: The unique relay message identifier
- `Channel`: The logical relay channel used for delivery
- `SenderId`: The unique identifier of the sender
- `SenderDisplayName`: The optional friendly display name of the sender
- `MessageType`: The serialized payload type name
- `Payload`: The raw payload content as a `JToken`
- `SentAtUtc`: The UTC timestamp at which the message was sent
- `RemoteEndPoint`: The remote endpoint from which the message was received
- `TargetPeerId`: The optional single target peer identifier
- `TargetPeerIds`: The optional collection of target peer identifiers
- `TransportKind`: The transport used to deliver the message (`Udp` or `Tcp`)
- `IsReliable`: Convenience boolean, `true` when `TransportKind` is `Tcp`

A strongly typed `NetworkRelayMessage<TPayload>` has the same shape but `Payload` is the deserialized type instead of `JToken`.

If you subscribe with typed handlers, the payload is deserialized automatically.
If you subscribe with raw message handlers, you can inspect both payload and transport metadata.

The raw message also exposes `GetPayload<TPayload>(...)` for on-demand deserialization and `ToTyped<TPayload>(...)` for converting to a strongly typed message.

### Wildcard Channel

The wildcard channel is `"*"` and is used to receive messages from all channels.

```csharp
relay.OnMessage(message =>
{
    NoireLogger.LogInfo($"[{message.TransportKind}] {message.Channel} -> {message.Payload}");
}, channel: "*");
```

### Typed vs Raw Consumption

Use typed subscriptions when:
- you know the expected payload type
- you only care about the data model
- you want the simplest code

Use raw message subscriptions when:
- you need sender identity or endpoint data
- you need to know whether the message came through UDP or TCP
- you want to inspect channel or type strings
- you want to log or trace all traffic

---

## Sending Data

All send methods require the local instance to be registered and active.
See [Self Registration](#self-registration).

### Best-Effort Send

```csharp
relay.Send("Hello everyone", channel: "chat");
relay.SendBytes(bytes, channel: "bytes.demo");
relay.Broadcast(payload, channel: "state.position");
relay.BroadcastString("Simple text", channel: "chat");
```

These methods use best-effort UDP by default.

### Explicit Delivery Mode

```csharp
relay.Send("Ping", "chat", NetworkRelayDeliveryMode.BestEffort);
relay.Send("Start step", "control", NetworkRelayDeliveryMode.Reliable);
```

The same delivery mode parameter exists for:
- `Send(...)`
- `SendBytes(...)`
- `Broadcast(...)`
- `SendToPeer(...)`
- `SendTo(...)`
- `SendToAllPeers(...)`

### Reliable Shortcut Methods

The relay provides convenience methods that default to reliable TCP delivery:

```csharp
relay.SendReliable("Start step", channel: "control.start");
relay.SendReliableBytes(bytes, channel: "control.raw");
relay.SendReliableToPeer("client-b", payload, channel: "control.direct");
relay.SendReliableTo("192.168.1.25", 53742, payload, channel: "control.endpoint");
relay.SendReliableToAllPeers(payload, channel: "control.fanout");
```

### Awaitable Reliable Sends with Built-In ACK

The relay exposes awaitable reliable send methods for the TCP path.
These methods automatically request an acknowledgement from the receiver over the same reliable connection.

```csharp
var receipt = await relay.SendReliableToPeerAsync(
    "client-b",
    payload,
    channel: "control.direct");
```

Available async methods:
- `SendReliableToPeerAsync(...)`: Send to a registered peer by ID and await ACK
- `SendReliableToAsync(...)`: Send to a specific host/port and await ACK

Behavior:
- the sender writes the reliable payload through TCP
- the receiver processes the message
- the receiver automatically writes back an acknowledgement envelope
- the sender completes successfully only when that acknowledgement is received
- transport failures, acknowledgement failures, and acknowledgement timeouts throw exceptions

The returned `NetworkRelaySendReceipt` contains:
- `MessageId`
- `Channel`
- `TargetPeerId`
- `RemoteEndPoint`
- `SentAtUtc`
- `AcknowledgedAtUtc`

Optional callbacks can be supplied:

```csharp
await relay.SendReliableToAsync(
    "192.168.1.25",
    53742,
    payload,
    channel: "control.endpoint",
    onSuccess: receipt => NoireLogger.LogInfo($"ACK: {receipt.MessageId}"),
    onFailure: ex => NoireLogger.LogError(ex, "Reliable send failed"));
```

Optional parameters also include `acknowledgementTimeout` and `cancellationToken`:

```csharp
var receipt = await relay.SendReliableToPeerAsync(
    "client-b",
    payload,
    channel: "control.direct",
    acknowledgementTimeout: TimeSpan.FromSeconds(5),
    cancellationToken: cts.Token);
```

The default acknowledgement wait duration is controlled by `ReliableAcknowledgementTimeout`:

```csharp
relay.SetReliableAcknowledgementTimeout(TimeSpan.FromSeconds(10));
```

### Direct Delivery to a Known Peer

Best-effort UDP:

```csharp
relay.RegisterPeer("client-b", "192.168.1.25", 53741, "Other Client");
relay.SendToPeer("client-b", "Hello client B", channel: "private.chat");
```

Reliable TCP:

```csharp
relay.RegisterPeer("client-b", "192.168.1.25", 53741, 53742, "Other Client");
relay.SendReliableToPeer("client-b", "Start the next step", channel: "control.start");
```

### Direct Delivery to an Endpoint

```csharp
relay.SendTo(
    hostOrAddress: "192.168.1.25",
    port: 53741,
    payload: new SyncPayload("Now"),
    channel: "sync.request");

relay.SendReliableTo(
    hostOrAddress: "192.168.1.25",
    port: 53742,
    payload: new SyncPayload("Commit"),
    channel: "control.commit");
```

Both methods accept an optional `targetPeerId` parameter for including a target peer ID in the envelope metadata:

```csharp
relay.SendTo(
    hostOrAddress: "192.168.1.25",
    port: 53741,
    payload: new SyncPayload("Now"),
    channel: "sync.request",
    targetPeerId: "client-b");
```

### Delivery to All Known Peers

```csharp
relay.SendToAllPeers(new PingPayload(DateTimeOffset.UtcNow), channel: "ping");
relay.SendReliableToAllPeers(new CommitPayload("Step2"), channel: "control.commit");
```

Important distinction:
- **UDP broadcast**: one LAN-wide packet, listeners on the subnet may receive it
- **Direct fan-out**: one direct send per tracked peer
- **Reliable fan-out**: TCP send to each tracked peer individually

When using `SendToAllPeers(...)`, the local instance is excluded from the fan-out unless `AllowLoopbackMessages` is enabled.

### Presence Announcements

```csharp
relay.AnnouncePresence();
```

This sends a discovery announcement packet so other relay instances can register or refresh this peer.
The announcement is sent as a UDP broadcast (if enabled) and also directly to all known peer endpoints.
When reliable transport is enabled, the announcement includes the reliable TCP port.

Requires the local instance to be registered and active (`IsSelfRegistered` and `IsSelfActive`).

### Auto Activation on Send

If `AutoActivateOnSend` is enabled (default), send calls automatically activate the module if it is inactive.
If disabled, sending while inactive throws an `InvalidOperationException`.

---

## Receiving Data

### Basic Payload Callbacks

#### `On<TPayload>(...)`

Registers a synchronous callback that receives just the deserialized payload:

```csharp
relay.On<string>(message =>
{
    NoireLogger.PrintToChat($"Chat message: {message}");
}, channel: "chat");
```

#### `OnAsync<TPayload>(...)`

Registers an asynchronous callback that receives just the deserialized payload:

```csharp
relay.OnAsync<string>(async message =>
{
    await Task.Delay(25);
    NoireLogger.LogInfo($"Async message: {message}");
}, channel: "chat.async");
```

#### Keyed Subscriptions

Using a key allows the subscription to be replaced if the same key is registered again, and enables unsubscription by key.
All callback and subscription methods have keyed overloads:

```csharp
relay.On<string>(
    key: "chat-listener",
    callback: message => NoireLogger.LogInfo(message),
    channel: "chat");

relay.OnAsync<string>(
    key: "chat-async-listener",
    callback: async message => { await Task.Delay(25); },
    channel: "chat");
```

#### Filters

```csharp
relay.On<int>(
    callback: value => NoireLogger.LogInfo($"Value: {value}"),
    channel: "numbers",
    filter: value => value > 10);
```

#### Owner-Based Grouping

```csharp
relay.On<string>(
    callback: message => NoireLogger.LogInfo(message),
    channel: "chat",
    owner: this);
```

Owner references enable bulk unsubscription via `UnsubscribeAll(owner)`.

### Full Message Callbacks

#### `OnMessage(...)`

Use this when sender and transport metadata matter:

```csharp
relay.OnMessage(message =>
{
    NoireLogger.LogInfo($"[{message.TransportKind}] {message.Channel} from {message.SenderId}");
});
```

#### `OnMessageAsync(...)`

```csharp
relay.OnMessageAsync(async message =>
{
    await Task.Delay(25);
    NoireLogger.LogInfo($"Async raw message from {message.SenderId} via {message.TransportKind}");
});
```

#### Keyed Message Callbacks

Both `OnMessage(...)` and `OnMessageAsync(...)` have keyed overloads:

```csharp
relay.OnMessage(
    key: "raw-logger",
    callback: message => NoireLogger.LogInfo($"{message.Channel}: {message.Payload}"),
    channel: "*");

relay.OnMessageAsync(
    key: "async-raw-logger",
    callback: async message =>
    {
        await Task.Delay(10);
        NoireLogger.LogInfo($"{message.Channel}: {message.Payload}");
    },
    channel: "*");
```

#### Message Filters

```csharp
relay.OnMessage(
    callback: message => NoireLogger.LogInfo(message.Payload.ToString()),
    channel: "chat",
    filter: message => message.IsReliable);
```

### Full Typed Subscriptions

For access to both metadata and typed payloads, use `Subscribe<TPayload>(...)`:

```csharp
relay.Subscribe<PlayerPositionPayload>(message =>
{
    NoireLogger.LogInfo($"{message.SenderId} via {message.TransportKind} -> {message.Payload.X}, {message.Payload.Y}, {message.Payload.Z}");
}, channel: "player.position");
```

Async version:

```csharp
relay.SubscribeAsync<PlayerPositionPayload>(async message =>
{
    await Task.Delay(10);
    NoireLogger.LogInfo($"Async position from {message.SenderId} via {message.TransportKind}");
}, channel: "player.position");
```

Keyed versions:

```csharp
relay.Subscribe<PlayerPositionPayload>(
    key: "position-handler",
    handler: message => NoireLogger.LogInfo($"{message.Payload.X}"),
    channel: "player.position");

relay.SubscribeAsync<PlayerPositionPayload>(
    key: "async-position-handler",
    handler: async message => { await Task.Delay(10); },
    channel: "player.position");
```

### Priorities

Higher priority handlers execute first:

```csharp
relay.On<string>(message => NoireLogger.LogInfo("Low priority"), channel: "chat", priority: 0);
relay.On<string>(message => NoireLogger.LogInfo("High priority"), channel: "chat", priority: 100);
```

### Async Awaiting Behavior

`AwaitAsyncHandlersOnReceive` controls whether async handlers are awaited during receive dispatch:

```csharp
relay.SetAwaitAsyncHandlersOnReceive(true);
```

- `true`: receive processing awaits async handlers in order
- `false`: async handlers are fire-and-forget, exceptions are captured in the continuation path
### Unsubscribing

```csharp
// By token
var token = relay.On<string>(message => NoireLogger.LogInfo(message), channel: "chat");
relay.Unsubscribe(token);

// By key
relay.Unsubscribe("chat-listener");

// First handler on a channel (optionally filtered by owner)
relay.UnsubscribeFirst("chat", owner: this);

// All handlers for an owner
relay.UnsubscribeAll(this);

// All handlers on a channel (optionally filtered by owner)
relay.UnsubscribeAll("chat");
relay.UnsubscribeAll("chat", owner: this);

// Everything
relay.ClearAllSubscriptions();
```

### Subscriber Count

```csharp
var count = relay.GetSubscriberCount("chat");
var wildcardCount = relay.GetSubscriberCount("*");
```

---

## Peer Management

### Manual Registration

Register a peer with UDP only:

```csharp
relay.RegisterPeer("client-b", "192.168.1.25", 53741, "Other Client");
```

Register a peer with UDP and TCP:

```csharp
relay.RegisterPeer("client-b", "192.168.1.25", 53741, 53742, "Other Client");
```

Using explicit `IPEndPoint` objects:

```csharp
relay.RegisterPeer(
    peerId: "client-b",
    endPoint: new IPEndPoint(IPAddress.Parse("192.168.1.25"), 53741),
    displayName: "Other Client");

relay.RegisterPeer(
    peerId: "client-b",
    endPoint: new IPEndPoint(IPAddress.Parse("192.168.1.25"), 53741),
    reliableEndPoint: new IPEndPoint(IPAddress.Parse("192.168.1.25"), 53742),
    displayName: "Other Client");
```

When using the hostname/IP string overload without an explicit reliable port:
- if `EnableReliableTransport` is `true`, the reliable endpoint defaults to the same port as the UDP port
- if `EnableReliableTransport` is `false`, no reliable endpoint is registered

### Removal

```csharp
relay.UnregisterPeer("client-b");
```

Clear all peers:

```csharp
relay.ClearPeers();                        // Removes all peers (dynamic and static)
relay.ClearPeers(includeStaticPeers: false); // Removes only dynamic (discovered) peers
```

### Retrieval

```csharp
var peers = relay.GetPeers();

if (relay.TryGetPeer("client-b", out var peer))
{
    NoireLogger.LogInfo($"UDP: {peer.EndPoint}");
    NoireLogger.LogInfo($"TCP: {peer.ReliableEndPoint}");
    NoireLogger.LogInfo($"Last seen: {peer.LastSeenUtc}");
    NoireLogger.LogInfo($"Dynamic: {peer.IsDynamic}");
}
```

The `NetworkRelayPeer` record exposes:
- `PeerId`: The unique identifier of the peer
- `DisplayName`: The friendly display name
- `EndPoint`: The UDP endpoint
- `ReliableEndPoint`: The optional TCP endpoint (nullable)
- `LastSeenUtc`: The UTC timestamp when the peer was last seen
- `IsDynamic`: `true` if discovered automatically, `false` if registered manually

### Discovery Behavior

With discovery enabled, the relay automatically tracks peers based on presence packets and incoming traffic.
Discovered peers can accumulate both a UDP endpoint and a reliable TCP endpoint.

Important distinction:
- **dynamic peer**: discovered automatically, may expire after `PeerExpiration`
- **static peer**: registered manually via `RegisterPeer(...)`, remains until explicitly removed

The `AutoRegisterPeers` property (default: `true`) controls whether newly discovered peers are automatically inserted into the peer table.
When disabled, only manually registered peers and already known peers are updated from discovery announcements.

---

## EventBus Integration

### Basic Integration Events

When `EventBus` is assigned, the relay automatically publishes integration events into the bus:
- `NetworkRelayMessageReceivedEvent(Message)`: Raised for every received relay message
- `NetworkRelayPeerSeenEvent(Peer, IsNewPeer)`: Raised when a peer is registered or refreshed
- `NetworkRelayPeerRemovedEvent(Peer, Expired)`: Raised when a peer is removed
- `NetworkRelayErrorEvent(Error)`: Raised when the relay observes an error

### Relay Local EventBus Publishes Outward

```csharp
public record TestEvent(string Message);

relay.RelayPublishedEvent<TestEvent>(channel: "events.test");

eventBus.Publish(new TestEvent("Hello network"));
```

The event will be serialized and sent over the relay on the specified channel.

`RelayPublishedEvent<TEvent>(...)` accepts:
- `channel`: Optional channel override. If omitted, the event type name is used.
- `broadcast`: Whether to broadcast or send to known peers (default: `true`)
- `targetPeerId`: Optional target peer ID for direct delivery
- `filter`: Optional filter to skip specific local events
- `owner`: Optional owner for the EventBus subscription
- `key`: Optional EventBus subscription key
- `deliveryMode`: The delivery mode for outbound relayed payloads (default: `BestEffort`)

### Republish Received Relay Events Locally

```csharp
relay.PublishReceivedToEventBus<TestEvent>(channel: "events.test");

eventBus.Subscribe<TestEvent>(evt =>
{
    NoireLogger.LogInfo($"Republished locally: {evt.Message}");
});
```

`PublishReceivedToEventBus<TEvent>(...)` accepts:
- `channel`: Optional channel override. If omitted, the event type name is used.
- `priority`: The relay subscription priority (default: `0`)
- `filter`: Optional filter for received relay events
- `owner`: Optional owner for the relay subscription
- `key`: Optional relay subscription key prefix

### Bridge Both Directions in One Call

```csharp
relay.BridgeEvent<TestEvent>(
    channel: "events.test",
    key: "test-event-bridge");
```

This creates both an outbound relay subscription and an inbound relay-to-EventBus subscription.

Optional one-way bridging:

```csharp
relay.BridgeEvent<TestEvent>(
    channel: "events.test",
    relayLocalPublishes: true,
    publishReceivedLocally: false);
```

`BridgeEvent<TEvent>(...)` accepts:
- `channel`: Optional channel override
- `relayLocalPublishes`: Whether local EventBus publishes are relayed outward (default: `true`)
- `publishReceivedLocally`: Whether received relay events are republished locally (default: `true`)
- `broadcast`: Whether outbound events use broadcast delivery (default: `true`)
- `targetPeerId`: Optional target peer ID for direct outbound delivery
- `priority`: Inbound relay subscription priority (default: `0`)
- `eventBusFilter`: Optional filter for outbound EventBus events
- `relayFilter`: Optional filter for inbound relay events
- `owner`: Optional owner for created subscriptions
- `key`: Optional key for managing the bridge registration
- `deliveryMode`: The delivery mode for outbound relayed payloads (default: `BestEffort`)

The method returns a `NetworkRelayEventBridgeHandle` which tracks:
- `Channel`: The relay channel used by the bridge
- `EventBusSubscriptionToken`: The outbound EventBus subscription token
- `RelaySubscriptionToken`: The inbound relay subscription token
- `HasEventBusSubscription`: Whether an outbound subscription exists
- `HasRelaySubscription`: Whether an inbound subscription exists

### Bridge Removal

```csharp
relay.UnbridgeEvent("test-event-bridge");   // By key
relay.UnbridgeEvent(handle);                 // By handle
relay.ClearEventBridges();                   // Remove all bridges
```

### Loop Suppression

The relay suppresses internal EventBus bridge echo paths when republishing remotely received events locally.
This prevents an inbound bridged event from being immediately re-relayed back outward by the same bridge path.

---

## Statistics and Diagnostics

### Public Events

The relay exposes CLR events:
- `MessageReceived`: Raised for every relay message successfully received and parsed
- `PeerSeen`: Raised when a peer is registered or refreshed
- `PeerRemoved`: Raised when a peer is removed or expired
- `Error`: Raised when a relay error is observed

```csharp
relay.MessageReceived += message =>
    NoireLogger.LogInfo($"Message: {message.Channel} via {message.TransportKind}");

relay.PeerSeen += peer =>
    NoireLogger.LogInfo($"Peer seen: {peer.DisplayName} at {peer.EndPoint}");

relay.PeerRemoved += peer =>
    NoireLogger.LogInfo($"Peer removed: {peer.DisplayName}");

relay.Error += error =>
    NoireLogger.LogError(error.Exception, $"Relay error during: {error.Operation}");
```

The `NetworkRelayError` record exposes:
- `Operation`: The operation during which the error occurred
- `Exception`: The exception that was observed
- `TimestampUtc`: The UTC timestamp at which the error was captured

### Runtime Statistics

```csharp
var stats = relay.GetStatistics();
```

The returned `NetworkRelayStatistics` record provides a snapshot of the relay state:

**Current state:**
- `ActivePeers`: Number of currently tracked peers
- `ActiveSubscriptions`: Number of currently active relay subscriptions
- `ActiveEventBridges`: Number of currently active EventBus bridge registrations
- `ReliableTransportEnabled`: Whether the TCP transport listener is enabled

**Aggregate counters:**
- `TotalMessagesSent`: Total relay messages sent (UDP + TCP)
- `TotalMessagesReceived`: Total relay messages received (UDP + TCP)
- `TotalBestEffortMessagesSent`: Total best-effort UDP messages sent
- `TotalBestEffortMessagesReceived`: Total best-effort UDP messages received
- `TotalReliableMessagesSent`: Total reliable TCP messages sent
- `TotalReliableMessagesReceived`: Total reliable TCP messages received
- `TotalReliableConnectionsAccepted`: Total TCP client connections accepted
- `TotalBytesSent`: Total bytes sent across both transports
- `TotalBytesReceived`: Total bytes received across both transports
- `TotalMessagesDropped`: Total dropped messages (size, targeting, filtering, duplicates)
- `TotalDuplicateMessagesDropped`: Total dropped duplicate messages
- `TotalPeerAnnouncementsReceived`: Total received peer announcements
- `TotalSendFailures`: Total send failures
- `TotalReceiveFailures`: Total receive failures
- `TotalDispatchExceptionsCaught`: Total exceptions caught during callback/event dispatch
- `TotalExceptionsCaught`: Total exceptions caught by the relay
- `TotalEventBusEventsRelayed`: Total local EventBus events relayed over the network
- `TotalEventBusEventsPublishedLocally`: Total received relay events republished locally
- `TotalSubscriptionsCreated`: Total relay subscriptions created
- `TotalPeersRegistered`: Total peers registered
- `TotalPeersRemoved`: Total peers removed

---

## Troubleshooting

### Messages are not received
- Ensure both clients use the same UDP `Port` for best-effort traffic.
- Ensure both clients use compatible `ReliablePort` values for reliable TCP traffic.
- Ensure the relay is active (`Start()`) or `AutoActivateOnSend` is enabled.
- Ensure the local instance is registered and active (`RegisterSelf()` / `selfActiveOnStart: true`).
- Ensure the same relay channel is used on both sender and receiver.
- Check firewall rules for local UDP and TCP traffic.
- Check `AllowLoopbackMessages` if testing on a single client instance.

### Sends throw InvalidOperationException
- Ensure `RegisterSelf()` has been called or `selfActiveOnStart: true` was passed to the constructor.
- Ensure `ActivateSelf()` has been called if the instance was registered but not activated.
- Ensure the relay module is active or `AutoActivateOnSend` is enabled.

### Reliable sends appear to do nothing
- Ensure `EnableReliableTransport` is enabled on the receiving relay.
- Ensure the target peer has a valid `ReliableEndPoint`.
- For `SendReliable(...)` and `SendReliableToAllPeers(...)`, ensure peers are already known.
- Verify the TCP `ReliablePort` is reachable and not blocked.

### Awaitable reliable send failed
- Check the exception from `SendReliableToPeerAsync(...)` or `SendReliableToAsync(...)`.
- Subscribe to `relay.Error` or listen for `NetworkRelayErrorEvent` for centralized error handling.
- If the send timed out waiting for an acknowledgement, ensure the receiver is actually processing the message and not dropping it due to targeting, filtering, or duplicate suppression.
- The acknowledgement timeout is controlled by `ReliableAcknowledgementTimeout`.
- Remember that the async reliable APIs confirm relay-level acknowledgement, not arbitrary application work after the receive handler returns.

### Same-host UDP behaves strangely when both instances share the same port
- On different PCs, using the same UDP port is normal and expected.
- On the same PC, two instances sharing the same IP and the same UDP port is not a reliable test setup.
- For same-host testing, use different UDP ports and different TCP ports for each instance.
- Presence or discovery can still appear to work in cases where direct same-host UDP sends do not behave deterministically.

### Broadcast does not work
- Ensure `EnableBroadcast` is enabled.
- Broadcast is IPv4-only in this implementation (`SetBindAddress(IPAddress.Any)`).
- Reliable TCP does not broadcast; it fans out to known peers instead.

### Direct peer messaging fails
- Make sure the peer is registered via `RegisterPeer(...)`.
- Verify the peer IP address and UDP/TCP ports.
- Use `TryGetPeer(...)` or `GetPeers()` to confirm the peer state.
- Check whether you intended to use `EndPoint` (UDP) or `ReliableEndPoint` (TCP).

### Peer discovery is inconsistent
- Ensure `EnablePeerDiscovery` is enabled on all clients.
- Ensure `AutoRegisterPeers` is enabled if you want newly discovered peers to be automatically registered.
- Ensure `AnnouncementInterval` is not zero if periodic announcements are expected.
- Ensure `PeerExpiration` is not too aggressive for your update frequency.

### EventBus bridging appears to do nothing
- Ensure `EventBus` is configured on the relay (constructor or `SetEventBus(...)`).
- Ensure all clients use the same bridge channel.
- Ensure you called `RelayPublishedEvent<TEvent>()`, `PublishReceivedToEventBus<TEvent>()`, or `BridgeEvent<TEvent>()`.
- Verify the bridged event type matches on both sides.

### Duplicate messages are observed
- Enable duplicate suppression: `SetDuplicateSuppression(true)`.
- Increase `DuplicateMessageWindow`.
- Avoid re-sending identical messages in tight loops.

### Large payloads fail to send
- Reduce payload size.
- Increase `UdpMaxPayloadBytes` for UDP-specific failures.
- Increase `ReliableMaxPayloadBytes` for TCP-specific failures.
- Use `SetMaxPayloadBytes(...)` only when you intentionally want both transports to share the same limit.
- Split large state sync into multiple smaller messages.

### Same-host testing is inconsistent
- Use different UDP/TCP port pairs for each game instance on the same PC.
- Manually register the sibling instance with `127.0.0.1` and its port pair.
- Prefer `SendToPeer(...)`, `SendReliableToPeer(...)`, `SendTo(...)`, or `SendReliableTo(...)` for same-host tests.
- Do not rely purely on discovery if the two local instances listen on different UDP ports.

### Async handlers appear to block receive flow
- Set `AwaitAsyncHandlersOnReceive(false)` if fire-and-forget handler execution is preferred.
- Keep receive callbacks lightweight and hand off heavy work to queues.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [EventBus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
