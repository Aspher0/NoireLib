using Dalamud.Game.ClientState.Objects.Types;
using System;
using System.Linq;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Helpers for measuring between game objects.
/// </summary>
public static class GameObjectHelper
{
    /// <summary>
    /// The distance between two objects, defaulting to the terms the game's own range checks use: height ignored
    /// and hitbox to hitbox rather than origin to origin.
    /// </summary>
    /// <param name="from">The object to measure from.</param>
    /// <param name="to">The object to measure to.</param>
    /// <param name="ignoreHeight">Whether to measure on the horizontal plane only, leaving Y out.</param>
    /// <param name="betweenHitboxes">Whether to subtract both objects' GetRadius, which makes a 15 yalm limit trip
    /// at roughly 17.5 yalms centre to centre between two players.</param>
    /// <returns>The distance in yalms, which is negative when two hitboxes overlap.</returns>
    public static unsafe float DistanceBetween(
        IGameObject from, IGameObject to, bool ignoreHeight = true, bool betweenHitboxes = true)
    {
        if (from == null || to == null)
            return float.MaxValue;

        var dx = to.Position.X - from.Position.X;
        var dy = ignoreHeight ? 0f : to.Position.Y - from.Position.Y;
        var dz = to.Position.Z - from.Position.Z;

        var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        if (!betweenHitboxes)
            return distance;

        var self = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)from.Address;
        var other = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)to.Address;

        if (self == null || other == null)
            return distance;

        return distance - self->GetRadius() - other->GetRadius();
    }

    /// <summary>
    /// The value the game puts in any target id field when there is no target.
    /// </summary>
    public const ulong NoTargetId = 0xE0000000;

    /// <summary>
    /// The bearing from one point to another, in the rotation a character's SetRotation takes, which is
    /// <c>Atan2(dx, dz)</c> rather than the usual <c>Atan2(dy, dx)</c>.
    /// </summary>
    /// <param name="from">The position to measure from.</param>
    /// <param name="to">The position to face.</param>
    /// <returns>The rotation in radians.</returns>
    public static float Bearing(Vector3 from, Vector3 to)
        => MathF.Atan2(to.X - from.X, to.Z - from.Z);

    /// <summary>
    /// The bearing from one object to another, in the rotation a character's SetRotation takes.
    /// </summary>
    /// <param name="from">The object to measure from.</param>
    /// <param name="to">The object to face.</param>
    /// <returns>The rotation in radians, or 0 when either object is missing.</returns>
    public static float Bearing(IGameObject from, IGameObject to)
        => from == null || to == null ? 0f : Bearing(from.Position, to.Position);

    /// <summary>
    /// The object the local player is acting on, taking the soft target over the hard target as the game does.
    /// </summary>
    /// <returns>The target, or null when there is none or there is no local player.</returns>
    public static IGameObject? GetLocalTarget()
    {
        if (NoireService.ObjectTable.LocalPlayer is null)
            return null;

        return NoireService.TargetManager.SoftTarget ?? NoireService.TargetManager.Target;
    }

    /// <summary>
    /// The id of the object any character is targeting, read off the character rather than the target manager, and
    /// taking the soft target over the hard target as the game does.
    /// </summary>
    /// <param name="character">The character whose target to read.</param>
    /// <returns>The target's game object id, or <see cref="NoTargetId"/> when there is none.</returns>
    public static unsafe ulong GetTargetId(ICharacter character)
    {
        if (character == null || character.Address == 0)
            return NoTargetId;

        var native = CharacterHelper.GetCharacterAddress(character);

        if (native == null)
            return NoTargetId;

        var soft = native->GetSoftTargetId().Id;
        if (soft != NoTargetId)
            return soft;

        return native->GetTargetId().Id;
    }

    /// <summary>
    /// The object carrying a game object id.
    /// </summary>
    /// <param name="gameObjectId">The id to look up, where <see cref="NoTargetId"/> always resolves to null.</param>
    /// <returns>The object, or null when nothing in the table carries that id.</returns>
    public static IGameObject? FindByGameObjectId(ulong gameObjectId)
        => gameObjectId == NoTargetId ? null : NoireService.ObjectTable.SearchById(gameObjectId);

    /// <summary>
    /// The object with a given base id at a given table slot, a pair that stays valid across clients where a raw
    /// address or a table index alone does not.
    /// </summary>
    /// <param name="baseId">The object's base id.</param>
    /// <param name="objectIndex">The object's index in the object table.</param>
    /// <returns>The object, or null when no table entry matches both.</returns>
    public static IGameObject? FindByBaseIdAndObjectIndex(uint baseId, ushort objectIndex)
        => NoireService.ObjectTable.FirstOrDefault(o => o != null && o.BaseId == baseId && o.ObjectIndex == objectIndex);

    /// <summary>
    /// The first object with a given base id.
    /// </summary>
    /// <param name="baseId">The object's base id.</param>
    /// <returns>The object, or null when no table entry carries that base id.</returns>
    public static IGameObject? FindByBaseId(uint baseId)
        => NoireService.ObjectTable.FirstOrDefault(o => o != null && o.BaseId == baseId);

    /// <summary>
    /// Resolves a live object from whichever identity is supplied, trying the content id, then the base id with the
    /// table slot, then the base id alone.
    /// </summary>
    /// <param name="baseId">The object's base id, when known.</param>
    /// <param name="objectIndex">The object's index in the object table, when known.</param>
    /// <param name="contentId">The player's content id, when known.</param>
    /// <returns>The object, or null when none of the identities resolves.</returns>
    public static IGameObject? FindByIdentity(uint? baseId, ushort? objectIndex, ulong? contentId)
    {
        if (contentId is > 0)
            return CharacterHelper.GetCharacterFromCID(contentId.Value);

        if (baseId is > 0 && objectIndex != null)
            return FindByBaseIdAndObjectIndex(baseId.Value, objectIndex.Value);

        return baseId is > 0 ? FindByBaseId(baseId.Value) : null;
    }
}
