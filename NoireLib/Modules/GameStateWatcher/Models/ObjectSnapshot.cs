using Dalamud.Game.ClientState.Objects.Enums;
using System.Numerics;

namespace NoireLib.GameStateWatcher;

/// <summary>
/// An immutable snapshot of a game object captured from the object table.
/// </summary>
/// <param name="EntityId">The unique entity identifier of the object.</param>
/// <param name="BaseId">The data-sheet identifier of the object.</param>
/// <param name="Name">The display name of the object at the time of capture.</param>
/// <param name="ObjectKind">The high-level object kind.</param>
/// <param name="SubKind">The object sub-kind value.</param>
/// <param name="Position">The world-space position of the object.</param>
public sealed record ObjectSnapshot(
    uint EntityId,
    uint BaseId,
    string Name,
    ObjectKind ObjectKind,
    byte SubKind,
    Vector3 Position);
