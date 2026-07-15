using Dalamud.Game.ClientState.Conditions;
using NoireLib.Core.Subscriptions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Raw condition-flag facts. The derived enter/leave pairs (combat, mounted, crafting, loading, …) are their
/// own event types - subscribe to them with <see cref="NoireGameWatcher.Subscribe{TEvent}"/> (e.g.
/// <see cref="CombatEnteredEvent"/>, <see cref="LoadingEndedEvent"/>) or await them with
/// <see cref="NoireGameWatcher.WaitFor{TEvent}"/>.
/// </summary>
public sealed class ConditionWatcher : GameWatcherFacade
{
    internal ConditionWatcher(NoireGameWatcher watcher) : base(watcher) { }

    /// <summary>
    /// Subscribes to raw condition-flag changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="flag">An optional flag restriction; null = every flag.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnChanged(Action<ConditionChangedEvent> handler, ConditionFlag? flag = null, NoireSubscriptionOptions<ConditionChangedEvent>? options = null)
        => On(handler, null, flag == null ? options : WithFilter(options, evt => evt.Flag == flag.Value), nameof(OnChanged));

    /// <inheritdoc cref="OnChanged(Action{ConditionChangedEvent}, ConditionFlag?, NoireSubscriptionOptions{ConditionChangedEvent}?)"/>
    public NoireSubscriptionToken OnChangedAsync(Func<ConditionChangedEvent, Task> handler, ConditionFlag? flag = null, NoireSubscriptionOptions<ConditionChangedEvent>? options = null)
        => On(null, handler, flag == null ? options : WithFilter(options, evt => evt.Flag == flag.Value), nameof(OnChanged));

    /// <summary>Whether a condition flag is currently set. Live read (framework thread only).</summary>
    /// <param name="flag">The condition flag.</param>
    /// <returns>True when set.</returns>
    public bool IsSet(ConditionFlag flag)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return NoireService.Condition[flag];
    }

    /// <summary>Every currently active condition flag. Live read (framework thread only).</summary>
    /// <returns>The active flags.</returns>
    public ConditionFlag[] GetActive()
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return NoireService.Condition.AsReadOnlySet().ToArray();
    }
}
