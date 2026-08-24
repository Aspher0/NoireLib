using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

/// <summary>
/// Keeps a window in front of the others, for input as well as for drawing.
/// </summary>
internal static class UiWindowOrder
{
    /// <summary>
    /// The window flag that promotes a window to the topmost draw layer.<br/>
    /// Named for what it does here rather than for the tooltips ImGui uses it for: it brings no tooltip behaviour with
    /// it, and nothing about it prevents a window from taking input.<br/>
    /// Only for a window that sets its own position every frame: ImGui moves any other window begun with this flag to
    /// the mouse, and reads the window background from <c>PopupBg</c> rather than <c>WindowBg</c>.
    /// </summary>
    internal const ImGuiWindowFlags TopLayerFlag = ImGuiWindowFlags.Tooltip;

    /// <summary>
    /// Moves the window currently being drawn to the front of the display list, so it takes input before every other
    /// window.<br/>
    /// Pair it with <see cref="TopLayerFlag"/> on the same window. Call it once per frame between <c>Begin</c> and
    /// <c>End</c>: focusing any other window undoes it, so it is reapplied every frame rather than set once.<br/>
    /// Within the top layer the order is still the display list, so the last window to call this each frame wins.
    /// </summary>
    internal static void KeepInFront()
    {
        if (!NoireService.IsInitialized())
            return;

        var window = ImGuiP.GetCurrentWindow();

        ImGuiP.BringWindowToDisplayFront(window);
        window.RootWindow.Flags |= TopLayerFlag;
    }

    /// <summary>
    /// Whether the window being drawn is in the top layer, so that a popup opened from it can join it.<br/>
    /// Read it before the popup is opened, since inside one the current window is the popup itself.
    /// </summary>
    internal static bool InTopLayer
        => NoireService.IsInitialized() && (ImGuiP.GetCurrentWindow().RootWindow.Flags & TopLayerFlag) != 0;
}
