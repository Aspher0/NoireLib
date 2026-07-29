using Lumina.Excel;

namespace NoireLib.UI;

/// <summary>
/// One row of game data, prepared for picking: its id, the row itself, the text it is searched and drawn by, and the
/// icon that goes beside it.
/// </summary>
/// <remarks>
/// Built once when the sheet is read rather than on demand, since a filter box re-scores every row on every keystroke.
/// </remarks>
/// <typeparam name="TRow">The Excel row type.</typeparam>
/// <param name="RowId">The row's id in its sheet.</param>
/// <param name="Row">The row itself.</param>
/// <param name="Display">The text the row is listed and searched by.</param>
/// <param name="IconId">The row's icon id, or zero when it has none.</param>
public readonly record struct ExcelPickerEntry<TRow>(uint RowId, TRow Row, string Display, uint IconId)
    where TRow : struct, IExcelRow<TRow>;
