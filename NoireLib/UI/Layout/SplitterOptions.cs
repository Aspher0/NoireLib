using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoireLayout.Splitter(string, ref float, SplitterOptions)"/> behaves and looks.
/// </summary>
/// <remarks>
/// The grab area and the divider are separate concerns here: <see cref="Thickness"/> is how much of the pointer's path
/// counts as the handle, while the line drawn down the middle of it is a hairline. A comfortable handle and a hairline
/// divider are what a resizable pane usually wants, and they are not the same number.
/// </remarks>
public sealed class SplitterOptions
{
    /// <summary>The smallest the pane may become, in real pixels. Zero uses a usable default, which does scale.</summary>
    public float MinSize { get; set; }

    /// <summary>The largest the pane may become, in real pixels. Zero leaves it bounded only by the space available.</summary>
    public float MaxSize { get; set; }

    /// <summary>The grab thickness, in real pixels. Zero uses a comfortable default, which does scale.</summary>
    public float Thickness { get; set; }

    /// <summary>
    /// Whether the divider is a vertical bar resizing the pane to its left. Set it to <see langword="false"/> for a
    /// horizontal bar resizing the pane above it.
    /// </summary>
    public bool Vertical { get; set; } = true;

    /// <summary>
    /// How long the divider is, across the panes it separates, in real pixels. Zero fills the space remaining in the
    /// current region, which is only right when the panes do too.
    /// </summary>
    public float Length { get; set; }

    /// <summary>The divider's line thickness, at 100%.</summary>
    public float LineWidth { get; set; } = 1f;

    /// <summary>The divider's color at rest. When <see langword="null"/>, a muted theme border.</summary>
    public Vector4? Color { get; set; }

    /// <summary>The divider's color while hovered. When <see langword="null"/>, a lit theme border.</summary>
    public Vector4? HoveredColor { get; set; }

    /// <summary>The divider's color while being dragged. When <see langword="null"/>, the theme's accent.</summary>
    public Vector4? ActiveColor { get; set; }

    /// <summary>Whether hovering or dragging sets the resize cursor. On by default.</summary>
    public bool ShowResizeCursor { get; set; } = true;

    /// <summary>
    /// Paints the divider yourself, in place of the line NoireUI would draw.
    /// </summary>
    /// <remarks>
    /// The splitter still owns the handle, the drag and the clamping whatever this draws, so a hook that draws nothing
    /// is the way to make an existing divider draggable without changing how it looks. See
    /// <see cref="UiSplitterDraw.DrawLine()"/> for the shipped line, when the hook only wants to add to it.
    /// </remarks>
    public Action<UiSplitterDraw>? CustomDraw { get; set; }
}
