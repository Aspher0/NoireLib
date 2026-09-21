# NoireLib Documentation - NoireRemote

You are reading the documentation for the `NoireRemote` system.

`NoireRemote` publishes part of your plugin to programs outside the game and calls what they publish. It is declared like `NoireIPC`, member by member.

## Table of Contents

- [Overview](#overview)
- [Getting Started](#getting-started)
- [Declaring an API](#declaring-an-api)
- [Members](#members)
- [Transports](#transports)
- [Calling from outside the game](#calling-from-outside-the-game)
- [The consumer class](#the-consumer-class)
- [Targeting an instance](#targeting-an-instance)
- [When the game is not there](#when-the-game-is-not-there)
- [Long work](#long-work)
- [Events](#events)
- [The browser console](#the-browser-console)
- [File transfer](#file-transfer)
- [Security](#security)
- [Reaching another machine](#reaching-another-machine)
- [Hosting outside the game](#hosting-outside-the-game)
- [Relationship with NoireIPC](#relationship-with-noireipc)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

- **Opt in, member by member.** Only a method, property or event carrying `[NoireRemote]` is published.
- **Two wires, one surface.** HTTP for a question and an answer, a socket for a connection that stays open and pushes events.
- **One object to call with.** `new NoireRemoteClient("MyApi")` finds the game, its port and its credential.
- **It repairs itself.** When the game restarts on another port, the plugin reloads or the network drops, the connection is rebuilt and every subscription is sent again.
- **Aim at a character or a tag.** An instance tags itself, and a caller says `.On("role", "tank")`.
- **No Dalamud in the caller.** Everything a program outside the game needs is in `NoireLib.Client`.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Publish something

```csharp
using NoireLib.Remote;
using System.Numerics;

[NoireRemoteClass("MyApi")]
public static class MyApi
{
    [NoireRemote]
    public static Vector3 GetPlayerPosition()
        => NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;

    [NoireRemote]
    public static void PlaceMarker(Vector3 position, string label)
    {
        // draw it
    }

    public static void ResetInternalCache()
    {
        // no attribute; nobody outside the game can reach this
    }
}
```

`NoireLibMain.Initialize` finds the class and publishes it. The listener binds only when something is published.

### 2. Call it from a program that is not a plugin

```csharp
using var api = new NoireRemoteClient("MyApi");

var position = await api.InvokeAsync<Vector3>("GetPlayerPosition");
await api.InvokeAsync("PlaceMarker", new { position, label = "here" });
```

### 3. Publish something that is not a static class

```csharp
this.publication = NoireRemote.Publish(this.mainWindow);            // a live window
NoireRemote.PublishType(typeof(MyApi));                             // a static type, by type
NoireRemote.Publish("MyApi", "Double", (int value) => value * 2);   // one member from a delegate
```

Each returns a `NoireRemotePublication`. Disposing it removes what it added. `NoireLibMain.Dispose` disposes the rest.

---

## Declaring an API

`[NoireRemoteClass("Name")]` names the surface and is **required**. Without it the `[NoireRemote]` members are inert unless you pass the type to `NoireRemote.Publish`. The compiler warns (`NoireLib_006`) for a static class with members and no class attribute.

The class attribute sets what its members inherit: `Transports`, `Thread`, `Requires`, `TimeoutSeconds`, `Access` and `Description`. A member overrides any of them.

```csharp
[NoireRemoteClass("MyApi", Thread = NoireRemoteThread.Background, Access = NoireRemoteAccess.Remote)]
```

## Members

| Member | What it does |
|---|---|
| Method | Published. Its parameters and return value must survive JSON. The compiler checks it (`NoireLib_004`). |
| Property whose type is **not** a delegate | Published read only: the getter is read on every call. |
| Property whose type **is** a delegate or `NoireRemoteConsumer<>` | Consumes someone else's member. See [the consumer class](#the-consumer-class). |
| Event | Published to whoever is listening. |

```csharp
[NoireRemote] public static int MajorVersion => 1;                  // read with a call, or a GET
[NoireRemote] public static event Action<string, bool>? OnState;    // pushed to listeners
```

A member runs on the framework thread by default. Anything taking more than a few milliseconds declares `Thread = NoireRemoteThread.Background`. Anything taking seconds also declares `Mode = NoireRemoteCallMode.Job`.

## Transports

A surface serves both wires unless it says otherwise:

```csharp
[NoireRemoteClass("MyApi", Transports = NoireRemoteTransport.Http)]        // HTTP only
[NoireRemote(Transports = NoireRemoteTransport.Websocket)]                  // this member, socket only
public static event Action<float>? OnTick;                                  // 60 times a second
```

The socket shares the listener's port and thread. `Transports` only limits exposure. A member kept off a wire answers there like a missing member.

## Calling from outside the game

The client names one surface and holds a transport default:

```csharp
using var auto   = new NoireRemoteClient("MyApi");                                 // per call
using var http   = new NoireRemoteClient("MyApi", NoireRemoteTransport.Http);      // nothing stays open
using var socket = new NoireRemoteClient("MyApi", NoireRemoteTransport.Websocket); // one connection, kept
```

`.Over(...)` reaches the other wire for one call. A client never holds more than one connection:

```csharp
await http.Over(NoireRemoteTransport.Websocket).InvokeAsync("SetState", new { on = true });
await socket.Over(NoireRemoteTransport.Http).InvokeAsync<bool>("IsReady");
```

Automatic means HTTP for a plain call and a socket once something has to be received. A script that calls three times and exits leaves no connection behind.

## The consumer class

The mirror of NoireIPC's consumers:

```csharp
[NoireRemoteClass("MyApi")]
public static class MyApiClient
{
    [NoireRemote] public static NoireRemoteConsumer<Func<Task<Vector3>>> GetPlayerPosition { get; set; } = null!;
    [NoireRemote] public static event Action<string, bool>? OnState;
}

using var binding = NoireRemote.Bind(typeof(MyApiClient));

var position = await MyApiClient.GetPlayerPosition.Invoke();
MyApiClient.OnState += (text, on) => Log($"{NoireRemote.Sender?.Label} said {text}");
```

The wrapper carries `IsAvailable`, `LastError` and the client's aiming methods. A type that both publishes and consumes is refused.

## Targeting an instance

The publishing side tags itself, like `networker.Self.Set(...)` in NoireNetworker:

```csharp
NoireRemote.Self.Set("role", "tank");
NoireRemote.Self.Set("slot", "2");
```

The calling side aims:

```csharp
await api.InvokeAsync("SetState", args);                        // whichever instance answers
await api.On("role", "tank").InvokeAsync("SetState", args);     // by tag
await api.On("Aspher Noire").InvokeAsync("SetState", args);     // by character
await api.On("Aspher Noire @ Omega").InvokeAsync(...);          // when two characters share a name
await api.On(12345).InvokeAsync(...);                           // by process id
await api.OnAll().InvokeAllAsync<bool>("IsReady");               // one answer per instance
```

A tag is the only handle **before a character is logged in**. Set one when the plugin loads. A tag change reaches connected callers at once.

With several instances online and none named, the most recently started answers and the caller is warned once. `NoireRemoteClient.DefaultTarget` sets an aim for a whole program.

## When the game is not there

- **A call to an absent target fails at once**, listing what is online and its tags.
- **Waiting is opt in**: `.WaitUpTo(TimeSpan.FromSeconds(30))`, or `NoireRemoteClient.DefaultWait`.
- **A connection repairs itself.** The target is resolved again on every attempt, and every subscription is sent again before the connection counts as open.
- `NoireRemoteClient.InstanceConnected` and `InstanceDisconnected` say when an instance comes and goes.

## Long work

```csharp
[NoireRemote(Mode = NoireRemoteCallMode.Job, Thread = NoireRemoteThread.Background)]
public static async Task<int> BuildReport(IProgress<double> progress, CancellationToken cancellation)
```

The `IProgress<T>` parameter never appears on the wire. A job call answers a handle straight away:

```csharp
var job = await api.StartAsync("BuildReport");
using var following = api.Follow(job, (percent, _) => Console.WriteLine(percent));

var result = await job.ResultAsync<int>();
```

Over a socket the result is pushed when the job ends. A running job is picked up again after a reconnection. Another program can follow it by id:

```csharp
var same = await api.AttachAsync(job.Id);   // Unknown when this listener has no such job
```

## Events

An annotated event reaches every caller watching its topic: the surface name and event name joined by a dot, lowercased, unless the attribute names one.

```csharp
[NoireRemote("myapi.state")] public static event Action<string, bool>? OnState;
```

A caller subscribes by prefix, over a socket:

```csharp
using var watching = api.Subscribe("myapi.", (topic, payload) => Log(topic));
```

Subscribe **before** starting the work to watch. A short job can finish before a late subscription lands. Without a socket, read `GET /noire/v1/_stream`, served as server-sent events (`EventSource` in a browser, `NoireSseClient` in NoireLib).

## The browser console

```csharp
NoireRemote.Console.Open();
```

A page generated from the manifest: every member with a form, every event as a live feed, the host's log and the metrics when enabled. `Confirm = true` makes the console ask before firing a member. A read-only property is drawn as a value.

It follows the plugin over its own websocket. Every change reaches the page within a fraction of a second: publishing or withdrawing an API, an event or a socket, **any setting** on `NoireRemote.Options`, the console's own `Style`, a client connecting or sending, a listener starting or stopping. Open cards, the scroll position and unsent input survive the redraw.

The **Sockets** view has one card per published socket. **Send to the plugin** connects the page as a client and runs the plugin's handler. The answers land in a resizable log. **Push to connected clients** sends to every client or one room and runs no handler. Below: every connected client with its address, time connected, rooms and traffic, a room filter, and a send or close per client. The same view is at `GET /noire/v1/_sockets`, and the actions at `POST /noire/v1/_sockets/{socket}/broadcast` and `.../clients/{id}/send|close`, for the console and callers on this machine only.

The **Traffic** pane shows one line per call on either wire with its result and time, and one per socket connection opened or closed.

```csharp
NoireRemote.Options.PublishTraffic = true;        // the default
NoireRemote.Options.PublishTrafficFrames = true;  // every frame in and out as well, off by default
NoireRemote.Options.MaxTrafficPreview = 256;      // how much of a frame crosses
NoireRemote.Options.TrafficBufferSize = 256;      // how many are kept
```

Nothing is built while nothing watches. With no page open a call costs one field read. Frames are off by default. A frame on a reserved socket is never reported.

The dock holds **Events**, **Traffic**, **Metrics** and the **History** of the page's calls, each replayable.

### Dalamud Logs

```csharp
NoireRemote.Options.LogScope = NoireRemoteLogScope.Plugin;      // the default: this plugin's lines
NoireRemote.Options.LogScope = NoireRemoteLogScope.Everything;  // every plugin's and Dalamud's
NoireRemote.Options.LogScope = NoireRemoteLogScope.None;        // no log view
```

Reads Dalamud's own log sink, whichever logger wrote a line, like `/xllog`: a minimum level from Verbose to Fatal, a filter and a highlight. **Copy mode** turns a drag into a selection for Copy or Ctrl+C and freezes the list. The channel keeps the last `LogBufferSize` lines (2000). `Everything` shows other plugins' lines to anyone opening the console, a phone on the network included.

### Driving a fleet

```csharp
NoireRemote.Options.AllowFleetControl = true;  // off by default
```

The driven listener gives the consent. A listener allowing it appears in the **Fleet** control of other consoles. Tick the listeners the page covers, with **Toggle all**. This listener is always in. The choice is remembered per browser.

With several listeners chosen, the APIs view merges them. Two members are **the same call** when their contract matches: API name, member name, parameters with types and defaults, and return type, compared by shape. One card per contract shows who serves it (`3/3`, or `2/3 · names`). A contract that differs on one listener is a second card. **Call** goes to every listener on the card and answers per listener with its time. A listener whose member changed since the page drew it refuses with `ContractChanged`.

The Sockets view has one card per socket per listener. Every dock pane and the log tag each line with its origin, and tabs filter to one listener or game client. Other listeners are reached through this one with the credential from their discovery record. Consent is checked on every call.

The **returns as JSON** switch (on by default) shows answers as coloured JSON, or off as an object viewer. Remembered per browser.

## File transfer

`EnableFiles` is off by default. It is the only feature writing to disk for a caller. A small `byte[]` crosses as base64. Larger data moves in chunks through `/noire/v1/_files`. See [PROTOCOL.md](PROTOCOL.md).

## Security

- **Loopback and a per-launch credential by default.** The token is in the discovery record, readable by the current user only, and changes on every reload.
- **A member is local by default.** `Access = NoireRemoteAccess.Remote` opens it to other machines, per member or per class.
- **Browser rules are enforced.** An unexpected `Origin` or `Sec-Fetch-*` set is refused.
- **The socket upgrade is authorised like any request**, with the same bearer credential.

## Changing a setting

Assign it. A running listener reconciles itself: console, discovery, record and every manifest holder follow. **The listener does not rebind and its port does not change.**

```csharp
NoireRemote.Options.EnableConsole = true;      // the console is serving, on its own socket
NoireRemote.Options.AllowFleetControl = false; // in force on the next request
```

Five settings need a restart: `BindAddress`, `Port`, `EnableRemote`, `RemoteSecret` and `EnableDualStack`. Assigning one is logged and applies at the next `Start()`, on the same port.

**A busy port is waited for.** The listener retries on the `BindRetry` schedule and takes the port once it frees. `BindRetry = NoireRetryPolicy.None` fails at the first refusal.

The refusal says what it found:

```
port 47821 is in use: another listener is accepting on 127.0.0.1. Trying again in 1 s.
port 47821 is in use: 3 connection(s) from a previous listener are still closing. Trying again in 2 s.
port 47821 is in use: nothing this process can see holds it. Trying again in 4 s.
```

Connections a previous listener left do **not** block a new one. Measured on Windows: `ExclusiveAddressUse` binds with `TIME_WAIT` and `FIN_WAIT_2` entries on the port. A refusal means something is accepting on it.

## Reaching another machine

```csharp
NoireRemote.Options.RemoteSecret = "a shared secret";
NoireRemote.Options.EnableRemote = true;
NoireRemote.Options.AnnounceOnNetwork = true;

NoireRemote.Stop();   // these three name the socket; they are the one case that rebinds
NoireRemote.Start();  // on the same port
```

Local network discovery is a signed question and a signed answer, never a beacon. The caller sets the same secret and calls `NoireRemoteClient.DiscoverAsync`, or `NoireRemoteClient.At("192.168.1.20:51234", secret)`.

## Hosting outside the game

The listener has no Dalamud reference. A console program, a service or a tool publishes like a plugin:

```csharp
var host = new NoireRemoteStandaloneHost { Name = "route-planner", Version = "1.0.0" };

using var server = new NoireRemoteServer(host);
server.PublishType(typeof(Planner));
server.Start();
```

A plugin calls it with the same `NoireRemoteClient`.

## Relationship with NoireIPC

| | NoireIPC | NoireRemote |
|---|---|---|
| Who calls | Another plugin, in the game | Anything outside the game |
| What may cross | Anything in the process, including pointers | Whatever survives JSON |
| Declaring | `[NoireIpcClass]` + `[NoireIpc]` | `[NoireRemoteClass]` + `[NoireRemote]` |
| Consuming | `NoireIpcConsumer<T>` property | `NoireRemoteConsumer<T>` property |
| Cost when unused | None | None: nothing binds until something is published |

A class can carry both. Each attribute is read by its own system.

## Troubleshooting

| What you see | What it means |
|---|---|
| The caller finds nothing | The surface is published but the listener never started, or `PublishDirectoryRecord` is off. Check `NoireRemote.IsListening`. |
| `UnknownMember` on a member you declared | It has no `[NoireRemote]`, or its signature cannot cross JSON and was dropped with a warning in the log. |
| `TransportNotServed` | The class or the member is restricted with `Transports`. |
| A call hangs | A framework-thread member with no framework thread, as in a test host. Declare `Thread = NoireRemoteThread.Background`. |
| `NotReady` | The member asks for character state and no character is loaded. `Requires = NoireRemoteReadiness.None` if it does not need one. |
| Events never arrive | The subscription was made after the work started, or the topic prefix does not match. Topics are lowercase. |
| 401 after a plugin reload | The credential rotated. A `NoireRemoteClient` handles it; a hand-written client reads the record again. |

## See Also

- [PROTOCOL.md](PROTOCOL.md), for writing a client in another language
- [NoireWebsocket](../Websocket/README.md), for talking to a server that is not a NoireLib listener
- [NoireIPC](../IPC/README.md), for the in-game half
