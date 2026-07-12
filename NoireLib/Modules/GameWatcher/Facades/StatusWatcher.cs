using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Status effect facts for any scoped character: gained/lost/stack-changed events, duration-threshold
/// watchers and group queries. Wide scopes in crowds are the one legitimately heavy path — dial the Statuses
/// source cadence down via <see cref="GameWatcherOptions.PollCadences"/> when needed.
/// </summary>
public sealed class StatusWatcher : GameWatcherFacade
{
    internal StatusWatcher(NoireGameWatcher watcher) : base(watcher) { }

    private NoireSubscriptionToken Scoped<TEvent>(
        Action<TEvent>? handler,
        Func<TEvent, Task>? asyncHandler,
        Scope? scope,
        uint? statusId,
        NoireSubscriptionOptions<TEvent>? options,
        Func<TEvent, StatusSnapshot> selectStatus,
        string description)
        where TEvent : class, ICharacterScopedEvent
    {
        if (handler == null && asyncHandler == null)
            throw new ArgumentNullException(nameof(handler));

        var effectiveScope = scope ?? Scope.LocalPlayer;
        var remove = Watcher.GetSource<StatusSource>(SourceKind.Statuses).AddScopeInterest(effectiveScope);

        var scopedOptions = WithFilter(options, evt =>
            (statusId == null || selectStatus(evt).StatusId == statusId.Value) && effectiveScope.Matches(evt.Subject));

        return Watcher.SubscribeCore(handler, asyncHandler, scopedOptions, SourceKind.Statuses, null, remove, $"{description} [{effectiveScope}]");
    }

    /// <summary>
    /// Subscribes to status gains.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Whose statuses to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="statusId">An optional status row id restriction; null = every status.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnGained(Action<StatusGainedEvent> handler, Scope? scope = null, uint? statusId = null, NoireSubscriptionOptions<StatusGainedEvent>? options = null)
        => Scoped(handler, null, scope, statusId, options, e => e.Status, nameof(OnGained));

    /// <inheritdoc cref="OnGained(Action{StatusGainedEvent}, Scope?, uint?, NoireSubscriptionOptions{StatusGainedEvent}?)"/>
    public NoireSubscriptionToken OnGainedAsync(Func<StatusGainedEvent, Task> handler, Scope? scope = null, uint? statusId = null, NoireSubscriptionOptions<StatusGainedEvent>? options = null)
        => Scoped(null, handler, scope, statusId, options, e => e.Status, nameof(OnGained));

    /// <summary>
    /// Subscribes to status losses.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Whose statuses to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="statusId">An optional status row id restriction; null = every status.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnLost(Action<StatusLostEvent> handler, Scope? scope = null, uint? statusId = null, NoireSubscriptionOptions<StatusLostEvent>? options = null)
        => Scoped(handler, null, scope, statusId, options, e => e.Status, nameof(OnLost));

    /// <inheritdoc cref="OnLost(Action{StatusLostEvent}, Scope?, uint?, NoireSubscriptionOptions{StatusLostEvent}?)"/>
    public NoireSubscriptionToken OnLostAsync(Func<StatusLostEvent, Task> handler, Scope? scope = null, uint? statusId = null, NoireSubscriptionOptions<StatusLostEvent>? options = null)
        => Scoped(null, handler, scope, statusId, options, e => e.Status, nameof(OnLost));

    /// <summary>
    /// Subscribes to status stack-count changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Whose statuses to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="statusId">An optional status row id restriction; null = every status.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnStackChanged(Action<StatusStackChangedEvent> handler, Scope? scope = null, uint? statusId = null, NoireSubscriptionOptions<StatusStackChangedEvent>? options = null)
        => Scoped(handler, null, scope, statusId, options, e => e.Current, nameof(OnStackChanged));

    /// <inheritdoc cref="OnStackChanged(Action{StatusStackChangedEvent}, Scope?, uint?, NoireSubscriptionOptions{StatusStackChangedEvent}?)"/>
    public NoireSubscriptionToken OnStackChangedAsync(Func<StatusStackChangedEvent, Task> handler, Scope? scope = null, uint? statusId = null, NoireSubscriptionOptions<StatusStackChangedEvent>? options = null)
        => Scoped(null, handler, scope, statusId, options, e => e.Current, nameof(OnStackChanged));

    /// <summary>
    /// Watches a status's remaining duration crossing below a threshold: the callback fires once per
    /// application when the remaining time first drops under <paramref name="thresholdSeconds"/>, and re-arms
    /// when the status is refreshed above it or reapplied.
    /// </summary>
    /// <param name="statusId">The status row id to watch.</param>
    /// <param name="thresholdSeconds">The threshold in seconds.</param>
    /// <param name="callback">Invoked with (owner, status) when the threshold is crossed.</param>
    /// <param name="scope">Whose statuses to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="owner">An optional owner for bulk removal.</param>
    /// <param name="key">An optional key for keyed replacement.</param>
    /// <returns>A token that stops the watcher when disposed.</returns>
    public NoireSubscriptionToken OnDurationBelow(
        uint statusId,
        float thresholdSeconds,
        Action<CharacterSnapshot, StatusSnapshot> callback,
        Scope? scope = null,
        object? owner = null,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (thresholdSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdSeconds), "Threshold must be positive.");

        var effectiveScope = scope ?? Scope.LocalPlayer;

        var remove = Watcher.GetSource<StatusSource>(SourceKind.Statuses).AddThresholdWatcher(new StatusSource.ThresholdWatcherRegistration
        {
            Scope = effectiveScope,
            StatusId = statusId,
            ThresholdSeconds = thresholdSeconds,
            Callback = callback,
        });

        return Watcher.RegisterExternalWatch($"OnDurationBelow({statusId}, {thresholdSeconds}s) [{effectiveScope}]", SourceKind.Statuses, owner, key, remove);
    }

    /// <summary>
    /// The current statuses of a character, or an empty list when absent. Live read (framework thread only);
    /// never activates anything.
    /// </summary>
    /// <param name="entityId">The character's entity id.</param>
    /// <returns>The status snapshots.</returns>
    public IReadOnlyList<StatusSnapshot> Get(uint entityId)
    {
        NoireGameWatcher.EnsureFrameworkThread();

        foreach (var obj in NoireService.ObjectTable)
        {
            if (obj.EntityId == entityId && obj is IBattleChara battleChara)
                return StatusSource.ReadStatuses(battleChara, DateTimeOffset.UtcNow).Values.ToArray();
        }

        return Array.Empty<StatusSnapshot>();
    }

    /// <summary>
    /// Whether a character currently has a status. Live read (framework thread only).
    /// </summary>
    /// <param name="entityId">The character's entity id.</param>
    /// <param name="statusId">The status row id.</param>
    /// <returns>True when present.</returns>
    public bool Has(uint entityId, uint statusId)
        => Get(entityId).Any(s => s.StatusId == statusId);
}
