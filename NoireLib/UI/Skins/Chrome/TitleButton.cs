using NoireLib.Localizer;
using System;

namespace NoireLib.UI;

/// <summary>A button a window puts in its title bar. The chrome decides how it looks; a native chrome makes it a Dalamud title bar button.</summary>
/// <param name="Id">A stable id, also the key the user hides it under.</param>
/// <param name="Icon">The icon.</param>
/// <param name="Tooltip">The tooltip.</param>
/// <param name="Click">What it does.</param>
public sealed record TitleButton(string Id, NoireIcon Icon, NoireString Tooltip, Action Click)
{
    /// <summary>Whether it shows, read each frame, or <see langword="null"/> for always.</summary>
    public Func<bool>? Visible { get; init; }
}
