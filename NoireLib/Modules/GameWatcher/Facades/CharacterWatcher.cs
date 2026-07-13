using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Character facts for <b>anyone</b> — the local player and every other character alike. One event stream per
/// fact; a <see cref="Scope"/> decides who it fires for (default: <see cref="Scope.LocalPlayer"/>).<br/>
/// Every helper has one shape — <c>(handler, scope, options)</c> — plus one async twin. Keyed replacement,
/// priority, extra filtering and one-shot all come from <see cref="NoireSubscriptionOptions{TContext}"/>.
/// </summary>
public sealed class CharacterWatcher : GameWatcherFacade
{
    internal CharacterWatcher(NoireGameWatcher watcher) : base(watcher) { }

    #region Subscription plumbing

    private NoireSubscriptionToken Scoped<TEvent>(
        CharacterAspect aspect,
        Action<TEvent>? handler,
        Func<TEvent, Task>? asyncHandler,
        Scope? scope,
        NoireSubscriptionOptions<TEvent>? options,
        string description,
        SourceKind source = SourceKind.Characters,
        SourceKind? secondaryInterest = null)
        where TEvent : class, ICharacterScopedEvent
    {
        if (handler == null && asyncHandler == null)
            throw new ArgumentNullException(nameof(handler));

        var effectiveScope = scope ?? Scope.LocalPlayer;

        IDisposable? interestHandle = null;

        if (source == SourceKind.Characters && aspect != CharacterAspect.None)
            interestHandle = Watcher.GetSource<CharacterSource>(SourceKind.Characters).AddScopedInterest(aspect, effectiveScope);

        var scopedOptions = WithFilter(options, evt => effectiveScope.Matches(evt.Subject));

        return Watcher.SubscribeCore(
            handler,
            asyncHandler,
            scopedOptions,
            source,
            secondaryInterest,
            interestHandle == null ? null : interestHandle.Dispose,
            $"{description} [{effectiveScope}]");
    }

    #endregion

    #region Presence

    /// <summary>
    /// Subscribes to characters appearing in the object table. The object table is the client's entire view of
    /// the area, so with a scope this <i>is</i> the presence event — someone arriving where you are.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnSpawned(Action<CharacterSpawnedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterSpawnedEvent>? options = null)
        => Scoped(CharacterAspect.Presence, handler, null, scope, options, nameof(OnSpawned));

    /// <inheritdoc cref="OnSpawned(Action{CharacterSpawnedEvent}, Scope?, NoireSubscriptionOptions{CharacterSpawnedEvent}?)"/>
    public NoireSubscriptionToken OnSpawnedAsync(Func<CharacterSpawnedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterSpawnedEvent>? options = null)
        => Scoped(CharacterAspect.Presence, null, handler, scope, options, nameof(OnSpawned));

    /// <summary>
    /// Subscribes to characters disappearing from the object table. During zone transitions the whole table
    /// respawns — check <see cref="CharacterDespawnedEvent.DuringZoneChange"/> to tell "they left" from "we left".
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnDespawned(Action<CharacterDespawnedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterDespawnedEvent>? options = null)
        => Scoped(CharacterAspect.Presence, handler, null, scope, options, nameof(OnDespawned));

    /// <inheritdoc cref="OnDespawned(Action{CharacterDespawnedEvent}, Scope?, NoireSubscriptionOptions{CharacterDespawnedEvent}?)"/>
    public NoireSubscriptionToken OnDespawnedAsync(Func<CharacterDespawnedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterDespawnedEvent>? options = null)
        => Scoped(CharacterAspect.Presence, null, handler, scope, options, nameof(OnDespawned));

    #endregion

    #region Vitals & shield

    /// <summary>
    /// Subscribes to HP changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnHpChanged(Action<CharacterHpChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterHpChangedEvent>? options = null)
        => Scoped(CharacterAspect.Vitals, handler, null, scope, options, nameof(OnHpChanged));

