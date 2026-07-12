using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Combat facts: fully parsed action effects (damage/heal/crit/direct-hit/block/parry, per target), scoped by
/// action source and by target, rolling statistics, opt-in bounded history, and the raw ActorControl tap.
/// </summary>
public sealed class CombatWatcher : GameWatcherFacade
{
    internal CombatWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to every action effect received from the server.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnAction(Action<ActionEffectEvent> handler, NoireSubscriptionOptions<ActionEffectEvent>? options = null)
        => On(handler, null, options, nameof(OnAction));

    /// <inheritdoc cref="OnAction(Action{ActionEffectEvent}, NoireSubscriptionOptions{ActionEffectEvent}?)"/>
    public NoireSubscriptionToken OnActionAsync(Func<ActionEffectEvent, Task> handler, NoireSubscriptionOptions<ActionEffectEvent>? options = null)
        => On(null, handler, options, nameof(OnAction));

    /// <summary>
    /// Subscribes to action effects <b>performed by</b> characters in a scope.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Whose actions to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnActionBy(Action<ActionEffectEvent> handler, Scope? scope = null, NoireSubscriptionOptions<ActionEffectEvent>? options = null)
    {
        var effectiveScope = scope ?? Scope.LocalPlayer;
        return On(handler, null, WithFilter(options, evt => MatchesSource(evt, effectiveScope)), $"{nameof(OnActionBy)} [{effectiveScope}]");
    }

    /// <inheritdoc cref="OnActionBy(Action{ActionEffectEvent}, Scope?, NoireSubscriptionOptions{ActionEffectEvent}?)"/>
    public NoireSubscriptionToken OnActionByAsync(Func<ActionEffectEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<ActionEffectEvent>? options = null)
    {
        var effectiveScope = scope ?? Scope.LocalPlayer;
        return On(null, handler, WithFilter(options, evt => MatchesSource(evt, effectiveScope)), $"{nameof(OnActionBy)} [{effectiveScope}]");
    }

    /// <summary>
    /// Subscribes to action effects <b>hitting</b> characters in a scope.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Whose incoming effects to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnActionAgainst(Action<ActionEffectEvent> handler, Scope? scope = null, NoireSubscriptionOptions<ActionEffectEvent>? options = null)
    {
        var effectiveScope = scope ?? Scope.LocalPlayer;
        return On(handler, null, WithFilter(options, evt => MatchesAnyTarget(evt, effectiveScope)), $"{nameof(OnActionAgainst)} [{effectiveScope}]");
    }

    /// <inheritdoc cref="OnActionAgainst(Action{ActionEffectEvent}, Scope?, NoireSubscriptionOptions{ActionEffectEvent}?)"/>
    public NoireSubscriptionToken OnActionAgainstAsync(Func<ActionEffectEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<ActionEffectEvent>? options = null)
    {
        var effectiveScope = scope ?? Scope.LocalPlayer;
        return On(null, handler, WithFilter(options, evt => MatchesAnyTarget(evt, effectiveScope)), $"{nameof(OnActionAgainst)} [{effectiveScope}]");
    }

    /// <summary>
    /// Subscribes to the raw ActorControl packet stream — tier 5 of the coverage doctrine.<br/>
    /// <b>Advanced and unstable</b>: categories and argument meanings are reverse-engineered and can change
    /// with game patches. Prefer the modeled events when they exist.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnRawActorControl(Action<RawActorControlEvent> handler, NoireSubscriptionOptions<RawActorControlEvent>? options = null)
        => On(handler, null, options, nameof(OnRawActorControl));

    /// <inheritdoc cref="OnRawActorControl(Action{RawActorControlEvent}, NoireSubscriptionOptions{RawActorControlEvent}?)"/>
    public NoireSubscriptionToken OnRawActorControlAsync(Func<RawActorControlEvent, Task> handler, NoireSubscriptionOptions<RawActorControlEvent>? options = null)
        => On(null, handler, options, nameof(OnRawActorControl));

    /// <summary>Rolling statistics over every observed action effect since the source last activated.</summary>
    public ActionEffectStatistics Statistics
        => Watcher.GetSource<ActionEffectSource>(SourceKind.ActionEffect).Statistics;

    /// <summary>
    /// The retained action-effect history, newest first. Only collects while the ActionEffect source runs with
    /// a configured <see cref="CombatSourceOptions.HistoryCapacity"/> (which marks the source always-on).
    /// </summary>
    /// <returns>The history snapshot.</returns>
    public IReadOnlyList<ActionEffectEntry> GetHistory()
        => Watcher.GetSource<ActionEffectSource>(SourceKind.ActionEffect).GetHistory();

    /// <summary>Clears the retained action-effect history.</summary>
    public void ClearHistory()
        => Watcher.GetSource<ActionEffectSource>(SourceKind.ActionEffect).ClearHistory();

    private bool MatchesSource(ActionEffectEvent evt, Scope scope)
        => MatchesEntity(evt.Entry.SourceEntityId, scope);

    private bool MatchesAnyTarget(ActionEffectEvent evt, Scope scope)
    {
        foreach (var targetId in evt.Entry.TargetEntityIds)
        {
            if (targetId <= uint.MaxValue && MatchesEntity((uint)targetId, scope))
                return true;
        }

        return false;
    }

    private bool MatchesEntity(uint entityId, Scope scope)
    {
        if (entityId == 0)
            return false;

        // Same-tick coherence: reuse the Characters source snapshot when it tracks the entity,
        // otherwise capture live.
        var tracked = Watcher.GetSource<CharacterSource>(SourceKind.Characters).TryGetTracked(entityId);

        if (tracked != null)
            return scope.Matches(tracked);

        var snapshot = Watcher.Characters.Find(entityId);
        return snapshot != null && scope.Matches(snapshot);
    }
}
