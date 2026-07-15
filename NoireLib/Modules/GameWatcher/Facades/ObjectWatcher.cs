using Dalamud.Game.ClientState.Objects.Enums;
using NoireLib.Core.Subscriptions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NoireLib.GameWatcher;

/// <summary>
/// Kind-agnostic object facts: spawn/despawn/changed for anything in the object table (treasure, NPCs, event
/// objects, …), plus distance-threshold and territory-bound region watchers.<br/>
/// Characters are objects too - use <see cref="CharacterWatcher"/> for people, this facade for everything.
/// </summary>
public sealed class ObjectWatcher : GameWatcherFacade
{
    internal ObjectWatcher(NoireGameWatcher watcher) : base(watcher) { }

    private static Func<TEvent, bool> KindFilter<TEvent>(ObjectKind[]? kinds, Func<TEvent, ObjectSnapshot> select)
        => evt =>
        {
            if (kinds == null || kinds.Length == 0)
                return true;

            var snapshot = select(evt);

            foreach (var kind in kinds)
            {
                if (snapshot.ObjectKind == kind)
                    return true;
            }

            return false;
        };

    /// <summary>
    /// Subscribes to any object appearing in the object table.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="kinds">Optional object-kind restriction; null = all kinds.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnSpawned(Action<ObjectSpawnedEvent> handler, ObjectKind[]? kinds = null, NoireSubscriptionOptions<ObjectSpawnedEvent>? options = null)
        => On(handler, null, WithFilter(options, KindFilter<ObjectSpawnedEvent>(kinds, e => e.Current)), nameof(OnSpawned));

    /// <inheritdoc cref="OnSpawned(Action{ObjectSpawnedEvent}, ObjectKind[], NoireSubscriptionOptions{ObjectSpawnedEvent}?)"/>
    public NoireSubscriptionToken OnSpawnedAsync(Func<ObjectSpawnedEvent, Task> handler, ObjectKind[]? kinds = null, NoireSubscriptionOptions<ObjectSpawnedEvent>? options = null)
        => On(null, handler, WithFilter(options, KindFilter<ObjectSpawnedEvent>(kinds, e => e.Current)), nameof(OnSpawned));

    /// <summary>
    /// Subscribes to any object disappearing from the object table.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="kinds">Optional object-kind restriction; null = all kinds.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnDespawned(Action<ObjectDespawnedEvent> handler, ObjectKind[]? kinds = null, NoireSubscriptionOptions<ObjectDespawnedEvent>? options = null)
        => On(handler, null, WithFilter(options, KindFilter<ObjectDespawnedEvent>(kinds, e => e.Last)), nameof(OnDespawned));

    /// <inheritdoc cref="OnDespawned(Action{ObjectDespawnedEvent}, ObjectKind[], NoireSubscriptionOptions{ObjectDespawnedEvent}?)"/>
    public NoireSubscriptionToken OnDespawnedAsync(Func<ObjectDespawnedEvent, Task> handler, ObjectKind[]? kinds = null, NoireSubscriptionOptions<ObjectDespawnedEvent>? options = null)
        => On(null, handler, WithFilter(options, KindFilter<ObjectDespawnedEvent>(kinds, e => e.Last)), nameof(OnDespawned));

    /// <summary>
    /// Subscribes to object property changes (name, owner, targetability, death state - not position;
    /// use distance/region watchers for movement).
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="kinds">Optional object-kind restriction; null = all kinds.</param>
    /// <param name="options">Optional subscription settings.</param>
    /// <returns>A token that unsubscribes when disposed.</returns>
    public NoireSubscriptionToken OnChanged(Action<ObjectChangedEvent> handler, ObjectKind[]? kinds = null, NoireSubscriptionOptions<ObjectChangedEvent>? options = null)
        => On(handler, null, WithFilter(options, KindFilter<ObjectChangedEvent>(kinds, e => e.Current)), nameof(OnChanged));

    /// <inheritdoc cref="OnChanged(Action{ObjectChangedEvent}, ObjectKind[], NoireSubscriptionOptions{ObjectChangedEvent}?)"/>
    public NoireSubscriptionToken OnChangedAsync(Func<ObjectChangedEvent, Task> handler, ObjectKind[]? kinds = null, NoireSubscriptionOptions<ObjectChangedEvent>? options = null)
        => On(null, handler, WithFilter(options, KindFilter<ObjectChangedEvent>(kinds, e => e.Current)), nameof(OnChanged));

