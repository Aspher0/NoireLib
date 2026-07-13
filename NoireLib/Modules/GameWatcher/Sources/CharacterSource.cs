using Dalamud.Game.ClientState.Conditions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameWatcher;

/// <summary>
/// The interest-masked polling source over every character in the object table.<br/>
/// Each scoped subscription contributes its aspect bits and its scope; the source keeps the union mask and
/// the union iteration class (recomputed on subscribe/unsubscribe, not per tick). Per tick it visits only
/// subjects matched by the union scope and compares only fields under the union mask — compare first,
/// materialize second: a new snapshot is allocated only when something changed.
/// </summary>
internal sealed class CharacterSource : GameWatcherSource
{
    private sealed class InterestRegistration
    {
        public required CharacterAspect Aspect { get; init; }
        public required Scope Scope { get; init; }
    }

    private readonly List<InterestRegistration> interests = new();
    private readonly Dictionary<uint, CharacterSnapshot> baseline = new();

    private CharacterAspect unionMask = CharacterAspect.None;
    private Scope.IterationClass unionIteration = Scope.IterationClass.LocalOnly;
    private Scope[] unionScopes = Array.Empty<Scope>();

    public CharacterSource(NoireGameWatcher owner) : base(owner, SourceKind.Characters) { }

    /// <summary>The current union interest mask, for diagnostics.</summary>
    internal CharacterAspect UnionMask => unionMask;

    /// <summary>The current union iteration class, for diagnostics.</summary>
    internal Scope.IterationClass UnionIteration => unionIteration;

    /// <summary>
    /// Registers a scoped aspect interest and returns its removal handle.
    /// Called by facade helpers alongside the registry subscription.
    /// </summary>
    internal IDisposable AddScopedInterest(CharacterAspect aspect, Scope scope)
    {
        var registration = new InterestRegistration { Aspect = aspect, Scope = scope };

        lock (interests)
        {
            interests.Add(registration);
            RecomputeUnionsLocked();
        }

        return new InterestHandle(this, registration);
    }

    private sealed class InterestHandle : IDisposable
    {
        private CharacterSource? source;
        private readonly InterestRegistration registration;

        public InterestHandle(CharacterSource source, InterestRegistration registration)
        {
            this.source = source;
            this.registration = registration;
        }

        public void Dispose()
        {
            var s = source;
            source = null;

            if (s == null)
                return;

            lock (s.interests)
            {
                s.interests.Remove(registration);
                s.RecomputeUnionsLocked();
            }
        }
    }

    private void RecomputeUnionsLocked()
    {
        var mask = CharacterAspect.None;
        var iteration = Scope.IterationClass.LocalOnly;

        foreach (var registration in interests)
        {
            mask |= registration.Aspect;

            var cls = registration.Scope.GetIterationClass();

            if (cls > iteration)
                iteration = cls;
        }

        unionMask = mask;
        unionIteration = iteration;
        unionScopes = interests.Select(i => i.Scope).Distinct().ToArray();
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        // Baseline seeding: subjects already present seed the baseline without firing spawn events —
        // subscribers observe changes from now on, not a replay of the present.
        baseline.Clear();
        SeedBaseline();
    }

    /// <inheritdoc/>
    protected override void OnDeactivate()
    {
        baseline.Clear();
    }

    private void SeedBaseline()
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        var now = DateTimeOffset.UtcNow;
        var localId = CharacterCapture.LocalEntityId();
        var scopes = unionScopes;
        var iteration = unionIteration;

