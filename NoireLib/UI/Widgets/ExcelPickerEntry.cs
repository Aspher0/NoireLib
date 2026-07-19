using Lumina.Excel;

namespace NoireLib.UI;

/// <summary>
/// One row of game data, prepared for picking: its id, the row itself, the text it is searched and drawn by, and the
/// icon that goes beside it.
/// </summary>
/// <remarks>
/// The display text is built once when the sheet is read rather than per frame, and that is the whole reason this type
/// exists. Turning a row's name into a string means reading a SeString out of the sheet and decoding it, and a filter
/// box scores every row on every keystroke: computing it on demand would decode forty thousand names per character
/// typed.
/// </remarks>
/// <typeparam name="TRow">The Excel row type.</typeparam>
/// <param name="RowId">The row's id in its sheet, which is what a plugin stores and restores.</param>
/// <param name="Row">The row itself, so a consumer can read anything else off it.</param>
/// <param name="Display">The text the row is listed and searched by.</param>
/// <param name="IconId">The row's icon id, or zero when it has none.</param>
public readonly record struct ExcelPickerEntry<TRow>(uint RowId, TRow Row, string Display, uint IconId)
    where TRow : struct, IExcelRow<TRow>;
