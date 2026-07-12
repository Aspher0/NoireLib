using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.GameWatcher;

/// <summary>
/// The kind-agnostic object-table diff source: spawn/despawn/changed for every
/// <see cref="Dalamud.Game.ClientState.Objects.Enums.ObjectKind"/>, plus distance-threshold and
/// territory-bound region watchers with per-registration hysteresis.
/// </summary>
internal sealed class ObjectSource : GameWatcherSource
{
    internal sealed class DistanceWatcherRegistration
    {
        public required float Radius { get; init; }
        public required float LeaveRadius { get; init; }
        public required Action<ObjectSnapshot> OnEntered { get; init; }
        public required Action<ObjectSnapshot, RegionExitReason> OnLeft { get; init; }
        public Dalamud.Game.ClientState.Objects.Enums.ObjectKind[]? Kinds { get; init; }
        public Func<ObjectSnapshot, bool>? Predicate { get; init; }
        public HashSet<uint> Inside { get; } = new();
    }

    internal sealed class RegionWatcherRegistration
    {
        public required uint TerritoryId { get; init; }
        public required RegionShape Shape { get; init; }
        public required float HysteresisMargin { get; init; }
        public required Action<ObjectSnapshot> OnEntered { get; init; }
        public required Action<ObjectSnapshot, RegionExitReason> OnLeft { get; init; }
        public Dalamud.Game.ClientState.Objects.Enums.ObjectKind[]? Kinds { get; init; }
        public Func<ObjectSnapshot, bool>? Predicate { get; init; }
        public HashSet<uint> Inside { get; } = new();
    }

    private readonly Dictionary<uint, ObjectSnapshot> baseline = new();
    private readonly List<DistanceWatcherRegistration> distanceWatchers = new();
    private readonly List<RegionWatcherRegistration> regionWatchers = new();

    public ObjectSource(NoireGameWatcher owner) : base(owner, SourceKind.Objects) { }

    /// <summary>Adds a distance watcher and returns its removal action.</summary>
    internal Action AddDistanceWatcher(DistanceWatcherRegistration registration)
    {
        lock (distanceWatchers)
            distanceWatchers.Add(registration);

        return () =>
        {
            lock (distanceWatchers)
                distanceWatchers.Remove(registration);
        };
    }

    /// <summary>Adds a region watcher and returns its removal action.</summary>
    internal Action AddRegionWatcher(RegionWatcherRegistration registration)
    {
        lock (regionWatchers)
            regionWatchers.Add(registration);

        return () =>
        {
            lock (regionWatchers)
                regionWatchers.Remove(registration);
        };
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

        lock (distanceWatchers)
        {
            foreach (var watcher in distanceWatchers)
                watcher.Inside.Clear();
        }

        lock (regionWatchers)
        {
            foreach (var watcher in regionWatchers)
                watcher.Inside.Clear();
        }
    }

    private void SeedBaseline()
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        var now = DateTimeOffset.UtcNow;

