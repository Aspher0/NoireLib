using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

/// <summary>
/// Everything a cell renderer is handed: which table, which row, which column, and whether the row is selected.
/// </summary>
/// <remarks>
/// The table has already placed the cell and decided its size, its selection and its order. A renderer only paints,
/// which is what makes a bespoke cell configuration rather than a fork.
/// </remarks>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="Table">The table being drawn.</param>
/// <param name="Row">The row this cell belongs to.</param>
/// <param name="RowIndex">Where the row sits in the source list.</param>
/// <param name="Column">The column this cell belongs to.</param>
/// <param name="ColumnIndex">Where the column sits in the table.</param>
/// <param name="Selected">Whether the row is selected.</param>
public readonly record struct UiTableCellDraw<T>(
    NoireTable<T> Table,
    T Row,
    int RowIndex,
    TableColumn<T> Column,
    int ColumnIndex,
    bool Selected)
{
    /// <summary>
    /// The text the column reads out of this row, which is what the table would have drawn.
    /// </summary>
    public string Text => Column.Read(Row);

    /// <summary>
    /// Draws the cell the way the table would have, for a renderer that only wants to add something beside it.
    /// </summary>
    public void DrawText()
    {
        ImGui.PushTextWrapPos(-1f);
        NoireText.Draw(Text);
        ImGui.PopTextWrapPos();
    }
}
