using System.Collections.Generic;

namespace NoireLib.Helpers;

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
