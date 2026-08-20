using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One of the game's dyes: the colors an item can actually be stained.</summary>
/// <param name="Id">Row id in the game's stain sheet.</param>
/// <param name="Name">Display name in the current client language, empty when the row names none.</param>
/// <param name="Color">The dye color, straight from the sheet.</param>
/// <param name="IsMetallic">Whether the game treats this dye as metallic.</param>
/// <param name="IsHousingApplicable">Whether the dye can be applied to housing furniture.</param>
public readonly record struct GameStain(uint Id, string Name, Vector3 Color, bool IsMetallic, bool IsHousingApplicable);
