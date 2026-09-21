using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One thing the game files describe: an event object, an event NPC, an aetheryte or a shared group, with what its
/// sheets say and where it stands. Exactly one of the detail members is set, none for a shared group.
/// </summary>
/// <param name="Kind">What it is. A single flag of <see cref="PlacementKinds"/>.</param>
/// <param name="BaseId">The EObj, ENpcBase or Aetheryte row id. Zero for a shared group.</param>
/// <param name="Name">The display name, the SGB file name for a shared group.</param>
/// <param name="PluralName">The plural display name, empty when the sheet has none.</param>
/// <param name="Title">The NPC's title from ENpcResident, empty for other kinds.</param>
/// <param name="Handlers">The event handlers it runs, array handlers unfolded into what they list.</param>
/// <param name="EventObject">The EObj details for an event object, null otherwise.</param>
/// <param name="EventNpc">The ENpcBase details for an event NPC, null otherwise.</param>
/// <param name="Aetheryte">The Aetheryte details for an aetheryte, null otherwise.</param>
/// <param name="Placements">Every placement within the query's scope, empty when placements were not asked for.</param>
/// <param name="AssetPath">The SGB path for a shared group, empty otherwise.</param>
public sealed record WorldObject(
    PlacementKinds Kind,
    uint BaseId,
    string Name,
    string PluralName,
    string Title,
    IReadOnlyList<WorldObjectHandler> Handlers,
    EventObjectDetails? EventObject,
    EventNpcDetails? EventNpc,
    AetheryteDetails? Aetheryte,
    IReadOnlyList<WorldObjectPlacement> Placements,
    string AssetPath);
