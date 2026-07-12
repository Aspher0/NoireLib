using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Newtonsoft.Json;
using NoireLib.Helpers;
using System;
using System.Linq;

namespace NoireLib.Models;

/// <summary>
/// A model representing a player character.<br/>
/// Inherits the generic object data and helpers from <see cref="ObjectModel"/>, and adds player-related information such as homeWorld, world ID, and content ID.
/// </summary>
[Serializable]
public class PlayerModel : ObjectModel
{
    /// <summary>
    /// The name of the object. Ignored during serialization on player models: <see cref="PlayerName"/> is serialized instead.
    /// </summary>
    [JsonIgnore]
    public override string Name
    {
        get => base.Name;
        set => base.Name = value;
    }

    /// <summary>
    /// The name of the player, without the homeWorld.
    /// </summary>
    public string PlayerName
    {
        get => Name;
        set => Name = value;
    }

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
    public PlayerModel(string playerName, string homeWorld, string? currentWorld = null, uint? homeWorldId = null, uint? currentWorldId = null, ulong? contentId = null)
        : base(null, playerName, ObjectKind.Pc)
    {
        HomeWorld = homeWorld;
        HomeWorldId = homeWorldId;
        CurrentWorld = currentWorld;
        CurrentWorldId = currentWorldId;
        ContentId = contentId;

        NoireService.Framework.RunOnFrameworkThread(TryUpdateFromObjectTable);
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
    public PlayerModel(Guid uniqueId, string playerName, string homeWorld, string? currentWorld = null, uint? homeWorldId = null, uint? currentWorldId = null, ulong? contentId = null)
        : base(uniqueId, playerName, ObjectKind.Pc)
    {
        HomeWorld = homeWorld;
        HomeWorldId = homeWorldId;
        CurrentWorld = currentWorld;
        CurrentWorldId = currentWorldId;
        ContentId = contentId;

        NoireService.Framework.RunOnFrameworkThread(TryUpdateFromObjectTable);
    }

    /// <summary>
    /// Constructs a new PlayerModel from an IPlayerCharacter object.
    /// </summary>
    /// <param name="character">The IPlayerCharacter object to extract data from.</param>
    public unsafe PlayerModel(IPlayerCharacter character)
        : base(character)
    {
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
        base.UpdateFromObject(character);

        HomeWorld = character.HomeWorld.Value.Name.ExtractText();
        HomeWorldId = character.HomeWorld.Value.RowId;
        CurrentWorld = character.CurrentWorld.Value.Name.ExtractText();
        CurrentWorldId = character.CurrentWorld.Value.RowId;
        ContentId = CharacterHelper.GetCIDFromPlayerCharacterAddress((nint)CharacterHelper.GetCharacterAddress(character));
    }

    /// <summary>
    /// Updates the PlayerModel's data from the given IGameObject instance.<br/>
    /// Player-specific data is only updated when the object is an IPlayerCharacter.
    /// </summary>
    /// <param name="gameObject">The IGameObject instance to extract data from.</param>
    public override void UpdateFromObject(IGameObject gameObject)
    {
        if (gameObject is IPlayerCharacter character)
            UpdateFromCharacter(character);
        else
            base.UpdateFromObject(gameObject);
    }

    /// <summary>
    /// Tries to find the player character on the current map based on PlayerName and HomeWorld.
    /// </summary>
    /// <returns>The matching IPlayerCharacter instance, or null if not found.</returns>
    public IPlayerCharacter? FindPlayerOnMap()
    {
        return NoireService.ObjectTable
            .OfType<IPlayerCharacter>()
            .FirstOrDefault(pc =>
                pc.Name.TextValue == PlayerName &&
                pc.HomeWorld.Value.Name.ExtractText() == HomeWorld);
    }

    /// <summary>
    /// Tries to find the player character on the current map based on PlayerName and HomeWorld.
    /// </summary>
    /// <returns>The matching IPlayerCharacter instance, or null if not found.</returns>
    public override IGameObject? FindObjectOnMap() => FindPlayerOnMap();

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
    public override PlayerModel Clone()
    {
        return new PlayerModel(UniqueId, PlayerName, HomeWorld, CurrentWorld, HomeWorldId, CurrentWorldId, ContentId);
    }
}
