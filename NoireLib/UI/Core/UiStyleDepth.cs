using Dalamud.Bindings.ImGui;

namespace NoireLib.UI;

// The depth of ImGui's colour and style variable stacks, for popping whatever a skin pushed without asking it to count.
internal readonly struct UiStyleDepth
{
    private readonly int colors;
    private readonly int styleVars;

    private UiStyleDepth(int colors, int styleVars)
    {
        this.colors = colors;
        this.styleVars = styleVars;
    }

    public static UiStyleDepth Capture()
    {
        var context = UiContext.Current;
        return new UiStyleDepth(context.ColorStack.Size, context.StyleVarStack.Size);
    }

    // What was pushed since this capture, as the counts to pop.
    public (int Colors, int StyleVars) Since()
    {
        var context = UiContext.Current;
        return (context.ColorStack.Size - colors, context.StyleVarStack.Size - styleVars);
    }

    public static void Pop((int Colors, int StyleVars) pushed)
    {
        if (pushed.Colors > 0)
            ImGui.PopStyleColor(pushed.Colors);

        if (pushed.StyleVars > 0)
            ImGui.PopStyleVar(pushed.StyleVars);
    }
}
