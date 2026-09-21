# NoireRemote wire protocol

The minimum to write a client in another language. C# callers use the `NoireLib.Client` package.

## 1. Finding a listener

Each listening instance writes a JSON record to `%LOCALAPPDATA%\NoireLib\http\instances\<guid>.json`:

```json
{ "instance": "<guid>", "address": "127.0.0.1", "port": 51234, "token": "<bearer>", "pid": 1234,
  "label": "Character @ World", "endpoints": ["MyApi"], "metadata": { "role": "tank" },
  "heartbeatUtc": "2026-09-20T12:00:00Z" }
```

Read the folder, drop records whose `pid` is gone, and pick by `label`, `metadata` or `endpoints`.
The base URL is `http://{address}:{port}`.

## 2. Authentication

Every request carries `Authorization: Bearer <token>` from the record. The token changes when the plugin reloads. Read the record again on a 401. Another machine needs a shared secret and a signed header. See the README.

## 3. Calling a member

```
POST /noire/v1/{api}/{member}
{ "args": { "label": "here" }, "timeoutMs": 10000 }
```

`args` is an object keyed by parameter name, or an array in declaration order. A member marked
`readonly` in the manifest also answers a bare `GET` on the same path.

The answer is always one envelope:

```json
{ "ok": true, "result": { }, "instance": "<guid>", "elapsedMs": 4 }
{ "ok": false, "error": { "code": "UnknownMember", "message": "...", "candidates": ["PlaceMarker"] } }
```

Codes worth handling: `UnknownEndpoint`, `UnknownMember`, `TransportNotServed`, `ArgumentMissing`, `ArgumentInvalid`, `Unauthorized`, `Forbidden`, `NotReady`, `Busy`, `Timeout`, `HandlerFault`, `InstanceMismatch`, `TooLarge`, and `ContractChanged` when a fleet listener's member no longer carries the contract the call was made for.

## 4. Reading the surface

```
GET /noire/v1/_manifest
```

Each member row carries `name`, `parameters` (name, JSON type, required, default), `returnJsonType`, `mode` (`sync` or `job`), `readonly`, `transports` (`["http", "ws"]`), `access`, `requires`, `deprecated`, its description and `contract`: a fingerprint of the API name, member name, parameters with types and defaults, and return type, compared by shape. Two listeners with the same `contract` answer the call the same way. `revision` changes with the surface. `GET /noire/v1/_ping` checks a cached manifest cheaply.

## 5. Events over HTTP

Held open as server-sent events:

```
GET /noire/v1/_stream?topics=nav.&cursors=events:418

event: nav.route.done
id: 419
data: {"seq":419,"channel":"events","topic":"nav.route.done","data":{},"atUtc":"..."}
```

A `: keep-alive` comment arrives when nothing else does. `event: _missed` means events were dropped because the reader fell behind.

Polling, for a caller that cannot hold a connection:

```
POST /noire/v1/_events   { "waitMs": 20000, "since": 418, "topics": ["nav."] }
```

The answer carries `events`, `cursor` and `missed`. Poll with the cursor the previous answer returned.

## 6. The socket

`ws://{address}:{port}/noire/ws/_api`, with the same bearer header on the upgrade. Every frame is one JSON object:

```json
{ "kind": "call", "id": 1, "api": "MyApi", "member": "PlaceMarker", "payload": { "label": "here" } }
{ "kind": "result", "id": 1, "payload": 42 }
{ "kind": "result", "id": 1, "error": { "code": "HandlerFault", "message": "..." } }
{ "kind": "subscribe", "id": 2, "member": "nav." }
{ "kind": "event", "member": "nav.route.done", "payload": [ ] }
{ "kind": "ping", "id": 3 }
```

`kind` is one of `call`, `result`, `subscribe`, `unsubscribe`, `event`, `progress`, `ping`, `pong`. `id` correlates a call with its result and is zero on anything the host sends unasked. A subscription matches topics by prefix. An empty `member` takes everything. A malformed frame gets an error result and never closes the connection.

## 7. Long work

A member whose manifest row says `"mode": "job"` answers with a job:

```json
{ "ok": true, "job": { "id": "a1b2c3d4", "state": "running", "progress": 0.25 } }
```

Poll it at `GET /noire/v1/_jobs/{id}`, cancel it with `DELETE` on the same path. Progress is also published on the topic `progress.{id}`.

Over the socket the call answers the job id, and one more frame with `id` zero arrives when the job ends:

```json
{ "kind": "result", "id": 0, "job": "a1b2c3d4", "payload": 42 }
```

`{ "kind": "progress", "id": 5, "job": "a1b2c3d4" }` follows a job this connection did not start, or follows one again after reconnecting. The answer carries the job's status. An unknown job answers `"state": "unknown"`.
