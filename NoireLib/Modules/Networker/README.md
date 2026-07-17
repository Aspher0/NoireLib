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

The `NoireNetworker` is a module that lets several running instances of your plugin talk to each other: multiple game clients on the same PC, and optionally on the same LAN. It provides:
- **Zero-configuration setup** on the same PC, only a network name is required
- **Automatic hub election** through a named kernel mutex, with no well-known ports and no server to run
- **Automatic failover** when the instance acting as hub goes away
- **Typed messaging** with broadcast and targeted sends
- **Request and response** exchanges with typed replies, timeouts, and fan-out to every peer
- **Peer presence** with synchronized metadata and coordination flags
- **Barriers** to wait until every instance reaches the same point
- **EventBus bridging**, so one event type can be published across every instance
- **Framework-thread delivery** for every handler, callback, and awaited continuation

One instance per machine is elected hub; every other instance on that machine connects to it over loopback TCP. Which instance holds the role never changes how you use the API. When LAN is enabled, each machine's hub links to the other machines' hubs, and remote peers appear in the same peer list as local ones.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create the Networker

Every instance that passes the same network name joins the same network:

```csharp
using NoireLib;
using NoireLib.Networker;

var networker = NoireLibMain.AddModule(new NoireNetworker("MyPlugin.Sync"));
```

That is all the setup needed for same-PC operation. The module elects a hub, joins the network, and reaches `NetworkerState.Ready` on its own.

### 2. Define a Message and Exchange It

A message is any JSON-serializable class:

```csharp
public class PingMessage
{
    public string Text { get; set; } = string.Empty;
}
```

Subscribe to the type to receive it, then send it. The local instance never receives its own broadcasts:

```csharp
networker.On<PingMessage>((peer, message) =>
{
    NoireLogger.LogInfo($"{peer} says: {message.Text}");
});

networker.Send(new PingMessage { Text = "Hello from another instance!" });
```

Handlers run on the framework thread, so it is safe to touch game state directly inside them.

### 3. Identify Your Instances

Peer ids are session-scoped and change on every relaunch. Durable identity belongs in metadata, which synchronizes to every peer automatically:

```csharp
networker.Self.Set("character", "Character Name");

foreach (var peer in networker.OtherPeers)
    NoireLogger.LogInfo($"Peer {peer.Id} is {peer["character"]}");
```

That's it! Your instances can now see and talk to each other.

---

## Configuration

### Module Parameters

You can configure the most important options of the module with the module's constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Networker"); // Optional

var networker = NoireLibMain.AddModule(new NoireNetworker(
    networkName: "MyPlugin.Sync",       // Required: instances only see peers using the same name
    moduleId: "Sync",                   // Optional identifier
    active: true,                       // Whether to join the network on creation
    enableLogging: true,                // Enable/disable logging for this module
    options: new NetworkerOptions       // Optional settings, same-PC operation needs none
    {
        EventBus = eventBus,
    }
));
```

A parameterless constructor also exists for deferred configuration. It creates the module inactive and without a network name, so set both before activating:

```csharp
var networker = new NoireNetworker();

networker.Options.EnableLan = true;
networker.SetNetworkName("MyPlugin.Sync");
networker.SetActive(true);

NoireLibMain.AddModule(networker);
```

`NoireLibMain.AddModule<NoireNetworker>()` lands in that same deferred state. A network name cannot be passed through it, and a networker without one cannot join anything, so it creates the module inactive instead of activating it. The same two steps finish the job:

```csharp
var networker = NoireLibMain.AddModule<NoireNetworker>();

networker.Options.EnableLan = true;
networker.SetNetworkName("MyPlugin.Sync");
networker.SetActive(true);
```

Activating with no network name set is refused: the module logs an error and returns itself to the inactive state. That only happens when you activate it yourself before naming a network, never as a consequence of how the module was created.

### Property Configuration

You can also configure the module after creation:

```csharp
var networker = NoireLibMain.GetModule<NoireNetworker>();

// Move to another network (leaves the current one and joins the new one when active)
networker?.SetNetworkName("MyPlugin.OtherNetwork");

// Enable or disable logging
networker?.SetEnableLogging(false);

// Leave the network, then join it again
networker?.SetActive(false);
networker?.SetActive(true);
```

You can also chain these methods for convenience:
```csharp
var networker = NoireLibMain.GetModule<NoireNetworker>();

