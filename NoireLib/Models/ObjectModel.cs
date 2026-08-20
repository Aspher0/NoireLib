using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Numerics;

namespace NoireLib.Models;

/// <summary>
/// A serializable description of any game object, and the base class for more specific models such as
/// <see cref="PlayerModel"/>.
/// </summary>
[Serializable]
public class ObjectModel
{
    /// <summary>A unique identifier for this instance.</summary>
    public Guid UniqueId { get; set; } = Guid.NewGuid();

    /// <summary>The name of the object, empty for unnamed objects.</summary>
    public virtual string Name { get; set; } = string.Empty;

    /// <summary>The kind of the object.</summary>
    public ObjectKind ObjectKind { get; set; }

    /// <summary>The object's game data row id, stable across spawns.</summary>
    public uint? BaseId { get; set; } = null;

    /// <summary>The object's entity id, which changes on respawn and goes invalid on despawn.</summary>
    public uint? EntityId { get; set; } = null;

    /// <summary>The object's full game object id, which changes on respawn and goes invalid on despawn.</summary>
    public ulong? GameObjectId { get; set; } = null;

    /// <summary>The position recorded the last time the model was refreshed from a game object.</summary>
    public Vector3? LastKnownPosition { get; set; } = null;

    /// <summary>Creates a model from explicit values and schedules an object table refresh.</summary>
    /// <param name="name">The name of the object.</param>
    /// <param name="objectKind">The kind of the object.</param>
    /// <param name="baseId">The object's game data row id.</param>
    /// <param name="entityId">The object's entity id.</param>
    /// <param name="gameObjectId">The object's full game object id.</param>
    /// <param name="lastKnownPosition">The object's last known position.</param>
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

    /// <summary>Creates a model from explicit values, keeping an existing identifier.</summary>
    /// <param name="uniqueId">The identifier to keep for this instance.</param>
    /// <param name="name">The name of the object.</param>
    /// <param name="objectKind">The kind of the object.</param>
    /// <param name="baseId">The object's game data row id.</param>
    /// <param name="entityId">The object's entity id.</param>
    /// <param name="gameObjectId">The object's full game object id.</param>
    /// <param name="lastKnownPosition">The object's last known position.</param>
    [JsonConstructor]
    public ObjectModel(Guid uniqueId, string name, ObjectKind objectKind, uint? baseId = null, uint? entityId = null, ulong? gameObjectId = null, Vector3? lastKnownPosition = null)
        : this(name, objectKind, baseId, entityId, gameObjectId, lastKnownPosition)
    {
        UniqueId = uniqueId;
    }

    /// <summary>Creates a model from a live game object.</summary>
    /// <param name="gameObject">The object to copy from.</param>
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
    /// Creates a model for a derived class without scheduling an object table refresh, which the derived
    /// constructor must do once its own fields are set.
    /// </summary>
    /// <param name="uniqueId">The identifier to keep, or null to generate one.</param>
    /// <param name="name">The name of the object.</param>
    /// <param name="objectKind">The kind of the object.</param>
    protected ObjectModel(Guid? uniqueId, string name, ObjectKind objectKind)
    {
        if (uniqueId.HasValue)
            UniqueId = uniqueId.Value;

        Name = name;
        ObjectKind = objectKind;
    }

    /// <summary>Overwrites this model's fields from a live game object.</summary>
    /// <param name="gameObject">The object to copy from.</param>
    public virtual void UpdateFromObject(IGameObject gameObject)
    {
        Name = gameObject.Name.TextValue;
        ObjectKind = gameObject.ObjectKind;
        BaseId = gameObject.BaseId;
        EntityId = gameObject.EntityId;
        GameObjectId = gameObject.GameObjectId;
        LastKnownPosition = gameObject.Position;
    }

    /// <summary>Refreshes this model from the matching object in the object table. Framework thread only.</summary>
    /// <returns>True when a matching object was found and the model was updated.</returns>
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
    /// Finds this object in the current object table, matching on <see cref="GameObjectId"/> first, then
    /// <see cref="EntityId"/>, then on kind, base id and name. Framework thread only.
    /// </summary>
    /// <returns>The matching object, or null when none is present.</returns>
    public virtual IGameObject? FindObjectOnMap()
    {
        var objectTable = NoireService.ObjectTable;

        if (GameObjectId.HasValue)
        {
            var byGameObjectId = objectTable.SearchById(GameObjectId.Value);
            if (byGameObjectId != null)
                return byGameObjectId;
        }

        if (EntityId.HasValue && EntityId.Value != 0 && EntityId.Value != Helpers.GameObjectHelper.NoTargetId)
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

    /// <summary>Whether this object is currently present in the object table. Framework thread only.</summary>
    /// <returns>True when the object was found.</returns>
    public bool IsOnMap() => FindObjectOnMap() != null;

    /// <summary>Measures the distance from another object to this one. Framework thread only.</summary>
    /// <param name="_object">The object to measure from.</param>
    /// <returns>The distance, or null when this object is not present.</returns>
    public float? DistanceFromObject(IGameObject _object)
    {
        var objectPosition = _object.Position;
        var thisObject = FindObjectOnMap();
        if (thisObject == null)
            return null;
        var thisObjectPosition = thisObject.Position;
        return Vector3.Distance(objectPosition, thisObjectPosition);
    }

    /// <summary>Measures the distance from the local player to this object. Framework thread only.</summary>
    /// <returns>The distance, or null when either is not present.</returns>
    public float? DistanceFromLocalPlayer()
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return null;
        return DistanceFromObject(localPlayer);
    }

    /// <summary>Whether this object is within the local player's 4 yalm interaction reach. Framework thread only.</summary>
    /// <returns>True when the object is in reach.</returns>
    public bool IsInteractable()
    {
        var distance = DistanceFromLocalPlayer();
        return distance.HasValue && distance.Value <= 4.0f;
    }

    /// <summary>Compares this model with another on name, kind, base id, entity id and game object id.</summary>
    /// <param name="other">The model to compare with.</param>
    /// <returns>True when every compared field matches.</returns>
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

    /// <summary>Compares this model with a live game object on name, kind, base id, entity id and game object id.</summary>
    /// <param name="gameObject">The object to compare with.</param>
    /// <returns>True when every compared field matches.</returns>
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

    /// <summary>Copies this model, keeping its identifier.</summary>
    /// <returns>The copy.</returns>
    public virtual ObjectModel Clone()
    {
        return new ObjectModel(UniqueId, Name, ObjectKind, BaseId, EntityId, GameObjectId, LastKnownPosition);
    }
}
