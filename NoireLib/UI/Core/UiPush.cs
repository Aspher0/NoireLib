using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// ImGui colours, style variables, fonts and disabled scopes pushed around a block of drawing and taken back off when
/// the block ends, for nothing.
/// </summary>
/// <remarks>
/// This exists because <c>ImRaii</c> allocates: its push wrappers are classes, so each call costs 24 bytes on the draw
/// thread, even when the condition it was given is <see langword="false"/> and it pushes nothing. The raw
/// <see cref="ImGui.PushStyleColor(ImGuiCol, Vector4)"/> and its siblings cost nothing, and a
/// <see langword="ref struct"/> cannot be boxed into the <see cref="System.IDisposable"/> that would put the cost
/// back.<br/>
/// <b>Dispose the accumulated value, once.</b> The accumulating methods mutate in place and return nothing, so that no
/// copy exists to dispose twice: two copies would pop more than was pushed, which underflows the ImGui stack rather
/// than leaking it.<br/>
/// A <see langword="default"/> value pushes and pops nothing, which is what a method returns when it turns out to have
/// no style to apply.
/// </remarks>
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
    /// <remarks>
    /// The colour, style-variable and font stacks are independent, so the order they unwind in does not matter. Being
    /// disabled is not a stack: <c>BeginDisabled</c> multiplies the style's alpha and remembers the value it displaced,
    /// and <c>EndDisabled</c> writes that remembered value straight back. So a style variable pushed while disabled has
    /// to come off before <c>EndDisabled</c> restores the alpha, or the pop puts the disabled alpha back afterwards and
    /// every window drawn for the rest of the frame is faded. Measured in both directions rather than reasoned about.
    /// </remarks>
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
        ImGui.PushFont(font);
        fonts++;
    }

    /// <summary>
    /// Disables widgets drawn inside this scope.
    /// </summary>
    /// <remarks>
    /// Nothing is begun when <paramref name="when"/> is <see langword="false"/>, rather than beginning a disabled scope
    /// that disables nothing. ImGui balances these by call count, so skipping both halves is the same to it and cheaper.
    /// </remarks>
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
    /// Wraps text at a given position for the rest of this scope.
    /// </summary>
    /// <remarks>
    /// The position is absolute rather than scaled, which is what ImGui takes and what every caller here already
    /// computes from the cursor and the region available.
    /// </remarks>
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
    /// <remarks>
    /// The counts are cleared as they are spent, so a second call has nothing left to pop rather than popping a second
    /// time into whatever the surrounding code had pushed.<br/>
    /// Style variables straddle the disabled scope: the ones pushed inside it come off first, then the scope is
    /// re-enabled, then the ones pushed before it. See <see cref="disabledAtStyleVars"/> for what goes wrong otherwise.
    /// </remarks>
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
