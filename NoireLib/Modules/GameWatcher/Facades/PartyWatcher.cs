using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Party and alliance facts: member joins/leaves/changes, leader changes, size and role composition, and
/// member territory changes — the latter working even for members outside the local object table
/// (remote presence for party members, server-synchronized and seconds-grained).
/// </summary>
public sealed class PartyWatcher : GameWatcherFacade
{
    internal PartyWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to members joining the party.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMemberJoined(Action<PartyMemberJoinedEvent> handler, NoireSubscriptionOptions<PartyMemberJoinedEvent>? options = null)
        => On(handler, null, options, nameof(OnMemberJoined));

    /// <inheritdoc cref="OnMemberJoined(Action{PartyMemberJoinedEvent}, NoireSubscriptionOptions{PartyMemberJoinedEvent}?)"/>
    public NoireSubscriptionToken OnMemberJoinedAsync(Func<PartyMemberJoinedEvent, Task> handler, NoireSubscriptionOptions<PartyMemberJoinedEvent>? options = null)
        => On(null, handler, options, nameof(OnMemberJoined));

    /// <summary>
    /// Subscribes to members leaving the party.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMemberLeft(Action<PartyMemberLeftEvent> handler, NoireSubscriptionOptions<PartyMemberLeftEvent>? options = null)
        => On(handler, null, options, nameof(OnMemberLeft));

    /// <inheritdoc cref="OnMemberLeft(Action{PartyMemberLeftEvent}, NoireSubscriptionOptions{PartyMemberLeftEvent}?)"/>
    public NoireSubscriptionToken OnMemberLeftAsync(Func<PartyMemberLeftEvent, Task> handler, NoireSubscriptionOptions<PartyMemberLeftEvent>? options = null)
        => On(null, handler, options, nameof(OnMemberLeft));

    /// <summary>
    /// Subscribes to party member property changes (level, job, HP as known to the party list).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMemberChanged(Action<PartyMemberChangedEvent> handler, NoireSubscriptionOptions<PartyMemberChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnMemberChanged));

    /// <inheritdoc cref="OnMemberChanged(Action{PartyMemberChangedEvent}, NoireSubscriptionOptions{PartyMemberChangedEvent}?)"/>
    public NoireSubscriptionToken OnMemberChangedAsync(Func<PartyMemberChangedEvent, Task> handler, NoireSubscriptionOptions<PartyMemberChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnMemberChanged));

    /// <summary>
    /// Subscribes to party member territory changes — remote presence for party members anywhere.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMemberTerritoryChanged(Action<PartyMemberTerritoryChangedEvent> handler, NoireSubscriptionOptions<PartyMemberTerritoryChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnMemberTerritoryChanged));

    /// <inheritdoc cref="OnMemberTerritoryChanged(Action{PartyMemberTerritoryChangedEvent}, NoireSubscriptionOptions{PartyMemberTerritoryChangedEvent}?)"/>
    public NoireSubscriptionToken OnMemberTerritoryChangedAsync(Func<PartyMemberTerritoryChangedEvent, Task> handler, NoireSubscriptionOptions<PartyMemberTerritoryChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnMemberTerritoryChanged));

    /// <summary>
    /// Subscribes to party leader changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnLeaderChanged(Action<PartyLeaderChangedEvent> handler, NoireSubscriptionOptions<PartyLeaderChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnLeaderChanged));

    /// <inheritdoc cref="OnLeaderChanged(Action{PartyLeaderChangedEvent}, NoireSubscriptionOptions{PartyLeaderChangedEvent}?)"/>
    public NoireSubscriptionToken OnLeaderChangedAsync(Func<PartyLeaderChangedEvent, Task> handler, NoireSubscriptionOptions<PartyLeaderChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnLeaderChanged));

    /// <summary>
    /// Subscribes to party size changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnSizeChanged(Action<PartySizeChangedEvent> handler, NoireSubscriptionOptions<PartySizeChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnSizeChanged));

    /// <inheritdoc cref="OnSizeChanged(Action{PartySizeChangedEvent}, NoireSubscriptionOptions{PartySizeChangedEvent}?)"/>
    public NoireSubscriptionToken OnSizeChangedAsync(Func<PartySizeChangedEvent, Task> handler, NoireSubscriptionOptions<PartySizeChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnSizeChanged));

    /// <summary>
    /// Subscribes to role composition changes (tank/healer/DPS counts).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCompositionChanged(Action<PartyCompositionChangedEvent> handler, NoireSubscriptionOptions<PartyCompositionChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnCompositionChanged));

    /// <inheritdoc cref="OnCompositionChanged(Action{PartyCompositionChangedEvent}, NoireSubscriptionOptions{PartyCompositionChangedEvent}?)"/>
    public NoireSubscriptionToken OnCompositionChangedAsync(Func<PartyCompositionChangedEvent, Task> handler, NoireSubscriptionOptions<PartyCompositionChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnCompositionChanged));

    /// <summary>
    /// Subscribes to alliance member list changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnAllianceChanged(Action<AllianceChangedEvent> handler, NoireSubscriptionOptions<AllianceChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnAllianceChanged));

    /// <inheritdoc cref="OnAllianceChanged(Action{AllianceChangedEvent}, NoireSubscriptionOptions{AllianceChangedEvent}?)"/>
    public NoireSubscriptionToken OnAllianceChangedAsync(Func<AllianceChangedEvent, Task> handler, NoireSubscriptionOptions<AllianceChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnAllianceChanged));

    /// <summary>
    /// The current party state: members, alliance members, leader and roles. Live read (framework thread only);
    /// never activates anything.
    /// </summary>
    public PartyState State
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return Watcher.GetSource<PartySource>(SourceKind.Party).CaptureState(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>The current party size (members only; 0 when solo). Live read (framework thread only).</summary>
    public int Size
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return NoireService.PartyList.Length;
        }
    }
}
