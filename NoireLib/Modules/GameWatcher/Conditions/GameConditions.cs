using Dalamud.Game.ClientState.Conditions;
using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// The prebuilt condition vocabulary: composable, awaitable game-state predicates covering the common cases.
/// All conditions are level-triggered ("is it true now?") and read live game state — evaluate and await them
/// from the framework thread (waits do this automatically).<br/>
/// Named <c>GameConditions</c> (not <c>Conditions</c>) because <c>watcher.Conditions</c> is the raw
/// condition-flag facade.
/// </summary>
public static class GameConditions
{
    /// <summary>Logged in, not loading between areas, and not occupied — safe to act.</summary>
    public static GameCondition PlayerAvailable { get; } = GameCondition.FromPredicateInternal(
        () => NoireService.ClientState.IsLoggedIn
            && !NoireService.Condition.Any(
                ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51,
                ConditionFlag.Occupied, ConditionFlag.Occupied30, ConditionFlag.Occupied33,
                ConditionFlag.Occupied38, ConditionFlag.Occupied39, ConditionFlag.OccupiedInEvent,
                ConditionFlag.OccupiedInQuestEvent, ConditionFlag.OccupiedInCutSceneEvent,
                ConditionFlag.OccupiedSummoningBell),
        nameof(PlayerAvailable));

    /// <summary>Not loading between areas and not watching a cutscene.</summary>
    public static GameCondition ScreenReady { get; } = GameCondition.FromPredicateInternal(
        () => NoireService.ClientState.IsLoggedIn
            && !NoireService.Condition.Any(
                ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51,
                ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent),
        nameof(ScreenReady));

    /// <summary>The local player is in combat.</summary>
    public static GameCondition InCombat { get; } = GameCondition.FromPredicateInternal(
        () => NoireService.Condition[ConditionFlag.InCombat], nameof(InCombat));

    /// <summary>The local player is not in combat.</summary>
    public static GameCondition NotInCombat { get; } = InCombat.Not();

    /// <summary>The local player is not casting.</summary>
    public static GameCondition NotCasting { get; } = GameCondition.FromPredicateInternal(
        () => NoireService.ObjectTable.LocalPlayer is not { IsCasting: true }, nameof(NotCasting));

    /// <summary>The local player is mounted.</summary>
    public static GameCondition Mounted { get; } = GameCondition.FromPredicateInternal(
        () => NoireService.Condition.Any(ConditionFlag.Mounted, ConditionFlag.RidingPillion),
        nameof(Mounted));

    /// <summary>The local player is not mounted.</summary>
    public static GameCondition NotMounted { get; } = Mounted.Not();

