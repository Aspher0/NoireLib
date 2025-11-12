using Dalamud.Game;
using Lumina.Excel;
using System;
using System.Collections.Concurrent;

namespace NoireLib.Internal;

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
    /// <returns>The Excel sheet of type T for the specified language, or null if not found.</returns>
    public static ExcelSheet<T>? GetSheet<T>(ClientLanguage? lang = null) where T : struct, IExcelRow<T>
    {
        var language = lang.HasValue ? lang.Value : NoireService.ClientState.ClientLanguage;

        if (Sheets.TryGetValue((typeof(T), language), out var sheet))
            return sheet as ExcelSheet<T>;

        // Lazy load if not found
        LoadSheets<T>();
        return Sheets.TryGetValue((typeof(T), language), out sheet) ? sheet as ExcelSheet<T> : null;
    }
}
