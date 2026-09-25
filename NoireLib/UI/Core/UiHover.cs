using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

// ImGui gives the hover to the first item under the mouse even when that item is disabled, and no later item can take it.
// A dialog drawn over a disabled body would then never be hovered or clicked wherever the body has an item beneath it.
internal static class UiHover
{
    // A disabled item releases the hover to the items drawn over it.
    internal static void ReleaseDisabled()
    {
        if (UiContext.Current.HoveredIdDisabled)
            ImGuiP.SetHoveredID(0);
    }
}