networker?
    .SetEnableLogging(false)
    .SetNetworkName("MyPlugin.OtherNetwork")
    .SetActive(true);
```

`Activate()` and `Deactivate()` are available as alternatives to `SetActive(bool)`.

Additionally, you can read the following properties after having created the module:

- `SelfId`: The unique, session-scoped identifier of the local instance. A relaunched instance gets a new one.
- `NetworkName`: The name of the network this instance belongs to. Default: `null` until set.
- `State`: The current `NetworkerState`. Default: `Stopped` until activated.
- `IsHub`: Whether this instance currently is the machine's hub. Informational only, usage never differs.
- `Self`: The local instance's own presence, a `NetworkerSelf`.
- `OtherPeers`: Every other peer on the network, same-PC and LAN alike, excluding `Self`.
- `Options`: The `NetworkerOptions` of this networker.
- `IsActive`: Whether the module is active. Default: set by the `active` constructor argument, and false whenever the module was created without a network name.
- `EnableLogging`: Whether this module logs its actions. Default: `true`.
- `ModuleId`: The optional module identifier. Default: `null`.

### Networker Options

Every setting below is public and has a default that works for same-PC operation. Options are snapshotted when the module activates, so **changes made while the networker is active require a restart (deactivate, then activate) to apply**.

- `EnableLan`: Whether this network participates on the LAN. Default: `false` (same-PC only).
- `LanSecret`: An optional pre-shared secret gating LAN peers. The handshake proves knowledge of it without sending it over the wire. Default: `null` (open to any LAN peer, which is logged as a warning).
- `EventBus`: An optional `NoireEventBus` to integrate with. Required by `ShareEvent<TEvent>` and by the publication of the module's own events. Default: `null`.
- `PublishModuleEvents`: Whether networker lifecycle events are published to the attached `EventBus`. Default: `true`.
- `BeaconPort`: Overrides the UDP port used for LAN discovery beacons. When null, the port is derived from the network name. Default: `null`.
- `DefaultRequestTimeout`: The default timeout for `Request` when none is provided. Default: `10 seconds`.
- `DeliveryQueueCapacity`: The maximum number of inbound deliveries queued for the framework thread before the oldest are dropped with an error log. Default: `4096`.
- `OutboundBufferCapacity`: The maximum number of outbound messages buffered while the network is starting or re-electing. Default: `4096`. See [Delivery Guarantees and Limitations](#delivery-guarantees-and-limitations).
- `MaxFrameBytes`: The maximum size of a single wire frame in bytes. Oversized frames are rejected and logged. Default: `1 MB`.
- `BeaconInterval`: The interval between LAN discovery beacons. Default: `3 seconds`.
- `PingInterval`: The interval between keep-alive pings on network links. Default: `2 seconds`.
- `LanLinkTimeout`: How long a LAN hub link may stay silent before it is considered dead. Default: `8 seconds`.

`Clone()` returns a shallow copy of an options object.

### The Network Name

The network name is the only thing that decides who sees whom. Instances using different names never meet: the name derives the kernel mutex and rendezvous names, so a different name elects an entirely separate hub, and it is verified again during every handshake. A connection whose network name does not match is rejected outright. Two plugins can therefore safely coexist on one machine by picking distinct names, and a typo in the name presents as an instance that is `Ready` but permanently alone.

---

## Messaging

### 1. Subscribing to a Message Type

A message type must be subscribed with `On<TMessage>` to be received. Inbound messages of a type nobody subscribed to are dropped and logged:

```csharp
var token = networker.On<PingMessage>((peer, message) =>
{
    // Runs on the framework thread.
});

// Optionally, use a key. Subscribing again with the same key replaces the previous subscription.
networker.On<PingMessage>((peer, message) => { /* ... */ }, key: "my-handler");

// Dispose the token to unsubscribe.
token.Dispose();
```

### 2. Broadcasting

`Send` reaches every other peer on the network. The sender never receives its own broadcast:

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

Targeting the local instance is ignored and logged as a warning.

---

## Requests and Responses

Where `Send` is fire-and-forget, a request expects a typed answer back.

### 1. Answering Requests

One handler per request type; registering the same request type again replaces the previous handler and logs a warning. Handlers run on the framework thread:

```csharp
public class StatusRequest { public int Value { get; set; } }
public class StatusReply { public int Echo { get; set; } }

