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

    // Brings the popups this window opened in front of it, into its top layer when kept in front. Call before its End.
    internal static unsafe void KeepPopupsInFront(bool topLayer)
    {
        if (!NoireService.IsInitialized())
            return;

        var owner = ImGuiP.GetCurrentWindow().RootWindow;
        var stack = UiContext.Current.OpenPopupStack;

        for (var index = 0; index < stack.Size; index++)
        {
            var popup = new ImGuiWindowPtr(stack[index].Window);

            if (popup.IsNull || !popup.Active || popup.ParentWindow.IsNull || popup.ParentWindow.RootWindow.Handle != owner.Handle)
                continue;

            ImGuiP.BringWindowToDisplayFront(popup);

            if (topLayer)
                popup.Flags |= TopLayerFlag;
        }
    }

    // Inside a popup the current window is the popup.
    internal static bool InTopLayer
        => NoireService.IsInitialized() && (ImGuiP.GetCurrentWindow().RootWindow.Flags & TopLayerFlag) != 0;
}
