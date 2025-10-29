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
    public string? Text { get; init; }
    public Vector4? TextColor { get; init; }
    public FontAwesomeIcon? Icon { get; init; }
    public Vector4? IconColor { get; init; }
    public string? ButtonText { get; init; }
    public Vector4? ButtonTextColor { get; init; }
    public Vector4? ButtonColor { get; init; }
    public Action<ImGuiMouseButton>? ButtonAction { get; init; }
    public bool IsHeader { get; init; } = false;
    public bool IsSeparator { get; init; } = false;
    public int IndentLevel { get; init; } = 0;
    public bool HasBullet { get; init; } = false;
    public bool IsRaw { get; init; } = false;
    public Action? RawAction { get; init; }
}
