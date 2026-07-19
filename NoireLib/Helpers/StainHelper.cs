using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One of the game's dyes: the colors an item can actually be stained.</summary>
/// <param name="Id">Row id in the game's stain sheet.</param>
/// <param name="Name">Display name in the current client language, empty when the row names none.</param>
/// <param name="Color">The dye color, straight from the sheet.</param>
/// <param name="IsMetallic">Whether the game treats this dye as metallic, which changes how it shades.</param>
/// <param name="IsHousingApplicable">Whether the dye can be applied to housing furniture.</param>
public readonly record struct GameStain(uint Id, string Name, Vector3 Color, bool IsMetallic, bool IsHousingApplicable);

/// <summary>
/// Reads the game's dye colors.<br/>
/// These are the real values the game stains items with, so anything drawn with them is showing a color
/// the game actually uses rather than one picked to look close.
/// </summary>
public static class StainHelper
{
    /// <summary>Every dye the game defines, in row order. Rows with no color are skipped.</summary>
    /// <param name="housingOnly">Restrict to dyes that can be applied to housing furniture.</param>
    public static IReadOnlyList<GameStain> All(bool housingOnly = false)
    {
        var sheet = ExcelSheetHelper.GetSheet<Stain>();
        if (sheet is null)
            return [];

        var stains = new List<GameStain>();
        foreach (var row in sheet)
        {
            // Row 0 is the absence of a dye, and unused rows carry no colour; neither is a dye anyone can apply.
            if (row.RowId == 0 || row.Color == 0)
                continue;

            if (housingOnly && !row.IsHousingApplicable)
                continue;

            stains.Add(new GameStain(
                row.RowId,
                row.Name.ExtractText() ?? string.Empty,
                ToColor(row.Color),
                row.IsMetallic,
                row.IsHousingApplicable));
        }

        return stains;
    }

    /// <summary>Looks up one dye by its row id.</summary>
    /// <param name="id">Row id in the game's stain sheet.</param>
    /// <param name="stain">The dye, when the row exists and carries a color.</param>
    public static bool TryGet(uint id, out GameStain stain)
    {
        stain = default;

        if (!ExcelSheetHelper.TryGetRow<Stain>(id, out var row) || row is not { } value || value.Color == 0)
            return false;

        stain = new GameStain(
            value.RowId,
            value.Name.ExtractText() ?? string.Empty,
            ToColor(value.Color),
            value.IsMetallic,
            value.IsHousingApplicable);

        return true;
    }

    /// <summary>The color of one dye, or null when the row does not exist or carries none.</summary>
    /// <param name="id">Row id in the game's stain sheet.</param>
    public static Vector3? ColorOf(uint id) => TryGet(id, out var stain) ? stain.Color : null;

    /// <summary>
    /// Unpacks a stain's packed color.<br/>
    /// The sheet stores it as <c>0x00RRGGBB</c>, and the bytes are display colors: they are the values a
    /// color picker would show, not linear ones.
    /// </summary>
    /// <param name="packed">The packed value from the sheet.</param>
    public static Vector3 ToColor(uint packed) => new(
        ((packed >> 16) & 0xFF) / 255f,
        ((packed >> 8) & 0xFF) / 255f,
        (packed & 0xFF) / 255f);
}
