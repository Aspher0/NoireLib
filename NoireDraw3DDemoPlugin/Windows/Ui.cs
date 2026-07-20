using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using NoireLib.Draw3D;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows;

/// <summary>
/// The demo's widget kit. Pages are built from these so the window reads as one thing instead of nine.
/// <para>
/// The unit of layout is the <b>form</b>: a two-column table, captions left, controls stretched right. Captions line up
/// down the page and controls share an edge, which is the whole reason a settings panel is scannable. Widgets bind
/// through a getter/setter and write back only on change, so pages keep no mirror state.
/// </para>
/// </summary>
internal static class Ui
{
    /// <summary>Caption-column width, before DPI scaling.</summary>
    private const float LabelColumnWidth = 190f;

    /// <summary>Tooltip wrap width in ems. Long help stays a readable column instead of one endless line.</summary>
    private const float TooltipWrapEm = 24f;

    /// <summary>The one accent. Nav selection and section rules; everything else is theme default or grey.</summary>
    public static readonly Vector4 Accent = new(0.45f, 0.72f, 0.90f, 1f);

    private static int formDepth;

    /// <summary>Dalamud's global DPI scale, applied to every hard-coded size here.</summary>
    public static float Scale => ImGuiHelpers.GlobalScale;

    // ---------------------------------------------------------------- chrome

