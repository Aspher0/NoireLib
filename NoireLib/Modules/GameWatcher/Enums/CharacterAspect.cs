using System;

namespace NoireLib.GameWatcher;

/// <summary>
/// The diffable facets of a character, used for interest-masked diffing:
/// the Characters source only compares the fields at least one subscription listens to.<br/>
/// Every character event type maps to exactly one aspect bit; subscribing contributes that bit
/// (and the subscription's scope) to the source's union mask.
/// </summary>
[Flags]
public enum CharacterAspect
{
    /// <summary>No aspect.</summary>
    None = 0,

    /// <summary>Appearing in / leaving the object table (spawn and despawn).</summary>
    Presence = 1 << 0,

    /// <summary>HP and MP (GP/CP are only meaningful for the local player - not synchronized for others).</summary>
    Vitals = 1 << 1,

    /// <summary>Shield percentage.</summary>
    Shield = 1 << 2,

    /// <summary>Cast started / completed / interrupted, cast action and target.</summary>
    Cast = 1 << 3,

    /// <summary>In-combat state.</summary>
    Combat = 1 << 4,

    /// <summary>The character's own target.</summary>
    Target = 1 << 5,

    /// <summary>Targetability.</summary>
    Targetable = 1 << 6,

    /// <summary>Death and revival.</summary>
    Life = 1 << 7,

    /// <summary>Character mode transitions (looping emotes, mounts, crafting stance, …).</summary>
    Mode = 1 << 8,

    /// <summary>The exact emote id currently played (from the character's emote controller) - one-shots, loops and cposes.</summary>
    Emote = 1 << 13,

    /// <summary>Online status (AFK, busy, looking for party, …).</summary>
    OnlineStatus = 1 << 9,

    /// <summary>Class/job and level.</summary>
    JobLevel = 1 << 10,

    /// <summary>
    /// World-space position. There is deliberately no raw position-changed event (a per-frame firehose);
    /// this aspect is reserved for distance and region watchers, which live on the Objects source.
    /// </summary>
    Position = 1 << 11,

    /// <summary>Identity data: name, home world, content id (changes usually mean entity-slot reuse).</summary>
    Identity = 1 << 12,

    /// <summary>Every aspect.</summary>
    All = Presence | Vitals | Shield | Cast | Combat | Target | Targetable | Life | Mode | Emote | OnlineStatus | JobLevel | Position | Identity,
}
