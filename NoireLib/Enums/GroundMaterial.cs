namespace NoireLib.Enums;

/// <summary>
/// The material a character is standing on, as the game classifies footing.
/// The names come from the game's own footstep-path table, where several ids share a name.
/// </summary>
public enum GroundMaterial : byte
{
    None = 0,

    /// <summary> Spelled "dart" in the game's own table. </summary>
    Dirt = 1,
    Grass = 2,
    Sand = 3,
    Stone = 4,
    Wood = 5,
    Metal = 6,
    Gravel = 7,
    Leaf = 8,
    Powder = 9,
    Carpet = 10,
    Snow = 11,
    Water = 12,
    Water2 = 13,
    Soil = 14,
    Soil2 = 15,
    Soil3 = 16,
    Soil4 = 17,
    Soil5 = 18,
    Water3 = 19,
    Grass2 = 20,
    Metal2 = 21,
}
