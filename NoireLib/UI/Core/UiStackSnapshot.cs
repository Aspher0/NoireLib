using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

// Every NoireUI container captures one before it runs a body and restores it afterwards, turning a raw
// ImGui.PushStyleColor left unpopped inside that body into a single log line.
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

    public static UiStackSnapshot Capture()
    {
        if (!NoireService.IsInitialized() || !NoireUI.Diagnostics.RepairStackLeaks)
            return default;

        return new UiStackSnapshot(ColorDepth, StyleVarDepth);
    }

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
