using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a <see cref="NoireLayout.Splitter(string, ref float, SplitterOptions)"/> behaves and looks.
/// </summary>
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
    /// current region.
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

    /// <summary>Whether hovering or dragging sets the resize cursor.</summary>
    public bool ShowResizeCursor { get; set; } = true;

    /// <summary>
    /// Paints the divider yourself, in place of the line NoireUI would draw. See
    /// <see cref="UiSplitterDraw.DrawLine()"/> for the shipped line.
    /// </summary>
    public Action<UiSplitterDraw>? CustomDraw { get; set; }
}
