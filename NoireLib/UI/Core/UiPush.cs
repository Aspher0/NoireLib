using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// ImGui colours, style variables, fonts and disabled scopes pushed around a block of drawing and taken back off when
/// the block ends, for nothing.<br/>
/// <b>Dispose the accumulated value, once.</b> Disposing a copy as well pops more than was pushed and underflows the
/// ImGui stack. A <see langword="default"/> value pushes and pops nothing.
/// </summary>
/// <example>
/// <code>
/// using var pushed = UiPush.Color(ImGuiCol.Text, theme.Resolve(ThemeColor.Text));
/// ImGui.TextUnformatted(label);
/// </code>
/// Several at once, accumulated into one scope:
/// <code>
/// using var pushed = UiPush.Color(ImGuiCol.PopupBg, background);
/// pushed.Push(ImGuiCol.Border, border);
/// pushed.Push(ImGuiStyleVar.WindowRounding, rounding);
/// pushed.Push(ImGuiCol.Text, text, style.TextColor.HasValue);
/// </code>
/// </example>
internal ref struct UiPush
{
    private int colors;
    private int styleVars;
    private int fonts;
    private int disabled;
    private int textWrapPositions;

    /// <summary>
    /// How many style variables were already pushed when this scope was disabled, so the ones pushed after that can be
    /// popped before it is re-enabled.
    /// </summary>
    private int disabledAtStyleVars;

    #region Opening a scope

    /// <summary>
    /// Opens a scope with one colour pushed.
    /// </summary>
    /// <param name="target">The colour slot to override.</param>
    /// <param name="value">The colour to push.</param>
    /// <param name="when">Whether to push at all. When <see langword="false"/> the scope stays empty.</param>
    /// <returns>The scope. Dispose it to pop what it pushed.</returns>
    public static UiPush Color(ImGuiCol target, Vector4 value, bool when = true)
    {
        var scope = default(UiPush);
        scope.Push(target, value, when);
        return scope;
    }

    /// <summary>
    /// Opens a scope with one single-value style variable pushed.
    /// </summary>
    /// <param name="target">The style variable to override.</param>
    /// <param name="value">The value to push.</param>
    /// <param name="when">Whether to push at all. When <see langword="false"/> the scope stays empty.</param>
    /// <returns>The scope. Dispose it to pop what it pushed.</returns>
    public static UiPush Style(ImGuiStyleVar target, float value, bool when = true)
    {
        var scope = default(UiPush);
        scope.Push(target, value, when);
        return scope;
    }

    /// <summary>
    /// Opens a scope with one two-value style variable pushed.
    /// </summary>
    /// <param name="target">The style variable to override.</param>
    /// <param name="value">The value to push.</param>
    /// <param name="when">Whether to push at all. When <see langword="false"/> the scope stays empty.</param>
    /// <returns>The scope. Dispose it to pop what it pushed.</returns>
    public static UiPush Style(ImGuiStyleVar target, Vector2 value, bool when = true)
    {
        var scope = default(UiPush);
        scope.Push(target, value, when);
        return scope;
    }

    /// <summary>
    /// Opens a scope with a font pushed.
    /// </summary>
    /// <param name="font">The font to draw in.</param>
    /// <returns>The scope. Dispose it to go back to the previous font.</returns>
    public static UiPush Font(ImFontPtr font)
    {
        var scope = default(UiPush);
        scope.PushFont(font);
        return scope;
    }

    /// <summary>
    /// Opens a scope in which widgets are greyed out and do not respond.
    /// </summary>
    /// <param name="when">Whether to disable at all. When <see langword="false"/> the scope stays empty.</param>
    /// <returns>The scope. Dispose it to re-enable.</returns>
    public static UiPush Disabled(bool when = true)
    {
        var scope = default(UiPush);
        scope.PushDisabled(when);
        return scope;
    }

    /// <summary>
    /// Opens a scope in which text wraps at a given position.
    /// </summary>
    /// <param name="position">Where to wrap, in window coordinates.</param>
    /// <param name="when">Whether to push at all. When <see langword="false"/> the scope stays empty.</param>
    /// <returns>The scope. Dispose it to go back to the previous wrap position.</returns>
    public static UiPush TextWrapPos(float position, bool when = true)
    {
        var scope = default(UiPush);
        scope.PushTextWrapPos(position, when);
        return scope;
    }

    #endregion

    #region Adding to an open scope

    /// <summary>
    /// Pushes another colour onto this scope.
    /// </summary>
    /// <param name="target">The colour slot to override.</param>
    /// <param name="value">The colour to push.</param>
    /// <param name="when">Whether to push at all.</param>
    public void Push(ImGuiCol target, Vector4 value, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushStyleColor(target, value);
        colors++;
    }

    /// <summary>
    /// Pushes another single-value style variable onto this scope.
    /// </summary>
    /// <param name="target">The style variable to override.</param>
    /// <param name="value">The value to push.</param>
    /// <param name="when">Whether to push at all.</param>
    public void Push(ImGuiStyleVar target, float value, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushStyleVar(target, value);
        styleVars++;
    }

    /// <summary>
    /// Pushes another two-value style variable onto this scope.
    /// </summary>
    /// <param name="target">The style variable to override.</param>
    /// <param name="value">The value to push.</param>
    /// <param name="when">Whether to push at all.</param>
    public void Push(ImGuiStyleVar target, Vector2 value, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushStyleVar(target, value);
        styleVars++;
    }

    /// <summary>
    /// Pushes a font onto this scope.
    /// </summary>
    /// <param name="font">The font to draw in.</param>
    public void PushFont(ImFontPtr font)
    {
        // A null font is not pushed at all rather than pushed and ignored: ImGui balances these by call count, so
        // skipping both halves is the same to it. This is what lets a surface ask for the icon font and keep drawing
        // when there is no Dalamud behind the library to answer with one. See UiIconFont.
        if (font.IsNull)
            return;

        ImGui.PushFont(font);
        fonts++;
    }

    /// <summary>
    /// Disables widgets drawn inside this scope.
    /// </summary>
    /// <param name="when">Whether to disable at all.</param>
    public void PushDisabled(bool when = true)
    {
        if (!when)
            return;

        if (disabled == 0)
            disabledAtStyleVars = styleVars;

        ImGui.BeginDisabled();
        disabled++;
    }

    /// <summary>
    /// Wraps text at a given position for the rest of this scope.<br/>
    /// The position is absolute rather than scaled, matching what ImGui takes.
    /// </summary>
    /// <param name="position">Where to wrap, in window coordinates.</param>
    /// <param name="when">Whether to push at all.</param>
    public void PushTextWrapPos(float position, bool when = true)
    {
        if (!when)
            return;

        ImGui.PushTextWrapPos(position);
        textWrapPositions++;
    }

    #endregion

    /// <summary>
    /// Pops everything this scope pushed. Safe to call more than once.
    /// </summary>
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

    /// <summary>
    /// Pops a number of style variables, tolerating a count of zero.
    /// </summary>
    private static void Pop(int count)
    {
        if (count > 0)
            ImGui.PopStyleVar(count);
    }
}
