using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

// Keeps a window in front of the others, for input as well as for drawing.
internal static class UiWindowOrder
{
    // Only for a window that sets its own position every frame: ImGui moves any other window begun with this flag to
    // the mouse, and reads the window background from PopupBg rather than WindowBg.
    internal const ImGuiWindowFlags TopLayerFlag = ImGuiWindowFlags.Tooltip;

    // Pair it with TopLayerFlag on the same window, once per frame between Begin and End: focusing any other window
    // undoes it. Within the top layer the order is still the display list, so the last window to call this wins.
    internal static void KeepInFront()
    {
        if (!NoireService.IsInitialized())
            return;

        var window = ImGuiP.GetCurrentWindow();

        ImGuiP.BringWindowToDisplayFront(window);
        window.RootWindow.Flags |= TopLayerFlag;
    }

    // Read it before the popup is opened, since inside one the current window is the popup itself.
    internal static bool InTopLayer
        => NoireService.IsInitialized() && (ImGuiP.GetCurrentWindow().RootWindow.Flags & TopLayerFlag) != 0;
}
