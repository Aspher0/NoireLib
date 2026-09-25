using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using NoireLib.Animations.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>In-game character-related helpers.</summary>
public static class CharacterHelper
{
    /// <summary>
    /// Whether the character is logged in and loaded. Login fires earlier: reading the teleport list, housing or quests
    /// before this access-violates.
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

    /// <summary>Whether the player object exists in the world. Calling into game code needs this, not <see cref="IsStateReady"/>.</summary>
    public static bool IsPlayerLoaded
        => IsStateReady && NoireService.IsInitialized() && NoireService.ObjectTable.LocalPlayer != null;

    /// <summary>Whether there is no character at all, unlike mid-login. Without one, an empty unlock read is the answer.</summary>
    public static bool IsLoggedOut => !NoireService.IsInitialized() || !NoireService.ClientState.IsLoggedIn;

    /// <summary>The logged-in character's content id, or zero when no character state is loaded.</summary>
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

    /// <summary>Retrieves the memory address of the given character.</summary>
    /// <param name="character">Character instance.</param>
    /// <returns>The memory address of the character.</returns>
    public static unsafe Character* GetCharacterAddress(ICharacter character) => (Character*)character.Address;

    /// <summary>Tries to retrieve a character instance from its memory address based on the Object Table.</summary>
    /// <param name="characterAddress">The character's memory address.</param>
    /// <returns>The character instance, or null if not found.</returns>
    public static ICharacter? GetCharacterFromAddress(nint characterAddress)
    {
        if (characterAddress == nint.Zero)
            return null;

        return NoireService.ObjectTable.FirstOrDefault(p => p is ICharacter && p.Address == characterAddress) as ICharacter;
    }

    /// <summary>Tries to retrieve the Content ID (CID) of a player character from its memory address.</summary>
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

    /// <summary>Tries to retrieve a character instance from its Content ID (CID) based on the Object Table.</summary>
    /// <param name="cid">The Content ID of the character.</param>
    /// <returns>The character instance, or null if not found, or if not a player character.</returns>
    public static ICharacter? GetCharacterFromCID(ulong cid)
    {
        return NoireService.ObjectTable.PlayerObjects
            .Where(o => o is IPlayerCharacter)
            .Select(o => o as ICharacter)
            .FirstOrDefault(p => p != null && GetCIDFromPlayerCharacterAddress(p.Address) == cid);
    }

    /// <summary>Tries to retrieve a character instance from its Base ID based on the Object Table.</summary>
    /// <param name="baseId">The Base ID of the character.</param>
    /// <returns>The character instance, or null if not found.</returns>
    public static ICharacter? GetCharacterFromBaseId(uint baseId)
    {
        return NoireService.ObjectTable
            .Where(o => o is ICharacter)
            .Select(o => o as ICharacter)
            .FirstOrDefault(p => p != null && p.BaseId == baseId);
    }

    /// <summary>Checks if the character's weapon is currently drawn.</summary>
    /// <param name="characterAddress">The character's memory address.</param>
    /// <returns>True if the weapon is drawn, false otherwise.</returns>
    public static unsafe bool IsCharacterWeaponDrawn(nint characterAddress)
    {
        var castChar = GetCharacterFromAddress(characterAddress);
        if (castChar == null) return false;
        return castChar.StatusFlags.HasFlag(StatusFlags.WeaponOut);
    }

    /// <summary>Returns whether the character exists in the Object Table.</summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is in the Object Table, false otherwise.</returns>
    public static unsafe bool IsCharacterInObjectTable(ICharacter character)
    {
        if (character == null) return false;
        return NoireService.ObjectTable.Any(o => o.Address == (nint)GetCharacterAddress(character));
    }

    /// <summary>Checks if the character is ground sitting.</summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is ground sitting, false otherwise.</returns>
    public static unsafe bool IsCharacterGroundSitting(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return (native->Mode == CharacterModes.EmoteLoop ||
                native->Mode == CharacterModes.InPositionLoop) &&
                native->ModeParam == 1;
    }

    /// <summary>Checks if the character is chair sitting.</summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is chair sitting, false otherwise.</returns>
    public static unsafe bool IsCharacterChairSitting(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return (native->Mode == CharacterModes.EmoteLoop || native->Mode == CharacterModes.InPositionLoop) && native->ModeParam == 2;
    }

    /// <summary>Checks if the character is sleeping.</summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is sleeping, false otherwise.</returns>
    public static unsafe bool IsCharacterSleeping(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return (native->Mode == CharacterModes.EmoteLoop || native->Mode == CharacterModes.InPositionLoop) && native->ModeParam == 3;
    }

    /// <summary>Checks if the character is mounted.</summary>
    /// <param name="character">The character instance.</param>
    /// <returns>True if the character is mounted, false otherwise.</returns>
    public static unsafe bool IsCharacterMounted(ICharacter character)
    {
        var native = GetCharacterAddress(character);
        return native->Mode == CharacterModes.Mounted;
    }

    /// <summary>Checks if the character is riding pillion.</summary>
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

