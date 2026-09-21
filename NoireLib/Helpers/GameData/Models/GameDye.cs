using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One of the game's dyes with the item a player buys to apply it.</summary>
/// <param name="StainId">Row id in the game's stain sheet.</param>
/// <param name="Name">Display name in the current client language, trimmed.</param>
/// <param name="Color">The dye color, display encoded, as <see cref="StainHelper.ToColor"/> returns it.</param>
/// <param name="ItemId">The item that applies the dye, zero when the sheet names none.</param>
/// <param name="IsSpecial">Whether the item applies this dye alone, such as Pure White.</param>
/// <param name="IsMetallic">Whether the game treats this dye as metallic.</param>
/// <param name="IsHousingApplicable">Whether housing furniture accepts the dye.</param>
public readonly record struct GameDye(
    uint StainId,
    string Name,
    Vector3 Color,
    uint ItemId,
    bool IsSpecial,
    bool IsMetallic,
    bool IsHousingApplicable);
