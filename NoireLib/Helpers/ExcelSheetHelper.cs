using Dalamud.Game;
using Lumina.Excel;
using NoireLib.Helpers.ObjectExtensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// A helper class for managing Excel sheets across different client languages.<br/>
/// Supports lazy loading and caching of sheets for fast retrieval.
/// </summary>
public static class ExcelSheetHelper
{
    private static readonly ConcurrentDictionary<(Type SheetType, ClientLanguage Language), object> Sheets = new();

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
    /// <returns>An instance of type <typeparamref name="T"/> representing the requested row if found; otherwise, null.</returns>
    public static T? GetRow<T>(uint rowId, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet?.TryGetRow(rowId, out var row) ?? false)
            return row;

        return null;
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
    /// <returns>An IEnumerable of <typeparamref name="T"/> representing all matching rows in the sheet, or an empty collection if no matches are found.</returns>
    public static IEnumerable<T> FindRows<T>(Func<T, bool> predicate, ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var sheet = GetSheet<T>(lang);

        if (sheet == null || predicate == null)
            return Enumerable.Empty<T>();

        return sheet.Where(predicate);
    }
}
