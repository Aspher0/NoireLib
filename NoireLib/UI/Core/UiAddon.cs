using NoireLib.Helpers;
using System;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// Reads the on-screen bounds of native game windows, so a NoireUI element can be placed against one and follow it as
/// the player moves, rescales or closes it.
/// </summary>
/// <remarks>
/// Every rectangle here is relative to the top left corner of the game window, in real pixels, which is the same frame
/// of reference <see cref="UiPositionMode.Absolute"/> uses. Resolving a position adds the ImGui viewport origin, so
/// nothing here needs an active frame and all of it stays testable.<br/>
/// Addon reads are safe from the draw thread: the pointers come from Dalamud's own lookup and every member fails soft
/// to "not there" rather than throwing, including before the library has been initialized at all.
/// </remarks>
public static class UiAddon
{
    /// <summary>
    /// Gets the bounds of a native game window.
    /// </summary>
    /// <param name="addonName">The addon name, for example <c>_PartyList</c>.</param>
    /// <param name="rect">The bounds, or <see cref="UiRect.Empty"/> when the addon is not on screen.</param>
    /// <returns>True when the addon exists, is visible, and has a real size.</returns>
    public static bool TryGetRect(string addonName, out UiRect rect)
    {
        rect = UiRect.Empty;

        if (string.IsNullOrWhiteSpace(addonName) || !NoireService.IsInitialized())
            return false;

        var addon = NoireAddon.Get(addonName);

        if (!addon.IsReady || !addon.IsVisible)
            return false;

        var size = new Vector2(addon.Width, addon.Height);

        if (size.X <= 1f || size.Y <= 1f)
            return false;

        rect = new UiRect(new Vector2(addon.X, addon.Y), size);
        return true;
    }

    /// <summary>
    /// Gets the bounds of a native game window, or <see langword="null"/> when it is not on screen.
    /// </summary>
    /// <remarks>
    /// This is the shape <see cref="UiPosition.TryResolve(Vector2, Vector2, Vector2, Func{string, UiRect?}, out Vector2)"/>
    /// takes, so a caller can substitute its own source of rectangles for a preview, an editor, or a test.
    /// </remarks>
    /// <param name="addonName">The addon name, for example <c>_PartyList</c>.</param>
    /// <returns>The bounds, or <see langword="null"/>.</returns>
    public static UiRect? GetRect(string addonName)
        => TryGetRect(addonName, out var rect) ? rect : null;

    /// <summary>
    /// Whether a native game window is currently on screen.
    /// </summary>
    /// <param name="addonName">The addon name.</param>
    /// <returns>True when it exists and is visible.</returns>
    public static bool IsVisible(string addonName)
        => TryGetRect(addonName, out _);

    /// <summary>
    /// The live source of addon rectangles, used whenever a caller does not supply one of its own.
    /// </summary>
    internal static readonly Func<string, UiRect?> LiveRects = GetRect;
}
