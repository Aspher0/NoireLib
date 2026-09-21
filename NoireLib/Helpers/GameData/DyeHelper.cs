using Dalamud.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Matches colors to the game's dyes and dyes to the items that apply them.
/// </summary>
public static class DyeHelper
{
    // The generated Lumina row does not expose this column.
    private const int StainItemColumn = 3;

    private static readonly object CacheLock = new();
    private static IReadOnlyList<GameDye>? cachedDyes;

    /// <summary>Every dye that carries a color, in row order.</summary>
    /// <param name="housingOnly">Keeps only the dyes housing furniture accepts.</param>
    /// <returns>The dyes, or an empty list when the sheets are unavailable.</returns>
    public static IReadOnlyList<GameDye> All(bool housingOnly = false)
    {
        var dyes = Load();
        return housingOnly ? dyes.Where(static dye => dye.IsHousingApplicable).ToList() : dyes;
    }

    /// <summary>Looks up one dye by its stain row id.</summary>
    /// <param name="stainId">Row id in the game's stain sheet.</param>
    /// <param name="dye">The dye, when the row exists and carries a color.</param>
    /// <returns>True when the dye was found.</returns>
    public static bool TryGet(uint stainId, out GameDye dye)
    {
        foreach (var candidate in Load())
        {
            if (candidate.StainId == stainId)
            {
                dye = candidate;
                return true;
            }
        }

        dye = default;
        return false;
    }

    /// <summary>Gets the item a player buys to apply a dye.</summary>
    /// <param name="stainId">Row id in the game's stain sheet.</param>
    /// <returns>The item row id, or zero when the dye does not exist or names no item.</returns>
    public static uint ItemOf(uint stainId) => TryGet(stainId, out var dye) ? dye.ItemId : 0u;

