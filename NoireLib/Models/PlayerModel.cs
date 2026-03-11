using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using NoireLib.Helpers;
using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;

namespace NoireLib.Models;

/// <summary>
/// A model representing a player character.<br/>
/// Allows easy access to player-related information such as name, homeWorld, world ID, and content ID.
/// </summary>
[Serializable]
public class PlayerModel
{
    /// <summary>
    /// A unique identifier for this PlayerModel instance.
    /// </summary>
    public string UniqueId { get; set; } = RandomGenerator.GenerateGuidString();

    /// <summary>
    /// The name of the player, without the homeWorld.
    /// </summary>
    public string PlayerName { get; set; }

    /// <summary>
    /// The name of the player's homeWorld.
    /// </summary>
    public string HomeWorld { get; set; }

    /// <summary>
    /// The world ID of the player's homeWorld.
    /// </summary>
    public uint? HomeWorldId { get; set; } = null;

    /// <summary>
    /// The name of the player's current world.
    /// </summary>
    public string? CurrentWorld { get; set; } = null;

    /// <summary>
    /// The world ID of the player's homeWorld.
    /// </summary>
    public uint? CurrentWorldId { get; set; } = null;

    /// <summary>
    /// The content ID (CID) of the player.
    /// </summary>
    public ulong? ContentId { get; set; } = null;

    /// <summary>
    /// Returns the full name of the player in the format "PlayerName@HomeWorld".
    /// </summary>
    public string FullName => $"{PlayerName}@{HomeWorld}";

    /// <summary>
    /// Constructs a new PlayerModel with the specified values.
    /// </summary>
    /// <param name="playerName">The name of the player.</param>
    /// <param name="homeWorld">The homeWorld of the player.</param>
    /// <param name="currentWorld">The current world of the player.</param>
    /// <param name="homeWorldId">The world ID of the player's homeWorld (optional).</param>
    /// <param name="currentWorldId">The world ID of the player's current world (optional).</param>
    /// <param name="contentId">The content ID (CID) of the player (optional).</param>
    public PlayerModel(string playerName, string homeWorld, string? currentWorld, uint? homeWorldId = null, uint? currentWorldId = null, ulong? contentId = null)
    {
        PlayerName = playerName;
        HomeWorld = homeWorld;
        HomeWorldId = homeWorldId;
        CurrentWorld = currentWorld;
        CurrentWorldId = currentWorldId;
        ContentId = contentId;

        TryUpdateFromObjectTable();
    }

    /// <summary>
    /// Constructs a new PlayerModel with the specified values.
    /// </summary>
    /// <param name="uniqueId">A unique identifier for this PlayerModel instance.</param>
    /// <param name="playerName">The name of the player.</param>
    /// <param name="homeWorld">The homeWorld of the player.</param>
    /// <param name="currentWorld">The current world of the player.</param>
    /// <param name="homeWorldId">The world ID of the player's homeWorld (optional).</param>
    /// <param name="currentWorldId">The world ID of the player's current world (optional).</param>
    /// <param name="contentId">The content ID (CID) of the player (optional).</param>
    [JsonConstructor]
    public PlayerModel(string uniqueId, string playerName, string homeWorld, string? currentWorld, uint? homeWorldId = null, uint? currentWorldId = null, ulong? contentId = null)
    {
        UniqueId = uniqueId;
        PlayerName = playerName;
        HomeWorld = homeWorld;
        HomeWorldId = homeWorldId;
        CurrentWorld = currentWorld;
        CurrentWorldId = currentWorldId;
        ContentId = contentId;

        TryUpdateFromObjectTable();
    }

    /// <summary>
    /// Constructs a new PlayerModel from an IPlayerCharacter object.
    /// </summary>
    /// <param name="character">The IPlayerCharacter object to extract data from.</param>
    public unsafe PlayerModel(IPlayerCharacter character)
    {
        PlayerName = character.Name.TextValue;
        HomeWorld = character.HomeWorld.Value.Name.ExtractText();
        HomeWorldId = character.HomeWorld.Value.RowId;
        CurrentWorld = character.CurrentWorld.Value.Name.ExtractText();
        CurrentWorldId = character.CurrentWorld.Value.RowId;
        ContentId = CharacterHelper.GetCIDFromPlayerCharacterAddress((nint)CharacterHelper.GetCharacterAddress(character));
    }