    /// <summary>
    /// The window's style: tighter than stock ImGui, which is loose enough that a dense panel reads as a pile. Pushed once
    /// per frame around the whole window.
    /// </summary>
    public static IDisposable Style() =>
        ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 5f) * Scale, true)
              .Push(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f) * Scale, true)
              .Push(ImGuiStyleVar.CellPadding, new Vector2(4f, 3f) * Scale, true)
              .Push(ImGuiStyleVar.FrameRounding, 3f * Scale, true)
              .Push(ImGuiStyleVar.GrabRounding, 3f * Scale, true)
              .Push(ImGuiStyleVar.ChildRounding, 4f * Scale, true)
              .Push(ImGuiStyleVar.ScrollbarSize, 11f * Scale, true);

    /// <summary>
    /// A group heading: a small accent caption with a rule running out to the right margin. Drawn to the draw list rather
    /// than as a full-width separator so the label and the rule sit on one line.
    /// </summary>
    /// <param name="title">The group name.</param>
    public static void Section(string title)
    {
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted(title.ToUpperInvariant());

        var rect = (Min: ImGui.GetItemRectMin(), Max: ImGui.GetItemRectMax());
        var y = MathF.Floor((rect.Min.Y + rect.Max.Y) * 0.5f) + 0.5f;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var left = rect.Max.X + 8f * Scale;
        if (right > left)
            ImGui.GetWindowDrawList().AddLine(new Vector2(left, y), new Vector2(right, y), ImGui.GetColorU32(ImGuiCol.Separator));

        ImGui.Spacing();
    }

    /// <summary>Dimmed wrapped prose. For the rare note that carries something the control names cannot.</summary>
    /// <param name="text">The prose.</param>
    public static void Note(string text)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3);
        ImGui.TextWrapped(text);
    }

    /// <summary>A dimmed live status line. Draws nothing when empty.</summary>
    /// <param name="text">The status text.</param>
    public static void Status(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Note(text);
    }

    /// <summary>A coloured callout for a caveat or prerequisite worth interrupting for.</summary>
    /// <param name="text">The message.</param>
    /// <param name="color">Its colour; defaults to Dalamud's warning yellow.</param>
    public static void Callout(string text, Vector4? color = null)
    {
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color ?? ImGuiColors.DalamudYellow);
        ImGui.TextWrapped(text);
    }

    /// <summary>Vertical breathing room between blocks.</summary>
    public static void Gap() => ImGui.Dummy(new Vector2(0f, 3f * Scale));

    /// <summary>
    /// A scrolling body filling the rest of the current window or child. Anything that can overflow goes in one of these,
    /// so whatever sits above it stays put: a tab bar or a toolbar inside the scroll region would slide away with the
    /// content, and a tab strip you have to scroll back up to reach is not a tab strip.
    /// </summary>
    /// <param name="id">A unique id for the child.</param>
    public static ImRaii.ChildDisposable Scroll(string id) => ImRaii.Child(id, Vector2.Zero, false);

    /// <summary>A <c>using</c>-scoped block that greys out and blocks everything inside it.</summary>
    /// <param name="disabled">Whether to disable the contents.</param>
    public static IDisposable Disabled(bool disabled) => ImRaii.Disabled(disabled);

    /// <summary>Numbers in the mono font, so columns of them line up and stop jittering as they change.</summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="color">Optional colour.</param>
    public static void Mono(string text, Vector4? color = null)
    {
        using var font = ImRaii.PushFont(UiBuilder.MonoFont);
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color ?? ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.TextUnformatted(text);
    }

    /// <summary>Draws a FontAwesome glyph in the fixed-width icon font.</summary>
    /// <param name="icon">The icon.</param>
    public static void Icon(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFontFixedWidth);
        ImGui.TextUnformatted(icon.ToIconString());
    }

    /// <summary>A button captioned with an icon and a label, in the two fonts that needs.</summary>
    /// <param name="icon">The leading glyph.</param>
    /// <param name="label">The caption, also the widget id.</param>
    /// <param name="width">Button width before scaling; 0 fits the content.</param>
    public static bool IconButton(FontAwesomeIcon icon, string label, float width = 0f)
    {
        var iconText = icon.ToIconString();
        float iconWidth;
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
            iconWidth = ImGui.CalcTextSize(iconText).X;

        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var textWidth = ImGui.CalcTextSize(label, true).X;
        var size = new Vector2(
            width > 0f ? width * Scale : iconWidth + textWidth + spacing + ImGui.GetStyle().FramePadding.X * 2f,
            0f);

        var start = ImGui.GetCursorScreenPos();
        var pressed = ImGui.Button($"##{label}", size);

        // The caption is painted over the button: it needs two fonts, which a button label cannot carry.
        var pad = ImGui.GetStyle().FramePadding;
        var dl = ImGui.GetWindowDrawList();
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
            dl.AddText(start + pad, textColor, iconText);
        dl.AddText(start + pad with { X = pad.X + iconWidth + spacing }, textColor, label);

        return pressed;
    }

    // ---------------------------------------------------------------- form

    /// <summary>The scope opened by <see cref="Form"/>: a two-column caption/control table.</summary>
    public readonly ref struct FormScope
    {
        private readonly bool open;

        internal FormScope(string id, float labelWidth)
        {
            open = ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingFixedFit);
            if (!open)
                return;

            ImGui.TableSetupColumn("##caption", ImGuiTableColumnFlags.WidthFixed, labelWidth * Scale);
            ImGui.TableSetupColumn("##control", ImGuiTableColumnFlags.WidthStretch);
            formDepth++;
        }

        /// <summary>Closes the form table.</summary>
        public void Dispose()
        {
            if (!open)
                return;

            formDepth--;
            ImGui.EndTable();
        }
    }

    /// <summary>Opens a caption/control form. Rows drawn outside one still render, stacked, rather than corrupting the enclosing table.</summary>
    /// <param name="id">A unique id for the underlying table.</param>
    /// <param name="labelWidth">Caption-column width before scaling. Narrow it inside a split pane, where the default would starve the controls.</param>
    public static FormScope Form(string id, float labelWidth = LabelColumnWidth) => new(id, labelWidth);

    /// <summary>
    /// Opens one form row: caption (and its help marker) left, cursor left in the control cell with the next item
    /// stretched to fill. Public so a page can put a button strip or a custom widget in the cell and still line up.
    /// </summary>
    /// <param name="label">The caption.</param>
    /// <param name="hint">Optional help, shown on hover of the caption or its marker.</param>
    public static void Row(string label, string? hint = null)
    {
        if (formDepth > 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        HelpMarker(hint);

        if (formDepth > 0)
            ImGui.TableNextColumn();

        ImGui.SetNextItemWidth(-1f);
    }

    /// <summary>
    /// Appends the "(?)" marker to the item just drawn, and shows <paramref name="hint"/> when either that item or the
    /// marker is hovered - so the caption itself is a hover target, not just the glyph.
    /// </summary>
    /// <param name="hint">The help text. Nothing is drawn when it is empty.</param>
    public static void HelpMarker(string? hint)
    {
        if (string.IsNullOrEmpty(hint))
            return;

        var captionHovered = ImGui.IsItemHovered();
        ImGui.SameLine(0f, 4f * Scale);
        ImGui.TextDisabled("(?)");
        if (captionHovered || ImGui.IsItemHovered())
            Tooltip(hint);
    }

    /// <summary>
    /// Orientation overrides for imported models, shared by every page that imports one.<br/>
    /// Off by default and correct that way for the game's own models and for a conforming glTF. The toggles
    /// live on the library so both loaders run the same code, and the panel is shared so the two pages driving
    /// it cannot describe it differently.
    /// </summary>
    /// <param name="id">A unique form id for the page hosting it.</param>
    /// <returns>Whether a toggle changed this frame, so a caller holding decoded meshes can rebuild them.</returns>
    public static bool ImportFlips(string id)
    {
        var flips = NoireDraw3D.Diagnostics.ImportFlips;

        Note("Overrides for files authored in an unusual convention. Leave off for game models and spec-conforming glTF.");
        Gap();
        Note("A single mirror reflects, so it turns a model into its mirror image. Mirror X and Mirror Z together are a 180 degree turn instead, which changes only which way the model faces.");
        Gap();

        var changed = false;
        using (Form(id))
        {
            changed |= Toggle2("Mirror Z", () => flips.MirrorZ, v => flips.MirrorZ = v, "Reflects the model through the XY plane.");
            changed |= Toggle2("Mirror X", () => flips.MirrorX, v => flips.MirrorX = v, "Reflects through the YZ plane. With Mirror Z on, the two together are a 180 degree turn about Y rather than a reflection.");
            changed |= Toggle2("Reverse winding", () => flips.ReverseWinding, v => flips.ReverseWinding = v, "Undoes the reversal the loaders already apply. A file whose winding was converted before it got here needs this; anything else will render inside out with it.");
            changed |= Toggle2("Flip texture U", () => flips.FlipU, v => flips.FlipU = v, "Mirrors the texture horizontally.");
            changed |= Toggle2("Flip texture V", () => flips.FlipV, v => flips.FlipV = v, "Mirrors the texture vertically.");
        }

        return changed;
    }

    /// <summary>A checkbox row that reports whether it changed, for callers that must react to the edit rather than just store it.</summary>
    public static bool Toggle2(string label, Func<bool> get, Action<bool> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (!ImGui.Checkbox($"##{label}", ref v))
            return false;

        set(v);
        return true;
    }

    /// <summary>Draws a wrapped tooltip. Explicit "\n" still forces a break.</summary>
    /// <param name="text">The tooltip body.</param>
    public static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * TooltipWrapEm);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
        finally
        {
            ImGui.EndTooltip();
        }
    }

    // ---------------------------------------------------------------- bound widgets

    /// <summary>A checkbox row bound to a bool getter/setter.</summary>
    public static void Toggle(string label, Func<bool> get, Action<bool> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.Checkbox($"##{label}", ref v))
            set(v);
    }

    /// <summary>A float slider row bound to a getter/setter.</summary>
    public static void Slider(string label, Func<float> get, Action<float> set, float min, float max, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.SliderFloat($"##{label}", ref v, min, max))
            set(v);
    }

    /// <summary>A float drag row bound to a getter/setter.</summary>
    public static bool Drag(string label, Func<float> get, Action<float> set, float speed, float min, float max, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        var changed = ImGui.DragFloat($"##{label}", ref v, speed, min, max);
        if (changed)
            set(v);
        return changed;
    }

    /// <summary>An integer input row bound to a getter/setter.</summary>
    public static void Int(string label, Func<int> get, Action<int> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.InputInt($"##{label}", ref v))
            set(v);
    }

    /// <summary>A text input row bound to a getter/setter.</summary>
    public static void Text(string label, Func<string> get, Action<string> set, string placeholder = "", int maxLength = 512, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.InputTextWithHint($"##{label}", placeholder, ref v, maxLength))
            set(v);
    }

    /// <summary>An RGB colour row bound to a <see cref="Vector3"/> getter/setter.</summary>
    public static void Color3(string label, Func<Vector3> get, Action<Vector3> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.ColorEdit3($"##{label}", ref v))
            set(v);
    }

    /// <summary>An RGBA colour row bound to a <see cref="Vector4"/> getter/setter.</summary>
    public static void Color4(string label, Func<Vector4> get, Action<Vector4> set, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.ColorEdit4($"##{label}", ref v))
            set(v);
    }

    /// <summary>A 3-component slider row bound to a <see cref="Vector3"/> getter/setter.</summary>
    public static void Slider3(string label, Func<Vector3> get, Action<Vector3> set, float min, float max, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.SliderFloat3($"##{label}", ref v, min, max))
            set(v);
    }

    /// <summary>A 3-component drag row bound to a <see cref="Vector3"/> getter/setter.</summary>
    public static bool Drag3(string label, Func<Vector3> get, Action<Vector3> set, float speed = 0.05f, float min = 0f, float max = 0f, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        var changed = ImGui.DragFloat3($"##{label}", ref v, speed, min, max);
        if (changed)
            set(v);
        return changed;
    }

    /// <summary>A 4-component drag row bound to a <see cref="Vector4"/> getter/setter.</summary>
    public static void Drag4(string label, Func<Vector4> get, Action<Vector4> set, float speed, string? hint = null)
    {
        Row(label, hint);
        var v = get();
        if (ImGui.DragFloat4($"##{label}", ref v, speed))
            set(v);
    }

    /// <summary>A read-only "caption: value" row, for live state.</summary>
    public static void Value(string label, string value, string? hint = null)
    {
        Row(label, hint);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(value);
    }

    /// <summary>A read-only row whose value is coloured.</summary>
    public static void Value(string label, string value, Vector4 color, string? hint = null)
    {
        Row(label, hint);
        ImGui.AlignTextToFramePadding();
        using var pushed = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(value);
    }

    /// <summary>A read-only counter row: mono digits so a column of them stays aligned.</summary>
    public static void Counter(string label, long value, string? hint = null)
    {
        Row(label, hint);
        ImGui.AlignTextToFramePadding();
        Mono(value.ToString("N0"), value == 0 ? ImGuiColors.DalamudGrey3 : null);
    }

    // ---------------------------------------------------------------- enums

    /// <summary>An enum dropdown row bound to a getter/setter.</summary>
    public static bool Enum<T>(string label, Func<T> get, Action<T> set, string? hint = null) where T : struct, Enum
    {
        Row(label, hint);

        var values = System.Enum.GetValues<T>();
        var index = Array.IndexOf(values, get());
        if (index < 0)
            index = 0;

        if (!Combo($"##{label}", System.Enum.GetNames<T>(), ref index))
            return false;

        set(values[index]);
        return true;
    }

    /// <summary>
    /// An enum dropdown row backed by an external index, for a setting whose live value cannot be read back (a predicate
    /// the consumer handed over as a lambda).
    /// </summary>
    public static bool Enum<T>(string label, ref int index, string? hint = null) where T : struct, Enum
    {
        Row(label, hint);

        var names = System.Enum.GetNames<T>();
        if (index < 0 || index >= names.Length)
            index = 0;

        return Combo($"##{label}", names, ref index);
    }

    /// <summary>
    /// A checkbox per flag of a <c>[Flags]</c> enum, on one line. The zero member and combined aliases are skipped: they
    /// are states of the single-bit boxes, not boxes of their own. A dropdown cannot edit flags - it holds one member.
    /// </summary>
    public static void Flags<T>(string label, Func<T> get, Action<T> set, string? hint = null) where T : struct, Enum
    {
        Row(label, hint);

        var current = Convert.ToInt64(get());
        var value = current;

        var first = true;
        foreach (var member in System.Enum.GetValues<T>())
        {
            var bit = Convert.ToInt64(member);
            if (bit == 0 || (bit & (bit - 1)) != 0)
                continue;

            if (!first)
                ImGui.SameLine();
            first = false;

            var on = (current & bit) != 0;
            if (ImGui.Checkbox($"{member}##{label}", ref on))
                value = on ? value | bit : value & ~bit;
        }

        if (value != current)
            set((T)System.Enum.ToObject(typeof(T), value));
    }

    /// <summary>
    /// A dropdown over a name list, mutating <paramref name="index"/> on selection. Hand-rolled because the array
    /// <c>ImGui.Combo</c> overload misbehaves in this binding.
    /// </summary>
    /// <param name="id">The widget id (pass "##..." to suppress a duplicate caption).</param>
    /// <param name="names">The options, in order.</param>
    /// <param name="index">The selected index, updated in place.</param>
    public static bool Combo(string id, string[] names, ref int index)
    {
        var preview = index >= 0 && index < names.Length ? names[index] : string.Empty;
        if (!ImGui.BeginCombo(id, preview))
            return false;

        var changed = false;
        try
        {
            for (var i = 0; i < names.Length; i++)
            {
                var selected = i == index;
                if (ImGui.Selectable(names[i], selected))
                {
                    index = i;
                    changed = true;
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
        }
        finally
        {
            ImGui.EndCombo();
        }

        return changed;
    }
}