        foreach (var obj in NoireService.ObjectTable)
        {
            if (obj.EntityId != 0)
                baseline[obj.EntityId] = CaptureObject(obj, now);
        }
    }

    /// <inheritdoc/>
    protected override void OnTick(DateTimeOffset now)
    {
        var duringZoneChange = NoireService.Condition[ConditionFlag.BetweenAreas] || NoireService.Condition[ConditionFlag.BetweenAreas51];

        if (!NoireService.ClientState.IsLoggedIn)
        {
            FlushDespawns(now, duringZoneChange: true, survivors: null);
            return;
        }

        var survivors = new HashSet<uint>();

        foreach (var obj in NoireService.ObjectTable)
        {
            var entityId = obj.EntityId;

            if (entityId == 0)
                continue;

            survivors.Add(entityId);

            if (!baseline.TryGetValue(entityId, out var prev))
            {
                var snapshot = CaptureObject(obj, now);
                baseline[entityId] = snapshot;
                Owner.DispatchEvent(new ObjectSpawnedEvent(snapshot, duringZoneChange));
                continue;
            }

            // Compare first (scalar fields only — position changes are covered by the watchers below).
            if (prev.DataId != obj.DataId
                || prev.OwnerId != obj.OwnerId
                || prev.IsTargetable != obj.IsTargetable
                || prev.IsDead != obj.IsDead)
            {
                var snapshot = CaptureObject(obj, now);
                baseline[entityId] = snapshot;
                Owner.DispatchEvent(new ObjectChangedEvent(prev, snapshot));
            }

            EvaluateWatchers(obj, now);
        }

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

        foreach (var entityId in gone)
        {
            var last = baseline[entityId];
            baseline.Remove(entityId);

            // A subject that despawns while inside fires the leave event with its last snapshot.
            NotifyDespawnToWatchers(entityId, last);

            Owner.DispatchEvent(new ObjectDespawnedEvent(last, duringZoneChange));
        }
    }

    private void EvaluateWatchers(IGameObject obj, DateTimeOffset now)
    {
        DistanceWatcherRegistration[] distanceSnapshot;
        RegionWatcherRegistration[] regionSnapshot;

        lock (distanceWatchers)
            distanceSnapshot = distanceWatchers.Count == 0 ? Array.Empty<DistanceWatcherRegistration>() : distanceWatchers.ToArray();

        lock (regionWatchers)
            regionSnapshot = regionWatchers.Count == 0 ? Array.Empty<RegionWatcherRegistration>() : regionWatchers.ToArray();

        if (distanceSnapshot.Length == 0 && regionSnapshot.Length == 0)
            return;

        var entityId = obj.EntityId;
        var position = obj.Position;

        if (distanceSnapshot.Length > 0)
        {
            var localPosition = NoireService.ObjectTable.LocalPlayer?.Position;

            foreach (var watcher in distanceSnapshot)
            {
                if (localPosition == null)
                    continue;

                if (entityId == CharacterCapture.LocalEntityId())
                    continue;

                if (!MatchesKinds(watcher.Kinds, obj))
                    continue;

                var distance = Vector3.Distance(localPosition.Value, position);
                var isInside = watcher.Inside.Contains(entityId);

                if (!isInside && distance <= watcher.Radius)
                {
                    var snapshot = CaptureObject(obj, now);

                    if (watcher.Predicate != null && !SafePredicate(watcher.Predicate, snapshot))
                        continue;

                    watcher.Inside.Add(entityId);
                    SafeInvoke(() => watcher.OnEntered(snapshot));
                }
                else if (isInside && distance > watcher.LeaveRadius)
                {
                    watcher.Inside.Remove(entityId);
                    var snapshot = CaptureObject(obj, now);
                    SafeInvoke(() => watcher.OnLeft(snapshot, RegionExitReason.Left));
                }
            }
        }

        if (regionSnapshot.Length > 0)
        {
            var territoryId = NoireService.ClientState.TerritoryType;

            foreach (var watcher in regionSnapshot)
            {
                if (watcher.TerritoryId != territoryId)
                {
                    watcher.Inside.Remove(entityId);
                    continue;
                }

                if (!MatchesKinds(watcher.Kinds, obj))
                    continue;

                var isInside = watcher.Inside.Contains(entityId);

                if (!isInside && watcher.Shape.Contains(position))
                {
                    var snapshot = CaptureObject(obj, now);

                    if (watcher.Predicate != null && !SafePredicate(watcher.Predicate, snapshot))
                        continue;

                    watcher.Inside.Add(entityId);
                    SafeInvoke(() => watcher.OnEntered(snapshot));
                }
                else if (isInside && !watcher.Shape.ContainsWithMargin(position, watcher.HysteresisMargin))
                {
                    watcher.Inside.Remove(entityId);
                    var snapshot = CaptureObject(obj, now);
                    SafeInvoke(() => watcher.OnLeft(snapshot, RegionExitReason.Left));
                }
            }
        }
    }

    private void NotifyDespawnToWatchers(uint entityId, ObjectSnapshot last)
    {
        lock (distanceWatchers)
        {
            foreach (var watcher in distanceWatchers)
            {
                if (watcher.Inside.Remove(entityId))
                    SafeInvoke(() => watcher.OnLeft(last, RegionExitReason.Despawned));
            }
        }

        lock (regionWatchers)
        {
            foreach (var watcher in regionWatchers)
            {
                if (watcher.Inside.Remove(entityId))
                    SafeInvoke(() => watcher.OnLeft(last, RegionExitReason.Despawned));
            }
        }
    }

    private static bool MatchesKinds(Dalamud.Game.ClientState.Objects.Enums.ObjectKind[]? kinds, IGameObject obj)
    {
        if (kinds == null || kinds.Length == 0)
            return true;

        foreach (var kind in kinds)
        {
            if (obj.ObjectKind == kind)
                return true;
        }

        return false;
    }

    private bool SafePredicate(Func<ObjectSnapshot, bool> predicate, ObjectSnapshot snapshot)
    {
        try
        {
            return predicate(snapshot);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, "An object watcher predicate threw; the subject is treated as not matching.");
            return false;
        }
    }

    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(Owner, ex, "An object watcher callback threw.");
        }
    }

    /// <summary>Captures an object snapshot. Also used by the Targets source and facade queries.</summary>
    internal static ObjectSnapshot CaptureObject(IGameObject obj, DateTimeOffset now) => new()
    {
        EntityId = obj.EntityId,
        GameObjectId = obj.GameObjectId,
        DataId = obj.DataId,
        OwnerId = obj.OwnerId,
        Name = obj.Name.TextValue,
        ObjectKind = obj.ObjectKind,
        SubKind = obj.SubKind,
        Position = obj.Position,
        Rotation = obj.Rotation,
        IsTargetable = obj.IsTargetable,
        IsDead = obj.IsDead,
        HitboxRadius = obj.HitboxRadius,
        CapturedAt = now,
    };
}
