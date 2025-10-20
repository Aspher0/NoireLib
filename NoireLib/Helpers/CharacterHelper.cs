using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// Helper class for in-game character-related operations.
/// </summary>
public static class CharacterHelper
{
    /// <summary>
    /// Retrieves the memory address of the given character.
    /// </summary>
    /// <param name="chara">Character instance.</param>
    /// <returns>The memory address of the character.</returns>
    public static unsafe Character* GetCharacterAddress(ICharacter chara) => (Character*)chara.Address;

    /// <summary>
    /// Tries to retrieve a character instance from its memory address based on the Object Table.
    /// </summary>
    /// <param name="charaAddress">The character's memory address.</param>
    /// <returns>The character instance, or null if not found.</returns>
    public static ICharacter? TryGetCharacterFromAddress(nint charaAddress)
    {
        if (charaAddress == nint.Zero)
            return null;

        return NoireService.ObjectTable.FirstOrDefault(p => p is ICharacter && p.Address == charaAddress) as ICharacter;
    }

    /// <summary>
    /// Tries to retrieve the Content ID (CID) of a player character from its memory address.
    /// </summary>
    /// <param name="charaAddress">The character's memory address.</param>
    /// <returns>The Content ID, or null if not found, or if not a player character.</returns>
    public unsafe static ulong? GetCIDFromPlayerCharacterAddress(nint charaAddress)
    {
        if (charaAddress == nint.Zero) return null;

        var castChar = TryGetCharacterFromAddress(charaAddress);

        if (castChar is not IPlayerCharacter) return null;

        var castBattleChara = (BattleChara*)castChar.Address;
        return castBattleChara->Character.ContentId;
    }

    /// <summary>
    /// Tries to retrieve a character instance from its Content ID (CID) based on the Object Table.
    /// </summary>
    /// <param name="cid">The Content ID of the character.</param>
    /// <returns>The character instance, or null if not found, or if not a player character.</returns>
    public static ICharacter? TryGetCharacterFromCID(ulong cid)
    {
        return NoireService.ObjectTable.PlayerObjects
            .Where(o => o is IPlayerCharacter)
            .Select(o => o as ICharacter)
            .FirstOrDefault(p => p != null && GetCIDFromPlayerCharacterAddress(p.Address) == cid);
    }

    /// <summary>
    /// Tries to retrieve a character instance from its Base ID based on the Object Table.
    /// </summary>
    /// <param name="baseId">The Base ID of the character.</param>
    /// <returns>The character instance, or null if not found.</returns>
    public static ICharacter? TryGetCharacterFromBaseId(uint baseId)
    {
        return NoireService.ObjectTable
            .Where(o => o is ICharacter)
            .Select(o => o as ICharacter)
            .FirstOrDefault(p => p != null && p.BaseId == baseId);
    }

    /// <summary>
    /// Checks if the character's weapon is currently drawn.
    /// </summary>
    /// <param name="charaAddress">The character's memory address.</param>
    /// <returns>True if the weapon is drawn, false otherwise.</returns>
    public static unsafe bool IsCharacterWeaponDrawn(nint charaAddress)
    {
        var castChar = TryGetCharacterFromAddress(charaAddress);
        if (castChar == null) return false;
        return castChar.StatusFlags.HasFlag(StatusFlags.WeaponOut);
    }

    /// <summary>
    /// Returns whether the character exists in the Object Table.
    /// </summary>
    /// <param name="chara">The character instance.</param>
    /// <returns>True if the character is in the Object Table, false otherwise.</returns>
    public static unsafe bool IsCharacterInObjectTable(ICharacter chara)
    {
        if (chara == null) return false;
        return NoireService.ObjectTable.Any(o => o.Address == (nint)GetCharacterAddress(chara));
    }

    /// <summary>
    /// Checks if the character is ground sitting.
    /// </summary>
    /// <param name="chara">The character instance.</param>
    /// <returns>True if the character is ground sitting, false otherwise.</returns>
    public static unsafe bool IsCharacterGroundSitting(ICharacter chara)
    {
        var native = GetCharacterAddress(chara);
        return (native->Mode == CharacterModes.EmoteLoop || native->Mode == CharacterModes.InPositionLoop) && native->ModeParam == 1;
    }

    /// <summary>
    /// Checks if the character is chair sitting.
    /// </summary>
    /// <param name="chara">The character instance.</param>
    /// <returns>True if the character is chair sitting, false otherwise.</returns>
    public static unsafe bool IsCharacterChairSitting(ICharacter chara)
    {
        var native = GetCharacterAddress(chara);
        return (native->Mode == CharacterModes.EmoteLoop || native->Mode == CharacterModes.InPositionLoop) && native->ModeParam == 2;
    }

    /// <summary>
    /// Checks if the character is sleeping.
    /// </summary>
    /// <param name="chara">The character instance.</param>
    /// <returns>True if the character is sleeping, false otherwise.</returns>
    public static unsafe bool IsCharacterSleeping(ICharacter chara)
    {
        var native = GetCharacterAddress(chara);
        return (native->Mode == CharacterModes.EmoteLoop || native->Mode == CharacterModes.InPositionLoop) && native->ModeParam == 3;
    }

    /// <summary>
    /// Checks if the character is mounted.
    /// </summary>
    /// <param name="chara">The character instance.</param>
    /// <returns>True if the character is mounted, false otherwise.</returns>
    public static unsafe bool IsCharacterMounted(ICharacter chara)
    {
        var native = GetCharacterAddress(chara);
        return native->Mode == CharacterModes.Mounted;
    }

    /// <summary>
    /// Checks if the character is riding pillion.
    /// </summary>
    /// <param name="chara">The character instance.</param>
    /// <returns>True if the character is riding pillion, false otherwise.</returns>
    public static unsafe bool IsCharacterRidingPillion(ICharacter chara)
    {
        var native = GetCharacterAddress(chara);
        return native->Mode == CharacterModes.RidingPillion;
    }
}
