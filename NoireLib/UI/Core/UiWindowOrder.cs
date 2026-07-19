using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

/// <summary>
/// Keeps a window in front of the others, for input as well as for drawing.
/// </summary>
/// <remarks>
/// ImGui decides "which window is in front" twice, and staying on top needs both answers.<br/>
/// <b>Drawing</b> is decided by the draw layer first and the display list second.
/// <see cref="TopLayerFlag"/> puts a window in the topmost layer, above everything in the ordinary one whatever the
/// display list happens to say that frame.<br/>
/// <b>Input</b> is decided by the display list alone: hit testing walks it back to front and stops at the first window
/// under the mouse, paying no attention to layers. A window promoted only by layer is therefore painted over
/// everything and receives none of the clicks aimed at it.<br/>
/// Using only the display list has the opposite problem. Clicking a window behind focuses it, which moves it to the
/// front of that list; the next frame puts this one back, and the round trip shows up as a flicker. The layer is what
/// makes the drawing immune to that churn while the reordering keeps the input right.
/// </remarks>
internal static class UiWindowOrder
{
    /// <summary>
    /// The window flag that promotes a window to the topmost draw layer.<br/>
    /// Named for what it does here rather than for the tooltips ImGui uses it for: it brings no tooltip behaviour with
    /// it, and nothing about it prevents a window from taking input.
    /// </summary>
    /// <remarks>
    /// It does carry one thing, and it is worth knowing before putting this on a window with a background. ImGui reads a
    /// window's background colour from an index it picks by flag, and this flag selects <c>PopupBg</c> where an ordinary
    /// window would use <c>WindowBg</c>. A window that pushes <c>WindowBg</c> and then sets this flag is drawn in the
    /// theme's popup colour instead, silently and only once it is promoted.<br/>
    /// Windows that draw no background of their own (<c>NoBackground</c>) are unaffected, which is most of the ones
    /// using this. Anything else has to push the colour to the index the flag actually selects.
    /// </remarks>
    internal const ImGuiWindowFlags TopLayerFlag = ImGuiWindowFlags.Tooltip;

    /// <summary>
    /// Moves the window currently being drawn to the front of the display list, so it takes input before every other
    /// window.<br/>
    /// Pair it with <see cref="TopLayerFlag"/> on the same window. Call it once per frame between <c>Begin</c> and
    /// <c>End</c>: focusing any other window undoes it, which is why it is reapplied every frame rather than set once.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>SetNextWindowFocus</c>, which would take keyboard focus every frame as well and make text
    /// fields in every other window impossible to type in.<br/>
    /// Within the top layer the order is still the display list, so the last window to call this each frame wins. That
    /// is what keeps a tooltip above an always-on-top element it overlaps: the tooltip is drawn after the thing it
    /// annotates, so it asks last.
    /// </remarks>
    internal static void KeepInFront()
    {
        if (!NoireService.IsInitialized())
            return;

        ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());
    }
}
