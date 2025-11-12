using Dalamud.Game.ClientState.Objects.SubKinds;
using NoireLib.Helpers;
using System.Linq;

namespace NoireLib.Models;

/// <summary>
/// A model representing a player character.<br/>
/// Allows easy access to player-related information such as name, homeworld, world ID, and content ID.
/// </summary>
public class PlayerModel
{
    /// <summary>
    /// The name of the player, without the homeworld.
    /// </summary>
    public string PlayerName { get; set; }

    /// <summary>
    /// The name of the player's homeworld.
    /// </summary>
    public string Homeworld { get; set; }

    /// <summary>
    /// The world ID of the player's homeworld.
    /// </summary>
    public uint? WorldId { get; set; } = null;

    /// <summary>
    /// The content ID (CID) of the player.
    /// </summary>
    public ulong? ContentId { get; set; } = null;

    /// <summary>
    /// Returns the full name of the player in the format "PlayerName@Homeworld".
    /// </summary>
    public string FullName => $"{PlayerName}@{Homeworld}";

    /// <summary>
    /// Constructs a new PlayerModel with the specified values.
    /// </summary>
    /// <param name="playerName">The name of the player.</param>
    /// <param name="homeworld">The homeworld of the player.</param>
    /// <param name="worldId">The world ID of the player's homeworld (optional).</param>
    /// <param name="contentId">The content ID (CID) of the player (optional).</param>
    public PlayerModel(string playerName, string homeworld, uint? worldId = null, ulong? contentId = null)
    {
        PlayerName = playerName;
        Homeworld = homeworld;
        WorldId = worldId;
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
        Homeworld = character.HomeWorld.Value.Name.ExtractText();
        WorldId = character.HomeWorld.Value.RowId;
        ContentId = CharacterHelper.GetCIDFromPlayerCharacterAddress((nint)CharacterHelper.GetCharacterAddress(character));
    }

    /// <summary>
    /// Updates the PlayerModel's data from the given IPlayerCharacter object.
    /// </summary>
    /// <param name="character">The IPlayerCharacter object to extract data from.</param>
    public unsafe void UpdateFromCharacter(IPlayerCharacter character)
    {
        PlayerName = character.Name.TextValue;
        Homeworld = character.HomeWorld.Value.Name.ExtractText();
        WorldId = character.HomeWorld.Value.RowId;
        ContentId = CharacterHelper.GetCIDFromPlayerCharacterAddress((nint)CharacterHelper.GetCharacterAddress(character));
    }

    /// <summary>
    /// Tries to update the PlayerModel's data by searching the Object Table for a matching player character.
    /// </summary>
    /// <returns>True if a matching character was found and the model was updated; otherwise, false.</returns>
    public bool TryUpdateFromObjectTable()
    {
        var matchingCharacter = NoireService.ObjectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(pc =>
                pc.Name.TextValue == PlayerName &&
                pc.HomeWorld.Value.Name.ExtractText() == Homeworld);

        if (matchingCharacter != null)
        {
            UpdateFromCharacter(matchingCharacter);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if this PlayerModel is equal to another PlayerModel based on PlayerName, Homeworld, WorldId, and ContentId.
    /// </summary>
    /// <param name="other">The other PlayerModel to compare with.</param>
    /// <returns>True if the models are equal; otherwise, false.</returns>
    public bool Equals(PlayerModel? other)
    {
        if (other == null)
            return false;
        return PlayerName == other.PlayerName &&
               Homeworld == other.Homeworld &&
               WorldId == other.WorldId &&
               ContentId == other.ContentId;
    }

    /// <summary>
    /// Checks if this PlayerModel is equal to an IPlayerCharacter based on PlayerName, Homeworld, WorldId, and ContentId.
    /// </summary>
    /// <param name="character">The IPlayerCharacter to compare with.</param>
    /// <returns>True if the character is equal to this model; otherwise, false.</returns>
    public unsafe bool Equals(IPlayerCharacter character)
    {
        if (character == null)
            return false;
        return PlayerName == character.Name.TextValue &&
               Homeworld == character.HomeWorld.Value.Name.ExtractText() &&
               WorldId == character.HomeWorld.Value.RowId &&
               ContentId == CharacterHelper.GetCIDFromPlayerCharacterAddress((nint)CharacterHelper.GetCharacterAddress(character));
    }
}
