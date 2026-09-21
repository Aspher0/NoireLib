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
    /// <summary>The distance between two objects. By default like the game's range checks: height ignored, hitbox to hitbox.</summary>
    /// <param name="from">The object to measure from.</param>
    /// <param name="to">The object to measure to.</param>
    /// <param name="ignoreHeight">Whether to measure on the horizontal plane only.</param>
    /// <param name="betweenHitboxes">Whether to subtract both objects' GetRadius. A 15 yalm limit then trips at about 17.5 yalms between two players.</param>
    /// <returns>The distance in yalms. Negative when two hitboxes overlap.</returns>
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
    /// Whether the local player is close enough to interact with an object, answered by the game's own range check,
    /// never by a distance of our choosing. The game measures hitbox to hitbox and ignores height. The
    /// limit is not a single number and cannot be replicated with a constant.
    /// </summary>
    /// <param name="target">The object to reach.</param>
    /// <param name="interactionType">The interaction the game should measure for. The game takes a byte here and
    /// publishes no enum for it. 0 is the plain object interaction every caller starts from.</param>
    /// <param name="logErrorsToUser">Whether to let the game print its own "too far away" error to the chat log.</param>
    /// <returns>True when the game considers the object in range, false when it does not or nothing can be read.</returns>
    public static unsafe bool IsWithinInteractRange(IGameObject target, byte interactionType = 0, bool logErrorsToUser = false)
    {
        if (target == null || target.Address == 0)
            return false;

        var local = NoireService.ObjectTable.LocalPlayer;

        if (local == null || local.Address == 0)
            return false;

        var eventFramework = FFXIVClientStructs.FFXIV.Client.Game.Event.EventFramework.Instance();

        if (eventFramework == null)
            return false;

        return eventFramework->CheckInteractRange(
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)local.Address,
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address,
            interactionType,
            logErrorsToUser);
    }

    /// <summary>
    /// The value the game puts in any target id field when there is no target.
    /// </summary>
    public const ulong NoTargetId = 0xE0000000;

    /// <summary>The bearing from one point to another as SetRotation takes it: <c>Atan2(dx, dz)</c>.</summary>
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

    /// <summary>The id of the object a character is targeting, read off the character. The soft target wins over the hard target.</summary>
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
