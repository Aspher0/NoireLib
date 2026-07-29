using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// What an <see cref="LevelObjectKind.ExitRange"/> trigger volume does when the character walks into it. The two
/// kinds read out of the same object and differ only in whether a territory is named; without the kind carried
/// alongside, one that names none looks like a broken zone line.
/// </summary>
public enum LevelExitKind : byte
{
    /// <summary>The object is not an exit range, or its trigger type is one no level file in the game authors.</summary>
    None,

    /// <summary>
    /// A seamless zone boundary: walking into it loads the territory the object names and lands the character on
    /// that territory's own arrival volume.
    /// </summary>
    ZoneLine,

    /// <summary>
    /// A teleport within the territory the trigger already stands in; it names no destination territory. The game
    /// authors these in facing pairs a short distance apart, each sending the character to where its partner sends
    /// them back from. This is how an underwater passage is crossed; a route that only follows zone lines can never
    /// find one.
    /// </summary>
    IntraZoneTeleport,
}

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

/// <summary>
/// One placed level object, flattened from a Lumina LGB instance into the fields a consumer reads. Kind-specific
/// fields are zero or empty when they do not apply, which keeps a hand-built fixture to only the fields the code
/// under test cares about.
/// </summary>
/// <param name="Kind">The object's kind.</param>
/// <param name="InstanceId">The object's instance id, unique within its level file and the key the game's own layout is keyed by.</param>
/// <param name="Position">The object's world position, from its transform translation.</param>
/// <param name="DestTerritoryId">
/// For an <see cref="LevelObjectKind.ExitRange"/>, the destination territory; zero otherwise, and also zero for an
/// <see cref="LevelExitKind.IntraZoneTeleport"/>, which stays in the territory it starts in and so names none.
/// </param>
/// <param name="DestInstanceId">
/// For an <see cref="LevelObjectKind.ExitRange"/>, the destination PopRange instance id. It is resolved against the
/// destination territory for a zone line and against the source territory for an intra-zone teleport.
/// </param>
/// <param name="BaseId">For an aetheryte, event NPC, or event object, its sheet row id; zero otherwise.</param>
/// <param name="AssetPath">For a <see cref="LevelObjectKind.SharedGroup"/>, its SGB asset path; empty otherwise.</param>
/// <param name="Yaw">The object's rotation about the up axis, in radians, which reconstructs an ExitRange's box; zero otherwise.</param>
/// <param name="Scale">The object's scale, an ExitRange's box half-extents; the default otherwise.</param>
/// <param name="FestivalId">
/// The festival that must be running for the layer to be placed, or zero when always placed. Ignoring this shows
/// every past year's seasonal decorations as permanently present.
/// </param>
/// <param name="FestivalPhase">The festival phase the layer belongs to, or zero when it belongs to every phase.</param>
/// <param name="ExitKind">
/// For an <see cref="LevelObjectKind.ExitRange"/>, what the trigger does; <see cref="LevelExitKind.None"/> otherwise.
/// </param>
/// <param name="ReturnInstanceId">
/// For an <see cref="LevelObjectKind.ExitRange"/>, the PopRange the game returns the character to, which an
/// intra-zone teleport pair uses to name each other's landing spot; zero when the object names none.
/// </param>
/// <param name="LayerTerritories">
/// The TerritoryType rows the object's layer belongs to, from <see cref="LayerSetHelper.ReadLayerTerritories"/>.
/// Null or empty when the layer is unconditional, which is most of them. Several territories share one level
/// directory, so an object whose layer names only the others is not standing in this territory at all.
/// </param>
public readonly record struct LevelObject(
    LevelObjectKind Kind,
    uint InstanceId,
    Vector3 Position,
    uint DestTerritoryId = 0,
    uint DestInstanceId = 0,
    uint BaseId = 0,
    string AssetPath = "",
    float Yaw = 0f,
    Vector3 Scale = default,
    ushort FestivalId = 0,
    ushort FestivalPhase = 0,
    LevelExitKind ExitKind = LevelExitKind.None,
    uint ReturnInstanceId = 0,
    IReadOnlyList<uint>? LayerTerritories = null)
{
    /// <summary>Whether the object's layer is part of a territory.</summary>
    /// <param name="territoryId">The territory being read.</param>
    /// <returns>True when it is, and for any object whose layer is unconditional.</returns>
    public bool BelongsTo(uint territoryId) => LayerSetHelper.Belongs(LayerTerritories, territoryId);
}

/// <summary>
/// What to keep from a level file: unfiltered, a whole-world pass would hold millions of scenery objects rather
/// than a few thousand. Every field is optional; the default keeps only the mapped kinds, so
/// <see cref="LevelFileHelper.ReadObjects(string, string, LevelObjectFilter)"/> needs no arguments.
/// </summary>
public readonly record struct LevelObjectFilter
{
    /// <summary>Keeps every mapped kind and drops <see cref="LevelObjectKind.Other"/>, which is the default.</summary>
    public static LevelObjectFilter Default => default;

    /// <summary>Keeps everything the file holds, including the kinds the mapping does not describe.</summary>
    public static LevelObjectFilter Everything => new() { IncludeUnmappedKinds = true };

    /// <summary>
    /// The only kinds to keep, or null to keep every mapped kind. Use it when one pass wants only crystals or only
    /// exit ranges, so nothing else is even allocated.
    /// </summary>
    public IReadOnlySet<LevelObjectKind>? Kinds { get; init; }

    /// <summary>
    /// The event-NPC base ids to keep, or null to keep every event NPC. A level file can hold thousands; passing a
    /// handful here keeps a whole-world pass inside memory.
    /// </summary>
    public IReadOnlySet<uint>? EventNpcBaseIds { get; init; }

    /// <summary>The event-object base ids to keep, or null to keep every event object.</summary>
    public IReadOnlySet<uint>? EventObjectBaseIds { get; init; }

    /// <summary>
    /// Whether to keep the objects whose <c>LayerEntryType</c> has no <see cref="LevelObjectKind"/> of its own, which
    /// arrive as <see cref="LevelObjectKind.Other"/> carrying only an instance id and a position. Off by default.
    /// </summary>
    public bool IncludeUnmappedKinds { get; init; }

    /// <summary>Whether an object survives this filter.</summary>
    /// <param name="levelObject">The mapped object.</param>
    /// <returns>True when the object should be kept.</returns>
    public bool Keeps(in LevelObject levelObject)
    {
        if (levelObject.Kind == LevelObjectKind.Other)
            return IncludeUnmappedKinds;

        if (Kinds != null && !Kinds.Contains(levelObject.Kind))
            return false;

        return levelObject.Kind switch
        {
            LevelObjectKind.EventNpc => EventNpcBaseIds == null || EventNpcBaseIds.Contains(levelObject.BaseId),
            LevelObjectKind.EventObject => EventObjectBaseIds == null || EventObjectBaseIds.Contains(levelObject.BaseId),
            _ => true,
        };
    }
}
