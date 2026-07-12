using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Numerics;

namespace NoireLib.Models;

/// <summary>
/// A model representing any game object (NPC, retainer, minion, mount, event object, etc.).<br/>
/// Serves as the base class for more specific models such as <see cref="PlayerModel"/>.<br/>
/// Allows easy access to object-related information such as name, kind, base ID, entity ID, and last known position.
/// </summary>
[Serializable]
public class ObjectModel
{
    /// <summary>
    /// A unique identifier for this ObjectModel instance.
    /// </summary>
    public Guid UniqueId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The name of the object. May be empty for unnamed objects.
    /// </summary>
    public virtual string Name { get; set; } = string.Empty;

    /// <summary>
    /// The kind of the object (Pc, BattleNpc, EventNpc, EventObj, Companion, etc.).
    /// </summary>
    public ObjectKind ObjectKind { get; set; }

    /// <summary>
    /// The base ID of the object (its game data row ID).<br/>
    /// Stable across spawns of the same object type.
    /// </summary>
    public uint? BaseId { get; set; } = null;

    /// <summary>
    /// The entity ID of the object.<br/>
    /// Not stable across spawns; may be invalid after the object despawns.
    /// </summary>
    public uint? EntityId { get; set; } = null;

    /// <summary>
    /// The full game object ID of the object.<br/>
    /// Not stable across spawns; may be invalid after the object despawns.
    /// </summary>
    public ulong? GameObjectId { get; set; } = null;

    /// <summary>
    /// The last known position of the object, updated whenever the model is refreshed from a game object.
    /// </summary>
    public Vector3? LastKnownPosition { get; set; } = null;

    /// <summary>
    /// Constructs a new ObjectModel with the specified values.
    /// </summary>
    /// <param name="name">The name of the object.</param>
    /// <param name="objectKind">The kind of the object.</param>
    /// <param name="baseId">The base ID of the object (optional).</param>
    /// <param name="entityId">The entity ID of the object (optional).</param>
    /// <param name="gameObjectId">The full game object ID of the object (optional).</param>
    /// <param name="lastKnownPosition">The last known position of the object (optional).</param>
    public ObjectModel(string name, ObjectKind objectKind, uint? baseId = null, uint? entityId = null, ulong? gameObjectId = null, Vector3? lastKnownPosition = null)
    {
        Name = name;
        ObjectKind = objectKind;
        BaseId = baseId;
        EntityId = entityId;
        GameObjectId = gameObjectId;
        LastKnownPosition = lastKnownPosition;

        NoireService.Framework.RunOnFrameworkThread(TryUpdateFromObjectTable);
    }

    /// <summary>
    /// Constructs a new ObjectModel with the specified values.
    /// </summary>
    /// <param name="uniqueId">A unique identifier for this ObjectModel instance.</param>
    /// <param name="name">The name of the object.</param>
    /// <param name="objectKind">The kind of the object.</param>
    /// <param name="baseId">The base ID of the object (optional).</param>
    /// <param name="entityId">The entity ID of the object (optional).</param>
    /// <param name="gameObjectId">The full game object ID of the object (optional).</param>
    /// <param name="lastKnownPosition">The last known position of the object (optional).</param>
    [JsonConstructor]
    public ObjectModel(Guid uniqueId, string name, ObjectKind objectKind, uint? baseId = null, uint? entityId = null, ulong? gameObjectId = null, Vector3? lastKnownPosition = null)
        : this(name, objectKind, baseId, entityId, gameObjectId, lastKnownPosition)
    {
        UniqueId = uniqueId;
    }

    /// <summary>
    /// Constructs a new ObjectModel from an IGameObject instance.
    /// </summary>
    /// <param name="gameObject">The IGameObject instance to extract data from.</param>
    public ObjectModel(IGameObject gameObject)
    {
        Name = gameObject.Name.TextValue;
        ObjectKind = gameObject.ObjectKind;
        BaseId = gameObject.BaseId;
        EntityId = gameObject.EntityId;
        GameObjectId = gameObject.GameObjectId;
        LastKnownPosition = gameObject.Position;
    }

    /// <summary>
    /// Minimal constructor for derived classes. Does not schedule an object table refresh:<br/>
    /// derived constructors should do so themselves once all their fields are set.
    /// </summary>
    /// <param name="uniqueId">A unique identifier for this instance, or null to generate a new one.</param>
    /// <param name="name">The name of the object.</param>
    /// <param name="objectKind">The kind of the object.</param>
    protected ObjectModel(Guid? uniqueId, string name, ObjectKind objectKind)
    {
        if (uniqueId.HasValue)
            UniqueId = uniqueId.Value;

        Name = name;
        ObjectKind = objectKind;
    }