    /// <inheritdoc cref="OnHpChanged(Action{CharacterHpChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterHpChangedEvent}?)"/>
    public NoireSubscriptionToken OnHpChangedAsync(Func<CharacterHpChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterHpChangedEvent>? options = null)
        => Scoped(CharacterAspect.Vitals, null, handler, scope, options, nameof(OnHpChanged));

    /// <summary>
    /// Subscribes to MP changes (and GP/CP — the latter are only meaningful for the local player,
    /// as the game does not synchronize them for others).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnMpChanged(Action<CharacterMpChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterMpChangedEvent>? options = null)
        => Scoped(CharacterAspect.Vitals, handler, null, scope, options, nameof(OnMpChanged));

    /// <inheritdoc cref="OnMpChanged(Action{CharacterMpChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterMpChangedEvent}?)"/>
    public NoireSubscriptionToken OnMpChangedAsync(Func<CharacterMpChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterMpChangedEvent>? options = null)
        => Scoped(CharacterAspect.Vitals, null, handler, scope, options, nameof(OnMpChanged));

    /// <summary>
    /// Subscribes to shield percentage changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnShieldChanged(Action<CharacterShieldChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterShieldChangedEvent>? options = null)
        => Scoped(CharacterAspect.Shield, handler, null, scope, options, nameof(OnShieldChanged));

    /// <inheritdoc cref="OnShieldChanged(Action{CharacterShieldChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterShieldChangedEvent}?)"/>
    public NoireSubscriptionToken OnShieldChangedAsync(Func<CharacterShieldChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterShieldChangedEvent>? options = null)
        => Scoped(CharacterAspect.Shield, null, handler, scope, options, nameof(OnShieldChanged));

    #endregion

    #region Life

    /// <summary>
    /// Subscribes to character deaths.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnDied(Action<CharacterDiedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterDiedEvent>? options = null)
        => Scoped(CharacterAspect.Life, handler, null, scope, options, nameof(OnDied));

    /// <inheritdoc cref="OnDied(Action{CharacterDiedEvent}, Scope?, NoireSubscriptionOptions{CharacterDiedEvent}?)"/>
    public NoireSubscriptionToken OnDiedAsync(Func<CharacterDiedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterDiedEvent>? options = null)
        => Scoped(CharacterAspect.Life, null, handler, scope, options, nameof(OnDied));

    /// <summary>
    /// Subscribes to character revivals.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnRevived(Action<CharacterRevivedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterRevivedEvent>? options = null)
        => Scoped(CharacterAspect.Life, handler, null, scope, options, nameof(OnRevived));

    /// <inheritdoc cref="OnRevived(Action{CharacterRevivedEvent}, Scope?, NoireSubscriptionOptions{CharacterRevivedEvent}?)"/>
    public NoireSubscriptionToken OnRevivedAsync(Func<CharacterRevivedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterRevivedEvent>? options = null)
        => Scoped(CharacterAspect.Life, null, handler, scope, options, nameof(OnRevived));

    #endregion

    #region Casts

    /// <summary>
    /// Subscribes to cast starts.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCastStarted(Action<CharacterCastStartedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCastStartedEvent>? options = null)
        => Scoped(CharacterAspect.Cast, handler, null, scope, options, nameof(OnCastStarted));

    /// <inheritdoc cref="OnCastStarted(Action{CharacterCastStartedEvent}, Scope?, NoireSubscriptionOptions{CharacterCastStartedEvent}?)"/>
    public NoireSubscriptionToken OnCastStartedAsync(Func<CharacterCastStartedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCastStartedEvent>? options = null)
        => Scoped(CharacterAspect.Cast, null, handler, scope, options, nameof(OnCastStarted));

    /// <summary>
    /// Subscribes to cast completions.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCastCompleted(Action<CharacterCastCompletedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCastCompletedEvent>? options = null)
        => Scoped(CharacterAspect.Cast, handler, null, scope, options, nameof(OnCastCompleted));

