using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

public partial class NoireGameWatcher
{
    private sealed class LedgerEntry
    {
        public required Type EventType { get; init; }
        public required string Description { get; init; }
        public NoireSubscriptionToken? OuterToken { get; set; }
        public NoireSubscriptionToken? InnerToken { get; set; }
        public SourceKind? Interest { get; init; }
        public SourceKind? SecondaryInterest { get; init; }
        public object? Owner { get; init; }
        public string? Key { get; init; }
        public Action? ExtraDispose { get; init; }
    }

    private readonly List<LedgerEntry> ledger = new();
    private readonly Dictionary<string, LedgerEntry> keyedEntries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, long> eventCounters = new();
    private readonly ConcurrentDictionary<Type, long> customPublishCounters = new();
    private readonly Queue<(DateTimeOffset At, string Description)> recentEvents = new();
    private readonly Dictionary<Type, List<EventBusMirror>> eventBusMirrors = new();

    private sealed class EventBusMirror
    {
        public required NoireSubscriptionToken Token { get; init; }
        public required Func<object, bool>? Filter { get; init; }
        public required Action<object> Publish { get; init; }
    }

    private static readonly Lazy<Dictionary<Type, SourceKind>> EventSourceMap = new(BuildEventSourceMap);

    #region Subscribe / Unsubscribe

