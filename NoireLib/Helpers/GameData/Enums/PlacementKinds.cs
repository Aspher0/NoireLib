using System;

namespace NoireLib.Helpers;

/// <summary>The kinds of placed thing <see cref="PlacementHelper"/> looks up, combinable.</summary>
[Flags]
public enum PlacementKinds
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>An event object, identified by its EObj row.</summary>
    EventObject = 1,

    /// <summary>An event NPC, identified by its ENpcBase row.</summary>
    EventNpc = 2,

    /// <summary>An aetheryte crystal, identified by its Aetheryte row.</summary>
    Aetheryte = 4,

    /// <summary>A shared group, identified by its SGB asset path, not a row.</summary>
    SharedGroup = 8,

    /// <summary>Every kind a sheet names: event objects, event NPCs and aetherytes.</summary>
    All = EventObject | EventNpc | Aetheryte,
}