    /// <inheritdoc cref="OnCastCompleted(Action{CharacterCastCompletedEvent}, Scope?, NoireSubscriptionOptions{CharacterCastCompletedEvent}?)"/>
    public NoireSubscriptionToken OnCastCompletedAsync(Func<CharacterCastCompletedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCastCompletedEvent>? options = null)
        => Scoped(CharacterAspect.Cast, null, handler, scope, options, nameof(OnCastCompleted));

    /// <summary>
    /// Subscribes to cast interruptions. Interrupts are inferred from polling — a cast that vanishes well before
    /// its total cast time is treated as interrupted rather than completed.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCastInterrupted(Action<CharacterCastInterruptedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCastInterruptedEvent>? options = null)
        => Scoped(CharacterAspect.Cast, handler, null, scope, options, nameof(OnCastInterrupted));

    /// <inheritdoc cref="OnCastInterrupted(Action{CharacterCastInterruptedEvent}, Scope?, NoireSubscriptionOptions{CharacterCastInterruptedEvent}?)"/>
    public NoireSubscriptionToken OnCastInterruptedAsync(Func<CharacterCastInterruptedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCastInterruptedEvent>? options = null)
        => Scoped(CharacterAspect.Cast, null, handler, scope, options, nameof(OnCastInterrupted));

    #endregion

    #region Combat, targets, targetability

    /// <summary>
    /// Subscribes to combat entries.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCombatEntered(Action<CharacterCombatEnteredEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCombatEnteredEvent>? options = null)
        => Scoped(CharacterAspect.Combat, handler, null, scope, options, nameof(OnCombatEntered));

    /// <inheritdoc cref="OnCombatEntered(Action{CharacterCombatEnteredEvent}, Scope?, NoireSubscriptionOptions{CharacterCombatEnteredEvent}?)"/>
    public NoireSubscriptionToken OnCombatEnteredAsync(Func<CharacterCombatEnteredEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCombatEnteredEvent>? options = null)
        => Scoped(CharacterAspect.Combat, null, handler, scope, options, nameof(OnCombatEntered));

    /// <summary>
    /// Subscribes to combat exits.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnCombatLeft(Action<CharacterCombatLeftEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCombatLeftEvent>? options = null)
        => Scoped(CharacterAspect.Combat, handler, null, scope, options, nameof(OnCombatLeft));

    /// <inheritdoc cref="OnCombatLeft(Action{CharacterCombatLeftEvent}, Scope?, NoireSubscriptionOptions{CharacterCombatLeftEvent}?)"/>
    public NoireSubscriptionToken OnCombatLeftAsync(Func<CharacterCombatLeftEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterCombatLeftEvent>? options = null)
        => Scoped(CharacterAspect.Combat, null, handler, scope, options, nameof(OnCombatLeft));

    /// <summary>
    /// Subscribes to a character's own target changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnTargetChanged(Action<CharacterTargetChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterTargetChangedEvent>? options = null)
        => Scoped(CharacterAspect.Target, handler, null, scope, options, nameof(OnTargetChanged));

    /// <inheritdoc cref="OnTargetChanged(Action{CharacterTargetChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterTargetChangedEvent}?)"/>
    public NoireSubscriptionToken OnTargetChangedAsync(Func<CharacterTargetChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterTargetChangedEvent>? options = null)
        => Scoped(CharacterAspect.Target, null, handler, scope, options, nameof(OnTargetChanged));

    /// <summary>
    /// Subscribes to targetability changes.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnTargetableChanged(Action<CharacterTargetableChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterTargetableChangedEvent>? options = null)
        => Scoped(CharacterAspect.Targetable, handler, null, scope, options, nameof(OnTargetableChanged));

