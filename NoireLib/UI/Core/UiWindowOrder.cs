using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

internal static class UiWindowOrder
{
    // ImGui moves any other window begun with this flag to the mouse and reads PopupBg for its background.
    internal const ImGuiWindowFlags TopLayerFlag = ImGuiWindowFlags.Tooltip;

    // Call once per frame between Begin and End, with TopLayerFlag. Focusing any other window undoes it.
    internal static void KeepInFront()
    {
        if (!NoireService.IsInitialized())
            return;

        var window = ImGuiP.GetCurrentWindow();

        ImGuiP.BringWindowToDisplayFront(window);
        window.RootWindow.Flags |= TopLayerFlag;
    }

    // Inside a popup the current window is the popup.
    internal static bool InTopLayer
        => NoireService.IsInitialized() && (ImGuiP.GetCurrentWindow().RootWindow.Flags & TopLayerFlag) != 0;
}
