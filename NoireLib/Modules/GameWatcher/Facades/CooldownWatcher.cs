using NoireLib.Core.Subscriptions;
using System;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Cooldown facts. Local player: exact recasts, charges and GCD read from the game.
/// Other characters: <b>estimates</b> inferred from observed action usage + sheet recast data -
/// always flagged <see cref="CooldownSnapshot.IsEstimate"/>, never to be treated as exact.
/// </summary>
public sealed class CooldownWatcher : GameWatcherFacade
{
    internal CooldownWatcher(NoireGameWatcher watcher) : base(watcher) { }

    private NoireSubscriptionToken WatchedAction<TEvent>(
        uint actionId,
        Action<TEvent>? handler,
        Func<TEvent, Task>? asyncHandler,
        NoireSubscriptionOptions<TEvent>? options,
        Func<TEvent, uint> selectActionId,
        string description)
        where TEvent : notnull
    {
        if (handler == null && asyncHandler == null)
            throw new ArgumentNullException(nameof(handler));

        var remove = Watcher.GetSource<CooldownSource>(SourceKind.Cooldowns).AddWatchedAction(actionId);

        return Watcher.SubscribeCore(
            handler,
            asyncHandler,
            WithFilter(options, evt => selectActionId(evt) == actionId),
            SourceKind.Cooldowns,
            null,
            remove,
            $"{description}({actionId})");
    }

    /// <summary>
    /// Subscribes to a local action going on cooldown. Exact.
    /// </summary>
    /// <param name="actionId">The action row id to watch.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCooldownStarted(uint actionId, Action<CooldownStartedEvent> handler, NoireSubscriptionOptions<CooldownStartedEvent>? options = null)
        => WatchedAction(actionId, handler, null, options, e => e.Cooldown.ActionId, nameof(OnCooldownStarted));

    /// <inheritdoc cref="OnCooldownStarted(uint, Action{CooldownStartedEvent}, NoireSubscriptionOptions{CooldownStartedEvent}?)"/>
    public NoireSubscriptionToken OnCooldownStartedAsync(uint actionId, Func<CooldownStartedEvent, Task> handler, NoireSubscriptionOptions<CooldownStartedEvent>? options = null)
        => WatchedAction(actionId, null, handler, options, e => e.Cooldown.ActionId, nameof(OnCooldownStarted));

    /// <summary>
    /// Subscribes to a local action coming off cooldown. Exact.
    /// </summary>
    /// <param name="actionId">The action row id to watch.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCooldownEnded(uint actionId, Action<CooldownEndedEvent> handler, NoireSubscriptionOptions<CooldownEndedEvent>? options = null)
        => WatchedAction(actionId, handler, null, options, e => e.Cooldown.ActionId, nameof(OnCooldownEnded));

    /// <inheritdoc cref="OnCooldownEnded(uint, Action{CooldownEndedEvent}, NoireSubscriptionOptions{CooldownEndedEvent}?)"/>
    public NoireSubscriptionToken OnCooldownEndedAsync(uint actionId, Func<CooldownEndedEvent, Task> handler, NoireSubscriptionOptions<CooldownEndedEvent>? options = null)
        => WatchedAction(actionId, null, handler, options, e => e.Cooldown.ActionId, nameof(OnCooldownEnded));

    /// <summary>
    /// Subscribes to a local action's charge count changes. Exact.
    /// </summary>
    /// <param name="actionId">The action row id to watch.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnChargesChanged(uint actionId, Action<ChargesChangedEvent> handler, NoireSubscriptionOptions<ChargesChangedEvent>? options = null)
        => WatchedAction(actionId, handler, null, options, e => e.Cooldown.ActionId, nameof(OnChargesChanged));

    /// <inheritdoc cref="OnChargesChanged(uint, Action{ChargesChangedEvent}, NoireSubscriptionOptions{ChargesChangedEvent}?)"/>
    public NoireSubscriptionToken OnChargesChangedAsync(uint actionId, Func<ChargesChangedEvent, Task> handler, NoireSubscriptionOptions<ChargesChangedEvent>? options = null)
        => WatchedAction(actionId, null, handler, options, e => e.Cooldown.ActionId, nameof(OnChargesChanged));

    /// <summary>
    /// Subscribes to global-cooldown state changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnGcdChanged(Action<GcdStateChangedEvent> handler, NoireSubscriptionOptions<GcdStateChangedEvent>? options = null)
        => On(handler, null, options, nameof(OnGcdChanged));