networker.OnRequest<StatusRequest, StatusReply>((peer, request) => new StatusReply { Echo = request.Value * 2 });
```

An asynchronous overload is available for handlers that need to await:

```csharp
networker.OnRequest<StatusRequest, StatusReply>(async (peer, request) =>
{
    await SomeWorkAsync();
    return new StatusReply { Echo = request.Value };
});
```

### 2. Asking One Peer

The await resumes on the framework thread. **Never sync-block (`.Wait()` or `.Result`) on the returned task from the framework thread, always await it**, since the completion is posted onto that same thread and blocking it self-deadlocks:

```csharp
var reply = await networker.Request<StatusRequest, StatusReply>(peer, new StatusRequest { Value = 21 });
// reply.Echo == 42

// With an explicit timeout, overriding NetworkerOptions.DefaultRequestTimeout.
var reply2 = await networker.Request<StatusRequest, StatusReply>(peer, new StatusRequest { Value = 1 }, TimeSpan.FromSeconds(3));
```

`Request` throws:
- `TimeoutException` when the peer did not answer in time.
- `PeerLeftException` (carrying `PeerId`) when the peer left the network before answering.
- `InvalidOperationException` when the remote handler failed, when the remote registered no handler for the type, when the networker is not active, or when the target is the local instance.

### 3. Asking Every Peer

`RequestAll` fans the request out and collects the answers. It completes when every peer answered or the timeout elapsed, and contains **only the successful answers**; failures are logged and omitted rather than thrown:

```csharp
var answers = await networker.RequestAll<StatusRequest, StatusReply>(new StatusRequest { Value = 0 });

foreach (var (peer, reply) in answers)
    NoireLogger.LogInfo($"{peer} answered {reply.Echo}");
```

An empty network returns an empty dictionary.

---

## Presence

### 1. The Peer List

`OtherPeers` is one flat list of every other instance, same-PC and LAN alike, excluding the local instance. Each entry is a `NetworkerPeer`:

- `Id`: The unique, session-scoped identifier of the peer.
- `IsSameMachine`: Whether the peer runs on this machine. Diagnostic only, the API never behaves differently for LAN peers.
- `Metadata`: A snapshot of the peer's metadata.
- `Flags`: A snapshot of the peer's coordination flags.
- `this[string key]`: Reads one metadata value, or null when the key is not set.
- `HasFlag(string flag)`: Whether the peer carries a coordination flag.

Peer state is only ever mutated on the delivery thread, so anything read from inside a handler is coherent.

### 2. Your Own Presence

`Self` is a `NetworkerSelf`, which extends `NetworkerPeer` with writes. Metadata set here is announced to every peer automatically:

```csharp
networker.Self
    .Set("character", "Character Name")
    .Set("world", "Ragnarok");

networker.Self.Remove("world");
```

Because peer ids are session-scoped, metadata is where durable identity belongs.

### 3. Presence Callbacks

All of these run on the framework thread, and all accept an optional `key` that replaces a previous subscription with the same key:

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

Each returns a `NoireSubscriptionToken`; dispose it to unsubscribe.

---

## Coordination Flags and Barriers

Flags are named booleans on an instance, visible to every peer. They clear automatically when the instance that set them leaves the network, so a departing instance can never hold a barrier open.

```csharp
networker.SetFlag("ready");
networker.ClearFlag("ready");

if (networker.HasFlag("ready")) { /* ... */ }

// Read a peer's flags.
var peerIsReady = peer.HasFlag("ready");
```

`WhenAllFlagged` waits until the local instance and every connected peer carry a flag:

```csharp
// Wait until this instance and at least 2 other peers all carry "ready", for up to 30 seconds.
var everyoneReady = await networker.WhenAllFlagged("ready", TimeSpan.FromSeconds(30), minimumOthers: 2);

if (!everyoneReady)
    NoireLogger.LogWarning("Not everyone got ready in time.");