    /// <summary>Finds a dye by its display name, ignoring case and surrounding spaces.</summary>
    /// <param name="name">The dye name, such as "Soot Black".</param>
    /// <param name="dye">The dye, when a name matches. Its <see cref="GameDye.Name"/> stays in the client language.</param>
    /// <param name="language">The language the name is written in, or null for the client language.</param>
    /// <returns>True when a dye carries that name.</returns>
    public static bool TryFindByName(string name, out GameDye dye, ClientLanguage? language = null)
    {
        dye = default;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        var wanted = name.Trim();

        if (language == null)
        {
            foreach (var candidate in Load())
            {
                if (string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    dye = candidate;
                    return true;
                }
            }

            return false;
        }

        var sheet = ExcelSheetHelper.GetSheet<Stain>(language);

        if (sheet == null)
            return false;

        foreach (var row in sheet)
        {
            if (row.RowId != 0 && string.Equals((row.Name.ExtractText() ?? string.Empty).Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                return TryGet(row.RowId, out dye);
        }

        return false;
    }

    /// <summary>Finds the dye whose color is closest to a color.</summary>
    /// <param name="color">The color, each channel between 0 and 1.</param>
    /// <param name="allowSpecialDyes">Whether dyes applied by their own item, such as Jet Black, may be chosen.</param>
    /// <param name="housingOnly">Keeps only the dyes housing furniture accepts.</param>
    /// <returns>The closest dye, or null when no dye qualifies.</returns>
    public static GameDye? Nearest(Vector3 color, bool allowSpecialDyes = true, bool housingOnly = false)
        => Nearest(All(housingOnly), color, allowSpecialDyes);

    /// <summary>Finds the dye whose color is closest to a color. Alpha is ignored.</summary>
    /// <param name="color">The color, each channel between 0 and 1.</param>
    /// <param name="allowSpecialDyes">Whether dyes applied by their own item, such as Jet Black, may be chosen.</param>
    /// <param name="housingOnly">Keeps only the dyes housing furniture accepts.</param>
    /// <returns>The closest dye, or null when no dye qualifies.</returns>
    public static GameDye? Nearest(Vector4 color, bool allowSpecialDyes = true, bool housingOnly = false)
        => Nearest(new Vector3(color.X, color.Y, color.Z), allowSpecialDyes, housingOnly);

    /// <summary>Finds the dye whose color is closest to a HEX color. Alpha is ignored.</summary>
    /// <param name="hex">Three, four, six or eight hex digits, with or without "#", such as "2B292300".</param>
    /// <param name="allowSpecialDyes">Whether dyes applied by their own item, such as Jet Black, may be chosen.</param>
    /// <param name="housingOnly">Keeps only the dyes housing furniture accepts.</param>
    /// <returns>The closest dye, or null when the string is not a color or no dye qualifies.</returns>
    public static GameDye? Nearest(string hex, bool allowSpecialDyes = true, bool housingOnly = false)
        => Nearest(All(housingOnly), hex, allowSpecialDyes);

    /// <summary>Finds the dye whose color is closest to a packed <c>0x00RRGGBB</c> color.</summary>
    /// <param name="rgb">The packed color. The high byte is ignored.</param>
    /// <param name="allowSpecialDyes">Whether dyes applied by their own item, such as Jet Black, may be chosen.</param>
    /// <param name="housingOnly">Keeps only the dyes housing furniture accepts.</param>
    /// <returns>The closest dye, or null when no dye qualifies.</returns>
    public static GameDye? Nearest(uint rgb, bool allowSpecialDyes = true, bool housingOnly = false)
        => Nearest(StainHelper.ToColor(rgb), allowSpecialDyes, housingOnly);

    /// <summary>Gets the item that applies the dye closest to a color.</summary>
    /// <param name="color">The color, each channel between 0 and 1.</param>
    /// <param name="allowSpecialDyes">Whether dyes applied by their own item, such as Jet Black, may be chosen.</param>
    /// <param name="housingOnly">Keeps only the dyes housing furniture accepts.</param>
    /// <returns>The item row id, or zero when no dye qualifies.</returns>
    public static uint ItemForColor(Vector3 color, bool allowSpecialDyes = true, bool housingOnly = false)
        => Nearest(color, allowSpecialDyes, housingOnly)?.ItemId ?? 0u;

    /// <summary>Gets the item that applies the dye closest to a HEX color. Alpha is ignored.</summary>
    /// <param name="hex">Three, four, six or eight hex digits, with or without "#", such as "2B292300".</param>
    /// <param name="allowSpecialDyes">Whether dyes applied by their own item, such as Jet Black, may be chosen.</param>
    /// <param name="housingOnly">Keeps only the dyes housing furniture accepts.</param>
    /// <returns>The item row id, or zero when the string is not a color or no dye qualifies.</returns>
    public static uint ItemForColor(string hex, bool allowSpecialDyes = true, bool housingOnly = false)
        => Nearest(hex, allowSpecialDyes, housingOnly)?.ItemId ?? 0u;

    internal static GameDye? Nearest(IEnumerable<GameDye> candidates, string hex, bool allowSpecialDyes)
        => ColorHelper.TryHexToVector3(hex, out var color) ? Nearest(candidates, color, allowSpecialDyes) : null;

    internal static GameDye? Nearest(IEnumerable<GameDye> candidates, Vector3 color, bool allowSpecialDyes)
    {
        GameDye? best = null;
        var bestDistance = float.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate.IsSpecial && !allowSpecialDyes)
                continue;

            var distance = Vector3.DistanceSquared(candidate.Color, color);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    // An item applying a single dye makes that dye special.
    internal static IReadOnlyList<GameDye> MarkSpecial(IReadOnlyList<GameDye> dyes)
    {
        var uses = dyes
            .Where(static dye => dye.ItemId != 0)
            .GroupBy(static dye => dye.ItemId)
            .ToDictionary(static group => group.Key, static group => group.Count());

        return dyes
            .Select(dye => dye with { IsSpecial = dye.ItemId != 0 && uses[dye.ItemId] == 1 })
            .ToList();
    }

    private static IReadOnlyList<GameDye> Load()
    {
        lock (CacheLock)
        {
            if (cachedDyes != null)
                return cachedDyes;
        }

        var loaded = SafeExecutor.ExecuteSafely(ReadSheets, []) ?? [];

        if (loaded.Count > 0)
        {
            lock (CacheLock)
                cachedDyes = loaded;
        }

        return loaded;
    }

    private static IReadOnlyList<GameDye> ReadSheets()
    {
        var stains = ExcelSheetHelper.GetSheet<Stain>();
        var raw = NoireService.DataManager.Excel.GetSheet<RawRow>(null, "Stain");

        if (stains == null || raw == null)
            return [];

        var dyes = new List<GameDye>();

        foreach (var row in stains)
        {
            if (row.RowId == 0 || row.Color == 0)
                continue;

            var itemId = raw.TryGetRow(row.RowId, out var rawRow)
                ? Convert.ToUInt32(rawRow.ReadColumn(StainItemColumn), CultureInfo.InvariantCulture)
                : 0u;

            if (itemId != 0 && !ExcelSheetHelper.TryGetRow<Item>(itemId, out _))
                itemId = 0;

            dyes.Add(new GameDye(
                row.RowId,
                (row.Name.ExtractText() ?? string.Empty).Trim(),
                StainHelper.ToColor(row.Color),
                itemId,
                false,
                row.IsMetallic,
                row.IsHousingApplicable));
        }

        return MarkSpecial(dyes);
    }
}
