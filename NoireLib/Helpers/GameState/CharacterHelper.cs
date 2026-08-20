using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using NoireLib.Animations.Helpers;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// In-game character-related helpers.
/// </summary>
public static class CharacterHelper
{
    /// <summary>
    /// Whether a character is logged in and their state is loaded, so character data is safe to read. Login alone
    /// fires before the client finishes assembling the character, and reading the teleport list, housing data or
    /// quest journal before then walks unfilled pointers and access-violates.
    /// </summary>
    public static unsafe bool IsStateReady
    {
        get
        {
            if (!NoireService.IsInitialized() || !NoireService.ClientState.IsLoggedIn)
                return false;

            var state = PlayerState.Instance();
            return state != null && state->IsLoaded;
        }
    }

    /// <summary>
    /// Whether the character is ready and their player object exists in the world, which is the bar a call into game
    /// code needs rather than <see cref="IsStateReady"/>. The player object is filled in after the state reports
    /// loaded and destroyed before the client reports logged out, leaving windows where a game function reaching for
    /// it access-violates.
    /// </summary>
    public static bool IsPlayerLoaded
        => IsStateReady && NoireService.IsInitialized() && NoireService.ObjectTable.LocalPlayer != null;

    /// <summary>
    /// Whether there is no character at all, as opposed to one whose state is not readable yet. With no character an
    /// empty unlock read is the answer; while one is being assembled the same empty read is not.
    /// </summary>
    public static bool IsLoggedOut => !NoireService.IsInitialized() || !NoireService.ClientState.IsLoggedIn;

    /// <summary>
    /// The logged-in character's content id, or zero when no character state is loaded, which tells one character
    /// from another across a switch that leaves stale game memory such as the teleport list behind.
    /// </summary>
    public static unsafe ulong LocalContentId
    {
        get
        {
            if (!IsStateReady)
                return 0;

            var playerState = PlayerState.Instance();
            return playerState == null ? 0 : playerState->ContentId;
        }
    }

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
    /// Gets the memory address of the owner character's companion object, which is their minion.
    /// </summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The memory address of the companion object, or 0 if not found.</returns>
    public unsafe static nint GetCompanionAddress(ICharacter ownerCharacter)
    {
        var native = CharacterHelper.GetCharacterAddress(ownerCharacter);
        return (nint)native->CompanionData.CompanionObject;
    }

    /// <summary>
    /// Gets the owner character's companion instance, which is their minion.
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
    /// Gets the memory address of the owner character's pet object, such as a Carbuncle or Eos.
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
    /// Gets the owner character's pet instance, such as a Carbuncle or Eos.
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
    /// Gets the memory address of the owner character's buddy object, which is their chocobo.
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
    /// Gets the owner character's buddy instance, which is their chocobo.
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
    /// Determines whether the character at the given memory address is the local player's companion, pet or buddy,
    /// which excludes the local player character itself.
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
    /// Determines whether the given character is the local player's companion, pet or buddy, which excludes the
    /// local player character itself.
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

    // Footing bits the game keeps on the character that FFXIVClientStructs does not name; offsets are for the
    // 2026-08-14 binary and only the emote code reads them.
    private const int GroundMaterialOffset = 0x598;
    private const int StandingInWaterOffset = 0x5AC;
    private const int WaterFallbackFlagsOffset = 0x190;
    private const byte WaterFallbackFlag = 8;

    /// <summary>
    /// The material the character is standing on, with values <see cref="Enums.GroundMaterial"/> does not name
    /// passing through as their raw number.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The material, or <see cref="Enums.GroundMaterial.None"/> when the character cannot be read.</returns>
    public static unsafe Enums.GroundMaterial GetGroundMaterial(ICharacter character)
    {
        if (character == null || character.Address == 0)
            return Enums.GroundMaterial.None;

        return (Enums.GroundMaterial)(*(byte*)(character.Address + GroundMaterialOffset) & 0x7F);
    }

