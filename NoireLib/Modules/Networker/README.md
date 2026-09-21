# Module Documentation : NoireNetworker

You are reading the documentation for the `NoireNetworker` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Messaging](#messaging)
- [Requests and Responses](#requests-and-responses)
- [Presence](#presence)
- [Coordination Flags and Barriers](#coordination-flags-and-barriers)
- [Connection State](#connection-state)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Delivery Guarantees and Limitations](#delivery-guarantees-and-limitations)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`NoireNetworker` lets running instances of your plugin talk to each other: several game clients on one PC, and optionally on the LAN. It provides:
- **Zero configuration** on one PC. Only a network name is required
- **Automatic hub election** through a named kernel mutex, with no well-known ports and no server
- **Automatic failover** when the instance acting as hub goes away
- **Typed messaging** with broadcast and targeted sends
- **Request and response** exchanges with typed replies, timeouts, and fan-out to every peer
- **Peer presence** with synchronized metadata and coordination flags
- **Barriers** to wait until every instance reaches the same point
- **EventBus bridging** publishes one event type across every instance
- **Framework-thread delivery** for every handler, callback, and awaited continuation

One instance per machine is elected hub. The others connect to it over loopback TCP. The role never changes the API. With LAN enabled, each machine's hub links to the other hubs, and remote peers join the same peer list.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create the Networker

Every instance using the same network name joins the same network:

```csharp
using NoireLib;
using NoireLib.Networker;

var networker = NoireLibMain.AddModule(new NoireNetworker("MyPlugin.Sync"));
```

That is all the setup same-PC operation needs. The module elects a hub, joins, and reaches `NetworkerState.Ready`.

### 2. Define a Message and Exchange It

A message is any JSON-serializable class:

```csharp
public class PingMessage
{
    public string Text { get; set; } = string.Empty;
}
```

Subscribe to the type, then send it. An instance never receives its own broadcasts:

```csharp
networker.On<PingMessage>((peer, message) =>
{
    NoireLogger.LogInfo($"{peer} says: {message.Text}");
});

networker.Send(new PingMessage { Text = "Hello from another instance!" });
```

Handlers run on the framework thread.

### 3. Identify Your Instances

Peer ids change on every relaunch. Durable identity belongs in metadata. It synchronizes to every peer:

```csharp
networker.Self.Set("character", "Character Name");

foreach (var peer in networker.OtherPeers)
    NoireLogger.LogInfo($"Peer {peer.Id} is {peer["character"]}");
```

---

## Configuration

### Module Parameters

The main options go through the constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Networker"); // Optional

var networker = NoireLibMain.AddModule(new NoireNetworker(
    networkName: "MyPlugin.Sync",       // instances only see peers using the same name
    moduleId: "Sync",
    active: true,
    enableLogging: true,
    options: new NetworkerOptions       // same-PC operation needs no options set
    {
        EventBus = eventBus,
    }
));
```

The parameterless constructor creates the module inactive and without a network name. Set both before activating:

```csharp
var networker = new NoireNetworker();

networker.Options.EnableLan = true;
networker.SetNetworkName("MyPlugin.Sync");
networker.SetActive(true);

NoireLibMain.AddModule(networker);
```

`NoireLibMain.AddModule<NoireNetworker>()` creates the same deferred state. Finish it the same way:

```csharp
var networker = NoireLibMain.AddModule<NoireNetworker>();

networker.Options.EnableLan = true;
networker.SetNetworkName("MyPlugin.Sync");
networker.SetActive(true);
```

Activating without a network name logs an error and leaves the module inactive.

### Property Configuration

Or configure it after creation:

```csharp
var networker = NoireLibMain.GetModule<NoireNetworker>();

// Leaves the current network and joins the new one when active.
networker?.SetNetworkName("MyPlugin.OtherNetwork");

networker?.SetEnableLogging(false);

networker?.SetActive(false);
networker?.SetActive(true);
```

The methods chain:
```csharp
var networker = NoireLibMain.GetModule<NoireNetworker>();

networker?
    .SetEnableLogging(false)
    .SetNetworkName("MyPlugin.OtherNetwork")
    .SetActive(true);
```

`Activate()` and `Deactivate()` are available as alternatives to `SetActive(bool)`.

Read-only properties:

- `SelfId`: The local instance's session-scoped id. A relaunch gets a new one.
- `NetworkName`: The name of the network this instance belongs to. Default: `null` until set.
- `State`: The current `NetworkerState`. Default: `Stopped` until activated.
- `IsHub`: Whether this instance is the machine's hub. Informational only.
- `Self`: The local instance's own presence, a `NetworkerSelf`.
- `OtherPeers`: Every other peer, same-PC and LAN alike.
- `Options`: The `NetworkerOptions` of this networker.
- `IsActive`: Whether the module is active. Set by the `active` constructor argument. False without a network name.
- `EnableLogging`: Whether this module logs its actions. Default: `true`.
- `ModuleId`: The optional module identifier. Default: `null`.

### Networker Options

Every setting has a working default for same-PC operation. Options are snapshotted on activation. **Changes need a restart (deactivate, then activate).**

- `EnableLan`: Whether this network participates on the LAN. Default: `false` (same-PC only).
- `LanSecret`: A pre-shared secret gating LAN peers. The handshake proves it without sending it. Default: `null` (open to any LAN peer, logged as a warning).
- `EventBus`: A `NoireEventBus` to integrate with, required by `ShareEvent<TEvent>` and the module's own events. Default: `null`.
- `PublishModuleEvents`: Whether networker lifecycle events are published to the attached `EventBus`. Default: `true`.
- `BeaconPort`: Overrides the UDP port for LAN beacons. Null derives it from the network name. Default: `null`.
- `DefaultRequestTimeout`: The default timeout for `Request` when none is provided. Default: `10 seconds`.
- `DeliveryQueueCapacity`: The most inbound deliveries queued for the framework thread. The oldest are dropped with an error log past it. Default: `4096`.
- `OutboundBufferCapacity`: The most outbound messages buffered while the network starts or re-elects. Default: `4096`. See [Delivery Guarantees and Limitations](#delivery-guarantees-and-limitations).
- `MaxFrameBytes`: The largest wire frame. Larger frames are rejected and logged. Default: `1 MB`.
- `BeaconInterval`: The interval between LAN discovery beacons. Default: `3 seconds`.
- `PingInterval`: The interval between keep-alive pings on network links. Default: `2 seconds`.
- `LanLinkTimeout`: How long a LAN hub link may stay silent before it counts as dead. Default: `8 seconds`.

`Clone()` returns a shallow copy of an options object.

### The Network Name

The network name decides who sees whom. It derives the kernel mutex and rendezvous names and is checked in every handshake. Two plugins coexist on one machine with distinct names. A typo shows as an instance that is `Ready` but alone.

---

## Messaging

### 1. Subscribing to a Message Type

A message type must be subscribed with `On<TMessage>`. Messages of an unsubscribed type are dropped and logged:

```csharp
var token = networker.On<PingMessage>((peer, message) =>
{
    // Runs on the framework thread.
});

// Optionally, use a key. Subscribing again with the same key replaces the previous subscription.
networker.On<PingMessage>((peer, message) => { /* ... */ }, key: "my-handler");

token.Dispose();
```

### 2. Broadcasting

`Send` reaches every other peer. The sender never receives its own broadcast:

```csharp
networker.Send(new PingMessage { Text = "Everyone gets this" });
```

### 3. Targeting One Peer

`SendTo` reaches exactly one peer:

```csharp
var peer = networker.OtherPeers.FirstOrDefault(p => p["character"] == "Character Name");

if (peer != null)
    networker.SendTo(peer, new PingMessage { Text = "Only you get this" });
```

Targeting the local instance is ignored with a warning.

---

## Requests and Responses

A request expects a typed answer.

### 1. Answering Requests

One handler per request type. Registering the type again replaces the handler with a warning. Handlers run on the framework thread:

```csharp
public class StatusRequest { public int Value { get; set; } }
public class StatusReply { public int Echo { get; set; } }

networker.OnRequest<StatusRequest, StatusReply>((peer, request) => new StatusReply { Echo = request.Value * 2 });
```

An asynchronous overload:

```csharp
networker.OnRequest<StatusRequest, StatusReply>(async (peer, request) =>
{
    await SomeWorkAsync();
    return new StatusReply { Echo = request.Value };
});
```

### 2. Asking One Peer

The await resumes on the framework thread. **Never `.Wait()` or `.Result` on the task from the framework thread.** The completion is posted onto that thread:

```csharp
var reply = await networker.Request<StatusRequest, StatusReply>(peer, new StatusRequest { Value = 21 });
// reply.Echo == 42

// With an explicit timeout, overriding NetworkerOptions.DefaultRequestTimeout.
var reply2 = await networker.Request<StatusRequest, StatusReply>(peer, new StatusRequest { Value = 1 }, TimeSpan.FromSeconds(3));
```

`Request` throws:
- `TimeoutException` when the peer did not answer in time.
- `PeerLeftException` (carrying `PeerId`) when the peer left the network before answering.
- `InvalidOperationException` when the remote handler failed or is missing, when the networker is inactive, or when the target is the local instance.

### 3. Asking Every Peer

`RequestAll` fans the request out. It completes when every peer answered or the timeout elapsed and holds **only the successful answers**. Failures are logged, never thrown:

```csharp
var answers = await networker.RequestAll<StatusRequest, StatusReply>(new StatusRequest { Value = 0 });

foreach (var (peer, reply) in answers)
    NoireLogger.LogInfo($"{peer} answered {reply.Echo}");
```

An empty network returns an empty dictionary.

---

## Presence

### 1. The Peer List

`OtherPeers` lists every other instance, same-PC and LAN alike. Each is a `NetworkerPeer`:

- `Id`: The peer's session-scoped id.
- `IsSameMachine`: Whether the peer runs on this machine. Diagnostic only.
- `Metadata`: A snapshot of the peer's metadata.
- `Flags`: A snapshot of the peer's coordination flags.
- `this[string key]`: Reads one metadata value, or null when the key is not set.
- `HasFlag(string flag)`: Whether the peer carries a coordination flag.

Peer state is only mutated on the delivery thread. Reads inside a handler are coherent.

### 2. Your Own Presence

`Self` is a `NetworkerSelf`, a `NetworkerPeer` with writes. Its metadata is announced to every peer:

```csharp
networker.Self
    .Set("character", "Character Name")
    .Set("world", "Ragnarok");

networker.Self.Remove("world");
```

Durable identity belongs in metadata.

### 3. Presence Callbacks

All run on the framework thread. An optional `key` replaces a previous subscription with the same key:

```csharp
networker.OnPeerJoined(peer => NoireLogger.LogInfo($"{peer} joined"));
networker.OnPeerLeft(peer => NoireLogger.LogInfo($"{peer} left"));

networker.OnPeerUpdated((peer, key) =>
{
    // key is the metadata key, "flag:<name>" for a flag change, or "*" for a full-state update.
    if (key == "character")
        NoireLogger.LogInfo($"{peer} is now {peer["character"]}");
});
```

Each returns a `NoireSubscriptionToken`. Dispose it to unsubscribe.

---

## Coordination Flags and Barriers

Flags are named booleans on an instance, visible to every peer. They clear when their instance leaves. A departed instance never holds a barrier open.

```csharp
networker.SetFlag("ready");
networker.ClearFlag("ready");

if (networker.HasFlag("ready")) { /* ... */ }

var peerIsReady = peer.HasFlag("ready");
```

`WhenAllFlagged` waits until the local instance and every connected peer carry a flag:

```csharp
// Wait until this instance and at least 2 other peers all carry "ready", for up to 30 seconds.
var everyoneReady = await networker.WhenAllFlagged("ready", TimeSpan.FromSeconds(30), minimumOthers: 2);

if (!everyoneReady)
    NoireLogger.LogWarning("Not everyone got ready in time.");
```

- `minimumOthers` (default `1`): the minimum number of other connected peers, against a barrier trivially true on an empty network. Negative throws `ArgumentOutOfRangeException`.
- `timeout` is optional. On expiry the task completes with `false`. Without one it only completes when the condition holds or the networker stops.
- Membership is live. A peer joining or leaving re-evaluates the barrier.
- Evaluation pauses while the networker is not `Ready`.
- Stopping the networker completes every pending barrier with `false`.
- Calling it while inactive returns a task faulted with `InvalidOperationException`.

The await resumes on the framework thread. Always await.

---

## Connection State

`State` is one of four `NetworkerState` values. `OnStateChanged` observes the transitions:

```csharp
networker.OnStateChanged(state =>
{
    if (state == NetworkerState.Ready)
        NoireLogger.LogInfo("Connected to the network.");
});
```

- `Stopped`: Not running, before activation and after deactivation or disposal.
- `Starting`: Joining the network after activation.
- `Ready`: Connected. Messages route immediately.
- `Reelecting`: The hub was lost and a new one is being elected. Outbound messages are buffered.

Transitions: `Stopped` to `Starting` on activation, `Starting` to `Ready` once joined, `Ready` to `Reelecting` when the hub disappears, `Reelecting` to `Ready` once a new hub is elected, and any state to `Stopped` on deactivation or disposal.

`Stopped` is always the last transition, delivered synchronously before `SetActive(false)` or `Dispose()` returns. By then the peer list is empty, `IsHub` is false, and sends are refused. See [Delivery Guarantees and Limitations](#delivery-guarantees-and-limitations).

### How Election Looks From Here

Election needs nothing from you:

- The first instance to acquire the network's named kernel mutex becomes hub. `IsHub` reports it.
- A hub and a client send, receive and request identically.
- When the hub disappears, the survivors move to `Reelecting`, one acquires the mutex, and the network returns to `Ready`. Peers, metadata and flags reconverge. Peers missing five seconds after a failover are reported as departed.
- A connected instance re-elects the moment its hub connection ends.
- An instance that cannot join retries, backing off from 100 milliseconds to a couple of seconds. A hub slow to start is still joined within a few hundred milliseconds.
- Requests to an instance that left fail immediately with `PeerLeftException`.

---

## EventBus Integration

The networker bridges a `NoireEventBus` across the network: publishing an event on one instance's bus publishes it on every other instance's bus. It also publishes its own lifecycle events.

Both need `NetworkerOptions.EventBus`, set before activation.

### Quick Example

```csharp
using NoireLib.EventBus;
using NoireLib.Networker;

// A shared event. Implementing INetworkerEvent is optional and stamps the origin peer on bridged-in events.
public class RaidStartedEvent : INetworkerEvent
{
    public string Encounter { get; set; } = string.Empty;
    public NetworkerPeer? Origin { get; set; }
}

var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Networker");

var networker = NoireLibMain.AddModule(new NoireNetworker(
    networkName: "MyPlugin.Sync",
    options: new NetworkerOptions { EventBus = eventBus }
));

var share = networker.ShareEvent<RaidStartedEvent>();

eventBus?.Subscribe<RaidStartedEvent>(evt =>
{
    // Origin is null when the event was published locally, and the sending peer when it arrived from the network.
    var source = evt.Origin != null ? evt.Origin.ToString() : "this instance";
    NoireLogger.LogInfo($"{evt.Encounter} started, reported by {source}");
}, owner: this);

// Reaches every instance's bus.
eventBus?.Publish(new RaidStartedEvent { Encounter = "Example" });

share.Dispose();
```

`ShareEvent<TEvent>` is loop-safe. An event bridged in is never bridged back out. A local publish reaches local subscribers once and each remote instance once.

`ShareEvent` takes an optional `NetworkerShareDirection`:
- `Both` (default) - local publishes go to all peers, and events from peers are published locally.
- `Outbound` - only local publishes go to all peers.
- `Inbound` - only events from peers are published locally.

`ShareEvent` without an `EventBus` logs a warning and returns an inert token.

### Available Events

- `NetworkerPeerJoinedEvent` - A peer joined the network
- `NetworkerPeerLeftEvent` - A peer left the network
- `NetworkerPeerUpdatedEvent` - A peer's metadata or flags changed
- `NetworkerStateChangedEvent` - The networker's connection state changed

Each carries the `NoireNetworker` that observed it. `NetworkerOptions.PublishModuleEvents = false` keeps them off the bus. `ShareEvent` still works.

---

## Advanced Features

### Multiple Networks

Distinct networks are distinct names, each its own module instance retrieved by module id:

```csharp
NoireLibMain.AddModule(new NoireNetworker("MyPlugin.Sync", moduleId: "Sync"));
NoireLibMain.AddModule(new NoireNetworker("MyPlugin.Control", moduleId: "Control"));

var sync = NoireLibMain.GetModule<NoireNetworker>("Sync");
var control = NoireLibMain.GetModule<NoireNetworker>("Control");
```

### Enabling LAN

```csharp
var networker = NoireLibMain.AddModule(new NoireNetworker(
    networkName: "MyPlugin.Sync",
    options: new NetworkerOptions
    {
        EnableLan = true,
        LanSecret = "a-shared-secret",    // Strongly recommended, see below
        BeaconPort = 41500,               // Optional, derived from the network name when null
    }
));
```

Each hub broadcasts UDP beacons carrying a salted hash of the network name, and hubs link over TCP. Remote peers appear in `OtherPeers` like local ones, told apart by `IsSameMachine`.

Notes:
- **Set a `LanSecret`.** Without one any LAN peer knowing the network name can join, and the module logs a warning.
- The first LAN use may need inbound connections allowed for the game in Windows Firewall.
- If another application holds the beacon port, LAN discovery is disabled for the session with an error log. Same-PC operation continues. Override `BeaconPort`.
- Links are single-hop. A hub does not forward LAN traffic to a third machine.

### Disposal

Disposing the module announces its departure, fails every pending request and barrier, releases the hub role (the survivors re-elect at once) and returns to `Stopped`.

```csharp
networker.Dispose();
```

Modules registered through `NoireLibMain.AddModule` are disposed with the library. `SetActive(false)` performs the same teardown and keeps the module reusable.

Teardown does not wait on the network. The departure announcement is written in the background and is best-effort. If it fails, peers notice at their next ping timeout.

---

## Delivery Guarantees and Limitations

### Everything Consumer-Visible Runs on the Framework Thread

Message handlers, request handlers, peer and state callbacks, bridged EventBus publishes, and the continuations of `Request`, `RequestAll` and `WhenAllFlagged` all run on the framework thread through one ordered queue:

- Game state is safe inside any networker callback.
- **Never `.Wait()` or `.Result` on a networker task from the framework thread.** It deadlocks. Always `await`.

Deliveries are processed in arrival order.

The queue holds `NetworkerOptions.DeliveryQueueCapacity` deliveries (default 4096). If the framework thread freezes long enough, the **oldest are dropped** with an error log.

**One exception.** The final `Stopped` change is delivered synchronously on the thread that stopped the networker. Stopping from a background thread runs that `OnStateChanged` handler there.

### Outbound Buffering Across a Hub Re-election Is Bounded and Lossy

The most important limit.

- While `Ready`, a send routes immediately.
- While `Starting` or `Reelecting`, sends are **buffered** and flushed once `Ready`.
- **Past `NetworkerOptions.OutboundBufferCapacity` (default 4096) buffered messages, further sends are dropped** with a warning.
- While `Stopped`, a send is dropped with a warning.

**Delivery across a hub failover is best-effort.** A message can also be lost when a link drops. If a message must not be lost, have the receiver confirm it (a `Request` does) and the sender retry.

Presence, state broadcasts, flags and requests are unaffected: each re-announces its state after a reconnect or surfaces the failure.

### Other Notes

- A message type must be subscribed with `On<TMessage>`. Messages of an unsubscribed type are dropped and logged.
- Messages are JSON through Newtonsoft.Json and only materialize into locally registered types. Keep them to plain serializable data.
- A frame is capped at `MaxFrameBytes` (default 1 MB). Chunk large payloads yourself.
- Peer ids are session-scoped. Put durable identity in metadata.

---

## Troubleshooting

### Instances do not see each other
- Ensure NoireLib is initialized before adding the module.
- Confirm the module is active (`IsActive == true`) and that `State == NetworkerState.Ready`.
- Every instance must use the **exact same network name**, case-sensitive.
- Confirm the instances run on the same PC, or that `EnableLan` is true on every instance for LAN peers.
- Check that nothing blocks loopback TCP for the game process.
- Check `/xllog`.
- If it still does not work, please report it.

### Messages are not received
- Ensure the receiver subscribed with `On<TMessage>`.
- The message class must match on both sides, namespace included.
- `Send` never delivers to the sender.
- The message type must be JSON-serializable with public get/set properties.
- Check for a warning about the outbound buffer or the frame size limit in `/xllog`.

### Requests time out or fail
- The target must register `OnRequest<TRequest, TResponse>` for that exact type. Otherwise the request fails with `InvalidOperationException` mentioning "No handler registered".
- Never `.Wait()` or `.Result` on the task from the framework thread.
- Raise the timeout with `timeout` or `NetworkerOptions.DefaultRequestTimeout` for a slow handler.
- `PeerLeftException` means the peer left before answering, expected during a failover.

### A barrier never completes
- Every instance, the local one included, must call `SetFlag` with the same flag name.
- Check `minimumOthers` against the connected peers.
- Evaluation pauses while the networker is not `Ready`.
- Pass a `timeout` to resolve to `false` on expiry.

### LAN peers do not appear
- Set `EnableLan = true` on **every** instance, on both machines.
- Both machines need the same network name and `LanSecret`. A mismatched secret fails the handshake and logs a rejection.
- Allow inbound connections for the game process in Windows Firewall on both machines.
- Both machines must be on the same subnet with UDP broadcast allowed.
- Look for a beacon port bind failure in `/xllog` and override `BeaconPort`.
- The LAN path is untested across two physical machines.

### EventBus events are not shared
- Set `NetworkerOptions.EventBus` **before** activation.
- Confirm `ShareEvent<TEvent>` was called on every instance that should send or receive the type.
- Check the `NetworkerShareDirection`: `Outbound` only sends and `Inbound` only receives.
- Confirm the EventBus module is active and has subscribers.
- For the lifecycle events, check `PublishModuleEvents`.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
