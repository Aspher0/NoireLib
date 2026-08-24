using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

// Dispose the accumulated value, once. Disposing a copy as well pops more than was pushed and underflows the ImGui
// stack. A default value pushes and pops nothing.
internal ref struct UiPush
{
    private int colors;
    private int styleVars;
    private int fonts;
    private int disabled;
    private int textWrapPositions;

    // How many style variables were already pushed when this scope was disabled, so the ones pushed after that can be
    // popped before it is re-enabled.
    private int disabledAtStyleVars;

    #region Opening a scope

    public static UiPush Color(ImGuiCol target, Vector4 value, bool when = true)
    {
        var scope = default(UiPush);
        scope.Push(target, value, when);
        return scope;
    }

    public static UiPush Style(ImGuiStyleVar target, float value, bool when = true)
    {
        var scope = default(UiPush);
        scope.Push(target, value, when);
        return scope;
    }

    public static UiPush Style(ImGuiStyleVar target, Vector2 value, bool when = true)
    {
        var scope = default(UiPush);
        scope.Push(target, value, when);
        return scope;
    }

    public static UiPush Font(ImFontPtr font)
    {
        var scope = default(UiPush);
        scope.PushFont(font);
        return scope;
    }

    public static UiPush Disabled(bool when = true)
    {
        var scope = default(UiPush);
        scope.PushDisabled(when);
        return scope;
    }

    public static UiPush TextWrapPos(float position, bool when = true)
    {
        var scope = default(UiPush);
        scope.PushTextWrapPos(position, when);
        return scope;
    }

    #endregion

    #region Adding to an open scope

    public void Push(ImGuiCol target, Vector4 value, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushStyleColor(target, value);
        colors++;
    }

    public void Push(ImGuiStyleVar target, float value, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushStyleVar(target, value);
        styleVars++;
    }

    public void Push(ImGuiStyleVar target, Vector2 value, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushStyleVar(target, value);
        styleVars++;
    }

    public void PushFont(ImFontPtr font)
    {
        // A null font is not pushed at all rather than pushed and ignored: ImGui balances these by call count, so
        // skipping both halves is the same to it. See UiIconFont.
        if (font.IsNull)
            return;

        ImGui.PushFont(font);
        fonts++;
    }

    public void PushDisabled(bool when = true)
    {
        if (!when)
            return;

        if (disabled == 0)
            disabledAtStyleVars = styleVars;

        ImGui.BeginDisabled();
        disabled++;
    }

    // The position is absolute rather than scaled, matching what ImGui takes.
    public void PushTextWrapPos(float position, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushTextWrapPos(position);
        textWrapPositions++;
    }

    #endregion

    // Safe to call more than once.
    public void Dispose()
    {
        if (disabled > 0)
        {
            Pop(styleVars - disabledAtStyleVars);
            styleVars = disabledAtStyleVars;

            while (disabled > 0)
            {
                ImGui.EndDisabled();
                disabled--;
            }
        }

        while (fonts > 0)
        {
            ImGui.PopFont();
            fonts--;
        }

        while (textWrapPositions > 0)
        {
            ImGui.PopTextWrapPos();
            textWrapPositions--;
        }

        Pop(styleVars);
        styleVars = 0;

        if (colors > 0)
        {
            ImGui.PopStyleColor(colors);
            colors = 0;
        }
    }

    private static void Pop(int count)
    {
        if (count > 0)
            ImGui.PopStyleVar(count);
    }
}
