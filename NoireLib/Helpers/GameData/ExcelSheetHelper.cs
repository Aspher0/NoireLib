using Dalamud.Game;
using Lumina.Excel;
using NoireLib.Helpers.ObjectExtensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// Manages Excel sheets across client languages, with lazy loading and caching.
/// </summary>
public static class ExcelSheetHelper
{
    private static readonly ConcurrentDictionary<(Type SheetType, ClientLanguage Language), object> Sheets = new();
    private static readonly ConcurrentDictionary<(Type SheetType, ClientLanguage Language), object> SubrowSheets = new();

    private static void LoadSheets<T>() where T : struct, IExcelRow<T>
    {
        foreach (var lang in Enum.GetValues<ClientLanguage>())
        {
            var sheet = NoireService.DataManager.GetExcelSheet<T>(lang);
            if (sheet != null)
                Sheets[(typeof(T), lang)] = sheet;
        }
    }

    /// <summary>The Excel sheet of a type and language, loaded and cached on first use.</summary>
    /// <typeparam name="T">The Excel row type.</typeparam>
    /// <param name="lang">The client language, or the current one when null.</param>
    /// <returns>The sheet, or null when not found.</returns>
    public static ExcelSheet<T>? GetSheet<T>(ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var language = lang.HasValue ? lang.Value : NoireService.ClientState.ClientLanguage;

        if (Sheets.TryGetValue((typeof(T), language), out var sheet))
            return sheet as ExcelSheet<T>;

        LoadSheets<T>();
        return Sheets.TryGetValue((typeof(T), language), out sheet) ? sheet as ExcelSheet<T> : null;
    }

    /// <summary>A sheet row by id.</summary>
    /// <typeparam name="T">The Excel row type.</typeparam>
    /// <param name="rowId">The row id.</param>
    /// <param name="lang">The client language, or the current one when null.</param>
    /// <returns>The row.</returns>
    /// <exception cref="IndexOutOfRangeException">If the sheet is unavailable or holds no row with that id. Use
    /// <see cref="TryGetRow{T}(uint, out T?, ClientLanguage?)"/> to test without throwing.</exception>
    public static T GetRow<T>(uint rowId, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet?.TryGetRow(rowId, out var row) ?? false)
            return row;

        throw new IndexOutOfRangeException($"Row with ID {rowId} not found in sheet of type {typeof(T).Name} for language {lang ?? NoireService.ClientState.ClientLanguage}");
    }

    /// <summary>A sheet row by id.</summary>
    /// <typeparam name="T">The Excel row type.</typeparam>
    /// <param name="rowId">The row id.</param>
    /// <param name="row">The row when found.</param>
    /// <param name="lang">The client language, or the current one when null.</param>
    /// <returns>True when the row was found.</returns>
    public static bool TryGetRow<T>(uint rowId, out T? row, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        row = null;

        var sheet = GetSheet<T>(lang);
        if (sheet == null)
            return false;

        if (sheet.TryGetRow(rowId, out var tempRow))
        {
            row = tempRow;
            return true;
        }

        return false;
    }

    /// <summary>The first row matching a predicate.</summary>
    /// <typeparam name="T">The Excel row type.</typeparam>
    /// <param name="predicate">The condition to test each row against.</param>
    /// <param name="lang">The client language, or the current one when null.</param>
    /// <returns>The row, or null when none matched.</returns>
    public static T? FindRow<T>(Func<T, bool> predicate, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet == null || predicate == null)
            return null;

        var row = sheet.FirstOrDefault(predicate);

        return row.IsDefault() ? null : row;
    }

    /// <summary>Every row matching a predicate.</summary>
    /// <typeparam name="T">The Excel row type.</typeparam>
    /// <param name="predicate">The condition to test each row against.</param>
    /// <param name="lang">The client language, or the current one when null.</param>
    /// <returns>The matching rows, empty when none matched.</returns>
    public static IEnumerable<T> FindRows<T>(Func<T, bool> predicate, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet == null || predicate == null)
            return Enumerable.Empty<T>();

        return sheet.Where(predicate);
    }

    /// <summary>
    /// The subrow Excel sheet of a type and language, cached separately from ordinary sheets.<br/>
    /// A subrow sheet's rows each hold a variable-length list of subrows (e.g. <c>MapMarker</c>, <c>ZoneSharedGroup</c>, <c>HousingMapMarkerInfo</c>).
    /// </summary>
    /// <typeparam name="T">The Excel subrow type.</typeparam>
    /// <param name="lang">The client language, or the current one when null.</param>
    /// <returns>The sheet, or null when not found.</returns>
    public static SubrowExcelSheet<T>? GetSubrowSheet<T>(ClientLanguage? lang = null) where T : struct, IExcelSubrow<T>
    {
        var language = lang ?? NoireService.ClientState.ClientLanguage;

        if (SubrowSheets.TryGetValue((typeof(T), language), out var cached))
            return cached as SubrowExcelSheet<T>;

        var sheet = NoireService.DataManager.GetSubrowExcelSheet<T>(language);
        if (sheet == null)
            return null;

        SubrowSheets[(typeof(T), language)] = sheet;
        return sheet;
    }

    /// <summary>A row's subrow collection by id.</summary>
    /// <typeparam name="T">The Excel subrow type.</typeparam>
    /// <param name="rowId">The row id.</param>
    /// <param name="subrows">The subrows when found.</param>
    /// <param name="lang">The client language, or the current one when null.</param>
    /// <returns>True when the row was found.</returns>
    public static bool TryGetSubrows<T>(uint rowId, out SubrowCollection<T> subrows, ClientLanguage? lang = null)
        where T : struct, IExcelSubrow<T>
    {
        var sheet = GetSubrowSheet<T>(lang);
        if (sheet != null)
            return sheet.TryGetRow(rowId, out subrows);

        subrows = default;
        return false;
    }
}
