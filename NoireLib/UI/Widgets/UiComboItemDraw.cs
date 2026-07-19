namespace NoireLib.UI;

/// <summary>
/// Everything one option of a <see cref="NoireComboBox{T}"/> knows about itself, handed to a custom renderer.
/// </summary>
/// <remarks>
/// The combo keeps the row: its size, its hit testing, its selection and keyboard state, its filtering and its
/// scrolling. The renderer only paints inside it, and is free to draw an icon, a second line, a badge or nothing at
/// all.<br/>
/// Call <see cref="DrawLabel"/> to get the combo's own text back, including the filter highlighting, rather than
/// reimplementing it. A renderer that wants an icon and the ordinary label is one line of its own plus that call.
/// </remarks>
/// <typeparam name="T">The item type of the combo.</typeparam>
/// <param name="Combo">The combo drawing this option.</param>
/// <param name="Item">The option's value.</param>
/// <param name="Index">The option's index in the combo's items.</param>
/// <param name="Display">The option's display text, as the combo would draw it.</param>
/// <param name="Selected">Whether this option is the current selection.</param>
/// <param name="Highlighted">Whether this option is the one the arrow keys are on.</param>
/// <example>
/// <code>
/// combo.ItemRenderer = option =&gt;
/// {
///     UiKit.Icon(IconFor(option.Item));
///     ImGui.SameLine();
///     option.DrawLabel();
/// };
/// </code>
/// </example>
public readonly record struct UiComboItemDraw<T>(
    NoireComboBox<T> Combo,
    T Item,
    int Index,
    string Display,
    bool Selected,
    bool Highlighted)
{
    /// <summary>
    /// Draws the option's own label, with the filter's matched characters picked out when there are any.
    /// </summary>
    public void DrawLabel() => Combo.DrawItemLabel(Display);
}
