using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// Tracks status effects on the local player.<br/>
/// Provides snapshot-based query methods for inspecting active status effects.
/// </summary>
public sealed class StatusEffectTracker : GameStateSubTracker
{
    private readonly Dictionary<uint, StatusEffectSnapshot> previousStatuses = new();
    private readonly Dictionary<uint, Dictionary<uint, StatusEffectSnapshot>> watchedEntityStatuses = new();
    private readonly object snapshotLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusEffectTracker"/> class.
    /// </summary>
    /// <param name="owner">The owning <see cref="NoireGameStateWatcher"/> module.</param>
    /// <param name="active">Whether the tracker should start in an active state.</param>
    internal StatusEffectTracker(NoireGameStateWatcher owner, bool active) : base(owner, active) { }

    /// <summary>
    /// Gets a snapshot of all currently tracked status effects on the local player.
    /// </summary>
    public IReadOnlyList<StatusEffectSnapshot> CurrentStatuses
    {
        get
        {
            lock (snapshotLock)
                return previousStatuses.Values.ToArray();
        }
    }

    /// <summary>
    /// Gets the currently watched entity identifiers used for entity-specific status tracking.
    /// </summary>
    public IReadOnlyList<uint> WatchedEntityIds
    {
        get
        {
            lock (snapshotLock)
                return watchedEntityStatuses.Keys.ToArray();
        }
    }

    /// <summary>
    /// Gets the number of currently tracked status effects.
    /// </summary>
    public int StatusCount
    {
        get
        {
            lock (snapshotLock)
                return previousStatuses.Count;
        }
    }

    /// <summary>
    /// Raised when a status effect is gained on the local player.
    /// </summary>
    public event Action<StatusEffectGainedEvent>? OnStatusGained;

    /// <summary>
    /// Raised when a status effect is lost from the local player.
    /// </summary>
    public event Action<StatusEffectLostEvent>? OnStatusLost;

    /// <summary>
    /// Raised when an existing status effect on the local player changes.
    /// </summary>
    public event Action<StatusEffectChangedEvent>? OnStatusChanged;

    /// <summary>
    /// Raised when a status effect is gained on a watched entity.
    /// </summary>
    public event Action<TrackedStatusEffectGainedEvent>? OnTrackedStatusGained;

    /// <summary>
    /// Raised when a status effect is lost from a watched entity.
    /// </summary>
    public event Action<TrackedStatusEffectLostEvent>? OnTrackedStatusLost;

    /// <summary>
    /// Raised when an existing status effect changes on a watched entity.
    /// </summary>
    public event Action<TrackedStatusEffectChangedEvent>? OnTrackedStatusChanged;

    /// <summary>
    /// Starts continuously tracking status effects for the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier to watch.</param>
    /// <returns><see langword="true"/> if the entity was newly added; otherwise, <see langword="false"/>.</returns>
    public bool WatchEntity(uint entityId)
    {
        if (entityId == 0)
            throw new ArgumentOutOfRangeException(nameof(entityId));

        lock (snapshotLock)
        {
            var isNew = !watchedEntityStatuses.ContainsKey(entityId);
            watchedEntityStatuses[entityId] = CaptureStatusesDictionary(Owner.Objects.GetCharacter(entityId) as IBattleChara);
            return isNew;
        }
    }

    /// <summary>
    /// Starts continuously tracking status effects for multiple entity identifiers.
    /// </summary>
    /// <param name="entityIds">The entity identifiers to watch.</param>
    /// <returns>The number of entity identifiers that were newly added.</returns>
    public int WatchEntities(IEnumerable<uint> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var added = 0;

        foreach (var entityId in entityIds)
        {
            if (WatchEntity(entityId))
                added++;
        }

        return added;
    }

    /// <summary>
    /// Stops continuously tracking status effects for the supplied entity identifier.
    /// </summary>
    /// <param name="entityId">The entity identifier to stop watching.</param>
    /// <returns><see langword="true"/> if the entity was removed; otherwise, <see langword="false"/>.</returns>
    public bool UnwatchEntity(uint entityId)
    {
        lock (snapshotLock)
            return watchedEntityStatuses.Remove(entityId);
    }

    /// <summary>
    /// Stops tracking every watched entity.
    /// </summary>
    public void ClearWatchedEntities()
    {
        lock (snapshotLock)
            watchedEntityStatuses.Clear();
    }