```

- `minimumOthers` (default `1`) is the minimum number of other peers that must be connected, which guards against a barrier that is trivially true on an empty network. Passing a negative value throws `ArgumentOutOfRangeException`.
- `timeout` is optional. When it elapses, the task completes with `false` rather than throwing. Without a timeout, the task only completes when the condition is met or the networker stops.
- Membership is evaluated live, so a peer joining or leaving re-evaluates the barrier.
- Evaluation pauses while the networker is not `Ready`, which keeps a barrier from completing on a partial view during a hub re-election.
- Stopping the networker completes every pending barrier with `false`.
- Calling it while the networker is not active returns a task faulted with `InvalidOperationException`.

The await resumes on the framework thread, so the same rule applies: always await, never sync-block.

---

## Connection State

`State` reports one of four `NetworkerState` values, and `OnStateChanged` observes the transitions:

```csharp
networker.OnStateChanged(state =>
{
    if (state == NetworkerState.Ready)
        NoireLogger.LogInfo("Connected to the network.");
});
```

- `Stopped`: The networker is not running. This is the state before activation and after deactivation or disposal.
- `Starting`: Joining the network for the first time after activation.
- `Ready`: Connected and fully operational. Messages route immediately.
- `Reelecting`: The hub was lost and a new one is being elected. Outbound messages are buffered until `Ready`.

The transitions a consumer can observe are `Stopped` to `Starting` on activation, `Starting` to `Ready` once the instance has joined, `Ready` to `Reelecting` when the hub disappears, `Reelecting` to `Ready` once a new hub is elected, and any state to `Stopped` on deactivation or disposal.

`Stopped` is always the last transition a handler sees, and it is delivered before `SetActive(false)` or `Dispose()` returns rather than on a later frame, so a handler that mirrors `State` into your own UI never gets stranded showing `Ready` after the networker has gone. By the time it runs, the peer list is empty, `IsHub` is false, and sends are refused, which is exactly the state it is reporting. See [Delivery Guarantees and Limitations](#delivery-guarantees-and-limitations) for the threading detail.

### How Election Looks From Here

Election needs nothing from you and is worth understanding only to know what you are seeing in the logs:

- Any instance may become the hub. The first one to acquire the network's named kernel mutex takes the role, and `IsHub` reports it.
- Nothing about the API changes based on the role. A hub and a client send, receive, and request identically.
- When the hub disappears (a clean exit or a crash), the survivors move to `Reelecting`, one of them acquires the mutex, and the network returns to `Ready` on its own. Peers, metadata, and flags reconverge automatically. Peers that do not reappear within a five second grace period after a failover are reported as departed.
- A failover is contended for immediately. An instance that is connected holds its hub connection for as long as the hub lives, and re-elects the moment that connection ends, so nothing delays a survivor from taking the vacant role.
- An instance that cannot join at all (the hub is still starting, or is unreachable) retries on its own, backing off from 100 milliseconds up to a couple of seconds between attempts. A network that never forms therefore settles into a quiet retry rather than re-electing continuously, and a hub that is merely slow to start is still joined within a few hundred milliseconds.
- Pending requests addressed to an instance that left fail with `PeerLeftException` instead of hanging until their timeout.

---

## EventBus Integration

The `NoireNetworker` can bridge a `NoireEventBus` across the network, so that publishing an event on one instance's bus publishes it on every other instance's bus. It also publishes its own lifecycle events to the attached bus.

Both require `NetworkerOptions.EventBus` to be set. Since options are snapshotted on activation, set it before the module activates.

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

// Share the type with the network.
var share = networker.ShareEvent<RaidStartedEvent>();

eventBus?.Subscribe<RaidStartedEvent>(evt =>
{
    // Origin is null when the event was published locally, and the sending peer when it arrived from the network.
    var source = evt.Origin != null ? evt.Origin.ToString() : "this instance";
    NoireLogger.LogInfo($"{evt.Encounter} started, reported by {source}");
}, owner: this);

// Publishing locally now reaches every instance's bus.
eventBus?.Publish(new RaidStartedEvent { Encounter = "Example" });

// Dispose the token to stop sharing the type.
share.Dispose();
```

`ShareEvent<TEvent>` is loop-safe by construction: an event bridged in from the network is never bridged back out, so two instances cannot ping-pong an event between their buses. A local publish reaches local subscribers exactly once, and each remote instance exactly once.

`ShareEvent` takes an optional `NetworkerShareDirection`:
- `Both` (default) - local publishes go to all peers, and events from peers are published locally.
- `Outbound` - only local publishes go to all peers.
- `Inbound` - only events from peers are published locally.

Calling `ShareEvent` without an `EventBus` configured logs a warning, shares nothing, and returns an inert token.

### Available Events

- `NetworkerPeerJoinedEvent` - A peer joined the network
- `NetworkerPeerLeftEvent` - A peer left the network
- `NetworkerPeerUpdatedEvent` - A peer's metadata or flags changed
- `NetworkerStateChangedEvent` - The networker's connection state changed