    /// <summary>
    /// Watches a distance threshold around the local player: <paramref name="onEntered"/> fires when a subject
    /// comes within <paramref name="radius"/> yalms, <paramref name="onLeft"/> when it moves back out (the
    /// leave threshold adds <see cref="GameWatcherOptions.DistanceHysteresis"/> so boundary oscillation does
    /// not flap) or despawns while inside (<see cref="RegionExitReason.Despawned"/>, last snapshot attached).
    /// </summary>
    /// <param name="radius">The enter threshold in yalms.</param>
    /// <param name="onEntered">Fires when a subject enters the radius.</param>
    /// <param name="onLeft">Fires when a subject leaves the radius or despawns inside it.</param>
    /// <param name="kinds">Optional object-kind restriction; null = all kinds.</param>
    /// <param name="predicate">An optional snapshot predicate evaluated when a subject would enter.</param>
    /// <param name="owner">An optional owner for bulk removal.</param>
    /// <param name="key">An optional key for keyed replacement.</param>
    /// <returns>A token that stops the watcher when disposed.</returns>
    public NoireSubscriptionToken WatchDistance(
        float radius,
        Action<ObjectSnapshot> onEntered,
        Action<ObjectSnapshot, RegionExitReason> onLeft,
        ObjectKind[]? kinds = null,
        Func<ObjectSnapshot, bool>? predicate = null,
        object? owner = null,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(onEntered);
        ArgumentNullException.ThrowIfNull(onLeft);

        if (radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");

        var source = Watcher.GetSource<ObjectSource>(SourceKind.Objects);

        var remove = source.AddDistanceWatcher(new ObjectSource.DistanceWatcherRegistration
        {
            Radius = radius,
            LeaveRadius = radius + Watcher.ActiveOptions.DistanceHysteresis,
            OnEntered = onEntered,
            OnLeft = onLeft,
            Kinds = kinds,
            Predicate = predicate,
        });

        return Watcher.RegisterExternalWatch($"WatchDistance({radius}y)", SourceKind.Objects, owner, key, remove);
    }

    /// <summary>
    /// Watches a territory-bound region: <paramref name="onEntered"/> fires when a subject enters the shape,
    /// <paramref name="onLeft"/> when it leaves (with hysteresis margin) or despawns inside. The watcher is
    /// inert while the local player is in a different territory.
    /// </summary>
    /// <param name="territoryId">The territory row id the region belongs to.</param>
    /// <param name="shape">The region shape (circle, box or predicate).</param>
    /// <param name="onEntered">Fires when a subject enters the region.</param>
    /// <param name="onLeft">Fires when a subject leaves the region or despawns inside it.</param>
    /// <param name="kinds">Optional object-kind restriction; null = all kinds.</param>
    /// <param name="predicate">An optional snapshot predicate evaluated when a subject would enter.</param>
    /// <param name="owner">An optional owner for bulk removal.</param>
    /// <param name="key">An optional key for keyed replacement.</param>
    /// <returns>A token that stops the watcher when disposed.</returns>
    public NoireSubscriptionToken WatchRegion(
        uint territoryId,
        RegionShape shape,
        Action<ObjectSnapshot> onEntered,
        Action<ObjectSnapshot, RegionExitReason> onLeft,
        ObjectKind[]? kinds = null,
        Func<ObjectSnapshot, bool>? predicate = null,
        object? owner = null,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(onEntered);
        ArgumentNullException.ThrowIfNull(onLeft);

        var source = Watcher.GetSource<ObjectSource>(SourceKind.Objects);

        var remove = source.AddRegionWatcher(new ObjectSource.RegionWatcherRegistration
        {
            TerritoryId = territoryId,
            Shape = shape,
            HysteresisMargin = Watcher.ActiveOptions.DistanceHysteresis,
            OnEntered = onEntered,
            OnLeft = onLeft,
            Kinds = kinds,
            Predicate = predicate,
        });

        return Watcher.RegisterExternalWatch($"WatchRegion(territory {territoryId})", SourceKind.Objects, owner, key, remove);
    }

    /// <summary>
    /// Snapshots every object currently in the object table, optionally restricted by kind.
    /// Live read (framework thread only); never activates anything.
    /// </summary>
    /// <param name="kinds">Optional object-kind restriction; null = all kinds.</param>
    /// <returns>The snapshots.</returns>
    public IReadOnlyList<ObjectSnapshot> GetAll(ObjectKind[]? kinds = null)
    {
        NoireGameWatcher.EnsureFrameworkThread();

        var now = DateTimeOffset.UtcNow;
        var result = new List<ObjectSnapshot>();

        foreach (var obj in NoireService.ObjectTable)
        {
            if (obj.EntityId == 0)
                continue;

            if (kinds is { Length: > 0 })
            {
                var matches = false;

                foreach (var kind in kinds)
                {
                    if (obj.ObjectKind == kind)
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                    continue;
            }

            result.Add(ObjectSource.CaptureObject(obj, now));
        }

        return result;
    }

    /// <summary>
    /// Snapshots an object by entity id, or null when absent. Live read (framework thread only).
    /// </summary>
    /// <param name="entityId">The entity id to find.</param>
    /// <returns>The snapshot, or null.</returns>
    public ObjectSnapshot? Find(uint entityId)
    {
        NoireGameWatcher.EnsureFrameworkThread();

        foreach (var obj in NoireService.ObjectTable)
        {
            if (obj.EntityId == entityId)
                return ObjectSource.CaptureObject(obj, DateTimeOffset.UtcNow);
        }

        return null;
    }
}
