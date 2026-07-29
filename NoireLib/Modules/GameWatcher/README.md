# Module Documentation : NoireGameWatcher

You are reading the documentation for the `NoireGameWatcher` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Subscribing](#subscribing)
- [Querying](#querying)
- [Waiting](#waiting)
- [TaskQueue Pairing](#taskqueue-pairing)
- [Watching Anything](#watching-anything)
- [Presence, at Three Ranges](#presence-at-three-ranges)
- [EventBus Integration](#eventbus-integration)
- [Advanced Features](#advanced-features)
- [Cost Model](#cost-model)
- [Guarantees and Their Honest Limits](#guarantees-and-their-honest-limits)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireGameWatcher` is a module that watches **anything and anyone** in the game: every character (the local
player and others), every object, party and alliance, zones, duties, conditions, chat, combat, cooldowns,
statuses, UI addons and inventory. It provides:

- **One subscription model** for every fact, with one token type and one options object
- **Scopes** so one event covers the local player, the party, a named person, or everyone
- **Queries** that read live state without subscribing to anything
- **Waiting primitives** (`GameCondition` and `WaitFor`) that plug straight into `NoireTaskQueue`
- **Demand-driven sources**: the first subscription touching a source starts it, the last token disposed stops it
- **Escape hatches** so a fact the catalog does not carry can be watched, or published, as a first-class event
- **Opt-in EventBus mirroring**, per event type
- **A diagnostics window** that answers why a watch is not firing

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Create the Module

```csharp
using NoireLib.GameWatcher;

var watcher = new NoireGameWatcher(new GameWatcherOptions());
```

Everything works with zero configuration, and there is nothing to enable manually: sources activate on demand.

### 2. Subscribe to a Fact

```csharp
// The local player, which is the default scope:
watcher.Characters.OnHpChanged(e => NoireLogger.LogInfo($"HP {e.Previous.CurrentHp} -> {e.Current.CurrentHp}"));

// Anyone in the object table:
watcher.Characters.OnDied(e => NoireLogger.LogInfo($"{e.Current.Name} died."), scope: Scope.AllPlayers);
```

That is the whole loop: nothing to start, nothing to poll, and disposing the returned token releases the source
again.

---

## Configuration

### Module Parameters

You can configure the most important options of the module with the module's constructor:

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Watcher"); // Optional

var watcher = new NoireGameWatcher(
    options: new GameWatcherOptions { EventBus = eventBus },  // Optional settings; everything works with none
    moduleId: "MyWatcher",                                    // Optional identifier, for several watchers
    active: true,                                             // Whether to activate on creation
    enableLogging: true                                       // Whether to enable logging for this module
);
```

Everything on `GameWatcherOptions` is optional:

- `EventBus`: an EventBus to mirror events to. Nothing is published until you opt an event type in. Default: `null`.
- `Sources`: per-source activation overrides (`SourceOverride.Default` / `AlwaysOn` / `Disabled`). Default: `empty`.
- `PollCadences`: per-source poll cadence overrides. Default: `empty` (every tick for the hot sources, 1 second for Fate/Weather/EorzeaTime/Friends).
- `Chat.HistoryCapacity`: chat messages retained in history. Default: `0` (no history).
- `Chat.DuplicateSuppressionWindow`: coalesces identical messages inside the window, reporting the count on the next dispatched one. Default: `null`.
- `Combat.HistoryCapacity`: action-effect entries retained in history. Default: `0` (no history).
- `AddonSafetyPollInterval`: the safety poll for addon node and visibility watchers. Default: `250 ms`.
- `FriendsRefreshCadence`: how often the friend list is refreshed in the background. Default: a jittered 30-40 seconds.
- `DistanceHysteresis`: extra yalms on a distance watcher's leave threshold, so a subject on the boundary does not flap. Default: `0.5`.
- `DiagnosticsEventLogCapacity`: recent events retained for the diagnostics window. Default: `100`. `0` disables it.

### Property Configuration

You can also configure the module after creation:

```csharp
var watcher = NoireLibMain.GetModule<NoireGameWatcher>();

// Replace the options; the watcher restarts itself so they take effect
watcher?.SetOptions(new GameWatcherOptions
{
    Chat = { HistoryCapacity = 200 },
    PollCadences = { [SourceKind.Statuses] = TimeSpan.FromMilliseconds(100) },
});

// Open the diagnostics window
watcher?.ShowDiagnostics();
```

You can also chain these methods for convenience:
```csharp
var watcher = NoireLibMain.GetModule<NoireGameWatcher>();

watcher?
    .SetOptions(new GameWatcherOptions { Combat = { HistoryCapacity = 100 } })
    .ShowDiagnostics();
```

**Options are snapshotted when the module activates.** Writing to `watcher.Options` directly while the watcher is
active changes nothing until a deactivate/activate cycle; `SetOptions` performs that cycle for you.

### Source Activation

Sources start and stop on demand, so nothing is configured in the normal case. Two things override that:

- A **configured history capacity implies always-on** for its source: `Chat.HistoryCapacity` or
  `Combat.HistoryCapacity` keeps its source running.
- **`SourceOverride.Disabled` beats everything**, including that implied always-on. The contradiction is logged
  rather than guessed at.

```csharp
var options = new GameWatcherOptions
{
    Sources = { [SourceKind.Fate] = SourceOverride.Disabled },
};
```

---

## Subscribing

Every subscription helper has one shape - `(handler, scope, options)` - plus an async twin, and every one returns
a `NoireSubscriptionToken` that releases the source when disposed.

```csharp
watcher.Characters.OnDied(e => Alert(e.Current.Name),
    scope: Scope.Party,
    options: new() { Key = "death-alert", Once = true });

// Plugin teardown - one line for everything ever registered with an owner:
watcher.UnsubscribeOwner(this);
```

Keyed replacement, priority, filtering, one-shot and owner tagging all come from `NoireSubscriptionOptions`, which
is the same options type the rest of the library's subscription surfaces use.

### Scopes

One event per fact; a `Scope` decides who it is about.

```csharp
watcher.Characters.OnHpChanged(e => ...);                                  // the local player (default)
watcher.Characters.OnHpChanged(e => ..., scope: Scope.Party);              // the whole party
watcher.Characters.OnDied(e => ..., scope: Scope.AllPlayers);              // anyone in the object table
watcher.Characters.OnCastStarted(e => ..., scope: Scope.Name("Some Player"));
```

Available scopes: `LocalPlayer`, `Party`, `Alliance`, `Friends`, `AllPlayers`, `AllCharacters`, `Entity(id)`,
`ContentId(cid)`, `Name(name, worldId)`, plus `.Where(predicate)` as a narrowing modifier and `.Union(other)`.

### The Facades

Each facade owns one domain and carries both its subscriptions and its queries:

`Characters`, `Objects`, `Party`, `Friends`, `Targets`, `Zone`, `Duty`, `Chat`, `Combat`, `Cooldowns`, `Statuses`,
`Addons`, `Inventory`, `Conditions`, `Fates`, `Toasts`.

---

## Querying

Queries always read live game state and never activate anything:

```csharp
CharacterSnapshot? me = watcher.Characters.Local;
IReadOnlyList<CharacterSnapshot> party = watcher.Characters.Get(Scope.Party);
ZoneInfo zone = watcher.Zone.Current;
bool inDuty = watcher.Duty.IsInDuty;
bool talkOpen = watcher.Addons.IsReady("Talk");
PartyState state = watcher.Party.State;
```

**A query must run on the framework thread.** It reads live game state, so calling one from a background thread
throws with a message saying so rather than reading torn data.

---

## Waiting

```csharp
// Level-triggered ("is it true now?"), composable, awaitable:
bool arrived = await GameConditions.TerritoryIs(198).And(GameConditions.ScreenReady)
    .WaitAsync(TimeSpan.FromSeconds(30));   // false = timeout, no exception

// Edge-triggered ("the NEXT time X happens"):
var evt = await watcher.WaitFor<CharacterDiedEvent>(e => e.Current.Flags.HasFlag(SubjectFlags.IsPartyMember),
    timeout: TimeSpan.FromSeconds(10));     // null = timeout
```

Prebuilt vocabulary on `GameConditions`: `PlayerAvailable`, `ScreenReady`, `InCombat` / `NotInCombat`,
`NotCasting`, `Mounted` / `NotMounted`, `TerritoryIs`, `InDuty` / `NotInDuty`, `AddonReady` / `AddonGone`,
`PartySize` / `PartySizeAtLeast`, `ActionReady`, `GcdReady`, `AnyCharacter` / `AllCharacters(scope, predicate)`,
`FromPredicate`, and `FromEvent<TEvent>` (a one-shot latch with `Reset()`). Conditions compose with `And`, `Or`
and `Not`.

**The one rule:** never sync-block (`.Wait()` / `.Result`) on a watcher task from the framework thread. Always
`await`.

---

## TaskQueue Pairing

Additive extension methods; the queue's own API is untouched.

```csharp
new TaskBuilder("teleport-home")
    .WithAction(() => ExecuteTeleport())
    .CompleteWhen(GameConditions.TerritoryIs(198).And(GameConditions.ScreenReady))
    .EnqueueTo(queue);

builder.CompleteOnGameEvent<SomethingObservedEvent>(watcher, e => e.SourceId == id);
```

`CompleteOnGameEvent` builds a **fresh latch per call**, so a retried or re-enqueued task never completes against
a stale match. No EventBus is required for any of this.

---

## Watching Anything

The catalog can never be complete:

```csharp
// Diff ANY value you can read, per tick or at an interval:
watcher.WatchValue(() => ReadSomeGameCounter(), (prev, cur) => ..., interval: TimeSpan.FromSeconds(1));

// Diff any property of any scoped character:
watcher.Characters.WatchValue(Scope.Party, s => s.Level, (subject, prev, cur) => ...);

// Detect a fact with your own hook, then hand it to the watcher - full citizenship:
public sealed record SomethingObservedEvent(uint SourceId);
myHook.OnDetected += id => watcher.Publish(new SomethingObservedEvent(id));

// From here on it is indistinguishable from a library event:
watcher.Subscribe<SomethingObservedEvent>(e => ...);
await watcher.WaitFor<SomethingObservedEvent>(timeout: TimeSpan.FromSeconds(10));
```

`Publish` is also the test seam: a simulated event reaches only your handlers, so handler logic is testable
without the game.

---

## Presence, at Three Ranges

- **Same area**: the object table is the client's entire view, so `watcher.Characters.OnSpawned(..., scope)` *is*
  the presence event, for zones, housing wards and plots alike.
- **Sub-areas**: `watcher.Objects.WatchRegion(territoryId, RegionShape.Circle(center, radius), onEntered, onLeft)`
  for territory-bound shapes with hysteresis (`Circle`, `Box`, `Predicate`), and `WatchDistance(radius, ...)` for
  proximity around you.
- **Remote**: `watcher.Party.OnMemberTerritoryChanged(...)` (party members anywhere, server-synced) and
  `watcher.Friends.OnTerritoryChanged(...)` (friends anywhere, at a **jittered** refresh cadence, held while the
  friend-list window is open).

Beyond the party and social lists, the client has no data.

---

## EventBus Integration

### Quick Example

```csharp
var eventBus = NoireLibMain.AddModule<NoireEventBus>("EventBus_Watcher");
var watcher = new NoireGameWatcher(new GameWatcherOptions { EventBus = eventBus });

// Nothing is published by default - opt in per event type:
watcher.PublishToEventBus<TerritoryChangedEvent>();
watcher.PublishToEventBus<CharacterDiedEvent>(e => e.Current.Flags.HasFlag(SubjectFlags.IsPartyMember));

// Then subscribe on the bus as usual:
eventBus.Subscribe<TerritoryChangedEvent>(e => NoireLogger.LogInfo($"Now in {e.TerritoryId}."));
```

Mirroring is opt-in per event type. `PublishToEventBus` returns a token that stops mirroring when disposed, and
the call is inert (and logged) when no bus is attached.

### Available Events

Any event type the watcher dispatches can be mirrored. They live in `Events/`, grouped by the source that raises
them:

- **Session** - `LoginEvent`, `LogoutEvent`, `TerritoryChangedEvent`, `MapChangedEvent`, `InstanceChangedEvent`, `PvpEnteredEvent`, `PvpLeftEvent`, `CfPopEvent`, `LocalClassJobChangedEvent`, `LocalLevelChangedEvent`, `HousingInteriorEnteredEvent`, `HousingInteriorLeftEvent`, `GPoseStateChangedEvent`
- **Characters** - spawn/despawn, `CharacterHpChangedEvent`, `CharacterMpChangedEvent`, `CharacterShieldChangedEvent`, died/revived, the three cast events, combat entered/left, target and targetable changes, `CharacterModeChangedEvent`, the three emote events, online status, job, level, identity
- **Objects** - `ObjectSpawnedEvent`, `ObjectDespawnedEvent`, `ObjectChangedEvent`
- **Party** - member joined/left/changed, `PartyMemberTerritoryChangedEvent`, leader, size, composition, `AllianceChangedEvent`
- **Friends** - online/offline, `FriendTerritoryChangedEvent`, added/removed
- **Targets** - target, focus, soft and mouse-over target changes
- **Duty** - started, wiped, recommenced, completed, queue entered/left, `DutyPopEvent`
- **Conditions** - `ConditionChangedEvent` plus the derived pairs: combat, mounted, flight, swimming, diving, crafting, gathering, fishing, performance, occupied, cutscene, loading, duty
- **Chat** - `ChatMessageEvent` (payloads preserved)
- **Combat** - `ActionEffectEvent`
- **Cooldowns** - started/ended, `ChargesChangedEvent`, `GcdStateChangedEvent`, and the estimated pair for other characters
- **Statuses** - gained, lost, `StatusStackChangedEvent`
- **Addons** - `AddonLifecycleEvent`, `AddonShownEvent`, `AddonHiddenEvent`
- **Inventory** - item added/removed/moved/changed/merged/split, `ItemCountChangedEvent`, `GilChangedEvent`
- **Extended** - the four Fate events, `WeatherChangedEvent`, the two Eorzea time events, and the three toast events

---

## Advanced Features

### Several Watchers

```csharp
var main = NoireLibMain.AddModule<NoireGameWatcher>("Watcher_Main");
var combat = NoireLibMain.AddModule<NoireGameWatcher>("Watcher_Combat");
```

Each instance keeps its own sources, subscriptions and options.

### Subscription Bookkeeping

```csharp
watcher.Unsubscribe("death-alert");   // by key
watcher.UnsubscribeOwner(this);       // everything registered with an owner
var live = watcher.SubscriptionCount;
```

### Diagnostics

```csharp
watcher.ShowDiagnostics();
```

The window reports per-source state (running, refcount, override, failures), interest masks, event counters and
tick durations, live subscriptions, active waits, custom-publish counters and a recent-event log. When a watch
"does not fire", this answers why.

---

## Cost Model

- **Interest-masked diffing**: the Characters source compares only the fields somebody listens to, and only for
  subjects in somebody's scope. The union mask and union scope are recomputed on subscribe and unsubscribe, not
  per tick.
- **Compare first, materialize second**: a snapshot is allocated only when something actually changed, so a
  crowded but static scene costs field comparisons rather than GC pressure.
- Event-driven sources (chat, duty, inventory, conditions, addons, toasts) have **zero** tick cost.
- The one heavy path is wide-scope status watching in crowds. Dial it down with
  `options.PollCadences[SourceKind.Statuses] = TimeSpan.FromMilliseconds(100)`.

---

## Guarantees and Their Honest Limits

- **Source isolation**: a source that breaks after a game patch (a changed struct layout, a moved ClientStructs
  member) disables *itself only* and reports in diagnostics; every other source keeps working.
- **Delivery**: every handler, filter, sampler and wait continuation runs inline on the framework thread.
- **Frame quantization**: polled facts are accurate to plus or minus one frame, so a value that changes and
  reverts inside one frame is invisible. Native events and hooks are not quantized.
- **Entity identity**: `EntityId` tracks the object-table *slot*, which is reusable; `ContentId` and `Name` track
  the *person*.
- **Zone transitions**: spawn and despawn events fired while loading carry `DuringZoneChange = true`.
- **Baseline seeding**: activating a source never fires a synthetic event storm. Subscribers observe changes from
  then on; current state is what queries are for.
- **Estimates say so**: another character's cooldowns are inferred (`IsEstimate = true`) and drift. They are never
  exact.
- **Module lifecycle**: deactivating suspends sources but keeps subscriptions; disposal invalidates every token.

---

## Troubleshooting

### A subscription never fires

- Check the scope. The default is the local player, so a fact about someone else needs `Scope.Party`,
  `Scope.AllPlayers` or a named scope.
- Check the source is not `SourceOverride.Disabled` in the options. Subscribing to a disabled source warns once.
- Check the module is active, and that the token has not been disposed or replaced by a same-`Key` subscription.
- Open `ShowDiagnostics()`: per-source state and the recent-event log say whether the fact was detected at all.
- Check `/xllog` for a source that disabled itself after a failure.

### A query throws

- Queries read live game state and must run on the framework thread. Hop with
  `NoireService.Framework.RunOnFrameworkThread(...)`, or call from an event handler or a tick, where you already
  are on it.

### An await never returns

- A `GameCondition` wait returns `false` on timeout and `WaitFor` returns `null`; neither throws, so a hang is a
  wait with no timeout. Pass one.
- Never sync-block on a watcher task from the framework thread: `.Wait()` and `.Result` deadlock against the very
  thread the continuation needs.

### Chat or combat history is empty

- History only collects while its source runs, so it needs a `HistoryCapacity` above zero.
- If the capacity is set and the history is still empty, the source is explicitly `Disabled`, which wins. That
  contradiction is logged on activation.

### Changing options changed nothing

- Options are snapshotted at activation. Use `SetOptions`, which restarts the module for you, rather than writing
  to `Options` on a running watcher.

### EventBus events not firing

- Mirroring is opt-in per event type: call `PublishToEventBus<TEvent>()` for each one you want on the bus.
- Check `GameWatcherOptions.EventBus` is set. Without a bus the call is inert and says so in the log.

If it still does not work after all of that, please report it.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
- [Task Queue Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/TaskQueue/README.md)
- [Game data helpers](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/GameData/README.md)
