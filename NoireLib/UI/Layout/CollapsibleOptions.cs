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
    /// Whether the open state survives a reload, stored in <see cref="NoireUiState"/> against the section's id.<br/>
    /// Off by default, like every persistence switch in the library.
    /// </summary>
    /// <remarks>
    /// The id is what the state is keyed on, so it has to be stable across sessions. A section given a blank id, or one
    /// built from something that changes each run, refuses to persist and logs once rather than filling the state file
    /// with entries nothing will ever read back.
    /// </remarks>
    public bool Persist { get; set; }

    /// <summary>
    /// Prose shown under the heading while the section is open, wrapped to the available width.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Extra content drawn on the header row, right-aligned: a count, a reset button, a status chip.<br/>
    /// Drawn whether the section is open or closed, so a summary stays visible once the detail is folded away.
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
    /// The header color. When <see langword="null"/>, the theme text color is used, or the danger color when
    /// <see cref="Danger"/> is set.
    /// </summary>
    public Vector4? HeaderColor { get; set; }

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
