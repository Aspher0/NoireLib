using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// Scope-unified status effect diffing: gained/lost/stack-changed for any scoped character, plus
/// duration-threshold watchers. Statuses are kept separate from <see cref="CharacterSnapshot"/> so the hot
/// character snapshot stays small.<br/>
/// This is the one legitimately heavy path when watching wide scopes in crowds - dial its cadence down via
/// <see cref="GameWatcherOptions.PollCadences"/> when needed.
/// </summary>
internal sealed class StatusSource : GameWatcherSource
{
    internal sealed class ThresholdWatcherRegistration
    {
        public required Scope Scope { get; init; }
        public required uint StatusId { get; init; }
        public required float ThresholdSeconds { get; init; }
        public required Action<CharacterSnapshot, StatusSnapshot> Callback { get; init; }
        public HashSet<(uint EntityId, uint SourceId)> Fired { get; } = new();
    }

    private sealed class ScopeRegistration
    {
        public required Scope Scope { get; init; }
    }

    private readonly List<ScopeRegistration> scopeRegistrations = new();
    private readonly List<ThresholdWatcherRegistration> thresholdWatchers = new();
    private readonly Dictionary<uint, Dictionary<(uint StatusId, uint SourceId), StatusSnapshot>> baseline = new();

    private Scope[] unionScopes = Array.Empty<Scope>();
    private Scope.IterationClass unionIteration = Scope.IterationClass.LocalOnly;

    public StatusSource(NoireGameWatcher owner) : base(owner, SourceKind.Statuses) { }

    /// <summary>Registers a scope interest and returns the removal action.</summary>
    internal Action AddScopeInterest(Scope scope)
    {
        var registration = new ScopeRegistration { Scope = scope };

        lock (scopeRegistrations)
        {
            scopeRegistrations.Add(registration);
            RecomputeUnionsLocked();
        }

        return () =>
        {
            lock (scopeRegistrations)
            {
                scopeRegistrations.Remove(registration);
                RecomputeUnionsLocked();
            }
        };
    }

    /// <summary>Registers a duration-threshold watcher and returns the removal action.</summary>
    internal Action AddThresholdWatcher(ThresholdWatcherRegistration registration)
    {
        lock (scopeRegistrations)
        {
            thresholdWatchers.Add(registration);
            RecomputeUnionsLocked();
        }

        return () =>
        {
            lock (scopeRegistrations)
            {
                thresholdWatchers.Remove(registration);
                RecomputeUnionsLocked();
            }
        };
    }

    private void RecomputeUnionsLocked()
    {
        var scopes = scopeRegistrations.Select(r => r.Scope)
            .Concat(thresholdWatchers.Select(w => w.Scope))
            .Distinct()
            .ToArray();

        unionScopes = scopes;

        var iteration = Scope.IterationClass.LocalOnly;

        foreach (var scope in scopes)
        {
            var cls = scope.GetIterationClass();

            if (cls > iteration)
                iteration = cls;
        }

        unionIteration = iteration;
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        baseline.Clear();
        SeedBaseline();
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        baseline.Clear();

        lock (scopeRegistrations)
        {
            foreach (var watcher in thresholdWatchers)
                watcher.Fired.Clear();
        }
    }

    private void SeedBaseline()
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        var now = DateTimeOffset.UtcNow;
        var localId = CharacterCapture.LocalEntityId();

