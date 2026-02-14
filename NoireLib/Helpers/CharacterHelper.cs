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
    /// <param name="character">Character instance.</param>
    /// <returns>The memory address of the character.</returns>
    public static unsafe Character* GetCharacterAddress(ICharacter character) => (Character*)character.Address;

    /// <summary>
    /// Tries to retrieve a character instance from its memory address based on the Object Table.
    /// </summary>
    /// <param name="characterAddress">The character's memory address.</param>
    /// <returns>The character instance, or null if not found.</returns>
    public static ICharacter? GetCharacterFromAddress(nint characterAddress)
    {
        if (characterAddress == nint.Zero)
            return null;

        return NoireService.ObjectTable.FirstOrDefault(p => p is ICharacter && p.Address == characterAddress) as ICharacter;
    }

    /// <summary>
    /// Tries to retrieve the Content ID (CID) of a player character from its memory address.
    /// </summary>
    /// <param name="characterAddress">The character's memory address.</param>
    /// <returns>The Content ID, or null if not found, or if not a player character.</returns>
    public unsafe static ulong? GetCIDFromPlayerCharacterAddress(nint characterAddress)
    {
        if (characterAddress == nint.Zero) return null;

        var castChar = GetCharacterFromAddress(characterAddress);

        if (castChar is not IPlayerCharacter) return null;

        var castBattleChara = (BattleChara*)castChar.Address;
        return castBattleChara->Character.ContentId;
    }

    /// <summary>
    /// Tries to retrieve a character instance from its Content ID (CID) based on the Object Table.
    /// </summary>
    /// <param name="cid">The Content ID of the character.</param>
    /// <returns>The character instance, or null if not found, or if not a player character.</returns>
    public static ICharacter? GetCharacterFromCID(ulong cid)
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
    public static ICharacter? GetCharacterFromBaseId(uint baseId)
    {
        return NoireService.ObjectTable
            .Where(o => o is ICharacter)
            .Select(o => o as ICharacter)
            .FirstOrDefault(p => p != null && p.BaseId == baseId);
    }

    /// <summary>
    /// Checks if the character's weapon is currently drawn.
    /// </summary>
    /// <param name="characterAddress">The character's memory address.</param>
    /// <returns>True if the weapon is drawn, false otherwise.</returns>
    public static unsafe bool IsCharacterWeaponDrawn(nint characterAddress)
    {
        var castChar = GetCharacterFromAddress(characterAddress);
        if (castChar == null) return false;
        return castChar.StatusFlags.HasFlag(StatusFlags.WeaponOut);
    }

    /// <summary>
    /// Returns whether the character exists in the Object Table.
    /// </summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is in the Object Table, false otherwise.</returns>
    public static unsafe bool IsCharacterInObjectTable(ICharacter character)
    {
        if (character == null) return false;
        return NoireService.ObjectTable.Any(o => o.Address == (nint)GetCharacterAddress(character));
    }

    /// <summary>
    /// Checks if the character is ground sitting.
    /// </summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is ground sitting, false otherwise.</returns>
    public static unsafe bool IsCharacterGroundSitting(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return (native->Mode == CharacterModes.EmoteLoop ||
                native->Mode == CharacterModes.InPositionLoop) &&
                native->ModeParam == 1;
    }

    /// <summary>
    /// Checks if the character is chair sitting.
    /// </summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is chair sitting, false otherwise.</returns>
    public static unsafe bool IsCharacterChairSitting(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return (native->Mode == CharacterModes.EmoteLoop || native->Mode == CharacterModes.InPositionLoop) && native->ModeParam == 2;
    }

    /// <summary>
    /// Checks if the character is sleeping.
    /// </summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is sleeping, false otherwise.</returns>
    public static unsafe bool IsCharacterSleeping(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return (native->Mode == CharacterModes.EmoteLoop || native->Mode == CharacterModes.InPositionLoop) && native->ModeParam == 3;
    }

    /// <summary>
    /// Checks if the character is mounted.
    /// </summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is mounted, false otherwise.</returns>
    public static unsafe bool IsCharacterMounted(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return native->Mode == CharacterModes.Mounted;
    }

    /// <summary>
    /// Checks if the character is riding pillion.
    /// </summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is riding pillion, false otherwise.</returns>
    public static unsafe bool IsCharacterRidingPillion(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return native->Mode == CharacterModes.RidingPillion;
    }

    /// <summary>
    /// Rotates the local player character to the specified target rotation, if it's safe to do so (not sitting or sleeping).
    /// </summary>
    /// <param name="targetRotation">The target rotation to apply.</param>
    /// <returns>True if the rotation was applied, false otherwise.</returns>
    public static unsafe bool RotateCharacterSafe(float targetRotation)
    {
        if (NoireService.ObjectTable.LocalPlayer is not ICharacter localCharacter)
            return false;

        if (IsCharacterChairSitting(localCharacter) ||
            IsCharacterGroundSitting(localCharacter) ||
            IsCharacterSleeping(localCharacter))
            return false;

        var character = GetCharacterAddress(localCharacter);
        character->SetRotation(targetRotation);
        return true;
    }

    /// <summary>
    /// Gets the memory address of the companion object associated with the given owner character, if any.<br/>
    /// A companion represents a minion.
    /// </summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The memory address of the companion object, or 0 if not found.</returns>
    public unsafe static nint GetCompanionAddress(ICharacter ownerCharacter)
    {
        var native = CharacterHelper.GetCharacterAddress(ownerCharacter);
        return (nint)native->CompanionData.CompanionObject;
    }

    /// <summary>
    /// Gets the companion character instance associated with the given owner character, if any.<br/>
    /// A companion represents a minion.
    /// </summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The companion character instance, or null if not found.</returns>
    public static ICharacter? GetCompanion(ICharacter ownerCharacter)
    {
        var companionAddress = GetCompanionAddress(ownerCharacter);
        if (companionAddress == nint.Zero)
            return null;
        return GetCharacterFromAddress(companionAddress);
    }

    /// <summary>
    /// Gets the memory address of the pet object associated with the given owner character, if any.<br/>
    /// A pet represents a Carbuncle or Eos, for example.
    /// </summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The memory address of the pet object, or 0 if not found.</returns>
    public unsafe static nint GetPetAddress(ICharacter ownerCharacter)
    {
        var native = CharacterHelper.GetCharacterAddress(ownerCharacter);
        var manager = CharacterManager.Instance();
        return (nint)manager->LookupPetByOwnerObject((BattleChara*)native);
    }

    /// <summary>
    /// Gets the pet character instance associated with the given owner character, if any.<br/>
    /// A pet represents a Carbuncle or Eos, for example.
    /// </summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The pet character instance, or null if not found.</returns>
    public static ICharacter? GetPet(ICharacter ownerCharacter)
    {
        var petAddress = GetPetAddress(ownerCharacter);
        if (petAddress == nint.Zero)
            return null;
        return GetCharacterFromAddress(petAddress);
    }

    /// <summary>
    /// Gets the memory address of the buddy object associated with the given owner character, if any.<br/>
    /// A buddy represents a chocobo.
    /// </summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The memory address of the buddy object, or 0 if not found.</returns>
    public unsafe static nint GetBuddyAddress(ICharacter ownerCharacter)
    {
        var native = CharacterHelper.GetCharacterAddress(ownerCharacter);
        var manager = CharacterManager.Instance();
        return (nint)manager->LookupBuddyByOwnerObject((BattleChara*)native);
    }

    /// <summary>
    /// Gets the buddy character instance associated with the given owner character, if any.<br/>
    /// A buddy represents a chocobo.
    /// </summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The buddy character instance, or null if not found.</returns>
    public static ICharacter? GetBuddy(ICharacter ownerCharacter)
    {
        var buddyAddress = GetBuddyAddress(ownerCharacter);
        if (buddyAddress == nint.Zero)
            return null;
        return GetCharacterFromAddress(buddyAddress);
    }

    /// <summary>
    /// Determines whether the character at the given memory address is owned by the local player.<br/>
    /// This will not return true for the local player character itself, but will return true for the local player's companion, pet, or buddy, if they exist.
    /// </summary>
    /// <param name="characterAddress">The memory address of the object to check.</param>
    /// <returns>True if the object is owned by the local player, false otherwise.</returns>
    public static bool IsCharacterOwnedByLocalPlayer(nint characterAddress)
    {
        var local = NoireService.ObjectTable.LocalPlayer;

        if (local == null)
            return false;

        return characterAddress == GetCompanionAddress(local) ||
               characterAddress == GetPetAddress(local) ||
               characterAddress == GetBuddyAddress(local);
    }

    /// <summary>
    /// Determines whether the given character instance is owned by the local player.<br/>
    /// This will not return true for the local player character itself, but will return true for the local player's companion, pet, or buddy, if they exist.
    /// </summary>
    /// <param name="character">The character instance to check.</param>
    /// <returns>True if the character is owned by the local player, false otherwise.</returns>
    public static bool IsCharacterOwnedByLocalPlayer(ICharacter character)
    {
        if (character == null)
            return false;
        return IsCharacterOwnedByLocalPlayer(character.Address);
    }

    /// <summary>
    /// Determines whether the object at the given memory address is the local player or one of their owned entities (companion, pet, buddy).
    /// </summary>
    /// <param name="characterAddress">The memory address of the character to check (companion, pet, buddy).</param>
    /// <returns>True if the object is the local player or one of their owned entities, false otherwise.</returns>
    public static bool IsLocalObject(nint characterAddress)
    {
        var nativeObject = GetCharacterFromAddress(characterAddress);
        if (nativeObject == null)
            return false;
        return IsLocalObject(nativeObject);
    }

    /// <summary>
    /// Determines whether the given character instance is the local player or one of their owned entities (companion, pet, buddy).
    /// </summary>
    /// <param name="character">The character instance to check.</param>
    /// <returns>True if the character is the local player or one of their owned entities, false otherwise.</returns>
    public static bool IsLocalObject(ICharacter character)
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;

        if (localPlayer == null)
            return false;

        var playerAddress = localPlayer.Address;
        var companionAddress = GetCompanionAddress(localPlayer);
        var petAddress = GetPetAddress(localPlayer);
        var buddyAddress = GetBuddyAddress(localPlayer);

        return playerAddress == character.Address ||
               companionAddress == character.Address ||
               petAddress == character.Address ||
               buddyAddress == character.Address;
    }
}
