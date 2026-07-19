using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Everything a row renderer is handed: which list, which row, where it sits, and whether it is being dragged.
/// </summary>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="List">The list being drawn.</param>
/// <param name="Item">The row.</param>
/// <param name="Index">Where the row sits.</param>
/// <param name="Label">The text the list would have drawn.</param>
/// <param name="Dragging">Whether this row is the one being dragged.</param>
/// <param name="Size">The space left for the row's content, between the grip and the buttons.</param>
public readonly record struct UiReorderRowDraw<T>(
    NoireReorderableList<T> List,
    T Item,
    int Index,
    string Label,
    bool Dragging,
    Vector2 Size)
{
    /// <summary>
    /// Draws the row the way the list would have, for a renderer that only wants to add something beside it.
    /// </summary>
    public void DrawLabel()
    {
        ImGui.PushTextWrapPos(-1f);
        NoireText.Draw(Label);
        ImGui.PopTextWrapPos();
    }
}
