using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the game's dye colors.
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
            // Row 0 is no dye, and unused rows carry no color; skip both.
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
    /// Finds the dye whose color is <paramref name="color"/>, the reverse of <see cref="ColorOf"/>.<br/>
    /// Dyeable furniture stores its default dye as the exact stain color in its material's <c>g_DiffuseColor</c>
    /// constant. This matches against that value. White never matches: it means no default dye, not the dye white.
    /// </summary>
    /// <param name="color">Display-encoded color to find, as <see cref="ColorOf"/> returns and <c>g_DiffuseColor</c> stores.</param>
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
    /// Unpacks a stain's packed color, stored as <c>0x00RRGGBB</c> in display (non-linear) space.
    /// </summary>
    /// <param name="packed">The packed value from the sheet.</param>
    public static Vector3 ToColor(uint packed) => new(
        ((packed >> 16) & 0xFF) / 255f,
        ((packed >> 8) & 0xFF) / 255f,
        (packed & 0xFF) / 255f);

    /// <summary>Scene file magic, then the scene-layer magic that follows it.</summary>
    private const uint SceneMagic = 0x31424753, SceneLayerMagic = 0x314E4353;

    /// <summary>Pointers inside a scene are relative to this offset rather than to the file.</summary>
    private const int ScenePointerBase = 0x14;

    /// <summary>Offset of the pointer to the scene's default stain.</summary>
    private const int SceneStainPointerOffset = 0x40;

    /// <summary>
    /// The stain a piece of furniture renders when nobody has dyed it, taken from the scene placed beside its model.
    /// </summary>
    /// <param name="modelGamePath">The furniture model's archive path, under <c>bgcommon/</c>.</param>
    /// <returns>The stain id, zero when the scene states none, or null when the model has no readable scene beside it.</returns>
    public static ushort? DefaultStainForModel(string modelGamePath)
        => GamePathHelper.SceneBesideModel(modelGamePath) is { } scenePath ? DefaultStainForScene(scenePath) : null;

    /// <summary>
    /// The stain an undyed placement of a scene renders.
    /// </summary>
    /// <param name="scenePath">Archive path of the scene (<c>.sgb</c>).</param>
    /// <returns>The stain id, zero when the scene states none, or null when no readable scene sits at the path.</returns>
    public static ushort? DefaultStainForScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return null;

        return SafeExecutor.ExecuteSafely<ushort?>(() =>
        {
            var file = NoireService.DataManager.GetFile(scenePath);
            return file != null && TryReadSceneDefaultStain(file.Data, out var stain) ? stain : null;
        }, null);
    }

    /// <summary>
    /// Reads a scene's default stain out of its raw bytes. Only the two fields the stain needs are read; the scene's
    /// placements are not parsed.
    /// </summary>
    /// <param name="data">The scene file's bytes.</param>
    /// <param name="stain">The stain id, zero when the scene states none.</param>
    /// <returns>False when the bytes are not a scene this layout can read.</returns>
    internal static bool TryReadSceneDefaultStain(ReadOnlySpan<byte> data, out ushort stain)
    {
        stain = 0;

        if (data.Length < 0x60
            || BitConverter.ToUInt32(data) != SceneMagic
            || BitConverter.ToUInt32(data[0xC..]) != SceneLayerMagic)
            return false;

        var pointer = BitConverter.ToUInt32(data[SceneStainPointerOffset..]);
        if (pointer == 0)
            return true;

        var at = ScenePointerBase + (long)pointer;
        if (at + 2 > data.Length)
            return true;

        // A value beyond the stain table means the pointer did not mean this here; report it as unstated rather than
        // as an arbitrary color.
        var value = BitConverter.ToUInt16(data[(int)at..]);
        stain = value > 1000 ? (ushort)0 : value;
        return true;
    }
}
