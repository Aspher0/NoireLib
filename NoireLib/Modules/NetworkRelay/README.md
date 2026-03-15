# Module Documentation : NoireNetworkRelay

You are reading the documentation for the `NoireNetworkRelay` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Transport Model](#transport-model)
- [Lifecycle and Module Registration](#lifecycle-and-module-registration)
- [Configuration](#configuration)
- [Channels, Messages, and Payloads](#channels-messages-and-payloads)
- [Sending Data](#sending-data)
- [Receiving Data](#receiving-data)
- [Peer Management](#peer-management)
- [EventBus Integration](#eventbus-integration)
- [Events and Diagnostics](#events-and-diagnostics)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireNetworkRelay` module provides **hybrid local-network communication** for NoireLib modules and plugins.
It keeps **UDP** for lightweight best-effort traffic and adds **TCP** for reliable delivery when you need stronger reliability.

The module includes:
- **UDP best-effort delivery** for fast, lossy-acceptable traffic
- **TCP reliable delivery** for controlled and reliable traffic
- **Broadcast delivery** for LAN-wide UDP messages
- **Direct peer delivery** for known endpoints
- **Automatic peer discovery** through presence announcements
- **Peer tracking** with expiration and manual registration support
- **Reliable peer metadata** through tracked TCP endpoints
- **Typed payload subscriptions** through `Subscribe<TPayload>(...)`, `On<TPayload>(...)`, and async variants
- **Raw message subscriptions** when sender or transport metadata is required
- **EventBus bridge support** for relaying local events outward and republishing remote events locally
- **Duplicate suppression** to reduce repeated packet processing
- **Runtime statistics** for monitoring both UDP and TCP activity
- **Fluent configuration** for transport, discovery, filtering, and serializer settings

In short:
- use **UDP** when you do not care if a packet is occasionally lost
- use **TCP** when you want the message to be delivered through a reliable transport path

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### Minimal Best-Effort Usage

If all you want is **"send a value and receive it"**, the smallest usable setup looks like this:

```csharp
var relay = NoireLibMain.AddModule(new NoireNetworkRelay(
    moduleId: "NetworkRelay",
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
- subscribes to incoming string payloads on the logical channel `chat`
- broadcasts a best-effort UDP string payload on that same channel

### Minimal Reliable Usage

If you want the relay to use the reliable TCP path for important messages:

```csharp
var relay = NoireLibMain.AddModule(new NoireNetworkRelay(
    moduleId: "NetworkRelay",
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
- periodic sync spam where another update is coming soon anyway
- messages where occasional loss is acceptable

#### `NetworkRelayDeliveryMode.Reliable`

This uses **TCP**.

Choose it for:
- reliability
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
- manual registration
- discovery plus auto-registration

### Message Transport Metadata

Every received `NetworkRelayMessage` carries transport information:
- `TransportKind`
- `IsReliable`

### Peer Transport Metadata

Each `NetworkRelayPeer` carries:
- `EndPoint` for UDP
- `ReliableEndPoint` for TCP

Discovery announcements also advertise the reliable TCP port when enabled.

### What Is Separated Between UDP and TCP

The relay separates the following transport-specific settings:
- `Port` vs `ReliablePort`
- `UdpMaxPayloadBytes` vs `ReliableMaxPayloadBytes`
- `UdpReceiveBufferSize` / `UdpSendBufferSize`
- `ReliableReceiveBufferSize` / `ReliableSendBufferSize`
- UDP-only settings like `EnableBroadcast` and `TimeToLive`
- TCP-only settings like `ReliableConnectTimeout` and `ReliableOperationTimeout`

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
    eventBus: eventBus,
    exceptionHandling: ExceptionBehavior.LogAndContinue,
    port: 53741,
    enableBroadcast: true,
    defaultChannel: "default",
    autoAnnounceOnStart: true,
    enablePeerDiscovery: true,
    allowLoopbackMessages: false,
    enableReliableTransport: true,
    reliablePort: 53742));
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
- optionally broadcasts presence immediately if `AutoAnnounceOnStart` is enabled

When deactivated, the relay:
- stops the UDP transport
- stops the TCP transport
- stops background loops
- disposes internal transport resources

---

## Configuration

### Constructor Parameters

```csharp
var relay = new NoireNetworkRelay(
    moduleId: "MyRelay",
    active: true,
    enableLogging: true,
    eventBus: eventBus,
    exceptionHandling: ExceptionBehavior.LogAndContinue,
    port: 53741,
    enableBroadcast: true,
    defaultChannel: "default",
    autoAnnounceOnStart: true,
    enablePeerDiscovery: true,
    allowLoopbackMessages: false,
    enableReliableTransport: true,
    reliablePort: 53742
);
```

Parameter summary:
- `moduleId`: Optional identifier for multiple relay instances
- `active`: Whether the module starts active immediately
- `enableLogging`: Enables module logging
- `eventBus`: Optional `NoireEventBus` for integration and bridging
- `exceptionHandling`: Relay exception behavior
- `port`: UDP port used by the relay
- `enableBroadcast`: Enables LAN broadcast sending
- `defaultChannel`: Default channel used when none is specified
- `autoAnnounceOnStart`: Sends a presence announcement on activation
- `enablePeerDiscovery`: Enables peer discovery behavior
- `allowLoopbackMessages`: Allows processing self-originated packets
- `enableReliableTransport`: Enables the TCP reliable transport listener
- `reliablePort`: Optional TCP port used for reliable delivery

### Fluent Configuration API

The relay exposes a fluent configuration surface so the instance can be tuned after creation.

#### Identity

```csharp
relay.SetInstanceId("client-a")
     .SetDisplayName("Craft Client A");
```

Meaning:
- `InstanceId` is the machine-readable identity used by message metadata and peer routing
- `DisplayName` is a human-readable label primarily useful for discovery, logs, and debugging

#### UDP Transport

```csharp
relay.SetBindAddress(IPAddress.Any)
     .SetPort(53741)
     .SetBroadcast(true)
     .SetUdpSocketBuffers(256 * 1024, 256 * 1024)
     .SetUdpMaxPayloadBytes(60_000)
     .SetTimeToLive(1)
     .SetMaxPayloadBytes(60_000);
```

Relevant properties:
- `BindAddress`
- `Port`
- `UdpMaxPayloadBytes`
- `EnableBroadcast`
- `UdpReceiveBufferSize`
- `UdpSendBufferSize`
- `TimeToLive`
- `MaxPayloadBytes`

Meaning:
- `BindAddress` controls the local network interface the relay listens on
- `Port` controls the UDP port for best-effort traffic
- `UdpMaxPayloadBytes` controls the maximum payload size accepted and sent through UDP
- `EnableBroadcast` allows sending UDP broadcast packets to the subnet
- `UdpReceiveBufferSize` and `UdpSendBufferSize` control the underlying socket buffer sizes for UDP transport
- `TimeToLive` controls the TTL for outgoing UDP packets
- `MaxPayloadBytes` remains a shared compatibility view and returns the smaller of the UDP and TCP payload limits

#### Reliable TCP Transport

```csharp
relay.SetReliableTransport(true, 53742)
     .SetReliableMaxPayloadBytes(256 * 1024)
     .SetReliableSocketBuffers(256 * 1024, 256 * 1024)
     .SetReliableTimeouts(
         connectTimeout: TimeSpan.FromSeconds(5),
         operationTimeout: TimeSpan.FromSeconds(10));
```

Relevant properties:
- `EnableReliableTransport`
- `ReliablePort`
- `ReliableMaxPayloadBytes`
- `ReliableReceiveBufferSize`
- `ReliableSendBufferSize`
- `ReliableConnectTimeout`
- `ReliableOperationTimeout`

Meaning:
- `EnableReliableTransport` controls whether the relay starts a TCP listener
- `ReliablePort` defines the TCP listening port used for reliable delivery
- `ReliableMaxPayloadBytes` controls the maximum payload size accepted and sent through TCP
- `ReliableReceiveBufferSize` and `ReliableSendBufferSize` control TCP socket buffer sizes
- `ReliableConnectTimeout` controls outbound TCP connection timeout
- `ReliableOperationTimeout` controls framed read/write timeout for reliable operations

#### Shared Convenience Methods

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

- discovery allows the relay to learn about other clients automatically
- auto-registration means newly discovered peers are inserted into the peer table without manual registration
- peer expiration removes stale dynamic peers that stop announcing themselves
- the announcement interval controls how often presence packets are sent while active
- presence announcements also advertise the reliable TCP port when reliable transport is enabled

#### Delivery Behavior

```csharp
relay.SetDefaultChannel("chat")
     .SetAllowLoopbackMessages(false)
     .SetAutoActivateOnSend(true)
     .SetAwaitAsyncHandlersOnReceive(true)
     .SetDuplicateSuppression(true, TimeSpan.FromSeconds(10));
```

Behavior summary:
- `DefaultChannel` reduces repetitive channel arguments when you use the same traffic lane often
- `AllowLoopbackMessages` controls whether a client can process its own outgoing messages if they come back through the network path
- `AutoActivateOnSend` makes send operations lazily start the relay if it is not active
- `AwaitAsyncHandlersOnReceive` controls whether async receive handlers are part of the receive pipeline or are detached continuations
- duplicate suppression tracks message IDs and prevents re-processing the same message within a configured time window

#### Peer Allow-List

```csharp
relay.SetAllowedPeerIds([
    "client-a",
    "client-b"
]);
```

If the allow-list is empty, all peer IDs are accepted.

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

### Exception Handling

The relay uses `ExceptionBehavior`.

```csharp
relay.SetExceptionHandling(ExceptionBehavior.LogAndContinue);
```

Modes:
- `LogAndContinue`: Logs and continues execution
- `LogAndThrow`: Logs and rethrows
- `Suppress`: Suppresses exceptions
- `Throw`: Rethrows without logging

---

## Channels, Messages, and Payloads

### Channels

Relay routing is channel-based.

Examples:
- `action.start`
- `sync.request`
- `events.player.ready`

When no channel is specified, `DefaultChannel` is used.

### Payload Shape

A received relay message exposes:
- `MessageId`
- `Channel`
- `SenderId`
- `SenderDisplayName`
- `MessageType`
- `Payload`
- `SentAtUtc`
- `RemoteEndPoint`
- `TargetPeerId`
- `TargetPeerIds`
- `TransportKind`
- `IsReliable`

If you subscribe with typed handlers, the payload is deserialized automatically.
If you subscribe with raw message handlers, you can inspect both payload and transport metadata.

### Wildcard Channel

The wildcard channel is `"*"` and is used internally by subscription helpers to receive all channels.

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

### Best-Effort Send

```csharp
relay.Send("Hello everyone", channel: "chat");
relay.SendBytes(bytes, channel: "bytes.demo");
relay.Broadcast(payload, channel: "state.position");
```

These methods use best-effort UDP by default.

### Explicit Delivery Mode

```csharp
relay.Send("Ping", "chat", NetworkRelayDeliveryMode.BestEffort);
relay.Send("Start step", "control", NetworkRelayDeliveryMode.Reliable);
```

The same pattern exists for:
- `Send(...)`
- `SendBytes(...)`
- `Broadcast(...)`
- `SendToPeer(...)`
- `SendTo(...)`
- `SendToAllPeers(...)`

### Reliable Shortcut Methods

The relay also provides convenience methods for the reliable path:

```csharp
relay.SendReliable("Start step", channel: "control.start");
relay.SendReliableBytes(bytes, channel: "control.raw");
relay.SendReliableToPeer("client-b", payload, channel: "control.direct");
relay.SendReliableTo("192.168.1.25", 53742, payload, channel: "control.endpoint");
relay.SendReliableToAllPeers(payload, channel: "control.fanout");
```

### Awaitable Reliable Sends with Built-In ACK

The relay also exposes awaitable reliable send methods for the TCP path.
These methods automatically request an acknowledgement from the receiver over the same reliable connection.

```csharp
var receipt = await relay.SendReliableToPeerAsync(
    "client-b",
    payload,
    channel: "control.direct");
```

Available async methods:
- `SendReliableToPeerAsync(...)`
- `SendReliableToAsync(...)`

Behavior:
- the sender writes the reliable payload through TCP
- the receiver processes the message
- the receiver automatically writes back an acknowledgement envelope
- the sender completes successfully only when that acknowledgement is received
- transport failures, acknowledgement failures, and acknowledgement timeouts throw exceptions

Optional callbacks can also be supplied:

```csharp
await relay.SendReliableToAsync(
    "192.168.1.25",
    53742,
    payload,
    channel: "control.endpoint",
    onSuccess: receipt => NoireLogger.LogInfo($"ACK: {receipt.MessageId}"),
    onFailure: ex => NoireLogger.LogError(ex, "Reliable send failed"));
```

The acknowledgement wait duration defaults to `ReliableAcknowledgementTimeout` and can be changed with:

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

### Delivery to All Known Peers

```csharp
relay.SendToAllPeers(new PingPayload(DateTimeOffset.UtcNow), channel: "ping");
relay.SendReliableToAllPeers(new CommitPayload("Step2"), channel: "control.commit");
```

Important distinction:
- UDP broadcast: one LAN-wide packet, listeners on the subnet may receive it
- direct fan-out: one direct send per tracked peer
- reliable fan-out: TCP send to each tracked peer individually

### Presence Announcements

```csharp
relay.AnnouncePresence();
```

This sends a discovery announcement packet so other relay instances can register or refresh this peer.
When reliable transport is enabled, the announcement also includes the reliable TCP port.

### Auto Activation on Send

If `AutoActivateOnSend` is enabled, send calls automatically activate the module if it is inactive.
If disabled, sending while inactive throws an exception.

---

## Receiving Data

### Basic Payload Callbacks

#### `On<TPayload>(...)`

```csharp
relay.On<string>(message =>
{
    NoireLogger.PrintToChat($"Chat message: {message}");
}, channel: "chat");
```

#### `OnAsync<TPayload>(...)`

```csharp
relay.OnAsync<string>(async message =>
{
    await Task.Delay(25);
    NoireLogger.LogInfo($"Async message: {message}");
}, channel: "chat.async");
```

#### Keyed Subscriptions

```csharp
relay.On<string>(
    key: "chat-listener",
    callback: message => NoireLogger.LogInfo(message),
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

### Full Message Callbacks

#### `OnMessage(...)`

Use this when sender and transport metadata matter.

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

### Full Typed Subscriptions

For access to both metadata and typed payloads, use `Subscribe<TPayload>(...)`.

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

### Priorities

Higher priority handlers execute first.

```csharp
relay.On<string>(message => NoireLogger.LogInfo("Low priority"), channel: "chat", priority: 0);
relay.On<string>(message => NoireLogger.LogInfo("High priority"), channel: "chat", priority: 100);
```

### Async Awaiting Behavior

`AwaitAsyncHandlersOnReceive` controls whether async handlers are awaited during receive dispatch.

```csharp
relay.SetAwaitAsyncHandlersOnReceive(true);
```

- `true`: receive processing awaits async handlers
- `false`: async handlers are fire-and-forget and exceptions are captured in the continuation path

### Unsubscribing

```csharp
var token = relay.On<string>(message => NoireLogger.LogInfo(message), channel: "chat");
relay.Unsubscribe(token);
relay.Unsubscribe("chat-listener");
relay.UnsubscribeFirst("chat", owner: this);
relay.UnsubscribeAll(this);
relay.UnsubscribeAll("chat");
relay.ClearAllSubscriptions();
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

Or use explicit endpoints:

```csharp
relay.RegisterPeer(
    peerId: "client-b",
    endPoint: new IPEndPoint(IPAddress.Parse("192.168.1.25"), 53741),
    reliableEndPoint: new IPEndPoint(IPAddress.Parse("192.168.1.25"), 53742),
    displayName: "Other Client");
```

### Removal

```csharp
relay.UnregisterPeer("client-b");
relay.ClearPeers();
```

### Retrieval

```csharp
var peers = relay.GetPeers();

if (relay.TryGetPeer("client-b", out var peer))
{
    NoireLogger.LogInfo($"UDP: {peer.EndPoint}");
    NoireLogger.LogInfo($"TCP: {peer.ReliableEndPoint}");
}
```

### Discovery Behavior

With discovery enabled, the relay automatically tracks peers based on presence packets and incoming traffic.
Discovered peers can accumulate both a UDP endpoint and a reliable TCP endpoint.

Important distinction:
- **dynamic peer**: discovered automatically, may expire
- **static peer**: registered manually, remains until explicitly removed

---

## EventBus Integration

### Basic Integration Events

When `EventBus` is assigned, the relay automatically publishes integration events such as:
- `NetworkRelayMessageReceivedEvent`
- `NetworkRelayPeerSeenEvent`
- `NetworkRelayPeerRemovedEvent`
- `NetworkRelayErrorEvent`

### Relay Local EventBus Publishes Outward

```csharp
public record TestEvent(string Message);

relay.RelayPublishedEvent<TestEvent>(channel: "events.test");

eventBus.Publish(new TestEvent("Hello network")); // The event will be published through the relay
```

### Republish Received Relay Events Locally

```csharp
relay.PublishReceivedToEventBus<TestEvent>(channel: "events.test"); // The event bus will receive TestEvent events

eventBus.Subscribe<TestEvent>(evt =>
{
    NoireLogger.LogInfo($"Republished locally: {evt.Message}");
});
```

### Bridge Both Directions in One Call

```csharp
relay.BridgeEvent<TestEvent>(
    channel: "events.test",
    key: "test-event-bridge");
```

Optional one-way bridging:

```csharp
relay.BridgeEvent<TestEvent>(
    channel: "events.test",
    relayLocalPublishes: true,
    publishReceivedLocally: false);
```

### Bridge Removal

```csharp
relay.UnbridgeEvent("test-event-bridge");
relay.ClearEventBridges();
```

### Loop Suppression

The relay suppresses internal EventBus bridge echo paths when republishing remotely received events locally.
This prevents an inbound bridged event from being immediately re-relayed back outward by the same bridge path.

---

## Events and Diagnostics

### Public Events

The relay exposes these events:
- `MessageReceived`
- `PeerSeen`
- `PeerRemoved`
- `Error`

Example:

```csharp
relay.MessageReceived += message =>
    NoireLogger.LogInfo($"Message: {message.Channel} via {message.TransportKind}");

relay.PeerSeen += peer =>
    NoireLogger.LogInfo($"Peer seen: {peer.DisplayName}");

relay.PeerRemoved += peer =>
    NoireLogger.LogInfo($"Peer removed: {peer.DisplayName}");

relay.Error += error =>
    NoireLogger.LogError(error.Exception, error.Operation);
```

---

## Troubleshooting

### Messages are not received
- Ensure both clients use the same UDP `Port` for best-effort traffic.
- Ensure both clients use compatible `ReliablePort` values for reliable TCP traffic.
- Ensure the relay is active or `AutoActivateOnSend` is enabled.
- Ensure the same relay channel is used on both sender and receiver.
- Check firewall rules for local UDP and TCP traffic.
- Check `AllowLoopbackMessages` if testing on a single client instance.

### Reliable sends appear to do nothing
- Ensure `EnableReliableTransport` is enabled on the receiving relay.
- Ensure the target peer has a valid `ReliableEndPoint`.
- For `SendReliable(...)` and `SendReliableToAllPeers(...)`, ensure peers are already known.
- Verify the TCP `ReliablePort` is reachable and not blocked.

### Awaitable reliable send failed
- Check the exception from `SendReliableToPeerAsync(...)` or `SendReliableToAsync(...)`.
- Subscribe to `relay.Error` or listen for `NetworkRelayErrorEvent` for centralized error handling.
- If the send timed out waiting for an acknowledgement, ensure the receiver is actually processing the message and not dropping it due to targeting, filtering, or duplicate suppression.
- Remember that the async reliable APIs confirm relay-level acknowledgement, not arbitrary application work after the receive handler returns.

### Same-host UDP behaves strangely when both instances share the same port
- On different PCs, using the same UDP port is normal and expected.
- On the same PC, two instances sharing the same IP and the same UDP port is not a reliable test setup.
- For same-host testing, use different UDP ports and different TCP ports for each instance.
- Presence or discovery can still appear to work in cases where direct same-host UDP sends do not behave deterministically.

### Broadcast does not work
- Ensure `EnableBroadcast` is enabled.
- Broadcast is IPv4-only in this implementation.
- Reliable TCP does not broadcast; it fans out to known peers instead.

### Direct peer messaging fails
- Make sure the peer is registered.
- Verify the peer IP address and UDP/TCP ports.
- Use `TryGetPeer(...)` or `GetPeers()` to confirm the peer state.
- Check whether you intended to use `EndPoint` or `ReliableEndPoint`.

### Peer discovery is inconsistent
- Ensure `EnablePeerDiscovery` is enabled on all clients.
- Ensure `AnnouncementInterval` is not zero if periodic announcements are expected.
- Ensure `PeerExpiration` is not too aggressive for your update frequency.

### EventBus bridging appears to do nothing
- Ensure `EventBus` is configured on the relay.
- Ensure all clients use the same bridge channel.
- Ensure you called `RelayPublishedEvent<TEvent>()`, `PublishReceivedToEventBus<TEvent>()`, or `BridgeEvent<TEvent>()`.
- Verify the bridged event type matches on both sides.

### Duplicate messages are observed
- Enable duplicate suppression.
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