    /// <inheritdoc cref="OnTargetableChanged(Action{CharacterTargetableChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterTargetableChangedEvent}?)"/>
    public NoireSubscriptionToken OnTargetableChangedAsync(Func<CharacterTargetableChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterTargetableChangedEvent>? options = null)
        => Scoped(CharacterAspect.Targetable, null, handler, scope, options, nameof(OnTargetableChanged));

    #endregion

    #region Modes & emotes

    /// <summary>
    /// Subscribes to character mode changes (mounts, crafting stance, looping emotes, …).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnModeChanged(Action<CharacterModeChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterModeChangedEvent>? options = null)
        => Scoped(CharacterAspect.Mode, handler, null, scope, options, nameof(OnModeChanged));

    /// <inheritdoc cref="OnModeChanged(Action{CharacterModeChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterModeChangedEvent}?)"/>
    public NoireSubscriptionToken OnModeChangedAsync(Func<CharacterModeChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterModeChangedEvent>? options = null)
        => Scoped(CharacterAspect.Mode, null, handler, scope, options, nameof(OnModeChanged));

    /// <summary>
    /// Subscribes to looping-emote starts (mode polling). For exact emote ids of any emote — one-shot
    /// included — use <see cref="OnEmotePlayed"/>.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnEmoteLoopStarted(Action<CharacterEmoteLoopStartedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterEmoteLoopStartedEvent>? options = null)
        => Scoped(CharacterAspect.Mode, handler, null, scope, options, nameof(OnEmoteLoopStarted));

    /// <inheritdoc cref="OnEmoteLoopStarted(Action{CharacterEmoteLoopStartedEvent}, Scope?, NoireSubscriptionOptions{CharacterEmoteLoopStartedEvent}?)"/>
    public NoireSubscriptionToken OnEmoteLoopStartedAsync(Func<CharacterEmoteLoopStartedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterEmoteLoopStartedEvent>? options = null)
        => Scoped(CharacterAspect.Mode, null, handler, scope, options, nameof(OnEmoteLoopStarted));

    /// <summary>
    /// Subscribes to looping-emote ends.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnEmoteLoopEnded(Action<CharacterEmoteLoopEndedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterEmoteLoopEndedEvent>? options = null)
        => Scoped(CharacterAspect.Mode, handler, null, scope, options, nameof(OnEmoteLoopEnded));

    /// <inheritdoc cref="OnEmoteLoopEnded(Action{CharacterEmoteLoopEndedEvent}, Scope?, NoireSubscriptionOptions{CharacterEmoteLoopEndedEvent}?)"/>
    public NoireSubscriptionToken OnEmoteLoopEndedAsync(Func<CharacterEmoteLoopEndedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterEmoteLoopEndedEvent>? options = null)
        => Scoped(CharacterAspect.Mode, null, handler, scope, options, nameof(OnEmoteLoopEnded));

    /// <summary>
    /// Subscribes to emote plays — one-shot emotes, looping emotes and cposes alike, with the exact emote id,
    /// for any character. Read by polling the character's emote controller; one-shot emotes have no end signal
    /// (they are fired animations, not states).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnEmotePlayed(Action<CharacterEmotePlayedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterEmotePlayedEvent>? options = null)
        => Scoped(CharacterAspect.Emote, handler, null, scope, options, nameof(OnEmotePlayed));

    /// <inheritdoc cref="OnEmotePlayed(Action{CharacterEmotePlayedEvent}, Scope?, NoireSubscriptionOptions{CharacterEmotePlayedEvent}?)"/>
    public NoireSubscriptionToken OnEmotePlayedAsync(Func<CharacterEmotePlayedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterEmotePlayedEvent>? options = null)
        => Scoped(CharacterAspect.Emote, null, handler, scope, options, nameof(OnEmotePlayed));

    #endregion

    #region Online status, job, level, identity

    /// <summary>
    /// Subscribes to online-status changes (AFK, busy, looking for party, …).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnOnlineStatusChanged(Action<CharacterOnlineStatusChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterOnlineStatusChangedEvent>? options = null)
        => Scoped(CharacterAspect.OnlineStatus, handler, null, scope, options, nameof(OnOnlineStatusChanged));