    /// <summary>Whether the character is standing on snow, the footing /throw requires for a snowball.</summary>
    /// <param name="character">The character to read.</param>
    /// <returns>True when the ground material is snow.</returns>
    public static bool IsStandingOnSnow(ICharacter character)
        => GetGroundMaterial(character) == Enums.GroundMaterial.Snow;

    /// <summary>
    /// Whether the character has water underfoot, the condition /splash requires, which shallow water satisfies and
    /// no condition flag or sheet column expresses.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <param name="includeFallback">Whether to also accept the secondary flag the game allows on its play path,
    /// which the gate deciding command acceptance ignores.</param>
    /// <returns>True when water is underfoot.</returns>
    public static unsafe bool IsStandingInWater(ICharacter character, bool includeFallback = false)
    {
        if (character == null || character.Address == 0)
            return false;

        if (*(byte*)(character.Address + StandingInWaterOffset) != 0)
            return true;

        return includeFallback
            && (*(byte*)(character.Address + WaterFallbackFlagsOffset) & WaterFallbackFlag) != 0;
    }

    /// <summary>
    /// Whether a mode is one of the two the game holds a character in while an emote runs, InPositionLoop for
    /// emotes that pin the character in place and EmoteLoop for the rest.
    /// </summary>
    /// <param name="mode">The mode to test.</param>
    /// <returns>True for either emote loop mode.</returns>
    public static bool IsEmoteLoopMode(CharacterModes mode)
        => mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop;

    /// <summary>
    /// Whether the character is held in an emote loop, whichever of the two loop modes the game chose.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>True when the character's mode is an emote loop.</returns>
    public static unsafe bool IsCharacterInEmoteLoop(ICharacter character)
    {
        if (character == null || character.Address == 0)
            return false;

        return IsEmoteLoopMode(GetCharacterAddress(character)->Mode);
    }

    /// <summary>
    /// The condition flags that mean the local player is too busy to be made to act: casting, either cutscene,
    /// either event occupancy, and the three crafting states. Gathering is absent because fishing raises the
    /// gathering flags too, so <see cref="IsLocalPlayerOccupied"/> tests it separately.
    /// </summary>
    public static readonly ConditionFlag[] OccupiedConditions =
    [
        ConditionFlag.Casting,
        ConditionFlag.Casting87,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInEvent,
        ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.Crafting,
        ConditionFlag.ExecutingCraftingAction,
        ConditionFlag.PreparingToCraft,
    ];

    /// <summary>
    /// The condition flags whose arrival means a character can no longer hold an animation: casting, either event
    /// occupancy, mounting, the three crafting states and the two gathering ones.
    /// </summary>
    public static readonly ConditionFlag[] AnimationInterruptingConditions =
    [
        ConditionFlag.Casting,
        ConditionFlag.Casting87,
        ConditionFlag.OccupiedInEvent,
        ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.Mounted,
        ConditionFlag.Crafting,
        ConditionFlag.ExecutingCraftingAction,
        ConditionFlag.PreparingToCraft,
        ConditionFlag.Gathering,
        ConditionFlag.ExecutingGatheringAction,
    ];

    /// <summary>
    /// Whether the local player is busy enough that making them act should be refused: casting or dead, in
    /// any of <see cref="OccupiedConditions"/>, or gathering by something other than fishing.
    /// </summary>
    /// <param name="includeGathering">Whether gathering counts as occupied, excluding fishing, which raises the
    /// gathering flags on a player who can still be acted on.</param>
    /// <returns>True when the local player should be left alone, and false when there is no local player.</returns>
    public static bool IsLocalPlayerOccupied(bool includeGathering = true)
    {
        if (NoireService.ObjectTable.LocalPlayer is not { } local)
            return false;

        if (local.IsCasting || local.IsDead)
            return true;

        if (NoireService.Condition.Any(OccupiedConditions))
            return true;

        return includeGathering
            && !NoireService.Condition[ConditionFlag.Fishing]
            && NoireService.Condition.Any(ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction);
    }

