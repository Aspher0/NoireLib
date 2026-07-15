# NoireGameWatcher

Watch **anything and anyone**: every character (local player *and* others), every object, party and alliance, zones, duties, conditions, chat, combat, cooldowns, statuses, UI addons and inventory - through one subscription model, one token type, one cost model, and one waiting primitive that plugs directly into `NoireTaskQueue`.

```csharp
var watcher = new NoireGameWatcher(new GameWatcherOptions());
```

Everything works with zero configuration. There is nothing to enable manually: **sources activate on demand** - the first subscription touching a source spins it up, disposing the last token shuts it down.

## One event per fact, a Scope decides who it's about

```csharp
// The local player (default scope):
watcher.Characters.OnHpChanged(e => Log($"HP {e.Previous.CurrentHp} → {e.Current.CurrentHp}"));

// The whole party:
watcher.Characters.OnHpChanged(e => ..., scope: Scope.Party);

// Anyone in the object table:
watcher.Characters.OnDied(e => Alert(e.Current.Name), scope: Scope.AllPlayers);

// A specific person, wherever they appear:
watcher.Characters.OnCastStarted(e => ..., scope: Scope.Name("Some Player"));

// Emotes from anyone nearby - one-shot and looping alike, exact emote id:
watcher.Characters.OnEmotePlayed(e => Log($"{e.Character.Name} used emote {e.EmoteId}"), scope: Scope.AllPlayers);
```

Scopes: `LocalPlayer`, `Party`, `Alliance`, `Friends`, `AllPlayers`, `AllCharacters`, `Entity(id)`, `ContentId(cid)`, `Name(name, worldId)`, plus `.Where(predicate)` (narrowing modifier) and `.Union(other)`.

Every subscription helper has one shape - `(handler, scope, options)` - plus an async twin. Keyed replacement, priority, filtering, one-shot and owner tagging come from `NoireSubscriptionOptions`:

```csharp
watcher.Characters.OnDied(e => Alert(e.Current.Name),
    scope: Scope.Party,
    options: new() { Key = "death-alert", Once = true });

// Plugin teardown - one line for everything ever registered with an owner:
watcher.UnsubscribeOwner(this);
```

## Query without subscribing

Queries always read live game state (framework thread) and never activate anything:

```csharp
CharacterSnapshot? me = watcher.Characters.Local;
IReadOnlyList<CharacterSnapshot> party = watcher.Characters.Get(Scope.Party);
ZoneInfo zone = watcher.Zone.Current;
bool inDuty = watcher.Duty.IsInDuty;
bool talkOpen = watcher.Addons.IsReady("Talk");
PartyState state = watcher.Party.State;
```

## Waiting - `GameCondition`

```csharp
// Level-triggered ("is it true now?"), composable, awaitable:
bool arrived = await GameConditions.TerritoryIs(198).And(GameConditions.ScreenReady)
    .WaitAsync(TimeSpan.FromSeconds(30));   // false = timeout (no exception)

// Edge-triggered ("the NEXT time X happens"):
var evt = await watcher.WaitFor<CharacterDiedEvent>(e => e.Current.Flags.HasFlag(SubjectFlags.IsPartyMember),
    timeout: TimeSpan.FromSeconds(10));     // null = timeout
```

Prebuilt vocabulary: `PlayerAvailable`, `ScreenReady`, `InCombat`/`NotInCombat`, `NotCasting`, `Mounted`/`NotMounted`, `TerritoryIs`, `InDuty`/`NotInDuty`, `AddonReady`/`AddonGone`, `PartySize`, `ActionReady`, `GcdReady`, `AnyCharacter`/`AllCharacters(scope, predicate)`, `FromPredicate`, `FromEvent<TEvent>` (one-shot latch with `Reset()`).

**The one rule:** never sync-block (`.Wait()`/`.Result`) on a watcher task from the framework thread - always `await`.

## TaskQueue pairing

Additive extension methods - the queue's own API is untouched:

```csharp
new TaskBuilder("teleport-home")
    .WithAction(() => ExecuteTeleport())
    .CompleteWhen(GameConditions.TerritoryIs(198).And(GameConditions.ScreenReady))
    .EnqueueTo(queue);

builder.CompleteOnGameEvent<SomethingObservedEvent>(watcher, e => e.SourceId == id);
```

