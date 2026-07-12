namespace NoireLib.GameWatcher;

/// <summary>
/// Fired when any game object appears in the object table.<br/>
/// Characters are objects too: a player appearing fires both this and <see cref="CharacterSpawnedEvent"/>,
/// each only if subscribed and masked independently.
/// </summary>
/// <param name="Current">The object's first snapshot.</param>
/// <param name="DuringZoneChange">True when the spawn happened while the client was loading between areas.</param>
public sealed record ObjectSpawnedEvent(ObjectSnapshot Current, bool DuringZoneChange);

/// <summary>
/// Fired when a game object disappears from the object table.
/// </summary>
/// <param name="Last">The object's last known snapshot.</param>
/// <param name="DuringZoneChange">True when the despawn happened while the client was loading between areas.</param>
public sealed record ObjectDespawnedEvent(ObjectSnapshot Last, bool DuringZoneChange);

/// <summary>
/// Fired when an observed property of a game object changes (name, targetability, owner, death state).
/// Position changes deliberately do not fire this event — use distance or region watchers.
/// </summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot after the change.</param>
public sealed record ObjectChangedEvent(ObjectSnapshot Previous, ObjectSnapshot Current);

/// <summary>The reason a subject left a watched region or distance threshold.</summary>
public enum RegionExitReason
{
    /// <summary>The subject moved out of the shape.</summary>
    Left,

    /// <summary>The subject despawned while inside; the event carries its last snapshot.</summary>
    Despawned,
}