        foreach (var chara in CharacterCapture.EnumerateSubjects(unionIteration))
        {
            var flags = CharacterCapture.ReadFlags(chara, localId);

            if (!PreMatchesAny(chara, flags))
                continue;

            if (chara is not IBattleChara battleChara)
                continue;

            baseline[chara.EntityId] = ReadStatuses(battleChara, now);
        }
    }

    private bool PreMatchesAny(ICharacter chara, SubjectFlags flags)
    {
        var scopes = unionScopes;

        if (scopes.Length == 0)
            return false;

        var probe = CharacterCapture.BuildProbe(chara, flags);

        foreach (var scope in scopes)
        {
            if (scope.PreMatches(in probe))
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        if (!NoireService.ClientState.IsLoggedIn)
        {
            baseline.Clear();
            return;
        }

        if (unionScopes.Length == 0)
        {
            if (baseline.Count > 0)
                baseline.Clear();

            return;
        }

        var localId = CharacterCapture.LocalEntityId();
        var survivors = new HashSet<uint>();

        ThresholdWatcherRegistration[] thresholds;

        lock (scopeRegistrations)
            thresholds = thresholdWatchers.Count == 0 ? Array.Empty<ThresholdWatcherRegistration>() : thresholdWatchers.ToArray();

        foreach (var chara in CharacterCapture.EnumerateSubjects(unionIteration))
        {
            var flags = CharacterCapture.ReadFlags(chara, localId);

            if (!PreMatchesAny(chara, flags))
                continue;

            if (chara is not IBattleChara battleChara)
                continue;

            var entityId = chara.EntityId;
            survivors.Add(entityId);

            var current = ReadStatuses(battleChara, now);

            if (!baseline.TryGetValue(entityId, out var previous))
            {
                // First sight of this subject: seed without events.
                baseline[entityId] = current;
                continue;
            }

            CharacterSnapshot? ownerSnapshot = null;

            CharacterSnapshot OwnerSnapshot()
                => ownerSnapshot ??= CharacterCapture.Capture(chara, flags, now);

            foreach (var (key, status) in current)
            {
                if (!previous.TryGetValue(key, out var prevStatus))
                {
                    Owner.DispatchEvent(new StatusGainedEvent(OwnerSnapshot(), status));
                }
                else if (prevStatus.Param != status.Param)
                {
                    Owner.DispatchEvent(new StatusStackChangedEvent(OwnerSnapshot(), prevStatus, status));
                }
            }

            foreach (var (key, prevStatus) in previous)
            {
                if (!current.ContainsKey(key))
                {
                    Owner.DispatchEvent(new StatusLostEvent(OwnerSnapshot(), prevStatus));

                    foreach (var watcher in thresholds)
                        watcher.Fired.Remove((entityId, key.SourceId));
                }
            }

            if (thresholds.Length > 0)
                EvaluateThresholds(thresholds, chara, flags, current, OwnerSnapshot);

            baseline[entityId] = current;
        }

        // Forget despawned subjects (statuses ending with a despawn are not observable - honest limit).
        List<uint>? gone = null;

        foreach (var entityId in baseline.Keys)
        {
            if (!survivors.Contains(entityId))
                (gone ??= new List<uint>()).Add(entityId);
        }

        if (gone != null)
        {
            foreach (var entityId in gone)
                baseline.Remove(entityId);
        }
    }

    private void EvaluateThresholds(
        ThresholdWatcherRegistration[] thresholds,
        ICharacter chara,
        SubjectFlags flags,
        Dictionary<(uint StatusId, uint SourceId), StatusSnapshot> current,
        Func<CharacterSnapshot> ownerSnapshot)
    {
        foreach (var watcher in thresholds)
        {
            foreach (var (key, status) in current)
            {
                if (key.StatusId != watcher.StatusId)
                    continue;

                var fireKey = (chara.EntityId, key.SourceId);

                if (status.RemainingTime <= 0)
                    continue;

                if (status.RemainingTime > watcher.ThresholdSeconds)
                {
                    // Re-arm when the duration went back above the threshold (refresh).
                    watcher.Fired.Remove(fireKey);
                    continue;
                }

                if (watcher.Fired.Contains(fireKey))
                    continue;

                var snapshot = ownerSnapshot();

                if (!watcher.Scope.Matches(snapshot))
                    continue;

                watcher.Fired.Add(fireKey);

                try
                {
                    watcher.Callback(snapshot, status);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(Owner, ex, "A status duration-threshold callback threw.");
                }
            }
        }
    }

    /// <summary>Reads the current statuses of a character, for facade queries.</summary>
    internal static Dictionary<(uint StatusId, uint SourceId), StatusSnapshot> ReadStatuses(IBattleChara battleChara, DateTimeOffset now)
    {
        var result = new Dictionary<(uint, uint), StatusSnapshot>();

        foreach (var status in battleChara.StatusList)
        {
            if (status == null || status.StatusId == 0)
                continue;

            result[(status.StatusId, status.SourceId)] = new StatusSnapshot
            {
                StatusId = status.StatusId,
                Param = status.Param,
                RemainingTime = status.RemainingTime,
                SourceEntityId = status.SourceId,
                OwnerEntityId = battleChara.EntityId,
                CapturedAt = now,
            };
        }

        return result;
    }
}
