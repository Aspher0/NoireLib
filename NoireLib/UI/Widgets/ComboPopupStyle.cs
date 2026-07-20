using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoireComboBox{T}"/>'s dropdown is drawn. Every value is optional and falls back to the theme.
/// </summary>
/// <remarks>
/// Restyling the closed box and leaving the dropdown alone is worse than restyling neither, because the two are seen one
/// after the other: a plated combo that opens into ImGui's own grey popup reads as the styling having failed.
/// </remarks>
public sealed class ComboPopupStyle
{
    /// <summary>The dropdown's surface. When <see langword="null"/>, the theme's surface.</summary>
    public Vector4? Background { get; set; }

    /// <summary>The dropdown's border. When <see langword="null"/>, the theme's border.</summary>
    public Vector4? BorderColor { get; set; }

    /// <summary>How thick that border is, in real pixels. Zero for none.</summary>
    public float BorderSize { get; set; } = 1f;

    /// <summary>How round the dropdown's corners are, at 100%. When <see langword="null"/>, the theme's rounding.</summary>
    public float? Rounding { get; set; }

    /// <summary>The room between the dropdown's edge and its rows, at 100%. See <see cref="NoireUI.Scale"/>.</summary>
    public Vector2 Padding { get; set; } = new(6f, 6f);

    /// <summary>The gap between two rows, at 100%.</summary>
    public Vector2 RowSpacing { get; set; } = new(0f, 2f);

    /// <summary>The room inside a row, at 100%. The vertical half is what makes a row taller than its text.</summary>
    public Vector2 RowPadding { get; set; } = new(8f, 5f);

    /// <summary>The row under the pointer. When <see langword="null"/>, a wash of the theme's accent.</summary>
    public Vector4? HoveredColor { get; set; }

    /// <summary>The chosen row. When <see langword="null"/>, a stronger wash of the theme's accent.</summary>
    public Vector4? SelectedColor { get; set; }

    /// <summary>The row text. When <see langword="null"/>, whatever is in force outside the dropdown.</summary>
    public Vector4? TextColor { get; set; }

    /// <summary>
    /// The size the dropdown's text is drawn at, at 100%. When <see langword="null"/>, the theme's body size.
    /// </summary>
    /// <remarks>
    /// Worth stating rather than inheriting, because a dropdown is read at arm's length from the box it belongs to: a
    /// row set a step smaller than the field that opened it reads as a different interface.
    /// </remarks>
    public float? TextSizePx { get; set; }

    /// <summary>The filter box's surface. When <see langword="null"/>, the theme's sunken surface.</summary>
    /// <remarks>
    /// The one part of a dropdown most likely to be left at ImGui's own colour, and the most obvious when it is: a pale
    /// input sitting at the top of a dark popup is the first thing the eye lands on.
    /// </remarks>
    public Vector4? FilterBackground { get; set; }

    /// <summary>The filter box's border. When <see langword="null"/>, <see cref="BorderColor"/>.</summary>
    public Vector4? FilterBorderColor { get; set; }

    /// <summary>How thick the filter box's border is, in real pixels. Zero for none.</summary>
    public float FilterBorderSize { get; set; } = 1f;

    /// <summary>The filter box's text. When <see langword="null"/>, <see cref="TextColor"/>.</summary>
    public Vector4? FilterTextColor { get; set; }

    /// <summary>The scrollbar's handle. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? ScrollbarColor { get; set; }

    /// <summary>The channel the scrollbar runs in. When <see langword="null"/>, a wash of the theme's sunken surface.</summary>
    public Vector4? ScrollbarBackground { get; set; }

    /// <summary>How wide the scrollbar is, at 100%.</summary>
    public float ScrollbarWidth { get; set; } = 10f;

    /// <summary>Returns a copy, so a shared style can be varied for one combo.</summary>
    /// <returns>A shallow copy.</returns>
    public ComboPopupStyle Clone() => (ComboPopupStyle)MemberwiseClone();
}