    /// <summary>
    /// Updates the ObjectModel's data from the given IGameObject instance.
    /// </summary>
    /// <param name="gameObject">The IGameObject instance to extract data from.</param>
    public virtual void UpdateFromObject(IGameObject gameObject)
    {
        Name = gameObject.Name.TextValue;
        ObjectKind = gameObject.ObjectKind;
        BaseId = gameObject.BaseId;
        EntityId = gameObject.EntityId;
        GameObjectId = gameObject.GameObjectId;
        LastKnownPosition = gameObject.Position;
    }

    /// <summary>
    /// Tries to update the ObjectModel's data by searching the Object Table for a matching object.
    /// </summary>
    /// <returns>True if a matching object was found and the model was updated; otherwise, false.</returns>
    public bool TryUpdateFromObjectTable()
    {
        var matchingObject = FindObjectOnMap();

        if (matchingObject != null)
        {
            UpdateFromObject(matchingObject);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to find the object on the current map.<br/>
    /// Matches by GameObjectId first, then EntityId, then by ObjectKind, BaseId and Name.
    /// </summary>
    /// <returns>The matching IGameObject instance, or null if not found.</returns>
    public virtual IGameObject? FindObjectOnMap()
    {
        var objectTable = NoireService.ObjectTable;

        if (GameObjectId.HasValue)
        {
            var byGameObjectId = objectTable.FirstOrDefault(o => o != null && o.GameObjectId == GameObjectId.Value);
            if (byGameObjectId != null)
                return byGameObjectId;
        }

        if (EntityId.HasValue && EntityId.Value != 0 && EntityId.Value != 0xE0000000)
        {
            var byEntityId = objectTable.FirstOrDefault(o => o != null && o.EntityId == EntityId.Value);
            if (byEntityId != null)
                return byEntityId;
        }

        return objectTable.FirstOrDefault(o =>
            o != null &&
            o.ObjectKind == ObjectKind &&
            (!BaseId.HasValue || o.BaseId == BaseId.Value) &&
            (string.IsNullOrEmpty(Name) || o.Name.TextValue == Name));
    }

    /// <summary>
    /// Checks whether the object represented by this ObjectModel is currently present in the Object Table.
    /// </summary>
    /// <returns>True if the object was found on the current map; otherwise, false.</returns>
    public bool IsOnMap() => FindObjectOnMap() != null;

    /// <summary>
    /// Gets the distance between the specified object and the object represented by this ObjectModel.
    /// </summary>
    /// <param name="_object">The object to measure the distance from.</param>
    /// <returns>The distance between the two objects, or null if this object was not found on the map.</returns>
    public float? DistanceFromObject(IGameObject _object)
    {
        var objectPosition = _object.Position;
        var thisObject = FindObjectOnMap();
        if (thisObject == null)
            return null;
        var thisObjectPosition = thisObject.Position;
        return Vector3.Distance(objectPosition, thisObjectPosition);
    }

    /// <summary>
    /// Gets the distance between the local player and the object represented by this ObjectModel.
    /// </summary>
    /// <returns>The distance between the local player and the object, or null if either was not found.</returns>
    public float? DistanceFromLocalPlayer()
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return null;
        return DistanceFromObject(localPlayer);
    }

    /// <summary>
    /// Checks whether the object represented by this ObjectModel is currently interactable by the local player (i.e., is within a reach of 4 yalms).
    /// </summary>
    /// <returns>True if the object is in reach to be interacted with; otherwise false.</returns>
    public bool IsInteractable()
    {
        var distance = DistanceFromLocalPlayer();
        return distance.HasValue && distance.Value <= 4.0f;
    }

    /// <summary>
    /// Checks if this ObjectModel is equal to another ObjectModel based on Name, ObjectKind, BaseId, EntityId, and GameObjectId.
    /// </summary>
    /// <param name="other">The other ObjectModel to compare with.</param>
    /// <returns>True if the models are equal; otherwise, false.</returns>
    public bool Equals(ObjectModel? other)
    {
        if (other == null)
            return false;
        return Name == other.Name &&
               ObjectKind == other.ObjectKind &&
               BaseId == other.BaseId &&
               EntityId == other.EntityId &&
               GameObjectId == other.GameObjectId;
    }

    /// <summary>
    /// Checks if this ObjectModel is equal to an IGameObject based on Name, ObjectKind, BaseId, EntityId, and GameObjectId.
    /// </summary>
    /// <param name="gameObject">The IGameObject to compare with.</param>
    /// <returns>True if the object is equal to this model; otherwise, false.</returns>
    public bool Equals(IGameObject? gameObject)
    {
        if (gameObject == null)
            return false;
        return Name == gameObject.Name.TextValue &&
               ObjectKind == gameObject.ObjectKind &&
               BaseId == gameObject.BaseId &&
               EntityId == gameObject.EntityId &&
               GameObjectId == gameObject.GameObjectId;
    }

    /// <summary>
    /// Clones this ObjectModel instance.
    /// </summary>
    /// <returns>The cloned ObjectModel.</returns>
    public virtual ObjectModel Clone()
    {
        return new ObjectModel(UniqueId, Name, ObjectKind, BaseId, EntityId, GameObjectId, LastKnownPosition);
    }
}