`CompleteOnGameEvent` builds a **fresh latch per call**, so retried/re-enqueued tasks never complete against a stale match. No EventBus required.

## Watch anything - the escape hatches

The catalog can never be complete, so completeness is unnecessary:

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

`Publish` is also the test seam: simulated events reach only your handlers, so handler logic is testable without the game.

## Presence, at three ranges

- **Same area**: the object table is the client's entire view - `watcher.Characters.OnSpawned(..., scope)` *is* the presence event (zones, housing wards and plots alike).
- **Sub-areas**: `watcher.Objects.WatchRegion(territoryId, RegionShape.Circle(center, r), onEntered, onLeft)` - territory-bound shapes with hysteresis. `WatchDistance(radius, ...)` for proximity around you.
- **Remote**: `watcher.Party.OnMemberTerritoryChanged(...)` (party members anywhere, server-synced) and `watcher.Friends.OnTerritoryChanged(...)` (friends anywhere, refresh-cadence). The friend proxy refreshes in the background on a **jittered** cadence (`FriendsRefreshCadence`, floored at 30s) so the requests are not a detectable fixed beat, and the refresh is skipped while the friend-list window is open so it never disturbs it. Beyond party and social lists the client has no data - that boundary is documented, never silently absorbed.

## Cost model

- **Interest-masked diffing**: the Characters source compares only fields somebody listens to, only for subjects in somebody's scope. Union mask and union scope are recomputed on subscribe/unsubscribe, not per tick.
- **Compare first, materialize second**: snapshots are allocated only when something changed - a crowded-but-static scene costs field comparisons, not GC pressure.
- Event-driven sources (chat, duty, inventory, conditions, addons, toasts) have **zero** tick cost.
- The one heavy path is wide-scope status watching in crowds: dial it down with `options.PollCadences[SourceKind.Statuses] = TimeSpan.FromMilliseconds(100)`.

## Configuration (all optional)

```csharp
var watcher = new NoireGameWatcher(new GameWatcherOptions
{
    EventBus = bus,                                            // opt-in mirroring target
    Chat = { HistoryCapacity = 200 },                          // implies the Chat source AlwaysOn
    Combat = { HistoryCapacity = 100 },
    Sources = { [SourceKind.Fate] = SourceOverride.Disabled }, // hard off; Disabled beats everything
    PollCadences = { [SourceKind.Statuses] = TimeSpan.FromMilliseconds(100) },
});

// EventBus mirroring is opt-in per event type (nothing is published by default):
watcher.PublishToEventBus<TerritoryChangedEvent>();
watcher.PublishToEventBus<CharacterDiedEvent>(e => e.Current.Flags.HasFlag(SubjectFlags.IsPartyMember));
```

## Guarantees (and their honest limits)

- **Source isolation**: a source that breaks after a game patch (changed struct layout, moved ClientStructs member) disables *itself only* and reports in diagnostics; every other source keeps working.
- **Delivery**: every handler, filter, sampler and wait continuation runs inline on the framework thread.
- **Frame quantization**: polled facts are accurate to ±1 frame; a value that changes and reverts within one frame is invisible. Native events and hooks are not quantized.
- **Entity identity**: `EntityId` tracks the object-table *slot* (reusable); `ContentId`/`Name` track the *person*.
- **Zone transitions**: spawn/despawn events fired while loading carry `DuringZoneChange = true`.
- **Baseline seeding**: activating a source never fires a synthetic event storm - subscribers observe changes from now on; current state is what queries are for.
- **Estimates say so**: other characters' cooldowns are inferred (`IsEstimate = true`) and drift - never exact.
- **Module lifecycle**: deactivating suspends sources but keeps subscriptions; disposal invalidates every token.

## Diagnostics

```csharp
watcher.ShowDiagnostics();
```

Per-source state (running/refcount/override/failures), interest masks, event counters and tick durations, live subscriptions, active waits, custom-publish counters and a recent-event log. When a watch "doesn't fire", this window answers why in ten seconds.