    /// <summary>Gets the address of the owner character's minion.</summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The address, or 0 if not found.</returns>
    public unsafe static nint GetCompanionAddress(ICharacter ownerCharacter)
    {
        var native = CharacterHelper.GetCharacterAddress(ownerCharacter);
        return (nint)native->CompanionData.CompanionObject;
    }

    /// <summary>Gets the owner character's minion.</summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The minion, or null if not found.</returns>
    public static ICharacter? GetCompanion(ICharacter ownerCharacter)
    {
        var companionAddress = GetCompanionAddress(ownerCharacter);
        if (companionAddress == nint.Zero)
            return null;
        return GetCharacterFromAddress(companionAddress);
    }

    /// <summary>Gets the memory address of the owner character's pet object, such as a Carbuncle or Eos.</summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The memory address of the pet object, or 0 if not found.</returns>
    public unsafe static nint GetPetAddress(ICharacter ownerCharacter)
    {
        var native = CharacterHelper.GetCharacterAddress(ownerCharacter);
        var manager = CharacterManager.Instance();
        return (nint)manager->LookupPetByOwnerObject((BattleChara*)native);
    }

    /// <summary>Gets the owner character's pet instance, such as a Carbuncle or Eos.</summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The pet character instance, or null if not found.</returns>
    public static ICharacter? GetPet(ICharacter ownerCharacter)
    {
        var petAddress = GetPetAddress(ownerCharacter);
        if (petAddress == nint.Zero)
            return null;
        return GetCharacterFromAddress(petAddress);
    }

    /// <summary>Gets the address of the owner character's chocobo.</summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The address, or 0 if not found.</returns>
    public unsafe static nint GetBuddyAddress(ICharacter ownerCharacter)
    {
        var native = CharacterHelper.GetCharacterAddress(ownerCharacter);
        var manager = CharacterManager.Instance();
        return (nint)manager->LookupBuddyByOwnerObject((BattleChara*)native);
    }

    /// <summary>Gets the owner character's chocobo.</summary>
    /// <param name="ownerCharacter">The owner character.</param>
    /// <returns>The chocobo, or null if not found.</returns>
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

    /// <summary>Whether the character is the local player's companion, pet or buddy.</summary>
    /// <param name="character">The character to check.</param>
    /// <returns>True if the local player owns it.</returns>
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

    // Footing bits FFXIVClientStructs does not name. Offsets are for the 2026-08-14 binary.
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

