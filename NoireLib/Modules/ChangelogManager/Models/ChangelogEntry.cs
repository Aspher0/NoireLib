using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Numerics;

namespace NoireLib.Changelog;

/// <summary>
/// Represents a single entry in the changelog.
/// </summary>
public record ChangelogEntry
{
    /// <summary>
    /// Gets the text content associated with this entry.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the color of the <see cref="Text"/> for this entry.
    /// </summary>
    public Vector4? TextColor { get; init; }

    /// <summary>
    /// Gets the FontAwesome icon associated with this entry.
    /// </summary>
    public FontAwesomeIcon? Icon { get; init; }

    /// <summary>
    /// Gets the color of the <see cref="Icon"/> for this entry.
    /// </summary>
    public Vector4? IconColor { get; init; }

    /// <summary>
    /// The text to display on the button for this entry.
    /// </summary>
    public string? ButtonText { get; init; }

    /// <summary>
    /// The color of the <see cref="ButtonText"/> for this entry.
    /// </summary>
    public Vector4? ButtonTextColor { get; init; }

    /// <summary>
    /// The color of the button background for this entry.
    /// </summary>
    public Vector4? ButtonColor { get; init; }

    /// <summary>
    /// The action to perform when the button is clicked.
    /// </summary>
    public Action<ImGuiMouseButton>? ButtonAction { get; init; }

    /// <summary>
    /// Determines if this entry is a header.
    /// </summary>
    public bool IsHeader { get; init; } = false;

    /// <summary>
    /// Determines if this entry is a separator.
    /// </summary>
    public bool IsSeparator { get; init; } = false;

    /// <summary>
    /// The indentation level for this entry.
    /// </summary>
    public int IndentLevel { get; init; } = 0;

    /// <summary>
    /// Determines if this entry has a leading bullet point.
    /// </summary>
    public bool HasBullet { get; init; } = false;

    /// <summary>
    /// Determines if this entry is a raw entry (custom rendering).
    /// </summary>
    public bool IsRaw { get; init; } = false;

    /// <summary>
    /// The raw action to perform for this entry if <see cref="IsRaw"/> is true.
    /// </summary>
    public Action? RawAction { get; init; }
}
