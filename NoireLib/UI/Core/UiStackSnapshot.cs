using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

/// <summary>
/// The depth of the ImGui style stacks at a point in time, and the ability to unwind back to it.<br/>
/// Every NoireUI container captures one before it runs a body and restores it afterwards, turning a raw
/// <c>ImGui.PushStyleColor</c> left unpopped inside that body into a single log line rather than a window that stays
/// the wrong colour for the rest of the frame.
/// </summary>
internal readonly struct UiStackSnapshot
{
    private readonly int colors;
    private readonly int styleVars;
    private readonly bool captured;

    private UiStackSnapshot(int colors, int styleVars)
    {
        this.colors = colors;
        this.styleVars = styleVars;
        captured = true;
    }

    /// <summary>
    /// Captures the current stack depths, or an inert snapshot when there is no ImGui context or leak repair is off.
    /// </summary>
    /// <returns>The snapshot to restore later.</returns>
    public static UiStackSnapshot Capture()
    {
        if (!NoireService.IsInitialized() || !NoireUI.Diagnostics.RepairStackLeaks)
            return default;

        return new UiStackSnapshot(ColorDepth, StyleVarDepth);
    }

    /// <summary>
    /// Pops whatever was pushed since the snapshot and was not popped.
    /// </summary>
    /// <param name="containerName">The container whose body leaked, named in the log.</param>
    /// <returns>How many stack entries were unwound.</returns>
    public int Restore(string containerName)
    {
        if (!captured)
            return 0;

        var leakedColors = ColorDepth - colors;
        var leakedStyleVars = StyleVarDepth - styleVars;

        if (leakedColors <= 0 && leakedStyleVars <= 0)
            return 0;

        if (leakedColors > 0)
            ImGui.PopStyleColor(leakedColors);

        if (leakedStyleVars > 0)
            ImGui.PopStyleVar(leakedStyleVars);

        var total = (leakedColors > 0 ? leakedColors : 0) + (leakedStyleVars > 0 ? leakedStyleVars : 0);
        NoireUI.Diagnostics.NoteStackRepair(containerName, total);
        return total;
    }

    private static int ColorDepth => ImGui.GetCurrentContext().ColorStack.Size;

    private static int StyleVarDepth => ImGui.GetCurrentContext().StyleVarStack.Size;
}