    /// <summary>
    /// The player who owns a minion, pet or chocobo, reading CompanionOwnerId for a minion and the object's own
    /// OwnerId for sub-kinds 2 and 3.
    /// </summary>
    /// <param name="characterAddress">The address of the owned object.</param>
    /// <returns>The owner's address, or 0 when the object is unowned or its owner is not in the object table.</returns>
    public static unsafe nint GetOwningPlayerAddress(nint characterAddress)
    {
        var owned = GetCharacterFromAddress(characterAddress);

        if (owned == null)
            return nint.Zero;

        uint ownerEntityId;

        if (owned.ObjectKind == ObjectKind.Companion)
            ownerEntityId = GetCharacterAddress(owned)->CompanionOwnerId;
        else if (owned.SubKind is 2 or 3)
            ownerEntityId = owned.OwnerId;
        else
            return nint.Zero;

        var owner = NoireService.ObjectTable.PlayerObjects.FirstOrDefault(p => p.EntityId == ownerEntityId);

        return owner?.Address ?? nint.Zero;
    }

    /// <summary>
    /// The player who owns a minion, pet or chocobo.
    /// </summary>
    /// <param name="owned">The owned object.</param>
    /// <returns>The owner, or null when the object is unowned or its owner is not in the object table.</returns>
    public static IPlayerCharacter? GetOwningPlayer(IGameObject owned)
    {
        if (owned == null)
            return null;

        var address = GetOwningPlayerAddress(owned.Address);

        return address == nint.Zero
            ? null
            : NoireService.ObjectTable.PlayerObjects.FirstOrDefault(p => p.Address == address) as IPlayerCharacter;
    }

    /// <summary>
    /// The draw object a character is currently built from and the human skeleton it animates as. The drawn model
    /// decides which skeleton's animations the game asks for, and its skeleton never changes after it is built, so
    /// a body change produces a new <paramref name="DrawObject"/>.
    /// </summary>
    /// <param name="DrawObject">The address of the drawn model.</param>
    /// <param name="SkeletonId">The human skeleton id, such as "c0801".</param>
    public readonly record struct DrawnBody(nint DrawObject, string SkeletonId);

    /// <summary>
    /// The drawn model and skeleton of a character.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The drawn body, or null when there is no draw object or it is not a human character model, as
    /// happens while a character is between models.</returns>
    public static unsafe DrawnBody? GetDrawnBody(ICharacter character)
    {
        if (character == null || character.Address == 0)
            return null;

        var native = GetCharacterAddress(character);
        if (native == null || native->DrawObject == null)
            return null;

        if (native->DrawObject->GetObjectType() != ObjectType.CharacterBase)
            return null;

        var drawn = (CharacterBase*)native->DrawObject;
        if (drawn->GetModelType() != CharacterBase.ModelType.Human)
            return null;

        return new DrawnBody((nint)drawn, EmotePathHelper.NormalizeHumanSkeletonId(((Human*)drawn)->RaceSexId));
    }

    /// <summary>
    /// The human skeleton id a character is drawn as.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The skeleton id, or null while there is no human model to read.</returns>
    public static string? GetDrawnSkeletonId(ICharacter character)
        => GetDrawnBody(character)?.SkeletonId;

    /// <summary>
    /// The human skeleton id to serve a character's animations from, taken from the drawn model and falling back to
    /// their customize bytes while no model is readable.
    /// </summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The skeleton id, such as "c0801".</returns>
    public static string ResolveSkeletonId(ICharacter character)
    {
        if (GetDrawnSkeletonId(character) is { } drawn)
            return drawn;

        var customize = character.Customize;

        return RaceGenderData.SkeletonFromCustomize(
            customize[(int)CustomizeIndex.Race],
            customize[(int)CustomizeIndex.Tribe],
            customize[(int)CustomizeIndex.Gender]);
    }
}
