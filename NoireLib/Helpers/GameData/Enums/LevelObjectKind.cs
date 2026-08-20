namespace NoireLib.Helpers;

/// <summary>
/// The kind of a placed level object, mapped from Lumina's <c>LayerEntryType</c> down to the kinds a consumer
/// usually asks for.
/// </summary>
public enum LevelObjectKind
{
    /// <summary>Any object kind <see cref="LevelFileHelper"/> does not map, such as scenery, sound, or a trigger box.</summary>
    Other,

    /// <summary>A seamless zone-boundary trigger volume (ExitRange), carrying the territory it leads to.</summary>
    ExitRange,

    /// <summary>An aetheryte or aethernet-shard crystal placement, carrying its Aetheryte row id.</summary>
    Aetheryte,

    /// <summary>A shared-group instance, such as a residential aethernet crystal, carrying its SGB asset path.</summary>
    SharedGroup,

    /// <summary>A spawn or arrival volume that a zone transition or a warp lands the character in.</summary>
    PopRange,

    /// <summary>An event NPC placement, carrying the ENpcBase row id.</summary>
    EventNpc,

    /// <summary>An event object placement, carrying the EObj row id.</summary>
    EventObject,
}
