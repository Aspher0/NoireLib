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
    /// Finds the dye whose color is <paramref name="color"/> - the reverse of <see cref="ColorOf"/>, and the
    /// step that turns an item's default dye into a stain id.<br/>
    /// Dyeable furniture ships its default dye in its material's <c>g_DiffuseColor</c> constant as the
    /// default stain's exact table color (verified across every such material in the archives: the values
    /// land on stain rows to the third decimal, or are plain white when the default colors nothing). Matching
    /// that constant against the table names the stain, which no sheet stores directly.<br/>
    /// The match is nearest-within-tolerance rather than exact because the constant carries the color through
    /// float conversion; the observed distance on real materials is 0, and the default tolerance is far below
    /// the spacing between any two rows that were checked. White returns false - it is the absence of a
    /// default, not a dye.
    /// </summary>
    /// <param name="color">The color to find, display-encoded, as <see cref="ColorOf"/> returns and <c>g_DiffuseColor</c> stores.</param>
    /// <param name="stain">The matching dye, when one is within tolerance.</param>
    /// <param name="tolerance">Maximum color distance to accept as a match.</param>
    public static bool TryFindByColor(Vector3 color, out GameStain stain, float tolerance = 0.005f)
    {
        stain = default;

        var bestDistance = float.MaxValue;
        foreach (var candidate in All())
        {
            var distance = (candidate.Color - color).Length();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                stain = candidate;
            }
        }

        return bestDistance <= tolerance;
    }

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