    /// <summary>The local player is bound by a duty.</summary>
    public static GameCondition InDuty { get; } = GameCondition.FromPredicateInternal(
        () => NoireService.Condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95),
        nameof(InDuty));

    /// <summary>The local player is not bound by a duty.</summary>
    public static GameCondition NotInDuty { get; } = InDuty.Not();

    /// <summary>
    /// The current territory matches.
    /// </summary>
    /// <param name="territoryId">The territory row id.</param>
    /// <returns>The condition.</returns>
    public static GameCondition TerritoryIs(uint territoryId)
        => GameCondition.FromPredicateInternal(() => NoireService.ClientState.TerritoryType == territoryId, $"TerritoryIs({territoryId})");

    /// <summary>
    /// An addon exists, is visible and fully loaded.
    /// </summary>
    /// <param name="addonName">The addon's internal name (e.g. "Talk").</param>
    /// <returns>The condition.</returns>
    public static GameCondition AddonReady(string addonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonName);
        return GameCondition.FromPredicateInternal(() => AddonSource.ReadIsReady(addonName), $"AddonReady({addonName})");
    }

    /// <summary>
    /// An addon does not exist or is hidden.
    /// </summary>
    /// <param name="addonName">The addon's internal name.</param>
    /// <returns>The condition.</returns>
    public static GameCondition AddonGone(string addonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonName);
        return GameCondition.FromPredicateInternal(() => !AddonSource.ReadIsVisible(addonName), $"AddonGone({addonName})");
    }

    /// <summary>
    /// The party has exactly the given size (members only; 0 = solo).
    /// </summary>
    /// <param name="size">The exact size.</param>
    /// <returns>The condition.</returns>
    public static GameCondition PartySize(int size)
        => GameCondition.FromPredicateInternal(() => NoireService.PartyList.Length == size, $"PartySize({size})");

    /// <summary>
    /// The party has at least the given size.
    /// </summary>
    /// <param name="size">The minimum size.</param>
    /// <returns>The condition.</returns>
    public static GameCondition PartySizeAtLeast(int size)
        => GameCondition.FromPredicateInternal(() => NoireService.PartyList.Length >= size, $"PartySizeAtLeast({size})");

    /// <summary>
    /// A local action is ready: off cooldown, or at least one charge available (exact read).
    /// </summary>
    /// <param name="actionId">The action row id.</param>
    /// <returns>The condition.</returns>
    public static GameCondition ActionReady(uint actionId)
        => GameCondition.FromPredicateInternal(
            () => CooldownSource.ReadLocalCooldown(actionId, DateTimeOffset.UtcNow)?.IsReady ?? false,
            $"ActionReady({actionId})");

    /// <summary>The local player's global cooldown is ready.</summary>
    public static GameCondition GcdReady { get; } = GameCondition.FromPredicateInternal(
        () => CooldownSource.ReadGcdReady(out _), nameof(GcdReady));

    /// <summary>
    /// True while <b>any</b> character in the scope satisfies the snapshot predicate (live captures per
    /// evaluation — pair wide scopes with generous poll intent).
    /// </summary>
    /// <param name="scope">Who to inspect.</param>
    /// <param name="predicate">The snapshot predicate.</param>
    /// <returns>The condition.</returns>
    public static GameCondition AnyCharacter(Scope scope, Func<CharacterSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(predicate);

        return GameCondition.FromPredicateInternal(
            () =>
            {
                var now = DateTimeOffset.UtcNow;
                var localId = CharacterCapture.LocalEntityId();

                foreach (var chara in CharacterCapture.EnumerateSubjects(scope.GetIterationClass()))
                {
                    var flags = CharacterCapture.ReadFlags(chara, localId);
                    var snapshot = CharacterCapture.Capture(chara, flags, now);

                    if (scope.Matches(snapshot) && predicate(snapshot))
                        return true;
                }

                return false;
            },
            $"AnyCharacter[{scope}]");
    }

    /// <summary>
    /// True while <b>every</b> character in the scope satisfies the snapshot predicate (and at least one is in
    /// scope).
    /// </summary>
    /// <param name="scope">Who to inspect.</param>
    /// <param name="predicate">The snapshot predicate.</param>
    /// <returns>The condition.</returns>
    public static GameCondition AllCharacters(Scope scope, Func<CharacterSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(predicate);

        return GameCondition.FromPredicateInternal(
            () =>
            {
                var now = DateTimeOffset.UtcNow;
                var localId = CharacterCapture.LocalEntityId();
                var any = false;

                foreach (var chara in CharacterCapture.EnumerateSubjects(scope.GetIterationClass()))
                {
                    var flags = CharacterCapture.ReadFlags(chara, localId);
                    var snapshot = CharacterCapture.Capture(chara, flags, now);

                    if (!scope.Matches(snapshot))
                        continue;

                    any = true;

                    if (!predicate(snapshot))
                        return false;
                }

                return any;
            },
            $"AllCharacters[{scope}]");
    }

    /// <summary>
    /// Wraps an arbitrary predicate.
    /// </summary>
    /// <param name="predicate">The predicate, evaluated on the framework thread.</param>
    /// <param name="name">An optional readable name for logs.</param>
    /// <returns>The condition.</returns>
    public static GameCondition FromPredicate(Func<bool> predicate, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return GameCondition.FromPredicateInternal(predicate, name);
    }

    /// <summary>
    /// An event latch: becomes (and stays) true when a matching watcher event is dispatched — edge-triggered
    /// capture with level-triggered consumption. One-shot per instance; re-arm with
    /// <see cref="GameEventLatchCondition{TEvent}.Reset"/>.<br/>
    /// The subscription arms on the first <see cref="GameCondition.IsMet"/> evaluation, or immediately with
    /// <paramref name="armImmediately"/> (capture may then precede whatever work you gate on it).
    /// Works for library and custom (<see cref="NoireGameWatcher.Publish{TEvent}"/>) events alike —
    /// no EventBus involved.
    /// </summary>
    /// <typeparam name="TEvent">The event type to latch on.</typeparam>
    /// <param name="watcher">The watcher whose events feed the latch.</param>
    /// <param name="filter">An optional filter the event must satisfy.</param>
    /// <param name="armImmediately">True to subscribe now instead of on first evaluation.</param>
    /// <returns>The latch condition.</returns>
    public static GameEventLatchCondition<TEvent> FromEvent<TEvent>(
        NoireGameWatcher watcher,
        Func<TEvent, bool>? filter = null,
        bool armImmediately = false)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(watcher);
        return new GameEventLatchCondition<TEvent>(watcher, filter, armImmediately);
    }
}
