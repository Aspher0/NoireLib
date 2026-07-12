using Dalamud.Game.ClientState.Objects.Enums;
using System;
using System.Numerics;

namespace NoireLib.GameWatcher;

/// <summary>
/// An immutable snapshot of any game object captured from the object table.<br/>
/// The kind-agnostic sibling of <see cref="CharacterSnapshot"/>: use this for treasure, NPCs, event objects
/// and anything else; use <see cref="CharacterSnapshot"/> for people.
/// </summary>
public sealed record ObjectSnapshot
{
    /// <summary>The entity id (object-table slot; can be reused by the game after despawn).</summary>
    public required uint EntityId { get; init; }

    /// <summary>The full game-object id.</summary>
    public required ulong GameObjectId { get; init; }

    /// <summary>The data-sheet row id of the object.</summary>
    public required uint DataId { get; init; }

    /// <summary>The entity id of the owner (e.g. a minion's player), or 0 when unowned.</summary>
    public required uint OwnerId { get; init; }

    /// <summary>The display name at capture time.</summary>
    public required string Name { get; init; }

    /// <summary>The high-level object kind.</summary>
    public required ObjectKind ObjectKind { get; init; }

    /// <summary>The object sub-kind value.</summary>
    public required byte SubKind { get; init; }

    /// <summary>The world-space position at capture time.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>The rotation in radians at capture time.</summary>
    public required float Rotation { get; init; }

    /// <summary>Whether the object was targetable at capture time.</summary>
    public required bool IsTargetable { get; init; }

    /// <summary>Whether the object was dead at capture time.</summary>
    public required bool IsDead { get; init; }

    /// <summary>The hitbox radius in yalms.</summary>
    public required float HitboxRadius { get; init; }

    /// <summary>The UTC timestamp when the snapshot was captured.</summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
