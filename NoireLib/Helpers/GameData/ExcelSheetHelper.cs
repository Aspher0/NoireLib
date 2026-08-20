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

    /// <summary>
    /// Loads the Excel sheets for the specified type across all client languages.
    /// </summary>
    /// <typeparam name="T">The type of the Excel row.</typeparam>
    private static void LoadSheets<T>() where T : struct, IExcelRow<T>
    {
        foreach (var lang in Enum.GetValues<ClientLanguage>())
        {
            var sheet = NoireService.DataManager.GetExcelSheet<T>(lang);
            if (sheet != null)
                Sheets[(typeof(T), lang)] = sheet;
        }
    }

    /// <summary>
    /// Gets the Excel sheet for the specified type and language.
    /// </summary>
    /// <typeparam name="T">The type of the Excel row.</typeparam>
    /// <param name="lang">The client language. If null, uses the current client language.</param>
    /// <returns>The Excel sheet of type <typeparamref name="T"/> for the specified language, or null if not found.</returns>
    public static ExcelSheet<T>? GetSheet<T>(ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var language = lang.HasValue ? lang.Value : NoireService.ClientState.ClientLanguage;

        if (Sheets.TryGetValue((typeof(T), language), out var sheet))
            return sheet as ExcelSheet<T>;

        // Lazy load if not found
        LoadSheets<T>();
        return Sheets.TryGetValue((typeof(T), language), out sheet) ? sheet as ExcelSheet<T> : null;
    }

    /// <summary>
    /// Retrieves a row of data from the specified Excel sheet by its unique identifier.
    /// </summary>
    /// <typeparam name="T">The type of the Excel row.</typeparam>
    /// <param name="rowId">The unique identifier of the row to retrieve.</param>
    /// <param name="lang">An optional client language to use when retrieving the row. If not specified, the default language is used.</param>
    /// <returns>The requested row.</returns>
    /// <exception cref="IndexOutOfRangeException">If the sheet is unavailable or holds no row with that id. Use
    /// <see cref="TryGetRow{T}(uint, out T?, ClientLanguage?)"/> to test instead of throwing.</exception>
    public static T GetRow<T>(uint rowId, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet?.TryGetRow(rowId, out var row) ?? false)
            return row;

        throw new IndexOutOfRangeException($"Row with ID {rowId} not found in sheet of type {typeof(T).Name} for language {lang ?? NoireService.ClientState.ClientLanguage}");
    }

    /// <summary>
    /// Tries to retrieve a row of data from the specified Excel sheet by its unique identifier.
    /// </summary>
    /// <typeparam name="T">The type of the Excel row.</typeparam>
    /// <param name="rowId">The unique identifier of the row to retrieve.</param>
    /// <param name="row">When this method returns, contains the retrieved row if found; otherwise, null.</param>
    /// <param name="lang">An optional client language to use when retrieving the row. If not specified, the default language is used.</param>
    /// <returns>True if the row was found; otherwise, false.</returns>
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

    /// <summary>
    /// Finds the first row in the specified Excel sheet that matches the given predicate.
    /// </summary>
    /// <typeparam name="T">The type of the Excel row.</typeparam>
    /// <param name="predicate">A function to test each row for a condition.</param>
    /// <param name="lang">An optional client language to use when retrieving the row. If not specified, the default language is used.</param>
    /// <returns>An instance of type <typeparamref name="T"/> representing the first matching row if found; otherwise, null.</returns>
    public static T? FindRow<T>(Func<T, bool> predicate, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet == null || predicate == null)
            return null;

        var row = sheet.FirstOrDefault(predicate);

        return row.IsDefault() ? null : row;
    }

    /// <summary>
    /// Finds all rows in the specified Excel sheet that match the given predicate.
    /// </summary>
    /// <typeparam name="T">The type of the Excel row.</typeparam>
    /// <param name="predicate">A function to test each row for a condition.</param>
    /// <param name="lang">An optional client language to use when retrieving the rows. If not specified, the default language is used.</param>
    /// <returns>All matching rows, or empty if none match.</returns>
    public static IEnumerable<T> FindRows<T>(Func<T, bool> predicate, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet == null || predicate == null)
            return Enumerable.Empty<T>();

        return sheet.Where(predicate);
    }

    /// <summary>
    /// Gets the subrow Excel sheet for the specified type and language, cached separately from ordinary sheets.<br/>
    /// A subrow sheet's rows each hold a variable-length list of subrows (e.g. <c>MapMarker</c>, <c>ZoneSharedGroup</c>, <c>HousingMapMarkerInfo</c>).
    /// </summary>
    /// <typeparam name="T">The type of the Excel subrow.</typeparam>
    /// <param name="lang">The client language. If null, uses the current client language.</param>
    /// <returns>The subrow Excel sheet of type <typeparamref name="T"/> for the specified language, or null if not found.</returns>
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

    /// <summary>
    /// Tries to retrieve a row's subrow collection from the specified subrow Excel sheet by its unique identifier.
    /// </summary>
    /// <typeparam name="T">The type of the Excel subrow.</typeparam>
    /// <param name="rowId">The unique identifier of the row to retrieve.</param>
    /// <param name="subrows">When this method returns, contains the row's subrows if found; otherwise, the default.</param>
    /// <param name="lang">An optional client language to use when retrieving the row. If not specified, the default language is used.</param>
    /// <returns>True if the row was found; otherwise, false.</returns>
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
