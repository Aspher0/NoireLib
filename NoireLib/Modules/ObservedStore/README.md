# Module Documentation : NoireObservedStore

You are reading the documentation for the `NoireObservedStore` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Scopes](#scopes)
- [Recording](#recording)
- [Reading](#reading)
- [Staleness and Expiry](#staleness-and-expiry)
- [Enumerating and Forgetting](#enumerating-and-forgetting)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireObservedStore` is a module that remembers what the client was seen to hold, so a plugin can answer a
question the game itself cannot. It provides:

- **Durable observations** in SQLite, surviving reloads, patches and character switches
- **Provenance on every entry**: where the sighting came from and when it happened
- **Scopes** so a fact about one character is kept apart from a fact about the world
- **Staleness** as a first-class answer, per entry, rather than a guess at the call site
- **Any serializable value**, with no model class to define
- **Bulk writes** in one transaction, for the hundreds of observations a container is
- **Subscription and EventBus surfaces** for reacting when something is learned or changes

**Why this exists.** The game does not populate everything it knows until the player looks at it. A retainer's
inventory, the saddlebag, a housing interior's contents: until the tab is opened, there is nothing to read, and an
empty container is indistinguishable from one nobody has opened. That is why a plugin has to accumulate what it
sees rather than query it, and why a cache is not an optimisation here but the only possible source of truth.

**What it deliberately does not do.** It never decides what is worth remembering, and it never decides that a
sighting is too old to use. It records, it reports the age, and the plugin owns the policy.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create the Module

```csharp
using NoireLib.ObservedStore;

var store = NoireLibMain.AddModule(new NoireObservedStore(new ObservedStoreOptions
{
    DatabaseName = "MyPluginObservations",
    DefaultSource = "my-plugin",
}));
```

Everything works with zero configuration; the options above only name the database and say where sightings come
from.

### 2. Write Down What You Saw

```csharp
// The player just opened a retainer, so this is the one moment the contents are readable.
store.Record($"retainer.{retainerId}.inventory", items);
```

### 3. Read It Back, With Its Age

```csharp
var seen = store.Read<List<ItemStack>>($"retainer.{retainerId}.inventory");

if (seen == null)
    NoireLogger.LogInfo("Never looked in that retainer.");
else
    NoireLogger.LogInfo($"{seen.Value.Count} items, last seen {seen.Age.TotalHours:F0}h ago.");
```

That is the whole loop. The store answers `null` for something never observed and an aged observation for
something observed once, and those are different answers on purpose.

---

## Configuration

### Module Parameters

You can configure the most important options of the module with the module's constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Store"); // Optional

var store = new NoireObservedStore(
    options: new ObservedStoreOptions { EventBus = eventBus },  // Optional settings; everything works with none
    moduleId: "MyStore",                                        // Optional identifier, for several stores
    active: true,                                               // Whether to activate on creation
    enableLogging: true                                         // Whether to enable logging for this module
);
```

Everything on `ObservedStoreOptions` is optional:

- `DatabaseName`: the database file under the plugin's own `Databases` directory. Two stores given different names share nothing. Default: `"NoireObservedStore"`.
- `DefaultScope`: the scope a record or read takes when it does not name one. Default: `ObservationScope.Character`.
- `DefaultSource`: the source a record takes when it does not name one. Default: `"unspecified"`.
- `DefaultExpiresAfter`: how long an observation stays good for when the record does not say. Default: `null` (never expires).
- `PruneExpiredOnActivate`: whether expired rows are deleted on activation. Default: `true`.
- `EventBus`: an optional bus to publish store events on. Default: `null`.
- `PublishModuleEvents`: whether events reach that bus. Default: `true`.
- `SerializerSettings`: the Newtonsoft settings values are stored with. Default: `null`.

### Property Configuration

You can also configure the module after creation:

```csharp
var store = NoireLibMain.GetModule<NoireObservedStore>();

var options = store!.Options.Clone();
options.DefaultSource = "retainer-window";
options.DefaultExpiresAfter = TimeSpan.FromDays(30);

store.SetOptions(options);
```

You can also chain these methods for convenience:
```csharp
var store = NoireLibMain.GetModule<NoireObservedStore>();

store?
    .SetOptions(newOptions)
    .SetEnableLogging(false);
```

**Options are snapshotted when the module activates.** Writing to `store.Options` directly while the store is
active changes nothing until a deactivate/activate cycle; `SetOptions` performs that cycle for you and keeps every
subscription across it.

### Serialization Safety

`SerializerSettings` is yours to set, with one field taken back:
`JsonSerializerSettings.TypeNameHandling` is always forced to `None`. A stored payload that names its own type
would let anything able to write the database file choose which type gets constructed on read, so the store always
deserializes into the type the caller asked for and never into the type the payload claims to be.

---

## Scopes

One key can mean two different things, so every observation belongs to a scope.

#### 1. Character

Keyed to the observing character's content id. Two characters recording the same key hold two separate
observations.

```csharp
store.Character.Record("gil", 1_250_000);
```

#### 2. Shared

Stored once and read by every character. This is for facts about the world rather than about a character.

```csharp
store.Shared.Record($"interior.{territoryId}.design", "Dark Minimalist Style");
```

#### 3. A named character

For writing down what was learned about a character other than the one logged in, as an import from a file does.

```csharp
store.Of(contentId).Record("gil", 84_000);
```

Each of those returns an `ObservationView` carrying the store's whole operation set bound to that scope. The
store's own same-named methods (`store.Record`, `store.Read`, ...) are `store.Default`, which is the view for
`Options.DefaultScope`.

**A character-scoped call with nobody logged in answers nothing** rather than writing under a placeholder id.
`store.CurrentCharacterId` is null in that state, and every character-scoped record returns `false` while it is.

---

## Recording

```csharp
// The everyday call.
store.Record("saddlebag", items);

// With provenance and a backdated sighting, which is what an import wants.
store.Record("saddlebag", items, new RecordOptions
{
    Source = "allagan-import",
    ObservedAt = exportTimestamp,
    ExpiresAfter = TimeSpan.FromDays(14),
});

// Hundreds of rows in one transaction.
store.Shared.RecordMany(shopStock.Select(s => new KeyValuePair<string, ShopEntry>($"shop.{s.Id}", s)));
```

Recording a key that already exists **replaces** it: the store holds the latest sighting per key, not a history.
The `ObservationRecordedEvent` carries what was replaced, so a consumer that wants a history can keep one.

`RecordOptions.Scope` and `RecordOptions.CharacterId`, when set, override the view's own binding. Explicit beats
implicit.

---

## Reading

```csharp
Observation<T>? read      = store.Read<T>("key");                    // the value plus its metadata
bool            got       = store.Character.TryRead<T>("key", out var observation);
T?              value     = store.ReadValue<T>("key", fallback);     // just the value
Observation<T>? fresh     = store.ReadFresh<T>("key", TimeSpan.FromHours(6));
ObservationInfo? info     = store.Describe("key");                   // metadata, no deserialization
bool            known     = store.Knows("key");
```

`ReadFresh` answers `null` both for something never seen and for something seen too long ago, which is usually
the same decision.

`Describe` is the cheap one. It reads the sighting's metadata without deserializing the value, so a staleness check
over hundreds of keys costs no JSON work at all.

**Reading a value as the wrong type answers `null` and logs**, rather than throwing. A key recorded as one type and
read as another is a bug worth seeing, not worth crashing a draw loop over.

---

## Staleness and Expiry

An `ObservationInfo` answers the age questions directly:

```csharp
observation.Age                              // how long ago the sighting happened
observation.IsOlderThan(TimeSpan.FromDays(1))
observation.IsExpired                        // past its own ExpiresAfter
```

Each also has an `...At(DateTimeOffset now)` form, so the rule can be exercised without waiting on a clock.

**Expiry is an instruction from the recorder, not a policy of the store.** An entry with an `ExpiresAfter` stops
being returned once it is past it; every read takes `includeExpired: true` to see it anyway. An entry recorded with
`ExpiresAfter = TimeSpan.Zero` never expires, which is how one record opts out of a store-wide
`DefaultExpiresAfter`.

```csharp
store.PruneExpired();   // reclaims the space; reads already ignored them
```

---

## Enumerating and Forgetting

```csharp
IReadOnlyList<string> keys            = store.Shared.Keys("shop.");
IReadOnlyList<Observation<T>> all     = store.Shared.ReadAll<T>("shop.");
IReadOnlyList<ObservationInfo> info   = store.Shared.DescribeAll("shop.");
int count                             = store.Shared.Count("shop.");

store.Shared.Forget("shop.17");
store.Shared.ForgetPrefix("shop.");
store.Shared.Prune(TimeSpan.FromDays(90));
store.Shared.Clear();
```

A prefix is matched literally: an `_` in a key prefix matches an underscore rather than any character, so keys
built from ids with underscores in them filter as written.

---

## EventBus Integration

### Quick Example

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Store");
var store = new NoireObservedStore(new ObservedStoreOptions { EventBus = eventBus });

eventBus.Subscribe<ObservationRecordedEvent>(e =>
{
    if (e.Replaced is { } previous && previous.ObservedAt < e.Info.ObservedAt)
        NoireLogger.LogInfo($"{e.Info.Key} was re-observed after {(e.Info.ObservedAt - previous.ObservedAt).TotalHours:F0}h.");
}, owner: this);
```

The same events are available on the module's own surface, which needs no bus:

```csharp
using var token = store.OnRecorded(e => Refresh(e.Info.Key));
store.OnPruned(e => NoireLogger.LogInfo($"{e.Count} observations removed ({e.Reason})."), new() { Owner = this });
store.UnsubscribeOwner(this);
```

The store publishes all three event types together: they fire on deliberate writes rather than on a poll, so
there is no firehose to guard against.

### Available Events

- `ObservationRecordedEvent` - an observation was written down, carrying what it replaced (null when the key is new)
- `ObservationForgottenEvent` - a single observation was deliberately forgotten
- `ObservationsPrunedEvent` - observations were removed in bulk, with the count and the reason

---

## Advanced Features

### Several Stores

```csharp
var world = NoireLibMain.AddModule(new NoireObservedStore(
    new ObservedStoreOptions { DatabaseName = "WorldFacts", DefaultScope = ObservationScope.Shared },
    moduleId: "Store_World"));

var characters = NoireLibMain.AddModule(new NoireObservedStore(
    new ObservedStoreOptions { DatabaseName = "CharacterFacts" },
    moduleId: "Store_Characters"));
```

Each keeps its own database file, options and subscriptions.

### Reading the Database Directly

The table is `observations`, one row per `(scope, character_id, key)`, and it is an ordinary NoireLib database.
`NoireDatabase.GetDatabaseFilePath(store.DatabaseName)` gives the file, so an export, a backup or an inspection in
any SQLite browser needs nothing from this module.

### Subscription Settings

Every subscription takes the library's shared `NoireSubscriptionOptions`, so keyed replacement, priority,
filtering, one-shot and owner tagging work the same way they do everywhere else:

```csharp
store.OnRecorded(
    e => Refresh(e.Info.Key),
    new() { Key = "refresh", Owner = this, Filter = e => e.Info.Key.StartsWith("retainer.") });
```

---

## Troubleshooting

### Nothing is recorded and `Record` returns false

- Check the store is active. An inactive store answers nothing rather than throwing.
- Check `store.CurrentCharacterId`. A character-scoped record has nothing to key on while nobody is logged in, and
  refuses rather than writing under a placeholder. Use `Shared` for a fact about the world, or `Of(id)` for a
  named character.
- Check `/xllog`: every database call runs behind the library's error boundary and logs what it caught.

### A read returns null for something that was definitely recorded

- Check the scope. A `Shared` observation is not a `Character` one, and two characters do not see each other's.
- Check expiry. An entry past its `ExpiresAfter` is skipped by default; pass `includeExpired: true` to confirm it
  is still there.
- Check the type. Reading a value as a type it was not recorded as answers null and logs a warning naming both.

### Ages look wrong

- An observation is aged from `ObservedAt`, which is when the sighting happened rather than when it was written.
  An import that does not set `RecordOptions.ObservedAt` will look brand new.

### Changing options changed nothing

- Options are snapshotted at activation. Use `SetOptions`, which restarts the module for you.

### Two plugins are fighting over the same data

- The database lives under the plugin's own configuration directory, so two plugins never share one by accident.
  Within one plugin, give each store its own `DatabaseName`.

If it still does not work after all of that, please report it.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Database System](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Database/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
- [Game Watcher Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/GameWatcher/README.md)