Each carries the `NoireNetworker` that observed it. Set `NetworkerOptions.PublishModuleEvents` to false to keep them off the bus while still using `ShareEvent`.

---

## Advanced Features

### Multiple Networks

Distinct networks are just distinct names, and each can be its own module instance retrieved by module id:

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

Each machine's hub broadcasts UDP beacons carrying a salted hash of the network name, never the name itself, and hubs link to each other over TCP. Remote peers then appear in `OtherPeers` exactly like local ones, distinguishable only by `IsSameMachine`.

Notes on enabling it:
- **Set a `LanSecret`.** Without one the network is open to any peer on the LAN that knows the network name, and the module logs a warning saying so. The handshake proves knowledge of the secret without transmitting it.
- The first LAN use may require allowing inbound connections for the game process in Windows Firewall.
- If a foreign application already occupies the beacon port, LAN discovery is disabled for the session with an error log, and same-PC operation continues unaffected. Override `BeaconPort` to pick another.
- Links are single-hop: a hub relays between its own clients and its direct hub links, and does not forward LAN traffic on to a third machine's hub.

### Disposal

Disposing the module leaves the network cleanly: it announces its departure so peers see it leave promptly rather than waiting for a timeout, fails every pending request and barrier, releases the hub role if it held one (which triggers an immediate re-election among the survivors), and returns to `Stopped`.

```csharp
networker.Dispose();
```

Modules registered through `NoireLibMain.AddModule` are disposed with the library, so an explicit call is only needed when you manage the lifecycle yourself. `SetActive(false)` performs the same teardown while leaving the module reusable; activating it again rejoins the network.

Teardown does not wait on the network. The departure announcement is written in the background and closes its socket once it is out, so disposing from the framework thread does not stall a frame waiting for peers to acknowledge anything. The announcement is best-effort, as it always was: if it cannot be written, peers fall back to noticing the departure at their next ping timeout instead.

---

## Delivery Guarantees and Limitations

This section documents behavior that is easier to read here than to discover in production.

### Everything Consumer-Visible Runs on the Framework Thread

Message handlers, request handlers, peer callbacks, state callbacks, bridged-in EventBus publishes, and the continuations of `Request`, `RequestAll`, and `WhenAllFlagged` are all delivered on the framework thread, through a single ordered queue drained on the framework update. Two consequences:

- You may touch game state directly inside any networker callback, with no marshalling of your own.
- **Never sync-block (`.Wait()` or `.Result`) on a networker task from the framework thread.** The completion is posted onto that same thread, so blocking it deadlocks against itself. Always `await`.

Ordering is preserved: deliveries are processed in the order they were received.

That inbound queue is bounded by `NetworkerOptions.DeliveryQueueCapacity` (default 4096). If the framework thread is frozen long enough for the queue to overflow, the **oldest deliveries are dropped** with an error log rather than growing memory without limit.

There is **one exception**: the final `Stopped` state change is delivered synchronously, on whatever thread stopped the networker, before `Dispose()` or `SetActive(false)` returns. The queue is torn down as part of stopping, so a `Stopped` routed through it would be discarded and never seen at all. Everything else, including the transitions to `Starting`, `Ready`, and `Reelecting`, goes through the queue as described above. If you deactivate or dispose the networker from a background thread, an `OnStateChanged` handler observing `Stopped` therefore runs on that thread, so avoid touching game state from inside it unless you know the stop came from the framework thread.

### Outbound Buffering Across a Hub Re-election Is Bounded and Lossy

This is the most important limit to know.

- While the networker is `Ready`, a send routes immediately.
- While it is `Starting` or `Reelecting`, sends are **buffered** and flushed the moment it becomes `Ready` again. A brief failover is therefore usually invisible to your code.
- **Past `NetworkerOptions.OutboundBufferCapacity` (default 4096) buffered messages, further sends are dropped** with a warning log. They are not queued, not retried, and not reported to the caller.
- While the networker is `Stopped`, a send is dropped with a warning.

**Delivery across a hub failover is therefore best-effort, not guaranteed.** A message can also be lost in the moment a link drops. If your plugin must not lose a message across a failover, implement your own acknowledgement: have the receiver confirm receipt (a `Request` round trip does this naturally) and have the sender retry when the confirmation does not arrive. Raising `OutboundBufferCapacity` widens the window but does not remove the limit.

