# `NoireGameStateWatcher` README

Remaining backlog and missing features for `GameStateWatcher`.

## Already covered

- module-level and tracker-level callback registration
- disposable subscriptions
- filtered subscriptions
- one-shot subscriptions
- addon lifecycle integration with `IAddonLifecycle`
- addon events for `created`, `setup`, `ready`, `shown`, `hidden`, and `finalized`
- per-addon state history
- `WaitForAddonReady`, `WaitForAddonVisible`, and `WaitForAddonHidden`
- addon node tracking for text, visibility, and component state
- addon callback helpers for specific addon names
- class/job change for tracked players
- object changed events
- tracked-entity status watching
- party member HP/job/zone change tracking
- cutscene enter/leave
- loading enter/leave
- proper tracker responsibility separation (`TerritoryTracker` for territory/map/instance/login/logout/PvP; `PlayerStateTracker` for all local-player and per-character state)
- per-character events expose the affected character via `CharacterStateSnapshot`
- MP tracking (per-character and local player)
- GP tracking (per-character and local player)
- CP tracking (per-character and local player)
- shield tracking as first-class events (per-character and local player)
- cast start / cast end tracking (per-character and local player)
- combat state tracking for every visible player character (not only local condition transitions)
- targetable state tracking (per-character and local player)
- mount / flying / swimming condition transitions for local player
- target / focus-target / soft-target change events
- GPose enter / leave
- condition flag change events
- player combat enter / leave via condition transitions
- player and character death / revive events
- character target change events
- PvP enter / leave
- `RegisterCallback<TTracker, TEvent>()` helper overloads directly on `NoireGameStateWatcher`
- priority-based callback ordering
- explicit ordered callback pipelines
- optional async callback support for tracker/module subscriptions
- a more formal token type instead of only `IDisposable`-based subscription handles
- cast interrupt vs. completion differentiation (per-character and local player)
- mount / dismount tracking for arbitrary tracked characters (via `CharacterMode`)
- emote start / emote end tracking (per-character and local player)
- online-status (interactable state) change tracking (per-character and local player)
- explicit target / focus-target / soft-target snapshots (full `ObjectSnapshot` alongside entity-id events)
- `CharacterMode` / `CharacterModeParam` / `OnlineStatusId` exposed on `CharacterStateSnapshot`
- convenience queries: `GetEmotingPlayers`, `GetMountedPlayers`, `IsEmoting`, `IsMounted`, `GetOnlineStatusId`
- wait helpers: `WaitForEmoting`, `WaitForNotEmoting`, `WaitForMounted`, `WaitForNotMounted`
- housing enter / leave tracking (via `HousingManager.Instance()->IsInside()`)
- instance queue / commence / queue time tracking (via `ConditionFlag.WaitingForDutyFinder` and `ConditionFlag.WaitingForDuty`)
- distance-threshold enter / leave watchers (`RegisterDistanceWatcher` / `UnregisterDistanceWatcher`)
- richer parsed action effect payload (action kind, damage/heal classification, crit, direct hit, block, parry, effect amounts, per-target effects)
- dedicated target-specific / source-specific / player-by-CID action effect subscription helpers
- explicit local-player incoming/outgoing action effect split helpers
- rolling skill statistics / counters / aggregations (`ActionEffectStatistics`)
- history grouped by source, target, or action with precomputed summaries
- command-like pattern matching helpers for chat
- regex chat subscriptions
- wildcard chat subscriptions
- per-channel chat history limits
- duplicate chat suppression
- spam collapsing / coalescing
- higher-level chat rule matching APIs (`ChatRule`, `RegisterRule`, `ChatRuleMatchedEvent`)

## Missing or partial

### Player / character state

- aggro / enmity tracking
- flying / swimming detection for arbitrary tracked characters (not just local condition transitions)
- nameplate or visibility-derived player-presence state

### Territory / duty

- cutover or zone-load-complete events beyond basic loading transitions
- duty phase tracking
- boss pull tracking
- duty encounter state modeling
- stronger duty-state analytics/history

### Object tracking

- nameplate visibility tracking
- model-change specific events
- status-update specific object events
- explicit ownership links for:
  - owner -> minion
- explicit territory-aware object filters on the tracker API
- direct filters by subkind as first-class helpers
- direct filters by territory as first-class helpers
- watcher APIs for objects entering or leaving custom predicates/zones

### Action effect tracking

- source/target snapshots at time of event
- separate action lifecycle events:
  - action started
  - cast finished
  - effect applied
  - interrupted

### Chat

- better sender resolution and normalization

### Addon tracking

- explicit `destroyed` / post-finalize style event if a separate destruction stage should exist beyond `finalized`
- addon node value tracking beyond current text/visibility/component-state coverage
- richer node/component watchers for:
  - numeric values
  - selected state
  - toggle/check state
  - list/item state
  - progress values
- tracked addon lifecycle history separated by lifecycle phase instead of only state-transition history
- addon watcher diagnostics or visual explorer

### Status tracking

- dedicated stack-count change events
- remaining-duration threshold callbacks
- source-based watch registrations
- blacklist support for tracked statuses
- whitelist support for tracked statuses
- grouped status watchers:
  - `any of`
  - `all of`
  - `none of`
- richer status matching rules by source, remaining time, param, and target category

### Party / alliance

- alliance tracker
- role composition change events
- ready-check tracking
- loot-state tracking
- party leader change tracking
- nearby party member convenience queries
- missing buff checks per role / job
- alliance-aware live member helpers
- party role summaries and party-state analytics

### Higher-level processor features

- timeline recorder
- event replay for debugging
- diagnostics window showing live tracker activity
- metrics counters per tracker
- tracker health / performance diagnostics