    /// <summary>
    /// Retrieves the last captured statuses for a watched entity.
    /// </summary>
    /// <param name="entityId">The watched entity identifier to inspect.</param>
    /// <returns>An array of last captured statuses, or an empty array if the entity is not being watched.</returns>
    public StatusEffectSnapshot[] GetTrackedStatuses(uint entityId)
    {
        lock (snapshotLock)
            return watchedEntityStatuses.TryGetValue(entityId, out var statuses) ? statuses.Values.ToArray() : [];
    }

    /// <summary>
    /// Checks whether the local player currently has a status effect with the given identifier.
    /// </summary>
    /// <param name="statusId">The status row identifier to check.</param>
    /// <returns><see langword="true"/> if the status effect is active; otherwise, <see langword="false"/>.</returns>
    public bool HasStatus(uint statusId)
    {
        lock (snapshotLock)
            return previousStatuses.ContainsKey(statusId);
    }

    /// <summary>
    /// Checks whether the local player has any of the specified status effects.
    /// </summary>
    /// <param name="statusIds">The status row identifiers to check.</param>
    /// <returns><see langword="true"/> if at least one of the status effects is active; otherwise, <see langword="false"/>.</returns>
    public bool HasAnyStatus(params uint[] statusIds)
    {
        lock (snapshotLock)
        {
            for (var i = 0; i < statusIds.Length; i++)
            {
                if (previousStatuses.ContainsKey(statusIds[i]))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Retrieves the snapshot of a specific status effect, or <see langword="null"/> if not active.
    /// </summary>
    /// <param name="statusId">The status row identifier to look up.</param>
    /// <returns>The status effect snapshot, or <see langword="null"/> if the status is not active.</returns>
    public StatusEffectSnapshot? GetStatus(uint statusId)
    {
        lock (snapshotLock)
            return previousStatuses.TryGetValue(statusId, out var snapshot) ? snapshot : null;
    }

    /// <summary>
    /// Gets the remaining time of a specific status effect, or 0 if the status is not active.
    /// </summary>
    /// <param name="statusId">The status row identifier to look up.</param>
    /// <returns>The remaining time in seconds, or 0 if the status is not active or is permanent.</returns>
    public float GetRemainingTime(uint statusId)
    {
        lock (snapshotLock)
            return previousStatuses.TryGetValue(statusId, out var snapshot) ? snapshot.RemainingTime : 0f;
    }

    /// <summary>
    /// Returns all active status effects applied by the specified source entity.
    /// </summary>
    /// <param name="sourceId">The entity identifier of the source.</param>
    /// <returns>An array of status effect snapshots from the specified source.</returns>
    public StatusEffectSnapshot[] GetAllFromSource(uint sourceId)
    {
        lock (snapshotLock)
            return previousStatuses.Values.Where(s => s.SourceId == sourceId).ToArray();
    }

    /// <summary>
    /// Retrieves all currently active status effects on the specified character.
    /// </summary>
    /// <param name="character">The character to inspect.</param>
    /// <returns>An array of status-effect snapshots for the specified character.</returns>
    public StatusEffectSnapshot[] GetStatuses(IBattleChara character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return CaptureStatuses(character);
    }

    /// <summary>
    /// Retrieves all currently active status effects on the specified character entity.
    /// </summary>
    /// <param name="entityId">The entity identifier of the character to inspect.</param>
    /// <returns>An array of status-effect snapshots for the specified character, or an empty array if not found.</returns>
    public StatusEffectSnapshot[] GetStatuses(uint entityId)
    {
        var character = Owner.Objects.GetCharacter(entityId) as IBattleChara;
        return character == null ? [] : CaptureStatuses(character);
    }

    /// <summary>
    /// Checks whether the specified character currently has the given status effect.
    /// </summary>
    /// <param name="character">The character to inspect.</param>
    /// <param name="statusId">The status row identifier to check.</param>
    /// <returns><see langword="true"/> if the status effect is active; otherwise, <see langword="false"/>.</returns>
    public bool HasStatus(IBattleChara character, uint statusId)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.StatusList.Any(status => status.StatusId == statusId);
    }

    /// <summary>
    /// Checks whether the specified character entity currently has the given status effect.
    /// </summary>
    /// <param name="entityId">The entity identifier of the character to inspect.</param>
    /// <param name="statusId">The status row identifier to check.</param>
    /// <returns><see langword="true"/> if the status effect is active; otherwise, <see langword="false"/>.</returns>
    public bool HasStatus(uint entityId, uint statusId)
    {
        var character = Owner.Objects.GetCharacter(entityId) as IBattleChara;
        return character != null && HasStatus(character, statusId);
    }

    /// <summary>
    /// Checks whether the specified character has any of the given status effects.
    /// </summary>
    /// <param name="character">The character to inspect.</param>
    /// <param name="statusIds">The status row identifiers to check.</param>
    /// <returns><see langword="true"/> if at least one status effect is active; otherwise, <see langword="false"/>.</returns>
    public bool HasAnyStatus(IBattleChara character, params uint[] statusIds)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(statusIds);

        for (var i = 0; i < statusIds.Length; i++)
        {
            if (HasStatus(character, statusIds[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Retrieves a specific status effect from the specified character, or <see langword="null"/> if not active.
    /// </summary>
    /// <param name="character">The character to inspect.</param>
    /// <param name="statusId">The status row identifier to look up.</param>
    /// <returns>The matching status-effect snapshot, or <see langword="null"/> if not active.</returns>
    public StatusEffectSnapshot? GetStatus(IBattleChara character, uint statusId)
    {
        ArgumentNullException.ThrowIfNull(character);
        return CaptureStatuses(character).FirstOrDefault(status => status.StatusId == statusId);
    }

    /// <summary>
    /// Retrieves a specific status effect from the specified character entity, or <see langword="null"/> if not active.
    /// </summary>
    /// <param name="entityId">The entity identifier of the character to inspect.</param>
    /// <param name="statusId">The status row identifier to look up.</param>
    /// <returns>The matching status-effect snapshot, or <see langword="null"/> if not active.</returns>
    public StatusEffectSnapshot? GetStatus(uint entityId, uint statusId)
    {
        var character = Owner.Objects.GetCharacter(entityId) as IBattleChara;
        return character == null ? null : GetStatus(character, statusId);
    }

    /// <summary>
    /// Retrieves all currently active status effects on the specified character that came from the specified source entity.
    /// </summary>
    /// <param name="character">The character to inspect.</param>
    /// <param name="sourceId">The source entity identifier to filter by.</param>
    /// <returns>An array of matching status-effect snapshots.</returns>
    public StatusEffectSnapshot[] GetAllFromSource(IBattleChara character, uint sourceId)
    {
        ArgumentNullException.ThrowIfNull(character);
        return CaptureStatuses(character).Where(status => status.SourceId == sourceId).ToArray();
    }

    /// <summary>
    /// Retrieves all live characters that currently have the specified status effect.
    /// </summary>
    /// <param name="statusId">The status row identifier to search for.</param>
    /// <returns>An array of live characters that currently have the status effect.</returns>
    public IBattleChara[] GetCharactersWithStatus(uint statusId) => Owner.Objects.GetCharacters().OfType<IBattleChara>().Where(character => HasStatus(character, statusId)).ToArray();

    /// <summary>
    /// Resolves a status-effect source entity into a live character, if available.
    /// </summary>
    /// <param name="sourceId">The source entity identifier to resolve.</param>
    /// <returns>The matching live character, or <see langword="null"/> if not found.</returns>
    public ICharacter? ResolveStatusSource(uint sourceId) => Owner.Objects.GetCharacter(sourceId);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the specified status effect is active.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="statusId">The status row identifier to wait for.</param>
    /// <returns>A predicate returning <see langword="true"/> when the status is active.</returns>
    public Func<bool> WaitForStatus(uint statusId) => () => HasStatus(statusId);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the specified status effect is no longer active.<br/>
    /// Useful as a wait condition for <see cref="NoireTaskQueue"/>.
    /// </summary>
    /// <param name="statusId">The status row identifier to wait for removal of.</param>
    /// <returns>A predicate returning <see langword="true"/> when the status is no longer active.</returns>
    public Func<bool> WaitForStatusRemoval(uint statusId) => () => !HasStatus(statusId);

    /// <summary>
    /// Returns a predicate that evaluates to <see langword="true"/> when the supplied watched entity has the specified status.
    /// </summary>
    /// <param name="entityId">The watched entity identifier.</param>
    /// <param name="statusId">The status row identifier to wait for.</param>
    /// <returns>A predicate returning <see langword="true"/> when the watched entity has the status.</returns>
    public Func<bool> WaitForTrackedStatus(uint entityId, uint statusId) => () => GetTrackedStatuses(entityId).Any(status => status.StatusId == statusId);

    /// <inheritdoc/>
    protected override void OnActivated()
    {
        CaptureInitialSnapshot();

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(StatusEffectTracker)} activated.");
    }

    /// <inheritdoc/>
    protected override void OnDeactivated()
    {
        lock (snapshotLock)
        {
            previousStatuses.Clear();
            watchedEntityStatuses.Clear();
        }

        if (Owner.EnableLogging)
            NoireLogger.LogDebug(Owner, $"{nameof(StatusEffectTracker)} deactivated.");
    }

    /// <inheritdoc/>
    internal override void Update()
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        var currentStatuses = new Dictionary<uint, StatusEffectSnapshot>();

        foreach (var status in localPlayer.StatusList)
        {
            if (status.StatusId == 0)
                continue;

            currentStatuses[status.StatusId] = new StatusEffectSnapshot(
                StatusId: status.StatusId,
                SourceId: status.SourceId,
                RemainingTime: status.RemainingTime,
                Param: status.Param);
        }

        lock (snapshotLock)
        {
            foreach (var (statusId, snapshot) in currentStatuses)
            {
                if (!previousStatuses.ContainsKey(statusId))
                {
                    var evt = new StatusEffectGainedEvent(snapshot);
                    PublishEvent(OnStatusGained, evt);
                }
                else
                {
                    var prev = previousStatuses[statusId];

                    if (prev.Param != snapshot.Param || prev.SourceId != snapshot.SourceId)
                    {
                        var evt = new StatusEffectChangedEvent(prev, snapshot);
                        PublishEvent(OnStatusChanged, evt);
                    }
                }
            }

            foreach (var (statusId, snapshot) in previousStatuses)
            {
                if (!currentStatuses.ContainsKey(statusId))
                {
                    var evt = new StatusEffectLostEvent(snapshot);
                    PublishEvent(OnStatusLost, evt);
                }
            }

            previousStatuses.Clear();
            foreach (var (statusId, snapshot) in currentStatuses)
                previousStatuses[statusId] = snapshot;

            foreach (var entityId in watchedEntityStatuses.Keys.ToArray())
            {
                var currentTrackedStatuses = CaptureStatusesDictionary(Owner.Objects.GetCharacter(entityId) as IBattleChara);
                var previousTrackedStatuses = watchedEntityStatuses[entityId];

                foreach (var (statusId, snapshot) in currentTrackedStatuses)
                {
                    if (!previousTrackedStatuses.ContainsKey(statusId))
                    {
                        PublishEvent(OnTrackedStatusGained, new TrackedStatusEffectGainedEvent(entityId, snapshot));
                    }
                    else
                    {
                        var previous = previousTrackedStatuses[statusId];

                        if (previous.Param != snapshot.Param || previous.SourceId != snapshot.SourceId || previous.RemainingTime != snapshot.RemainingTime)
                            PublishEvent(OnTrackedStatusChanged, new TrackedStatusEffectChangedEvent(entityId, previous, snapshot));
                    }
                }

                foreach (var (statusId, snapshot) in previousTrackedStatuses)
                {
                    if (!currentTrackedStatuses.ContainsKey(statusId))
                        PublishEvent(OnTrackedStatusLost, new TrackedStatusEffectLostEvent(entityId, snapshot));
                }

                watchedEntityStatuses[entityId] = currentTrackedStatuses;
            }
        }
    }

    private void CaptureInitialSnapshot()
    {
        lock (snapshotLock)
        {
            previousStatuses.Clear();

            var localPlayer = NoireService.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return;

            foreach (var status in localPlayer.StatusList)
            {
                if (status.StatusId == 0)
                    continue;

                previousStatuses[status.StatusId] = new StatusEffectSnapshot(
                    StatusId: status.StatusId,
                    SourceId: status.SourceId,
                    RemainingTime: status.RemainingTime,
                    Param: status.Param);
            }

            foreach (var entityId in watchedEntityStatuses.Keys.ToArray())
                watchedEntityStatuses[entityId] = CaptureStatusesDictionary(Owner.Objects.GetCharacter(entityId) as IBattleChara);
        }
    }

    private static Dictionary<uint, StatusEffectSnapshot> CaptureStatusesDictionary(IBattleChara? character)
    {
        Dictionary<uint, StatusEffectSnapshot> statuses = new();

        if (character == null)
            return statuses;

        foreach (var status in character.StatusList)
        {
            if (status.StatusId == 0)
                continue;

            statuses[status.StatusId] = new StatusEffectSnapshot(
                StatusId: status.StatusId,
                SourceId: status.SourceId,
                RemainingTime: status.RemainingTime,
                Param: status.Param);
        }

        return statuses;
    }

    private static StatusEffectSnapshot[] CaptureStatuses(IBattleChara character)
    {
        return CaptureStatusesDictionary(character).Values.ToArray();
    }
}