    /// <summary>Whether the character has water underfoot, the condition /splash requires. Shallow water counts.</summary>
    /// <param name="character">The character to read.</param>
    /// <param name="includeFallback">Whether to also accept the secondary flag the game's play path allows.</param>
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
    /// The condition flags that keep the local player from acting: events, cutscenes, crafting, gathering, combat, zoning,
    /// jumping, mounting, carrying, performing, housing and mini games. Riding and flying are not among them.
    /// </summary>
    public static IReadOnlyList<ConditionFlag> OccupiedConditions { get; } =
    [
        ConditionFlag.Occupied,
        ConditionFlag.Occupied30,
        ConditionFlag.Occupied33,
        ConditionFlag.Occupied38,
        ConditionFlag.Occupied39,
        ConditionFlag.OccupiedInEvent,
        ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.OccupiedSummoningBell,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.Casting,
        ConditionFlag.Casting87,
        ConditionFlag.Crafting,
        ConditionFlag.PreparingToCraft,
        ConditionFlag.ExecutingCraftingAction,
        ConditionFlag.Gathering,
        ConditionFlag.ExecutingGatheringAction,
        ConditionFlag.Fishing,
        ConditionFlag.MeldingMateria,
        ConditionFlag.InCombat,
        ConditionFlag.Unconscious,
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.LoggingOut,
        ConditionFlag.Jumping,
        ConditionFlag.Jumping61,
        ConditionFlag.Mounting,
        ConditionFlag.Mounting71,
        ConditionFlag.MountOrOrnamentTransition,
        ConditionFlag.RidingPillion,
        ConditionFlag.UsingChocoboTaxi,
        ConditionFlag.CarryingItem,
        ConditionFlag.CarryingObject,
        ConditionFlag.BeingMoved,
        ConditionFlag.OperatingSiegeMachine,
        ConditionFlag.Performing,
        ConditionFlag.UsingHousingFunctions,
        ConditionFlag.TradeOpen,
        ConditionFlag.ChocoboRacing,
        ConditionFlag.PlayingMiniGame,
        ConditionFlag.PlayingLordOfVerminion,
        ConditionFlag.ReadyingVisitOtherWorld,
        ConditionFlag.WaitingToVisitOtherWorld,
        ConditionFlag.EditingPortrait,
        ConditionFlag.EditingStrategyBoard,
        ConditionFlag.DutyRecorderPlayback,
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
    /// Checks whether the local player cannot be made to act: missing, dead, casting, untargetable, jumping or
    /// falling, or in any of <see cref="OccupiedConditions"/>.
    /// </summary>
    /// <returns>True when the player should be left alone, also when no local player is loaded.</returns>
    public static bool IsLocalPlayerOccupied()
        => ReadLocalPlayerOccupancy().IsOccupied;

    /// <summary>
    /// Checks whether the local player cannot be made to act, leaving some of <see cref="OccupiedConditions"/> out.
    /// </summary>
    /// <param name="ignoredConditions">The flags not to count, such as the OccupiedInEvent an open market board raises.</param>
    /// <returns>True when the player should be left alone, also when no local player is loaded.</returns>
    public static bool IsLocalPlayerOccupied(params ConditionFlag[] ignoredConditions)
        => ReadLocalPlayerOccupancy(ignoredConditions).IsOccupied;

    /// <summary>Every cause that keeps the local player from acting. Framework thread only.</summary>
    /// <returns>The causes, <see cref="PlayerOccupancy.NoPlayer"/> without a local player.</returns>
    public static PlayerOccupancy ReadLocalPlayerOccupancy()
        => ReadLocalPlayerOccupancy([]);

    /// <summary>Every cause that keeps the local player from acting, some conditions ignored. Framework thread only.</summary>
    /// <param name="ignoredConditions">The flags not to count, such as the OccupiedInEvent a market board raises.</param>
    /// <returns>The causes, <see cref="PlayerOccupancy.NoPlayer"/> without a local player.</returns>
    public static unsafe PlayerOccupancy ReadLocalPlayerOccupancy(params ConditionFlag[] ignoredConditions)
    {
        if (!NoireService.IsInitialized() || NoireService.ObjectTable.LocalPlayer is not { } local || local.Address == 0)
            return PlayerOccupancy.NoPlayer;

        var native = GetCharacterAddress(local);

        return EvaluateOccupancy(
            local.IsDead,
            local.IsCasting,
            local.IsTargetable,
            native != null && native->IsJumping(),
            static flag => NoireService.Condition[flag],
            ignoredConditions);
    }

    internal static PlayerOccupancy EvaluateOccupancy(
        bool isDead,
        bool isCasting,
        bool isTargetable,
        bool isAirborne,
        Func<ConditionFlag, bool> isConditionActive,
        IReadOnlyCollection<ConditionFlag>? ignoredConditions)
    {
        var active = new List<ConditionFlag>();

        foreach (var flag in OccupiedConditions)
        {
            if (ignoredConditions?.Contains(flag) != true && isConditionActive(flag))
                active.Add(flag);
        }

        return new PlayerOccupancy(false, isDead, isCasting, !isTargetable, isAirborne, active);
    }

    /// <summary>The player owning a minion, pet or chocobo.</summary>
    /// <param name="characterAddress">The owned object's address.</param>
    /// <returns>The owner's address, or 0 when unowned or the owner is not in the object table.</returns>
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

    /// <summary>The player who owns a minion, pet or chocobo.</summary>
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

    /// <summary>The draw object a character is built from and the human skeleton it animates as. A body change produces a new <paramref name="DrawObject"/>.</summary>
    /// <param name="DrawObject">The address of the drawn model.</param>
    /// <param name="SkeletonId">The human skeleton id, such as "c0801".</param>
    public readonly record struct DrawnBody(nint DrawObject, string SkeletonId);

    /// <summary>The drawn model and skeleton of a character.</summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The drawn body, or null without a human draw object, as between models.</returns>
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

    /// <summary>The human skeleton id a character is drawn as.</summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The skeleton id, or null while there is no human model to read.</returns>
    public static string? GetDrawnSkeletonId(ICharacter character)
        => GetDrawnBody(character)?.SkeletonId;

    /// <summary>The animation folder a character's battle motions come from, such as "bt_swd_sld".</summary>
    /// <param name="character">The character to read.</param>
    /// <returns>The folder, or null without a human model or motion table.</returns>
    public static unsafe string? GetWeaponMotionFolder(ICharacter character)
    {
        if (GetDrawnBody(character) == null || WeaponMotionTable.Current is not { } table)
            return null;

        var native = GetCharacterAddress(character);
        if (native == null)
            return null;

        return WeaponMotionFolders.Compose(
            table.CodeFor(WeaponModelSetId(native, DrawDataContainer.WeaponSlot.MainHand)),
            table.CodeFor(WeaponModelSetId(native, DrawDataContainer.WeaponSlot.OffHand)));
    }

    /// <summary>Whether a character carries a second weapon. A two-handed weapon leaves the slot empty.</summary>
    /// <param name="character">The character to read.</param>
    /// <returns>Whether the off hand holds something.</returns>
    public static unsafe bool HasOffHandWeapon(ICharacter character)
    {
        if (GetDrawnBody(character) == null)
            return false;

        var native = GetCharacterAddress(character);

        return native != null && WeaponModelSetId(native, DrawDataContainer.WeaponSlot.OffHand) != 0;
    }

    // Falls back to the equipped model id while the weapon is hidden or loading. An empty hand reads as zero.
    private static unsafe ushort WeaponModelSetId(Character* native, DrawDataContainer.WeaponSlot slot)
    {
        ref var data = ref native->DrawData.Weapon(slot);

        return data.Weapon != null ? data.Weapon->ModelSetId : data.ModelId.Id;
    }

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