    /// <inheritdoc cref="OnOnlineStatusChanged(Action{CharacterOnlineStatusChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterOnlineStatusChangedEvent}?)"/>
    public NoireSubscriptionToken OnOnlineStatusChangedAsync(Func<CharacterOnlineStatusChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterOnlineStatusChangedEvent>? options = null)
        => Scoped(CharacterAspect.OnlineStatus, null, handler, scope, options, nameof(OnOnlineStatusChanged));

    /// <summary>
    /// Subscribes to class/job changes. For the local player, the Session source also fires the native-backed
    /// <see cref="LocalClassJobChangedEvent"/> — use whichever fits your code shape.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnJobChanged(Action<CharacterJobChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterJobChangedEvent>? options = null)
        => Scoped(CharacterAspect.JobLevel, handler, null, scope, options, nameof(OnJobChanged));

    /// <inheritdoc cref="OnJobChanged(Action{CharacterJobChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterJobChangedEvent}?)"/>
    public NoireSubscriptionToken OnJobChangedAsync(Func<CharacterJobChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterJobChangedEvent>? options = null)
        => Scoped(CharacterAspect.JobLevel, null, handler, scope, options, nameof(OnJobChanged));

    /// <summary>
    /// Subscribes to level changes. For the local player, the Session source also fires the native-backed
    /// <see cref="LocalLevelChangedEvent"/>.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnLevelChanged(Action<CharacterLevelChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterLevelChangedEvent>? options = null)
        => Scoped(CharacterAspect.JobLevel, handler, null, scope, options, nameof(OnLevelChanged));

    /// <inheritdoc cref="OnLevelChanged(Action{CharacterLevelChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterLevelChangedEvent}?)"/>
    public NoireSubscriptionToken OnLevelChangedAsync(Func<CharacterLevelChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterLevelChangedEvent>? options = null)
        => Scoped(CharacterAspect.JobLevel, null, handler, scope, options, nameof(OnLevelChanged));

    /// <summary>
    /// Subscribes to identity changes on the same entity slot (name, home world, content id) — usually a sign
    /// the game reused the entity id for a different character.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="scope">Who to watch; null = <see cref="Scope.LocalPlayer"/>.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnIdentityChanged(Action<CharacterIdentityChangedEvent> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterIdentityChangedEvent>? options = null)
        => Scoped(CharacterAspect.Identity, handler, null, scope, options, nameof(OnIdentityChanged));

    /// <inheritdoc cref="OnIdentityChanged(Action{CharacterIdentityChangedEvent}, Scope?, NoireSubscriptionOptions{CharacterIdentityChangedEvent}?)"/>
    public NoireSubscriptionToken OnIdentityChangedAsync(Func<CharacterIdentityChangedEvent, Task> handler, Scope? scope = null, NoireSubscriptionOptions<CharacterIdentityChangedEvent>? options = null)
        => Scoped(CharacterAspect.Identity, null, handler, scope, options, nameof(OnIdentityChanged));

    #endregion

    #region Value watchers