Nothing here is a problem for the common cases (presence, state broadcasts, coordination flags, requests that carry their own timeout), because each of those either re-announces its full state after a reconnect or surfaces the failure to the caller.

### LAN Has Not Been Verified Across Two Physical Machines

Same-PC multi-process operation is covered by an integration suite that runs the real stack: kernel mutex election, the rendezvous file, and loopback TCP, across multiple module instances, including hub failover and reconvergence.

**The LAN path (UDP discovery beacons and hub-to-hub links) is implemented but has never been verified across two physical machines.** It is untested territory rather than a known-broken feature, and it is off by default. Treat `EnableLan = true` as unproven, and test it in your own environment before relying on it.

### Other Notes

- A message type must be subscribed with `On<TMessage>` before it can be received. Inbound messages of a type nobody subscribed to are dropped and logged.
- Messages are serialized as JSON with Newtonsoft.Json and materialize only into locally registered types, so a payload cannot pick the type it deserializes into. Keep message types to plain serializable data.
- A single wire frame is capped at `MaxFrameBytes` (default 1 MB). Larger frames are rejected and logged, so chunk large payloads yourself.
- Peer ids are session-scoped. A relaunched instance is a new peer; put durable identity in metadata.

---

## Troubleshooting

### Instances do not see each other
- Ensure NoireLib is initialized before adding the module.
- Confirm the module is active (`IsActive == true`) and that `State == NetworkerState.Ready`.
- Verify every instance uses the **exact same network name**, which is case-sensitive. A mismatched name presents as an instance that is `Ready` but alone.
- Confirm the instances run on the same PC, or that `EnableLan` is true on every instance for LAN peers.
- Check that no security software is blocking loopback TCP connections for the game process.
- Check the dalamud logs with `/xllog`.
- If it still does not work, please report it.

### Messages are not received
- Ensure the receiving instance subscribed to the type with `On<TMessage>`; unsubscribed types are dropped on arrival.
- Confirm the message class is identical on both sides, including its namespace, since the full type name identifies it on the wire.
- Remember that `Send` never delivers to the sender itself. Call your own logic directly if the local instance must react too.
- Ensure the message type is JSON-serializable with public get/set properties.
- Check for a warning about the outbound buffer or the frame size limit in `/xllog`.

### Requests time out or fail
- Ensure the target instance registered a handler with `OnRequest<TRequest, TResponse>` for that exact request type. Without one, the request fails with `InvalidOperationException` mentioning "No handler registered".
- Confirm nothing sync-blocks (`.Wait()` or `.Result`) on the task from the framework thread, which deadlocks. Always await.
- Raise the timeout with the `timeout` argument or `NetworkerOptions.DefaultRequestTimeout` if the handler is genuinely slow.
- A `PeerLeftException` means the peer left before answering, which is expected during a failover.

### A barrier never completes
- Confirm every instance, including the local one, actually calls `SetFlag` with the same flag name.
- Check `minimumOthers` against the number of peers actually connected; a barrier waiting on more peers than exist can never complete.
- Remember that barrier evaluation pauses while the networker is not `Ready`, so a re-election in progress delays completion.
- Pass a `timeout` so the barrier resolves to `false` instead of waiting indefinitely.

### LAN peers do not appear
- Set `EnableLan = true` on **every** instance, on both machines.
- Ensure both machines use the same network name and the same `LanSecret`. A mismatched secret fails the handshake and logs a rejection.
- Allow inbound connections for the game process in Windows Firewall on both machines.
- Confirm both machines are on the same subnet and that UDP broadcast is not blocked by the network hardware.
- Look for a beacon port bind failure in `/xllog` and override `BeaconPort` if another application holds it.
- The LAN path has not been verified across two physical machines; see [Delivery Guarantees and Limitations](#delivery-guarantees-and-limitations).

### EventBus events are not shared
- Ensure `NetworkerOptions.EventBus` is set **before** the module activates, since options are snapshotted on activation.
- Confirm `ShareEvent<TEvent>` was called on every instance that should send or receive the type.
- Check the `NetworkerShareDirection`: `Outbound` only sends and `Inbound` only receives.
- Confirm the EventBus module is active and has subscribers.
- If `NetworkerPeerJoinedEvent` and friends are missing, confirm `PublishModuleEvents` is true.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
