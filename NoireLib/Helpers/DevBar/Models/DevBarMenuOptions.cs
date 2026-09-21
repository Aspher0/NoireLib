using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>What one dev bar menu holds. Supply <see cref="Items"/>, <see cref="OnDraw"/>, or both.</summary>
public sealed class DevBarMenuOptions
{
    /// <summary>The menu's name in the bar. Unique among the plugin's menus.</summary>
    public required string Name { get; init; }

    /// <summary>The lines the menu draws, in order.</summary>
    public IReadOnlyList<DevBarItem>? Items { get; init; }

    /// <summary>
    /// Draws whatever the caller likes inside the open menu, after <see cref="Items"/>. It runs between the menu's
    /// begin and end. Any ImGui call is valid there.
    /// </summary>
    public Action? OnDraw { get; init; }

    /// <summary>Where the menu sits among the ones this library adds, lower first.</summary>
    public int Priority { get; init; }
}