    /// <summary>
    /// Subscribes to any watcher event type — the facade-bypassing power path, also used for custom events
    /// injected through <see cref="Publish{TEvent}"/>.<br/>
    /// Keyed replacement, priority, filtering, one-shot and owner tagging all come from
    /// <see cref="NoireSubscriptionOptions{TContext}"/>. Handlers run inline on the framework thread.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The handler invoked for each dispatched event.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken Subscribe<TEvent>(Action<TEvent> handler, NoireSubscriptionOptions<TEvent>? options = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeCore(handler, null, options, LookupSource(typeof(TEvent)), null, null, typeof(TEvent).Name);
    }

    /// <summary>
    /// Subscribes an asynchronous handler to any watcher event type. The returned task is fire-and-forget;
    /// faults are logged.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">The async handler invoked for each dispatched event.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken SubscribeAsync<TEvent>(Func<TEvent, Task> handler, NoireSubscriptionOptions<TEvent>? options = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeCore(null, handler, options, LookupSource(typeof(TEvent)), null, null, typeof(TEvent).Name);
    }

    /// <summary>
    /// The single subscription path behind every facade helper and the public <see cref="Subscribe{TEvent}"/>.
    /// Handles keyed replacement, owner tagging, demand-driven interest and once-cleanup in one place.
    /// </summary>
    internal NoireSubscriptionToken SubscribeCore<TEvent>(
        Action<TEvent>? handler,
        Func<TEvent, Task>? asyncHandler,
        NoireSubscriptionOptions<TEvent>? userOptions,
        SourceKind? interest,
        SourceKind? secondaryInterest,
        Action? extraDispose,
        string description)
        where TEvent : notnull
    {
        userOptions ??= new NoireSubscriptionOptions<TEvent>();

        if (interest is { } kind && IsSourceDisabled(kind))
            NoireLogger.LogWarning(this, $"Subscribing to {description}, but its source ({kind}) is Disabled by configuration — the subscription will never fire.");

        var entry = new LedgerEntry
        {
            EventType = typeof(TEvent),
            Description = description,
            Interest = interest,
            SecondaryInterest = secondaryInterest,
            Owner = userOptions.Owner,
            Key = userOptions.Key,
            ExtraDispose = extraDispose,
        };

        var outerToken = new NoireSubscriptionToken(userOptions.Key, userOptions.Priority, _ => RemoveLedgerEntry(entry, disposeInner: true));
        entry.OuterToken = outerToken;

        var typedFilter = userOptions.Filter;
        var once = userOptions.Once;

        // Once is deliberately handled here, not by the registry: the registry claims once-entries before
        // running their filter, which would let a non-matching event consume a filtered one-shot
        // (breaking WaitFor and event latches). Removing after a real invocation gives once-on-match.
        var innerOptions = new NoireSubscriptionOptions<object>
        {
            Priority = userOptions.Priority,
            Filter = typedFilter == null ? null : context => context is TEvent typed && typedFilter(typed),
        };

        var onceFired = 0;

        bool TryClaimOnce()
            => !once || System.Threading.Interlocked.Exchange(ref onceFired, 1) == 0;

        void InvokeAndMaybeComplete(Action invoke)
        {
            if (!TryClaimOnce())
                return;

            try
            {
                invoke();
            }
            finally
            {
                if (once)
                    RemoveLedgerEntry(entry, disposeInner: true);
            }
        }

        NoireSubscriptionToken innerToken;

        if (handler != null)
        {
            innerToken = registry.Subscribe(typeof(TEvent), context => InvokeAndMaybeComplete(() => handler((TEvent)context)), innerOptions);
        }
        else
        {
            var asyncTyped = asyncHandler!;
            innerToken = registry.SubscribeAsync(typeof(TEvent), context =>
            {
                Task task = Task.CompletedTask;
                InvokeAndMaybeComplete(() => task = asyncTyped((TEvent)context));
                return task;
            }, innerOptions);
        }

        entry.InnerToken = innerToken;

        LedgerEntry? replaced = null;

        lock (gate)
        {
            if (entry.Key != null && keyedEntries.TryGetValue(entry.Key, out replaced))
                keyedEntries.Remove(entry.Key);

            ledger.Add(entry);

            if (entry.Key != null)
                keyedEntries[entry.Key] = entry;
        }

        // Keyed replacement: disposing the previous outer token releases its interest and inner subscription.
        replaced?.OuterToken?.Dispose();

        if (interest is { } primary)
            AddInterest(primary);

        if (secondaryInterest is { } secondary)
            AddInterest(secondary);

        return outerToken;
    }

    /// <summary>
    /// Registers a non-registry watch (distance/region watchers, node watchers, threshold watchers) in the
    /// ledger so it gets keyed replacement, owner teardown and demand-driven interest like every subscription.
    /// </summary>
    internal NoireSubscriptionToken RegisterExternalWatch(
        string description,
        SourceKind? interest,
        object? owner,
        string? key,
        Action removeRegistration)
    {
        var entry = new LedgerEntry
        {
            EventType = typeof(object),
            Description = description,
            Interest = interest,
            SecondaryInterest = null,
            Owner = owner,
            Key = key,
            ExtraDispose = removeRegistration,
        };

        var outerToken = new NoireSubscriptionToken(key, 0, _ => RemoveLedgerEntry(entry, disposeInner: true));
        entry.OuterToken = outerToken;

        LedgerEntry? replaced = null;

        lock (gate)
        {
            if (key != null && keyedEntries.TryGetValue(key, out replaced))
                keyedEntries.Remove(key);

            ledger.Add(entry);

            if (key != null)
                keyedEntries[key] = entry;
        }

        replaced?.OuterToken?.Dispose();

        if (interest is { } kind)
            AddInterest(kind);

        return outerToken;
    }

    private void RemoveLedgerEntry(LedgerEntry entry, bool disposeInner)
    {
        bool removed;

        lock (gate)
        {
            removed = ledger.Remove(entry);

            if (removed && entry.Key != null && keyedEntries.TryGetValue(entry.Key, out var keyed) && ReferenceEquals(keyed, entry))
                keyedEntries.Remove(entry.Key);
        }

        if (!removed)
            return;

        if (disposeInner)
            entry.InnerToken?.Dispose();

        entry.OuterToken?.Invalidate();

        try
        {
            entry.ExtraDispose?.Invoke();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(this, ex, $"Cleanup of subscription '{entry.Description}' threw.");
        }

        if (entry.Interest is { } primary)
            ReleaseInterest(primary);

        if (entry.SecondaryInterest is { } secondary)
            ReleaseInterest(secondary);
    }

    /// <summary>
    /// Unsubscribes the subscription registered with the given key.
    /// </summary>
    /// <param name="key">The key the subscription was registered under.</param>
    /// <returns>True when a subscription was removed.</returns>
    public bool Unsubscribe(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        LedgerEntry? entry;

        lock (gate)
            keyedEntries.TryGetValue(key, out entry);

        if (entry == null)
            return false;

        entry.OuterToken?.Dispose();
        return true;
    }

    /// <summary>
    /// Removes every subscription, value watcher and wait registered with the given owner — one call for
    /// plugin-wide teardown.
    /// </summary>
    /// <param name="owner">The owner object subscriptions were tagged with.</param>
    /// <returns>The number of registrations removed.</returns>
    public int UnsubscribeOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        List<LedgerEntry> matches;

        lock (gate)
            matches = ledger.Where(e => ReferenceEquals(e.Owner, owner)).ToList();

        foreach (var entry in matches)
            entry.OuterToken?.Dispose();

        var watcherCount = RemoveValueWatchersByOwner(owner);

        return matches.Count + watcherCount;
    }

    /// <summary>The total number of live subscriptions (including facade helpers and internal latches).</summary>
    public int SubscriptionCount
    {
        get
        {
            lock (gate)
                return ledger.Count;
        }
    }

    #endregion

    #region Dispatch & custom events

    /// <summary>
    /// Injects an external event into the watcher — tier 5 of the coverage doctrine with full citizenship.<br/>
    /// Detect a fact however you like (your own hook, a network callback, anything), publish it here, and from
    /// then on it is indistinguishable from a library event: same subscriptions and options, same
    /// <see cref="WaitFor{TEvent}"/>, same <see cref="GameConditions.FromEvent{TEvent}"/>, same TaskQueue pairing.<br/>
    /// Dispatches inline when called on the framework thread and marshals to it otherwise. Library internals
    /// never consume the public stream, so a simulated event reaches only your handlers — this is also the
    /// test seam for exercising handler logic without the game.
    /// </summary>
    /// <typeparam name="TEvent">Any user-defined event type.</typeparam>
    /// <param name="evt">The event to publish.</param>
    public void Publish<TEvent>(TEvent evt) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (NoireService.IsInitialized() && !NoireService.Framework.IsInFrameworkUpdateThread)
        {
            NoireService.Framework.RunOnFrameworkThread(() => PublishInline(evt));
            return;
        }

        PublishInline(evt);
    }

    private void PublishInline<TEvent>(TEvent evt) where TEvent : notnull
    {
        customPublishCounters.AddOrUpdate(evt.GetType(), 1, (_, count) => count + 1);
        DispatchUntyped(evt);
    }

    /// <summary>
    /// The single dispatch path: counts the event, feeds the diagnostics log, dispatches through the registry
    /// and mirrors to the EventBus for opted-in types.
    /// </summary>
    internal void DispatchEvent<TEvent>(TEvent evt) where TEvent : notnull
        => DispatchUntyped(evt);

    /// <summary>
    /// Dispatches an event by its runtime type — used by table-driven sources that create events from factories.
    /// </summary>
    internal void DispatchUntyped(object evt)
    {
        var type = evt.GetType();

        eventCounters.AddOrUpdate(type, 1, (_, count) => count + 1);
        RecordRecentEvent(type);

        registry.Dispatch(type, evt);
        MirrorToEventBus(type, evt);
    }

    private void RecordRecentEvent(Type type)
    {
        var capacity = ActiveOptions.DiagnosticsEventLogCapacity;

        if (capacity <= 0)
            return;

        lock (recentEvents)
        {
            recentEvents.Enqueue((DateTimeOffset.UtcNow, type.Name));

            while (recentEvents.Count > capacity)
                recentEvents.Dequeue();
        }
    }

    #endregion

    #region Event → source map

    /// <summary>
    /// Looks up the producing source for an event type. Custom (user-defined) events have none.
    /// </summary>
    internal static SourceKind? LookupSource(Type eventType)
        => EventSourceMap.Value.TryGetValue(eventType, out var kind) ? kind : null;

    private static Dictionary<Type, SourceKind> BuildEventSourceMap()
    {
        var map = new Dictionary<Type, SourceKind>();

        void Add(SourceKind kind, params Type[] types)
        {
            foreach (var type in types)
                map[type] = kind;
        }

        Add(SourceKind.Session,
            typeof(LoginEvent), typeof(LogoutEvent), typeof(TerritoryChangedEvent), typeof(MapChangedEvent),
            typeof(InstanceChangedEvent), typeof(PvpEnteredEvent), typeof(PvpLeftEvent), typeof(CfPopEvent),
            typeof(LocalClassJobChangedEvent), typeof(LocalLevelChangedEvent),
            typeof(HousingInteriorEnteredEvent), typeof(HousingInteriorLeftEvent), typeof(GPoseStateChangedEvent));

        Add(SourceKind.Condition, typeof(ConditionChangedEvent));

        foreach (var row in ConditionPairTable.Rows)
        {
            map[row.EnterEventType] = SourceKind.Condition;
            map[row.LeaveEventType] = SourceKind.Condition;
        }

        Add(SourceKind.Characters,
            typeof(CharacterSpawnedEvent), typeof(CharacterDespawnedEvent), typeof(CharacterHpChangedEvent),
            typeof(CharacterMpChangedEvent), typeof(CharacterShieldChangedEvent), typeof(CharacterDiedEvent),
            typeof(CharacterRevivedEvent), typeof(CharacterCastStartedEvent), typeof(CharacterCastCompletedEvent),
            typeof(CharacterCastInterruptedEvent), typeof(CharacterCombatEnteredEvent), typeof(CharacterCombatLeftEvent),
            typeof(CharacterTargetChangedEvent), typeof(CharacterTargetableChangedEvent), typeof(CharacterModeChangedEvent),
            typeof(CharacterEmoteLoopStartedEvent), typeof(CharacterEmoteLoopEndedEvent),
            typeof(CharacterOnlineStatusChangedEvent), typeof(CharacterJobChangedEvent),
            typeof(CharacterLevelChangedEvent), typeof(CharacterIdentityChangedEvent));

        Add(SourceKind.Objects, typeof(ObjectSpawnedEvent), typeof(ObjectDespawnedEvent), typeof(ObjectChangedEvent));

        Add(SourceKind.Targets,
            typeof(TargetChangedEvent), typeof(FocusTargetChangedEvent),
            typeof(SoftTargetChangedEvent), typeof(MouseOverTargetChangedEvent));

        Add(SourceKind.Party,
            typeof(PartyMemberJoinedEvent), typeof(PartyMemberLeftEvent), typeof(PartyMemberChangedEvent),
            typeof(PartyMemberTerritoryChangedEvent), typeof(PartyLeaderChangedEvent), typeof(PartySizeChangedEvent),
            typeof(PartyCompositionChangedEvent), typeof(AllianceChangedEvent));

        Add(SourceKind.Friends,
            typeof(FriendOnlineEvent), typeof(FriendOfflineEvent), typeof(FriendTerritoryChangedEvent),
            typeof(FriendAddedEvent), typeof(FriendRemovedEvent));

        Add(SourceKind.Duty,
            typeof(DutyStartedEvent), typeof(DutyWipedEvent), typeof(DutyRecommencedEvent), typeof(DutyCompletedEvent),
            typeof(DutyQueueEnteredEvent), typeof(DutyQueueLeftEvent), typeof(DutyPopEvent));

        Add(SourceKind.Chat, typeof(ChatMessageEvent));

        Add(SourceKind.ActionEffect, typeof(ActionEffectEvent));

        Add(SourceKind.ActorControl, typeof(RawActorControlEvent), typeof(CharacterEmotePlayedEvent));

        Add(SourceKind.Cooldowns,
            typeof(CooldownStartedEvent), typeof(CooldownEndedEvent), typeof(ChargesChangedEvent),
            typeof(GcdStateChangedEvent), typeof(EstimatedCooldownStartedEvent), typeof(EstimatedCooldownEndedEvent));

        Add(SourceKind.Statuses, typeof(StatusGainedEvent), typeof(StatusLostEvent), typeof(StatusStackChangedEvent));

        Add(SourceKind.Addons, typeof(AddonLifecycleEvent), typeof(AddonShownEvent), typeof(AddonHiddenEvent));

        Add(SourceKind.Inventory,
            typeof(ItemAddedEvent), typeof(ItemRemovedEvent), typeof(ItemMovedEvent), typeof(ItemChangedEvent),
            typeof(ItemMergedEvent), typeof(ItemSplitEvent), typeof(ItemCountChangedEvent), typeof(GilChangedEvent));

        Add(SourceKind.Fate,
            typeof(FateSpawnedEvent), typeof(FateExpiredEvent), typeof(FateProgressChangedEvent), typeof(FateStateChangedEvent));

        Add(SourceKind.Weather, typeof(WeatherChangedEvent));

        Add(SourceKind.EorzeaTime, typeof(EorzeaHourChangedEvent), typeof(EorzeaDayNightChangedEvent));

        Add(SourceKind.Toast, typeof(ToastShownEvent), typeof(QuestToastShownEvent), typeof(ErrorToastShownEvent));

        return map;
    }

    #endregion

    #region Diagnostics accessors (internal)

    /// <summary>Per-event-type dispatch counters, for diagnostics.</summary>
    internal IReadOnlyDictionary<Type, long> EventCounters => eventCounters;

    /// <summary>Per-event-type custom publish counters, for diagnostics.</summary>
    internal IReadOnlyDictionary<Type, long> CustomPublishCounters => customPublishCounters;

    /// <summary>A snapshot of the recent event log, newest last, for diagnostics.</summary>
    internal (DateTimeOffset At, string Description)[] RecentEventsSnapshot()
    {
        lock (recentEvents)
            return recentEvents.ToArray();
    }

    /// <summary>A snapshot of live subscriptions (description, event type, key, interest), for diagnostics.</summary>
    internal (string Description, string EventType, string? Key, SourceKind? Interest)[] LedgerSnapshot()
    {
        lock (gate)
            return ledger.Select(e => (e.Description, e.EventType.Name, e.Key, e.Interest)).ToArray();
    }

    #endregion
}
