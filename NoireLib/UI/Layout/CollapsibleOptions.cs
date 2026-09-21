using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// How a collapsible section built with <see cref="NoireLayout.Collapsible(string, string, Action, CollapsibleOptions)"/>
/// behaves and looks.
/// </summary>
public sealed class CollapsibleOptions
{
    /// <summary>
    /// Whether the section starts open the first time it is drawn. Defaults to <see langword="true"/>.
    /// </summary>
    public bool DefaultOpen { get; set; } = true;

    /// <summary>
    /// Whether the open state survives a reload, stored in <see cref="NoireUiState"/> against the section's id.
    /// </summary>
    public bool Persist { get; set; }

    /// <summary>
    /// Prose shown under the heading while the section is open, wrapped to the available width.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Extra content drawn on the header row, right-aligned: a count, a reset button, a status chip.
    /// </summary>
    public Action? HeaderExtras { get; set; }

    /// <summary>
    /// The width reserved for <see cref="HeaderExtras"/> in pixels. When <see langword="null"/>, the remaining space on
    /// the header row is used.
    /// </summary>
    public float? HeaderExtrasWidth { get; set; }

    /// <summary>
    /// Draws the header in the theme's danger color, for a section holding destructive settings.
    /// </summary>
    public bool Danger { get; set; }

    /// <summary>
    /// The color of the header label and arrow. When <see langword="null"/>, the theme text color is used, or the
    /// danger color when <see cref="Danger"/> is set.
    /// </summary>
    public Vector4? HeaderColor { get; set; }

    /// <summary>
    /// The fill behind the header. When <see langword="null"/>, the theme's <see cref="ThemeColor.Control"/> color at 30%
    /// opacity. A zero alpha draws no fill and moves the hover to the label.
    /// </summary>
    public Vector4? HeaderBackground { get; set; }

    /// <summary>
    /// The fill behind the header while it is hovered. When <see langword="null"/>, <see cref="HeaderBackground"/>
    /// shifted by the theme's hover amount.
    /// </summary>
    public Vector4? HeaderHoveredBackground { get; set; }

    /// <summary>
    /// The corner radius of the header fill in pixels at 100% scale. When <see langword="null"/>, the theme rounding.
    /// </summary>
    public float? HeaderRounding { get; set; }

    /// <summary>
    /// The space between the edge of the header fill and its arrow and label, in pixels at 100% scale. When
    /// <see langword="null"/>, the theme frame padding.
    /// </summary>
    public Vector2? HeaderPadding { get; set; }

    /// <summary>
    /// How far the body is indented under the header, in pixels. Zero uses the current ImGui indent step.
    /// </summary>
    public float Indent { get; set; }

    /// <summary>
    /// Whether a separator is drawn under the header. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Separator { get; set; } = true;

    /// <summary>
    /// How long the arrow takes to turn, in seconds. Ignored under <see cref="NoireUI.ReducedMotion"/>.
    /// </summary>
    public float AnimationDuration { get; set; } = 0.12f;
}
