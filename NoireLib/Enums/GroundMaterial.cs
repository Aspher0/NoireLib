namespace NoireLib.Enums;

/// <summary>
/// The material a character is standing on, as the game classifies footing. It selects the footstep sound and
/// gates a few emotes (<see cref="Snow"/> for the snowball /throw, the water values for /splash). The names come
/// from the game's own footstep-path table, where several ids share a name, so the duplicates are numbered here.
/// </summary>
public enum GroundMaterial : byte
{
    /// <summary> Nothing underfoot, or the character could not be read. </summary>
    None = 0,

    /// <summary> Bare earth, spelled "dart" in the game's own table. </summary>
    Dirt = 1,

    /// <summary> Grass. </summary>
    Grass = 2,

    /// <summary> Sand, as on a beach. </summary>
    Sand = 3,

    /// <summary> Stone, paving and masonry. </summary>
    Stone = 4,

    /// <summary> Wood, including decking and floorboards. </summary>
    Wood = 5,

    /// <summary> Metal. </summary>
    Metal = 6,

    /// <summary> Gravel. </summary>
    Gravel = 7,

    /// <summary> Leaves and flower beds. </summary>
    Leaf = 8,

    /// <summary> Powder. </summary>
    Powder = 9,

    /// <summary> Carpet. </summary>
    Carpet = 10,

    /// <summary> Snow, the value that turns /throw into the snowball emote. </summary>
    Snow = 11,

    /// <summary> Water. </summary>
    Water = 12,

    /// <summary> Water, reported by puddles, streams, ankle-deep shallows and open sea while swimming. </summary>
    Water2 = 13,

    /// <summary> Soil. </summary>
    Soil = 14,

    /// <summary> Soil. </summary>
    Soil2 = 15,

    /// <summary> Soil. </summary>
    Soil3 = 16,

    /// <summary> Soil. </summary>
    Soil4 = 17,

    /// <summary> Soil. </summary>
    Soil5 = 18,

    /// <summary> Water, reported by knee-deep housing bath furnishings. </summary>
    Water3 = 19,

    /// <summary> Grass. </summary>
    Grass2 = 20,

    /// <summary> Metal. </summary>
    Metal2 = 21,
}