    /// <summary>
    /// Updates the PlayerModel's data from the given IPlayerCharacter object.
    /// </summary>
    /// <param name="character">The IPlayerCharacter object to extract data from.</param>
    public unsafe void UpdateFromCharacter(IPlayerCharacter character)
    {
        PlayerName = character.Name.TextValue;
        HomeWorld = character.HomeWorld.Value.Name.ExtractText();
        HomeWorldId = character.HomeWorld.Value.RowId;
        CurrentWorld = character.CurrentWorld.Value.Name.ExtractText();
        CurrentWorldId = character.CurrentWorld.Value.RowId;
        ContentId = CharacterHelper.GetCIDFromPlayerCharacterAddress((nint)CharacterHelper.GetCharacterAddress(character));
    }

    /// <summary>
    /// Tries to update the PlayerModel's data by searching the Object Table for a matching player character.
    /// </summary>
    /// <returns>True if a matching character was found and the model was updated; otherwise, false.</returns>
    public bool TryUpdateFromObjectTable()
    {
        var matchingCharacter = FindPlayerOnMap();

        if (matchingCharacter != null)
        {
            UpdateFromCharacter(matchingCharacter);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to find the player character on the current map based on PlayerName and HomeWorld.
    /// </summary>
    /// <returns></returns>
    public IPlayerCharacter? FindPlayerOnMap()
    {
        var matchingCharacter = NoireService.ObjectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(pc =>
                pc.Name.TextValue == PlayerName &&
                pc.HomeWorld.Value.Name.ExtractText() == HomeWorld);

        return matchingCharacter;
    }

    /// <summary>
    /// Gets the distance between the specified object and the character represented by this PlayerModel.
    /// </summary>
    /// <param name="_object">The object to measure the distance from.</param>
    /// <returns>The distance between the object and the character, or null if </returns>
    public float? DistanceFromObject(IGameObject _object)
    {
        var objectPosition = _object.Position;
        var character = FindPlayerOnMap();
        if (character == null)
            return null;
        var characterPosition = character.Position;
        return Vector3.Distance(objectPosition, characterPosition);
    }

    /// <summary>
    /// Checks whether the character represented by this PlayerModel is currently interactable by the local player (i.e., is within a reach a 4 yalms).
    /// </summary>
    /// <returns>True if the character is in reach to be interacted with; otherwise false.</returns>
    public bool IsInteractable()
    {
        var playerCharacter = FindPlayerOnMap();
        if (playerCharacter == null || NoireService.ObjectTable.LocalPlayer == null)
            return false;
        return DistanceFromObject(NoireService.ObjectTable.LocalPlayer) <= 4.0f;
    }

    /// <summary>
    /// Determines whether this character is currently in their home world.
    /// </summary>
    /// <returns>True if the character is in their home world; otherwise, false.</returns>
    public bool IsInHomeWorld()
    {
        TryUpdateFromObjectTable();
        return CurrentWorld == HomeWorld;
    }

    /// <summary>
    /// Checks if this PlayerModel is equal to another PlayerModel based on PlayerName, HomeWorld, HomeWorldId, and ContentId.
    /// </summary>
    /// <param name="other">The other PlayerModel to compare with.</param>
    /// <returns>True if the models are equal; otherwise, false.</returns>
    public bool Equals(PlayerModel? other)
    {
        if (other == null)
            return false;
        return PlayerName == other.PlayerName &&
               HomeWorld == other.HomeWorld &&
               HomeWorldId == other.HomeWorldId &&
               ContentId == other.ContentId;
    }

    /// <summary>
    /// Checks if this PlayerModel is equal to an IPlayerCharacter based on PlayerName, HomeWorld, HomeWorldId, and ContentId.
    /// </summary>
    /// <param name="character">The IPlayerCharacter to compare with.</param>
    /// <returns>True if the character is equal to this model; otherwise, false.</returns>
    public unsafe bool Equals(IPlayerCharacter character)
    {
        if (character == null)
            return false;
        return PlayerName == character.Name.TextValue &&
               HomeWorld == character.HomeWorld.Value.Name.ExtractText() &&
               HomeWorldId == character.HomeWorld.Value.RowId &&
               ContentId == CharacterHelper.GetCIDFromPlayerCharacterAddress((nint)CharacterHelper.GetCharacterAddress(character));
    }

    /// <summary>
    /// Clones this PlayerModel instance.
    /// </summary>
    /// <returns>The cloned PlayerModel.</returns>
    public PlayerModel Clone()
    {
        return new PlayerModel(UniqueId, PlayerName, HomeWorld, CurrentWorld, HomeWorldId, CurrentWorldId, ContentId);
    }
}