    /// <summary>
    /// Diffs any selected value per scoped subject: the selector runs against fresh snapshots of every subject
    /// in scope each tick, and the callback fires per subject when its value changes. The first sample per
    /// subject seeds silently.<br/>
    /// This is independent of the interest-mask machinery — the cost (snapshot per subject per tick) is
    /// carried by this watcher alone.
    /// </summary>
    /// <typeparam name="T">The selected value type.</typeparam>
    /// <param name="scope">Who to watch.</param>
    /// <param name="selector">Selects the diffed value from a subject snapshot.</param>
    /// <param name="onChanged">Invoked with (subject, previous, current) when a subject's value changes.</param>
    /// <param name="interval">The sampling interval; null = every tick.</param>
    /// <param name="comparer">An optional equality comparer.</param>
    /// <param name="owner">An optional owner for bulk removal.</param>
    /// <returns>A token that stops the watcher when disposed.</returns>
    public NoireSubscriptionToken WatchValue<T>(
        Scope scope,
        Func<CharacterSnapshot, T> selector,
        Action<CharacterSnapshot, T?, T> onChanged,
        TimeSpan? interval = null,
        IEqualityComparer<T>? comparer = null,
        object? owner = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(onChanged);

        var equality = comparer ?? EqualityComparer<T>.Default;
        var values = new Dictionary<uint, T>();
        var watcher = Watcher;

        return watcher.WatchTick(
            () =>
            {
                var now = DateTimeOffset.UtcNow;
                var localId = CharacterCapture.LocalEntityId();
                var seen = new HashSet<uint>();

                foreach (var chara in CharacterCapture.EnumerateSubjects(scope.GetIterationClass()))
                {
                    var flags = CharacterCapture.ReadFlags(chara, localId);
                    var snapshot = CharacterCapture.Capture(chara, flags, now);

                    if (!scope.Matches(snapshot))
                        continue;

                    seen.Add(snapshot.EntityId);

                    var value = selector(snapshot);

                    if (!values.TryGetValue(snapshot.EntityId, out var previous))
                    {
                        values[snapshot.EntityId] = value;
                        continue;
                    }

                    if (equality.Equals(previous, value))
                        continue;

                    values[snapshot.EntityId] = value;
                    onChanged(snapshot, previous, value);
                }

                values.Keys.Where(id => !seen.Contains(id)).ToList().ForEach(id => values.Remove(id));
            },
            interval,
            owner,
            $"Characters.WatchValue<{typeof(T).Name}> [{scope}]");
    }

    #endregion

    #region Queries

    /// <summary>
    /// The local player's current snapshot, or null while logged out. Live read (framework thread only);
    /// never activates anything.
    /// </summary>
    public CharacterSnapshot? Local
    {
        get
        {
            NoireGameWatcher.EnsureFrameworkThread();

            var local = NoireService.ObjectTable.LocalPlayer;

            if (local == null)
                return null;

            var flags = CharacterCapture.ReadFlags(local, local.EntityId);
            return CharacterCapture.Capture(local, flags, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Snapshots every character currently matching a scope. Live read (framework thread only);
    /// never activates anything.
    /// </summary>
    /// <param name="scope">The scope to materialize.</param>
    /// <returns>The matching snapshots.</returns>
    public IReadOnlyList<CharacterSnapshot> Get(Scope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        NoireGameWatcher.EnsureFrameworkThread();

        var now = DateTimeOffset.UtcNow;
        var localId = CharacterCapture.LocalEntityId();
        var result = new List<CharacterSnapshot>();

        foreach (var chara in CharacterCapture.EnumerateSubjects(scope.GetIterationClass()))
        {
            var flags = CharacterCapture.ReadFlags(chara, localId);
            var snapshot = CharacterCapture.Capture(chara, flags, now);

            if (scope.Matches(snapshot))
                result.Add(snapshot);
        }

        return result;
    }

    /// <summary>
    /// Snapshots a character by entity id, or null when absent. Live read (framework thread only).
    /// </summary>
    /// <param name="entityId">The entity id to find.</param>
    /// <returns>The snapshot, or null.</returns>
    public CharacterSnapshot? Find(uint entityId)
    {
        NoireGameWatcher.EnsureFrameworkThread();

        var localId = CharacterCapture.LocalEntityId();

        foreach (var chara in CharacterCapture.EnumerateSubjects(Scope.IterationClass.AllCharacters))
        {
            if (chara.EntityId != entityId)
                continue;

            var flags = CharacterCapture.ReadFlags(chara, localId);
            return CharacterCapture.Capture(chara, flags, DateTimeOffset.UtcNow);
        }

        return null;
    }

    #endregion
}
