using NoireLib.EventBus;
using System;
using System.Collections.Generic;

namespace NoireLib.GameWatcher;

/// <summary>
/// Options for a <see cref="NoireGameWatcher"/> instance.<br/>
/// Options are snapshotted when the module activates; changes made while active require a
/// deactivate/activate cycle to apply.
/// </summary>
public sealed class GameWatcherOptions
{
    /// <summary>
    /// An optional EventBus to mirror events to. Nothing is published by default — opt in per event type
    /// with <see cref="NoireGameWatcher.PublishToEventBus{TEvent}"/>.
    /// </summary>
    public NoireEventBus? EventBus { get; set; }

    /// <summary>
    /// Per-source activation overrides. Sources not listed use demand-driven activation.<br/>
    /// <see cref="SourceOverride.Disabled"/> beats everything, including the always-on implied by a configured
    /// history capacity — the contradiction is logged rather than guessed at.
    /// </summary>
    public Dictionary<SourceKind, SourceOverride> Sources { get; set; } = new();

    /// <summary>
    /// Per-source poll cadence overrides for polling sources. Unlisted sources use their defaults:
    /// every tick for the hot sources (Characters, Objects, Targets, Statuses, Party),
    /// and 1 second for Fate/Weather/EorzeaTime/Friends.<br/>
    /// Dial a source down (e.g. Statuses to 100 ms) when watching wide scopes in crowded areas.
    /// </summary>
    public Dictionary<SourceKind, TimeSpan> PollCadences { get; set; } = new();

    /// <summary>Chat-specific options.</summary>
    public ChatSourceOptions Chat { get; set; } = new();

    /// <summary>Combat (action effect) specific options.</summary>
    public CombatSourceOptions Combat { get; set; } = new();

    /// <summary>
    /// The safety-poll interval for addon node and visibility watchers. Node watchers re-evaluate on addon
    /// refresh events; the safety poll catches addons that mutate nodes without a refresh. Default: 250 ms.
    /// </summary>
    public TimeSpan AddonSafetyPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How often the Friends source refreshes the game's social proxy in the background (via
    /// <c>InfoProxyFriendList.RequestData</c>) so friend online/offline/location updates without the friend
    /// list being open. Default: a jittered 30–40 seconds (floored at 30) so the request timing is not a
    /// detectable fixed beat. The refresh is <b>skipped while the friend-list window is open</b> so it never
    /// re-sorts or scrolls the addon; the game keeps the list live while it is open anyway.
    /// </summary>
    public JitteredInterval FriendsRefreshCadence { get; set; } = JitteredInterval.Default;

    /// <summary>
    /// The extra distance (in yalms) added to a distance watcher's leave threshold so a subject oscillating
    /// on the boundary does not flap. Default: 0.5.
    /// </summary>
    public float DistanceHysteresis { get; set; } = 0.5f;

    /// <summary>
    /// The number of recent events retained for the diagnostics window. Default: 100. Set to 0 to disable.
    /// </summary>
    public int DiagnosticsEventLogCapacity { get; set; } = 100;

    /// <summary>
    /// Creates a deep copy of the options.
    /// </summary>
    /// <returns>The copy.</returns>
    public GameWatcherOptions Clone() => new()
    {
        EventBus = EventBus,
        Sources = new Dictionary<SourceKind, SourceOverride>(Sources),
        PollCadences = new Dictionary<SourceKind, TimeSpan>(PollCadences),
        Chat = Chat.Clone(),
        Combat = Combat.Clone(),
        AddonSafetyPollInterval = AddonSafetyPollInterval,
        FriendsRefreshCadence = FriendsRefreshCadence,
        DistanceHysteresis = DistanceHysteresis,
        DiagnosticsEventLogCapacity = DiagnosticsEventLogCapacity,
    };
}

/// <summary>
/// Chat-specific options for <see cref="GameWatcherOptions.Chat"/>.
/// </summary>
public sealed class ChatSourceOptions
{
    /// <summary>
    /// The number of chat messages retained in history. 0 (default) disables history.<br/>
    /// Configuring a capacity marks the Chat source always-on — a capacity that silently collected nothing
    /// would be a footgun (unless the source is explicitly disabled, which wins and is logged).
    /// </summary>
    public int HistoryCapacity { get; set; }

    /// <summary>
    /// When set, identical messages (same channel, sender and text) arriving within this window of the last
    /// dispatched instance are suppressed and coalesced: the next dispatched instance carries the count in
    /// <see cref="ChatMessageEvent.RepeatCount"/>. Null (default) disables suppression.
    /// </summary>
    public TimeSpan? DuplicateSuppressionWindow { get; set; }

    /// <summary>
    /// Creates a copy of the options.
    /// </summary>
    /// <returns>The copy.</returns>
    public ChatSourceOptions Clone() => new()
    {
        HistoryCapacity = HistoryCapacity,
        DuplicateSuppressionWindow = DuplicateSuppressionWindow,
    };
}

/// <summary>
/// Combat-specific options for <see cref="GameWatcherOptions.Combat"/>.
/// </summary>
public sealed class CombatSourceOptions
{
    /// <summary>
    /// The number of action-effect entries retained in history. 0 (default) disables history.<br/>
    /// Configuring a capacity marks the ActionEffect source always-on (unless explicitly disabled, which wins).
    /// </summary>
    public int HistoryCapacity { get; set; }

    /// <summary>
    /// Creates a copy of the options.
    /// </summary>
    /// <returns>The copy.</returns>
    public CombatSourceOptions Clone() => new()
    {
        HistoryCapacity = HistoryCapacity,
    };
}