    /// <inheritdoc cref="OnGcdChanged(Action{GcdStateChangedEvent}, NoireSubscriptionOptions{GcdStateChangedEvent}?)"/>
    public NoireSubscriptionToken OnGcdChangedAsync(Func<GcdStateChangedEvent, Task> handler, NoireSubscriptionOptions<GcdStateChangedEvent>? options = null)
        => On(null, handler, options, nameof(OnGcdChanged));

    private NoireSubscriptionToken Estimated<TEvent>(
        Action<TEvent>? handler,
        Func<TEvent, Task>? asyncHandler,
        NoireSubscriptionOptions<TEvent>? options,
        string description)
        where TEvent : notnull
    {
        if (handler == null && asyncHandler == null)
            throw new ArgumentNullException(nameof(handler));

        var removeEstimateInterest = Watcher.GetSource<CooldownSource>(SourceKind.Cooldowns).AddEstimateInterest();

        // Estimation needs the ActionEffect hook observing action usage.
        return Watcher.SubscribeCore(
            handler,
            asyncHandler,
            options,
            SourceKind.Cooldowns,
            SourceKind.ActionEffect,
            removeEstimateInterest,
            description);
    }

    /// <summary>
    /// Subscribes to estimated cooldown starts of <b>other characters</b> - inferred from observed action
    /// usage plus sheet recast data (doctrine tier 4). Estimates drift with skill/spell speed, haste effects
    /// and unseen charge usage; never treat them as exact.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnEstimatedCooldownStarted(Action<EstimatedCooldownStartedEvent> handler, NoireSubscriptionOptions<EstimatedCooldownStartedEvent>? options = null)
        => Estimated(handler, null, options, nameof(OnEstimatedCooldownStarted));

    /// <inheritdoc cref="OnEstimatedCooldownStarted(Action{EstimatedCooldownStartedEvent}, NoireSubscriptionOptions{EstimatedCooldownStartedEvent}?)"/>
    public NoireSubscriptionToken OnEstimatedCooldownStartedAsync(Func<EstimatedCooldownStartedEvent, Task> handler, NoireSubscriptionOptions<EstimatedCooldownStartedEvent>? options = null)
        => Estimated(null, handler, options, nameof(OnEstimatedCooldownStarted));

    /// <summary>
    /// Subscribes to estimated cooldown ends of other characters. <b>Estimate</b> - see
    /// <see cref="OnEstimatedCooldownStarted"/>.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnEstimatedCooldownEnded(Action<EstimatedCooldownEndedEvent> handler, NoireSubscriptionOptions<EstimatedCooldownEndedEvent>? options = null)
        => Estimated(handler, null, options, nameof(OnEstimatedCooldownEnded));

    /// <inheritdoc cref="OnEstimatedCooldownEnded(Action{EstimatedCooldownEndedEvent}, NoireSubscriptionOptions{EstimatedCooldownEndedEvent}?)"/>
    public NoireSubscriptionToken OnEstimatedCooldownEndedAsync(Func<EstimatedCooldownEndedEvent, Task> handler, NoireSubscriptionOptions<EstimatedCooldownEndedEvent>? options = null)
        => Estimated(null, handler, options, nameof(OnEstimatedCooldownEnded));

    /// <summary>
    /// The exact local recast state of an action, or null when unavailable. Live read (framework thread only);
    /// never activates anything.
    /// </summary>
    /// <param name="actionId">The action row id.</param>
    /// <returns>The recast state.</returns>
    public CooldownSnapshot? GetLocal(uint actionId)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return CooldownSource.ReadLocalCooldown(actionId, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The estimated recast state of another character's action, or null when its usage was never observed.<br/>
    /// Estimates only accumulate while estimation runs (an estimated-event subscription exists, or the
    /// Cooldowns and ActionEffect sources are marked always-on).
    /// </summary>
    /// <param name="entityId">The character's entity id.</param>
    /// <param name="actionId">The action row id.</param>
    /// <returns>The estimated recast state (<see cref="CooldownSnapshot.IsEstimate"/> is true), or null.</returns>
    public CooldownSnapshot? GetEstimate(uint entityId, uint actionId)
    {
        NoireGameWatcher.EnsureFrameworkThread();
        return Watcher.GetSource<CooldownSource>(SourceKind.Cooldowns).GetEstimate(entityId, actionId, DateTimeOffset.UtcNow);
    }

    /// <summary>Whether a local action is ready (off cooldown / has a charge). Live read (framework thread only).</summary>
    /// <param name="actionId">The action row id.</param>
    /// <returns>True when ready.</returns>
    public bool IsActionReady(uint actionId)
        => GetLocal(actionId)?.IsReady ?? false;

    /// <summary>Whether the global cooldown is ready. Live read (framework thread only).</summary>
    public bool IsGcdReady
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();
            return CooldownSource.ReadGcdReady(out _);
        }
    }
}