        foreach (var chara in CharacterCapture.EnumerateSubjects(iteration))
        {
            var flags = CharacterCapture.ReadFlags(chara, localId);

            if (!PreMatchesAny(scopes, chara, flags))
                continue;

            baseline[chara.EntityId] = CharacterCapture.Capture(chara, flags, now);
        }
    }

    private static bool PreMatchesAny(Scope[] scopes, Dalamud.Game.ClientState.Objects.Types.ICharacter chara, SubjectFlags flags)
    {
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
            // While logged out, subject-based sources idle; the emptied table must not fire a despawn storm
            // beyond the zone-change marking below.
            if (baseline.Count > 0)
                FlushDespawns(now, duringZoneChange: true, survivors: null);

            return;
        }

        var mask = unionMask;
        var scopes = unionScopes;

        if (mask == CharacterAspect.None || scopes.Length == 0)
        {
            if (baseline.Count > 0)
                baseline.Clear();

            return;
        }

        var duringZoneChange = NoireService.Condition[ConditionFlag.BetweenAreas] || NoireService.Condition[ConditionFlag.BetweenAreas51];
        var localId = CharacterCapture.LocalEntityId();

        HashSet<uint>? survivors = baseline.Count > 0 ? new HashSet<uint>() : null;

        foreach (var chara in CharacterCapture.EnumerateSubjects(unionIteration))
        {
            var flags = CharacterCapture.ReadFlags(chara, localId);

            if (!PreMatchesAny(scopes, chara, flags))
                continue;

            var entityId = chara.EntityId;
            survivors?.Add(entityId);

            if (!baseline.TryGetValue(entityId, out var prev))
            {
                var snapshot = CharacterCapture.Capture(chara, flags, now);
                baseline[entityId] = snapshot;

                if ((mask & CharacterAspect.Presence) != 0)
                    Owner.DispatchEvent(new CharacterSpawnedEvent(snapshot, duringZoneChange));

                continue;
            }

            // Compare first, materialize second.
            var prevFields = CharacterFieldSet.FromSnapshot(prev);
            var curFields = CharacterCapture.ReadFields(chara, flags);
            var changed = CharacterDiffEngine.ComputeChangedAspects(in prevFields, in curFields);

            // Flags feed scope matching and must stay current even when no masked aspect changed.
            var flagsChanged = prev.Flags != flags;

            var relevant = changed & mask;

            if (relevant == CharacterAspect.None)
            {
                if (flagsChanged)
                    baseline[entityId] = prev with { Flags = flags };

                continue;
            }

            var cur = CharacterCapture.Capture(chara, flags, now);
            baseline[entityId] = cur;

            EmitChangeEvents(prev, cur, relevant);
        }

        if (survivors != null)
            FlushDespawns(now, duringZoneChange, survivors);
    }

    private void FlushDespawns(DateTimeOffset now, bool duringZoneChange, HashSet<uint>? survivors)
    {
        List<uint>? gone = null;

        foreach (var entityId in baseline.Keys)
        {
            if (survivors == null || !survivors.Contains(entityId))
                (gone ??= new List<uint>()).Add(entityId);
        }

        if (gone == null)
            return;

        var mask = unionMask;

        foreach (var entityId in gone)
        {
            var last = baseline[entityId];
            baseline.Remove(entityId);

            if ((mask & CharacterAspect.Presence) != 0)
                Owner.DispatchEvent(new CharacterDespawnedEvent(last, duringZoneChange));
        }
    }

    private void EmitChangeEvents(CharacterSnapshot prev, CharacterSnapshot cur, CharacterAspect relevant)
    {
        if ((relevant & CharacterAspect.Vitals) != 0)
        {
            if (prev.CurrentHp != cur.CurrentHp || prev.MaxHp != cur.MaxHp)
                Owner.DispatchEvent(new CharacterHpChangedEvent(prev, cur));

            if (prev.CurrentMp != cur.CurrentMp || prev.MaxMp != cur.MaxMp
                || prev.CurrentGp != cur.CurrentGp || prev.MaxGp != cur.MaxGp
                || prev.CurrentCp != cur.CurrentCp || prev.MaxCp != cur.MaxCp)
            {
                Owner.DispatchEvent(new CharacterMpChangedEvent(prev, cur));
            }
        }

        if ((relevant & CharacterAspect.Shield) != 0)
            Owner.DispatchEvent(new CharacterShieldChangedEvent(prev, cur));

        if ((relevant & CharacterAspect.Cast) != 0)
            EmitCastEvents(prev, cur);

        if ((relevant & CharacterAspect.Combat) != 0)
        {
            if (cur.IsInCombat)
                Owner.DispatchEvent(new CharacterCombatEnteredEvent(prev, cur));
            else
                Owner.DispatchEvent(new CharacterCombatLeftEvent(prev, cur));
        }

        if ((relevant & CharacterAspect.Target) != 0)
            Owner.DispatchEvent(new CharacterTargetChangedEvent(prev, cur));

        if ((relevant & CharacterAspect.Targetable) != 0)
            Owner.DispatchEvent(new CharacterTargetableChangedEvent(prev, cur));

        if ((relevant & CharacterAspect.Life) != 0)
        {
            if (cur.IsDead)
                Owner.DispatchEvent(new CharacterDiedEvent(prev, cur));
            else
                Owner.DispatchEvent(new CharacterRevivedEvent(prev, cur));
        }

        if ((relevant & CharacterAspect.Mode) != 0)
        {
            Owner.DispatchEvent(new CharacterModeChangedEvent(prev, cur));

            if (!prev.IsEmoting && cur.IsEmoting)
                Owner.DispatchEvent(new CharacterEmoteLoopStartedEvent(prev, cur));
            else if (prev.IsEmoting && !cur.IsEmoting)
                Owner.DispatchEvent(new CharacterEmoteLoopEndedEvent(prev, cur));
        }

        if ((relevant & CharacterAspect.Emote) != 0 && cur.EmoteId != 0)
            Owner.DispatchEvent(new CharacterEmotePlayedEvent(cur, cur.EmoteId));

        if ((relevant & CharacterAspect.OnlineStatus) != 0)
            Owner.DispatchEvent(new CharacterOnlineStatusChangedEvent(prev, cur));

        if ((relevant & CharacterAspect.JobLevel) != 0)
        {
            if (prev.ClassJobId != cur.ClassJobId)
                Owner.DispatchEvent(new CharacterJobChangedEvent(prev, cur));

            if (prev.Level != cur.Level)
                Owner.DispatchEvent(new CharacterLevelChangedEvent(prev, cur));
        }

        if ((relevant & CharacterAspect.Identity) != 0)
            Owner.DispatchEvent(new CharacterIdentityChangedEvent(prev, cur));
    }

    private void EmitCastEvents(CharacterSnapshot prev, CharacterSnapshot cur)
    {
        var wasCasting = prev.IsCasting;
        var isCasting = cur.IsCasting;

        if (!wasCasting && isCasting)
        {
            Owner.DispatchEvent(new CharacterCastStartedEvent(prev, cur));
            return;
        }

        if (wasCasting && !isCasting)
        {
            EmitCastEnd(prev, cur);
            return;
        }

        if (wasCasting && isCasting && prev.CastActionId != cur.CastActionId)
        {
            // A chained cast: the previous one ended and a new one began within the same tick.
            EmitCastEnd(prev, cur);
            Owner.DispatchEvent(new CharacterCastStartedEvent(prev, cur));
        }
    }

    private void EmitCastEnd(CharacterSnapshot prev, CharacterSnapshot cur)
    {
        // Interrupt vs. complete is inferred from how close the cast was to its total time when it vanished
        // (polling resolution is ±1 frame): a cast that disappears well before its total time was interrupted.
        var remaining = prev.TotalCastTime - prev.CurrentCastTime;

        if (prev.TotalCastTime > 0 && remaining > 0.25f)
            Owner.DispatchEvent(new CharacterCastInterruptedEvent(prev, cur, prev.CastActionId));
        else
            Owner.DispatchEvent(new CharacterCastCompletedEvent(prev, cur, prev.CastActionId));
    }

    /// <summary>
    /// The stored snapshot for an entity when the source is active and tracking it, for same-tick coherence
    /// lookups by other sources.
    /// </summary>
    internal CharacterSnapshot? TryGetTracked(uint entityId)
        => baseline.TryGetValue(entityId, out var snapshot) ? snapshot : null;
}
