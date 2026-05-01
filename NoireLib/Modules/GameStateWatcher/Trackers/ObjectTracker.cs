using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Helpers;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks game object spawns and despawns by polling the object table each framework tick.<br/>
/// Provides snapshot-based query methods for finding and filtering tracked objects.
/// </summary>
public sealed class ObjectTracker : GameStateSubTracker
{
    private readonly Dictionary<uint, ObjectSnapshot> previousObjects = new();
    private readonly object snapshotLock = new();

    private readonly Dictionary<string, (float ThresholdSq, Func<ObjectSnapshot, bool>? Predicate, HashSet<uint> InsideEntities)> distanceWatchers = new(StringComparer.Ordinal);
    private readonly object distanceWatcherLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    internal ObjectTracker(NoireGameStateWatcher owner, bool active) : base(owner, active)
    {
    }

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        CaptureInitialSnapshot();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(ObjectTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        lock (snapshotLock)
            previousObjects.Clear();

        lock (distanceWatcherLock)
        {
            foreach (var watcher in distanceWatchers.Values)
                watcher.InsideEntities.Clear();
        }

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(ObjectTracker)} deactivated.");
    }

    /// <inheritdoc/>
    internal override void Update()
    {
        if (!NoireService.ClientState.IsLoggedIn)
            return;

        var currentIds = new HashSet<uint>();
        var objectTable = NoireService.ObjectTable;

        lock (snapshotLock)
        {
            foreach (var obj in objectTable)
            {
                if (obj == null || obj.EntityId == 0xE0000000)
                    continue;

                currentIds.Add(obj.EntityId);

                if (!previousObjects.ContainsKey(obj.EntityId))
                {
                    var snapshot = TakeSnapshot(obj);
                    previousObjects[obj.EntityId] = snapshot;

                    var evt = new ObjectSpawnedEvent(snapshot);
                    PublishEvent(OnObjectSpawned, evt);
                }
                else
                {
                    var previousSnapshot = previousObjects[obj.EntityId];
                    var currentSnapshot = TakeSnapshot(obj);

                    if (previousSnapshot != currentSnapshot)
                    {
                        previousObjects[obj.EntityId] = currentSnapshot;
                        PublishEvent(OnObjectChanged, new ObjectChangedEvent(previousSnapshot, currentSnapshot));
                    }
                }
            }

            var despawnedIds = previousObjects.Keys.Where(id => !currentIds.Contains(id)).ToArray();

            foreach (var id in despawnedIds)
            {
                if (previousObjects.Remove(id, out var snapshot))
                {
                    var evt = new ObjectDespawnedEvent(snapshot);
                    PublishEvent(OnObjectDespawned, evt);
                }
            }
        }

        CheckDistanceWatchers();
    }

    /// <summary>
    /// Gets a snapshot of all currently tracked objects.
    /// </summary>
    public IReadOnlyList<ObjectSnapshot> CurrentObjects
    {
        get
        {
            lock (snapshotLock)
                return previousObjects.Values.ToArray();
        }
    }

    /// <summary>
    /// Gets the number of currently tracked objects.
    /// </summary>
    public int ObjectCount
    {
        get
        {
            lock (snapshotLock)
                return previousObjects.Count;
        }
    }

    /// <summary>
    /// Gets the number of tracked player-character snapshots.
    /// </summary>
    public int PlayerCharacterCount => CountByKind(ObjectKind.Pc);

    /// <summary>
    /// Gets all tracked player-character snapshots.
    /// </summary>
    public ObjectSnapshot[] PlayerCharacters => FindByKind(ObjectKind.Pc);

    /// <summary>
    /// Raised when a game object appears in the object table.
    /// </summary>
    public event Action<ObjectSpawnedEvent>? OnObjectSpawned;

    /// <summary>
    /// Raised when a game object disappears from the object table.
    /// </summary>
    public event Action<ObjectDespawnedEvent>? OnObjectDespawned;

    /// <summary>
    /// Raised when a tracked game object changes while remaining present in the object table.
    /// </summary>
    public event Action<ObjectChangedEvent>? OnObjectChanged;

    /// <summary>
    /// Raised when a tracked object enters the registered distance threshold of the local player.
    /// </summary>
    public event Action<ObjectEnteredDistanceEvent>? OnObjectEnteredDistance;

    /// <summary>
    /// Raised when a tracked object leaves the registered distance threshold of the local player.
    /// </summary>
    public event Action<ObjectLeftDistanceEvent>? OnObjectLeftDistance;

    /// <summary>
    /// Registers a distance-threshold watcher that fires enter/leave events when objects cross the boundary.
    /// </summary>
    /// <param name="key">A unique key for this watcher registration.</param>
    /// <param name="threshold">The distance threshold in world units.</param>
    /// <param name="predicate">An optional predicate to filter which objects are watched.</param>
    /// <returns>A <see cref="DistanceWatcherRegistration"/> that removes the watcher when disposed.</returns>
    public DistanceWatcherRegistration RegisterDistanceWatcher(string key, float threshold, Func<ObjectSnapshot, bool>? predicate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (threshold < 0f)
            throw new ArgumentOutOfRangeException(nameof(threshold));

        lock (distanceWatcherLock)
            distanceWatchers[key] = (threshold * threshold, predicate, new HashSet<uint>());

        return new DistanceWatcherRegistration(key, threshold, predicate, () => UnregisterDistanceWatcher(key));
    }

    /// <summary>
    /// Removes a distance-threshold watcher by key.
    /// </summary>
    /// <param name="key">The key of the watcher registration to remove.</param>
    /// <returns><see langword="true"/> if a watcher was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnregisterDistanceWatcher(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (distanceWatcherLock)
            return distanceWatchers.Remove(key);
    }

    /// <summary>
    /// Removes all registered distance-threshold watchers.
    /// </summary>
    public void ClearDistanceWatchers()
    {
        lock (distanceWatcherLock)
            distanceWatchers.Clear();
    }

    /// <summary>
    /// Finds the nearest object matching the provided predicate based on the current snapshot.<br/>
    /// Uses the local player's position from <see cref="NoireService.ObjectTable"/>.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>The nearest matching object snapshot, or <see langword="null"/> if none match.</returns>
    public ObjectSnapshot? FindNearest(Func<ObjectSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return null;

        var playerPos = localPlayer.Position;

        lock (snapshotLock)
        {
            ObjectSnapshot? nearest = null;
            var nearestDistSq = float.MaxValue;

            foreach (var obj in previousObjects.Values)
            {
                if (!predicate(obj))
                    continue;

                var diff = obj.Position - playerPos;
                var distSq = diff.LengthSquared();

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = obj;
                }
            }

            return nearest;
        }
    }

    /// <summary>
    /// Returns all tracked objects matching the provided predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>An array of matching object snapshots.</returns>
    public ObjectSnapshot[] FindAll(Func<ObjectSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (snapshotLock)
            return previousObjects.Values.Where(predicate).ToArray();
    }

    /// <summary>
    /// Returns all tracked objects within the specified distance of the local player.
    /// </summary>
    /// <param name="maxDistance">The maximum world-space distance from the local player.</param>
    /// <param name="predicate">An optional additional filter predicate.</param>
    /// <returns>An array of matching object snapshots.</returns>
    public ObjectSnapshot[] FindWithinDistance(float maxDistance, Func<ObjectSnapshot, bool>? predicate = null)
    {
        if (maxDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(maxDistance));

        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return [];

        var maxDistanceSquared = maxDistance * maxDistance;
        var playerPosition = localPlayer.Position;

        lock (snapshotLock)
        {
            return previousObjects.Values
                .Where(snapshot => (predicate == null || predicate(snapshot)) && Vector3.DistanceSquared(snapshot.Position, playerPosition) <= maxDistanceSquared)
                .ToArray();
        }
    }

    /// <summary>
    /// Finds all tracked objects whose name contains the specified string (case-insensitive).
    /// </summary>
    /// <param name="name">The name substring to search for.</param>
    /// <returns>An array of matching object snapshots.</returns>
    public ObjectSnapshot[] FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (snapshotLock)
            return previousObjects.Values
                .Where(o => o.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    /// <summary>
    /// Finds all tracked objects with the specified data-sheet identifier.
    /// </summary>
    /// <param name="dataId">The data identifier to match.</param>
    /// <returns>An array of matching object snapshots.</returns>
    public ObjectSnapshot[] FindByDataId(uint dataId)
    {
        lock (snapshotLock)
            return previousObjects.Values.Where(o => o.BaseId == dataId).ToArray();
    }

    /// <summary>
    /// Finds all tracked objects of the specified kind.
    /// </summary>
    /// <param name="kind">The object kind to filter by.</param>
    /// <returns>An array of matching object snapshots.</returns>
    public ObjectSnapshot[] FindByKind(ObjectKind kind)
    {
        lock (snapshotLock)
            return previousObjects.Values.Where(o => o.ObjectKind == kind).ToArray();
    }

    /// <summary>
    /// Counts the number of tracked objects of the specified kind.
    /// </summary>
    /// <param name="kind">The object kind to count.</param>
    /// <returns>The number of matching tracked objects.</returns>
    public int CountByKind(ObjectKind kind)
    {
        lock (snapshotLock)
            return previousObjects.Values.Count(o => o.ObjectKind == kind);
    }

    /// <summary>
    /// Checks whether a tracked object with the specified entity identifier currently exists.
    /// </summary>
    /// <param name="entityId">The entity identifier to check.</param>
    /// <returns><see langword="true"/> if the object is currently tracked; otherwise, <see langword="false"/>.</returns>
    public bool IsObjectPresent(uint entityId)
    {
        lock (snapshotLock)
            return previousObjects.ContainsKey(entityId);
    }

    /// <summary>
    /// Attempts to retrieve a tracked object snapshot by entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier to look up.</param>
    /// <param name="snapshot">When this method returns <see langword="true"/>, contains the object snapshot.</param>
    /// <returns><see langword="true"/> if the object was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetObject(uint entityId, out ObjectSnapshot? snapshot)
    {
        lock (snapshotLock)
            return previousObjects.TryGetValue(entityId, out snapshot);
    }

    /// <summary>
    /// Retrieves all live characters currently present in the object table.
    /// </summary>
    /// <returns>An array of live character objects.</returns>
    public ICharacter[] GetCharacters() => NoireService.ObjectTable.OfType<ICharacter>().ToArray();

    /// <summary>
    /// Retrieves all live player characters currently present in the object table.
    /// </summary>
    /// <returns>An array of live player-character objects.</returns>
    public IPlayerCharacter[] GetPlayerCharacters() => NoireService.ObjectTable.PlayerObjects.OfType<IPlayerCharacter>().ToArray();

    /// <summary>
    /// Retrieves a live character by entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier to look up.</param>
    /// <returns>The live character, or <see langword="null"/> if none matched.</returns>
    public ICharacter? GetCharacter(uint entityId) => NoireService.ObjectTable.OfType<ICharacter>().FirstOrDefault(character => character.EntityId == entityId);

    /// <summary>
    /// Attempts to retrieve a live character by entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier to look up.</param>
    /// <param name="character">When this method returns <see langword="true"/>, contains the matching character.</param>
    /// <returns><see langword="true"/> if a matching character was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetCharacter(uint entityId, out ICharacter? character)
    {
        character = GetCharacter(entityId);
        return character != null;
    }

    /// <summary>
    /// Retrieves a live player character by content identifier.
    /// </summary>
    /// <param name="contentId">The content identifier to look up.</param>
    /// <returns>The live player character, or <see langword="null"/> if none matched.</returns>
    public IPlayerCharacter? GetPlayerCharacterByContentId(ulong contentId) => CharacterHelper.GetCharacterFromCID(contentId) as IPlayerCharacter;

    /// <summary>
    /// Retrieves all live characters whose names match the specified text.
    /// </summary>
    /// <param name="name">The text to match against character names.</param>
    /// <param name="exactMatch">Whether the name must match exactly instead of using a substring comparison.</param>
    /// <param name="playersOnly">Whether to restrict the search to player characters.</param>
    /// <returns>An array of matching live characters.</returns>
    public ICharacter[] GetCharactersByName(string name, bool exactMatch = false, bool playersOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return playersOnly
            ? GetPlayerCharacters().Where(character => NameMatches(character.Name.TextValue, name, exactMatch)).Cast<ICharacter>().ToArray()
            : GetCharacters().Where(character => NameMatches(character.Name.TextValue, name, exactMatch)).ToArray();
    }

    /// <summary>
    /// Retrieves the nearest live character matching the provided predicate.
    /// </summary>
    /// <param name="predicate">An optional filter predicate.</param>
    /// <param name="includeLocalPlayer">Whether the local player can be returned.</param>
    /// <returns>The nearest matching character, or <see langword="null"/> if none matched.</returns>
    public ICharacter? GetNearestCharacter(Func<ICharacter, bool>? predicate = null, bool includeLocalPlayer = false)
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return null;

        var playerAddress = localPlayer.Address;
        var playerPosition = localPlayer.Position;

        return GetCharacters()
            .Where(character => (includeLocalPlayer || character.Address != playerAddress) && (predicate == null || predicate(character)))
            .OrderBy(character => Vector3.DistanceSquared(character.Position, playerPosition))
            .FirstOrDefault();
    }

    /// <summary>
    /// Retrieves all live characters within the specified distance of the local player.
    /// </summary>
    /// <param name="maxDistance">The maximum world-space distance from the local player.</param>
    /// <param name="predicate">An optional additional filter predicate.</param>
    /// <param name="includeLocalPlayer">Whether the local player can be included in the result.</param>
    /// <returns>An array of matching live characters.</returns>
    public ICharacter[] GetNearbyCharacters(float maxDistance, Func<ICharacter, bool>? predicate = null, bool includeLocalPlayer = false)
    {
        if (maxDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(maxDistance));

        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return [];

        var maxDistanceSquared = maxDistance * maxDistance;
        var playerAddress = localPlayer.Address;
        var playerPosition = localPlayer.Position;

        return GetCharacters()
            .Where(character => (includeLocalPlayer || character.Address != playerAddress)
                && (predicate == null || predicate(character))
                && Vector3.DistanceSquared(character.Position, playerPosition) <= maxDistanceSquared)
            .ToArray();
    }

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when an object with the specified data identifier is present.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="baseId">The data identifier to wait for.</param>
    /// <returns>A predicate returning <see langword="true"/> when the object is present.</returns>
    public Func<bool> WaitForObject(uint baseId) => () =>
    {
        lock (snapshotLock)
            return previousObjects.Values.Any(o => o.BaseId == baseId);
    };

    private void CaptureInitialSnapshot()
    {
        lock (snapshotLock)
        {
            previousObjects.Clear();

            if (!NoireService.ClientState.IsLoggedIn)
                return;

            foreach (var obj in NoireService.ObjectTable)
            {
                if (obj == null || obj.EntityId == 0xE0000000)
                    continue;

                previousObjects[obj.EntityId] = TakeSnapshot(obj);
            }
        }
    }

    private static ObjectSnapshot TakeSnapshot(IGameObject obj)
    {
        return new ObjectSnapshot(
            EntityId: obj.EntityId,
            BaseId: obj.BaseId,
            Name: obj.Name.TextValue,
            ObjectKind: obj.ObjectKind,
            SubKind: obj.SubKind,
            Position: obj.Position);
    }

    private static bool NameMatches(string value, string search, bool exactMatch)
    {
        return exactMatch
            ? value.Equals(search, StringComparison.OrdinalIgnoreCase)
            : value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void CheckDistanceWatchers()
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        var playerPosition = localPlayer.Position;

        lock (distanceWatcherLock)
        {
            if (distanceWatchers.Count == 0)
                return;

            lock (snapshotLock)
            {
                foreach (var (key, watcher) in distanceWatchers)
                {
                    var currentInside = new HashSet<uint>();

                    foreach (var snapshot in previousObjects.Values)
                    {
                        if (watcher.Predicate != null && !watcher.Predicate(snapshot))
                            continue;

                        var distSq = Vector3.DistanceSquared(snapshot.Position, playerPosition);

                        if (distSq <= watcher.ThresholdSq)
                        {
                            currentInside.Add(snapshot.EntityId);

                            if (!watcher.InsideEntities.Contains(snapshot.EntityId))
                            {
                                var threshold = MathF.Sqrt(watcher.ThresholdSq);
                                PublishEvent(OnObjectEnteredDistance, new ObjectEnteredDistanceEvent(key, threshold, snapshot));
                            }
                        }
                    }

                    foreach (var entityId in watcher.InsideEntities)
                    {
                        if (!currentInside.Contains(entityId) && previousObjects.TryGetValue(entityId, out var leftSnapshot))
                        {
                            var threshold = MathF.Sqrt(watcher.ThresholdSq);
                            PublishEvent(OnObjectLeftDistance, new ObjectLeftDistanceEvent(key, threshold, leftSnapshot));
                        }
                    }

                    watcher.InsideEntities.Clear();
                    foreach (var id in currentInside)
                        watcher.InsideEntities.Add(id);
                }
            }
        }
    }
}
